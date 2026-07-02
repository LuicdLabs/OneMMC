using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Manager;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;

namespace OneMMC.Core.Features.PolicyManagement.ViewModels.GpEdit
{
    public partial class GroupPolicyEditorViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<GroupPolicyEditorViewModel> _logger;
        private readonly IAdminService _adminService;
        private AdmxBundle _admxBundle;
        private IPolicyService? _computerPolicyService;
        private IPolicyService? _userPolicyService;
        private SynchronizationContext? _syncContext;
        private bool _hasGpoInfrastructure;
        private bool _disposed;

        private string _filterText = string.Empty;
        private PolicyManagerCategory? _lastCategory;
        private bool _lastIsComputer;

        [ObservableProperty]
        public partial ObservableCollection<GroupPolicyTreeItem> RootNodes { get; set; } = new();

        [ObservableProperty]
        public partial GroupPolicyTreeItem? SelectedNode { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<PolicyListItem> CurrentPolicies { get; set; } = new();

        [ObservableProperty]
        public partial PolicyListItem? SelectedPolicy { get; set; }

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string? WindowsEditionWarning { get; set; }

        // Error message for UI
        private string? _lastErrorMessage;
        public string? LastErrorMessage
        {
            get => _lastErrorMessage;
            private set => SetProperty(ref _lastErrorMessage, value);
        }

        /// <summary>
        /// Raised when a write operation fails due to insufficient administrator privileges.
        /// </summary>
        public event EventHandler? AdminPermissionRequired;

        public GroupPolicyEditorViewModel(ILogger<GroupPolicyEditorViewModel> logger, IAdminService adminService)
        {
            _logger = logger;
            _adminService = adminService;
            _admxBundle = new AdmxBundle();
            _syncContext = SynchronizationContext.Current;
            
            // Check Windows edition and warn if necessary
            CheckWindowsEditionSupport();
            
            _ = LoadPoliciesAsync();
        }

        private void CheckWindowsEditionSupport()
        {
            _hasGpoInfrastructure = PolicyServiceFactory.HasGroupPolicyInfrastructure();
            
            if (!_hasGpoInfrastructure)
            {
                WindowsEditionWarning = LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.WindowsEditionNotice_Message);
                LogDebug("[WARNING] Windows edition detected without full Group Policy infrastructure - using direct Registry mode");
            }
            else
            {
                WindowsEditionWarning = null;
                LogDebug("[INFO] Windows edition supports Group Policy infrastructure");
            }
        }

        private async Task LoadPoliciesAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading ADMX files...";

            await Task.Run(() =>
            {
                try
                {
                    // Load ADMX files
                    string policyDefinitionsPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\PolicyDefinitions");
                    _admxBundle.LoadFolder(policyDefinitionsPath, System.Globalization.CultureInfo.CurrentCulture.Name);

                    // Initialize policy services using the factory
                    _computerPolicyService = PolicyServiceFactory.CreateMachinePolicyService();
                    _userPolicyService = PolicyServiceFactory.CreateUserPolicyService();

                    if (_computerPolicyService is null || _userPolicyService is null)
                    {
                        LogDebug("[ERROR] Failed to initialize policy services");
                        _syncContext?.Post(_ =>
                        {
                            LastErrorMessage = "Failed to initialize policy services";
                            IsLoading = false;
                            StatusMessage = "Error";
                        }, null);
                        return;
                    }

                    LogDebug("[GPO] Policy services initialized successfully");

                    _syncContext?.Post(_ =>
                    {
                        BuildPolicyTree();
                        IsLoading = false;
                        StatusMessage = "Ready";
                    }, null);
                }
                catch (Exception ex)
                {
                    LogDebug($"[ERROR] LoadPoliciesAsync failed: {ex.Message}");
                    _syncContext?.Post(_ =>
                    {
                        LastErrorMessage = $"Failed to load policies: {ex.Message}";
                        IsLoading = false;
                        StatusMessage = "Error";
                    }, null);
                }
            });
        }

        private void BuildPolicyTree()
        {
            RootNodes.Clear();

            var computerRoot = new GroupPolicyTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeComputerConfiguration),
                null,
                true,
                null,
                this);
            var compAdmin = new GroupPolicyTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeAdministrativeTemplates),
                null,
                true,
                _admxBundle.Categories.Values,
                this);
            computerRoot.Children.Add(compAdmin);

            var userRoot = new GroupPolicyTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeUserConfiguration),
                null,
                false,
                null,
                this);
            var userAdmin = new GroupPolicyTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeAdministrativeTemplates),
                null,
                false,
                _admxBundle.Categories.Values,
                this);
            userRoot.Children.Add(userAdmin);

            RootNodes.Add(computerRoot);
            RootNodes.Add(userRoot);
        }

        partial void OnSelectedNodeChanged(GroupPolicyTreeItem? value)
        {
            if (value?.Category is not null)
            {
                UpdatePolicyList(value.Category, value.IsComputerConfiguration);
            }
            else
            {
                CurrentPolicies.Clear();
            }
        }

        public void UpdatePolicyList(PolicyManagerCategory category, bool isComputer)
        {
            UpdatePolicyList(category, isComputer, _filterText);
        }

        /// <summary>
        /// Updates the CurrentPolicies list, optionally filtering by policy name.
        /// </summary>
        /// <param name="category">The policy category.</param>
        /// <param name="isComputer">Is computer configuration.</param>
        /// <param name="filter">Optional filter text.</param>
        public void UpdatePolicyList(PolicyManagerCategory category, bool isComputer, string? filter)
        {
            CurrentPolicies.Clear();
            _lastCategory = category;
            _lastIsComputer = isComputer;

            var section = isComputer ? AdmxPolicySection.Machine : AdmxPolicySection.User;
            var policies = category.Policies
                .Where(p => p.RawPolicy.Section == section || p.RawPolicy.Section == AdmxPolicySection.Both);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var lower = filter.Trim().ToLowerInvariant();
                policies = policies.Where(p => p.DisplayName.ToLowerInvariant().Contains(lower));
            }

            foreach (var policy in policies.OrderBy(p => p.DisplayName))
            {
                var state = GetPolicyState(policy, isComputer);
                CurrentPolicies.Add(new PolicyListItem(policy, state));
            }
        }

        public PolicyState GetPolicyState(PolicyManagerPolicy policy, bool isComputerContext)
        {
            var service = isComputerContext ? _computerPolicyService : _userPolicyService;
            if (service is null) return PolicyState.NotConfigured;

            try
            {
                return service.GetPolicyState(policy);
            }
            catch (Exception ex)
            {
                LogDebug($"[ERROR] GetPolicyState failed: {ex.Message}");
                return PolicyState.Unknown;
            }
        }

        public Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy, bool isComputerContext)
        {
            var service = isComputerContext ? _computerPolicyService : _userPolicyService;
            if (service is null) return new Dictionary<string, object>();

            try
            {
                return service.GetPolicyOptions(policy);
            }
            catch (Exception ex)
            {
                LogDebug($"[ERROR] GetPolicyOptions failed: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        public void SetPolicyState(PolicyManagerPolicy policy, bool isComputerContext, PolicyState state, Dictionary<string, object> options)
        {
            var service = isComputerContext ? _computerPolicyService : _userPolicyService;

            if (service is null)
            {
                LastErrorMessage = "Policy service not initialized";
                return;
            }

            try
            {
                LogDebug($"[GPO] Setting policy state: {policy.DisplayName} -> {state}");

                if (!service.SetPolicyState(policy, state, options))
                {
                    LastErrorMessage = service.LastError;
                    LogPolicyAction("Set-Fail", policy, state);

                    // If the service reports !IsWritable (access denied), treat as permission error
                    if (!service.IsWritable)
                    {
                        AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
                        throw new UnauthorizedAccessException(service.LastError);
                    }

                    throw new InvalidOperationException(service.LastError);
                }

                var saveStatus = service.Save();
                LogPolicyAction("Save", policy, state);
                LogDebug($"[GPO] Save status: {saveStatus}");

                // Trigger policy refresh if on Pro/Enterprise
                if (_hasGpoInfrastructure)
                {
                    try
                    {
                        PInvoke.RefreshPolicyEx(!isComputerContext, PInvoke.RP_FORCE);
                        LogDebug($"[GPO] Triggered policy refresh for {(isComputerContext ? "Machine" : "User")}");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"[GPO] RefreshPolicyEx failed (non-critical): {ex.Message}");
                    }
                }

                LastErrorMessage = null;
                _syncContext?.Post(_ => ReloadAndRefreshUI(), null);
            }
            catch (UnauthorizedAccessException ex)
            {
                var msg = isComputerContext
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine)
                    : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_User);
                LogPolicyAction("Save-Fail-Unauthorized", policy, state, ex);
                LastErrorMessage = msg;
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
                throw;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                if (_adminService.IsPermissionError(ex))
                {
                    LogPolicyAction("Save-Fail-Permission", policy, state, ex);
                    LastErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
                    AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
                    throw new UnauthorizedAccessException(LastErrorMessage, ex);
                }

                var msg = $"Failed to save policy: {ex.Message}";
                LogPolicyAction("Save-Fail-Exception", policy, state, ex);
                LastErrorMessage = msg;
                throw;
            }
        }

        // Reload and refresh current displayed UI
        public void ReloadAndRefreshUI()
        {
            if (SelectedNode?.Category is not null)
            {
                LogDebug($"[UI] Refreshing policy list for category: {SelectedNode.Category.DisplayName}");
                UpdatePolicyList(SelectedNode.Category, SelectedNode.IsComputerConfiguration);
            }
        }
        
        /// <summary>
        /// Reload policy sources (for refreshing after external changes)
        /// </summary>
        public void ReloadPolicySources()
        {
            try
            {
                _computerPolicyService?.Dispose();
                _userPolicyService?.Dispose();

                _computerPolicyService = PolicyServiceFactory.CreateMachinePolicyService();
                _userPolicyService = PolicyServiceFactory.CreateUserPolicyService();

                LogDebug("[GPO] Policy sources reloaded");
                _syncContext?.Post(_ => ReloadAndRefreshUI(), null);
            }
            catch (Exception ex)
            {
                LogDebug($"[GPO] Failed to reload policy sources: {ex.Message}");
            }
        }

        public void SavePolicy(PolicyListItem item, PolicyState state, Dictionary<string, object> options)
        {
            bool isComputer = SelectedNode?.IsComputerConfiguration ?? false;

            try
            {
                SetPolicyState(item.Policy, isComputer, state, options);
                item.State = state;
                LogDebug($"[POLICY] Successfully saved policy: {item.Policy.DisplayName} with state: {state}");
            }
            catch (Exception ex)
            {
                LogDebug($"[ERROR] SavePolicy failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Filters the CurrentPolicies list by policy name.
        /// </summary>
        /// <param name="filter">The filter text.</param>
        public void FilterPoliciesByName(string filter)
        {
            _filterText = filter;
            if (_lastCategory is not null)
            {
                UpdatePolicyList(_lastCategory, _lastIsComputer, filter);
            }
        }

        // Log policy operations (for debugging and auditing)
        private void LogPolicyAction(string action, PolicyManagerPolicy? policy, PolicyState state, Exception? ex = null)
        {
            var user = Environment.UserName;
            var time = DateTime.Now.ToString("u");
            var msg = $"[{time}] User: {user}, Action: {action}, Policy: {policy?.DisplayName ?? "null"}, State: {state}";
            if (ex is not null)
            {
                msg += $", Error: {ex.GetType().Name} - {ex.Message}";
            }
            LogDebug(msg);
        }

        private void LogDebug(string message)
        {
            _logger.LogDebug("{Message}", message);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _computerPolicyService?.Dispose();
            _userPolicyService?.Dispose();
            _computerPolicyService = null;
            _userPolicyService = null;

            _disposed = true;
        }

        public partial class GroupPolicyTreeItem : ObservableObject
        {
            public string Name { get; set; }
            public PolicyManagerCategory? Category { get; set; }
            public bool IsComputerConfiguration { get; set; }
            public ObservableCollection<GroupPolicyTreeItem> Children { get; set; } = new();
            private readonly GroupPolicyEditorViewModel _viewModel;

            public GroupPolicyTreeItem(string name, PolicyManagerCategory? category, bool isComputer, IEnumerable<PolicyManagerCategory>? childrenToPopulate, GroupPolicyEditorViewModel vm)
            {
                Name = name;
                Category = category;
                IsComputerConfiguration = isComputer;
                _viewModel = vm;

                if (childrenToPopulate is not null)
                {
                    PopulateChildren(childrenToPopulate, isComputer ? AdmxPolicySection.Machine : AdmxPolicySection.User);
                }
            }

            private void PopulateChildren(IEnumerable<PolicyManagerCategory> categories, AdmxPolicySection section)
            {
                foreach (var cat in categories.OrderBy(c => c.DisplayName))
                {
                    if (HasPoliciesInSection(cat, section))
                    {
                        Children.Add(new GroupPolicyTreeItem(cat.DisplayName, cat, IsComputerConfiguration, cat.Children, _viewModel));
                    }
                }
            }

            private static bool HasPoliciesInSection(PolicyManagerCategory category, AdmxPolicySection section)
            {
                if (category.Policies.Any(p => p.RawPolicy.Section == section || p.RawPolicy.Section == AdmxPolicySection.Both))
                    return true;

                return category.Children.Any(c => HasPoliciesInSection(c, section));
            }
        }
    }
}


