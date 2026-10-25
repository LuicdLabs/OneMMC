using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using OneMMC.Core.Infrastructure.Collections;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol
{
    /// <summary>
    /// Represents a policy category item for display in the tree view.
    /// </summary>
    public sealed class PolicyCategoryItem
    {
        public string DisplayName { get; }
        public SecurityPolicyCategory Category { get; }

        public PolicyCategoryItem(string displayName, SecurityPolicyCategory category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Shared state and commands for the Account Policies and Local Policies pages: category
    /// selection, a single version-guarded load, filtering, saving, and permission-error reporting.
    /// The derived page contributes only its categories.
    /// </summary>
    public abstract partial class SecurityPoliciesViewModelBase : ObservableObject
    {
        private readonly SecurityPolicyService _policyService;
        private readonly IAdminService _adminService;
        private readonly ILogger _logger;
        private readonly string _logName;

        private List<SecurityPolicyValue> _allPolicies = new();
        private int _loadVersion;
        private bool _isStopped;

        /// <summary>Initializes the shared policy list state for a page.</summary>
        /// <param name="policyService">Reads and writes the policies.</param>
        /// <param name="logger">The page's logger.</param>
        /// <param name="adminService">Classifies permission failures.</param>
        /// <param name="logName">The name prefixed to log messages.</param>
        protected SecurityPoliciesViewModelBase(
            SecurityPolicyService policyService,
            ILogger logger,
            IAdminService adminService,
            string logName)
        {
            _policyService = policyService;
            _logger = logger;
            _adminService = adminService;
            _logName = logName;

            _logger.LogDebug("[{ViewModel}] Initializing", _logName);
        }

        /// <summary>Raised when an operation fails due to insufficient administrator privileges.</summary>
        public event EventHandler? AdminPermissionRequired;

        /// <summary>Available categories within the page.</summary>
        public ObservableCollection<PolicyCategoryItem> Categories { get; } = new();

        /// <summary>Policies displayed for the currently selected category.</summary>
        public ObservableCollection<SecurityPolicyValue> CurrentPolicies { get; } = new();

        [ObservableProperty]
        public partial PolicyCategoryItem? SelectedCategory { get; set; }

        [ObservableProperty]
        public partial SecurityPolicyValue? SelectedPolicy { get; set; }

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string FilterText { get; set; } = string.Empty;

        /// <summary>Adds a category to the page's tree.</summary>
        protected void AddCategory(string displayName, SecurityPolicyCategory category)
            => Categories.Add(new PolicyCategoryItem(displayName, category));

        /// <summary>Selects the first category, which starts its load.</summary>
        protected void SelectFirstCategory()
        {
            if (Categories.Count > 0)
                SelectedCategory = Categories[0];
        }

        private void ApplyFilter()
        {
            IEnumerable<SecurityPolicyValue> filtered = string.IsNullOrWhiteSpace(FilterText)
                ? _allPolicies
                : _allPolicies.Where(p => p.Definition.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
            CurrentPolicies.ReplaceAll(filtered);
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(PolicyCategoryItem? value)
        {
            _allPolicies.Clear();
            ApplyFilter();
            if (value is not null)
            {
                _logger.LogDebug("[{ViewModel}] Category changed to: {CategoryDisplayName}", _logName, value.DisplayName);
                LoadPoliciesCommand.Execute(null);
            }
        }

        /// <summary>
        /// Stops publishing results or errors from pending loads after the owning page unloads.
        /// The synchronous native policy read finishes normally.
        /// </summary>
        public void StopLoading()
        {
            _isStopped = true;
            ++_loadVersion;
            IsLoading = false;
        }

        /// <summary>Loads policies from the system for the currently selected category.</summary>
        [RelayCommand]
        private async Task LoadPoliciesAsync()
        {
            PolicyCategoryItem? category = SelectedCategory;
            if (_isStopped || category is null) return;

            int version = ++_loadVersion;

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            SelectedPolicy = null;
            _allPolicies.Clear();
            ApplyFilter();

            _logger.LogDebug("[{ViewModel}] Loading policies for: {CategoryDisplayName}", _logName, category.DisplayName);

            try
            {
                var policies = await Task.Run(() => _policyService.ReadPolicies(category.Category));
                if (version != _loadVersion || SelectedCategory != category)
                    return;

                _allPolicies = new List<SecurityPolicyValue>(policies);
                ApplyFilter();

                _logger.LogInformation("[{ViewModel}] Loaded {PolicyCount} policies", _logName, _allPolicies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ViewModel}] Error loading policies.", _logName);
                if (version != _loadVersion || SelectedCategory != category)
                    return;

                ReportError(ex);
            }
            finally
            {
                if (version == _loadVersion)
                    IsLoading = false;
            }
        }

        /// <summary>Refreshes the current policy list from the system.</summary>
        [RelayCommand]
        private async Task RefreshAsync()
        {
            _logger.LogDebug("[{ViewModel}] Refreshing policies", _logName);
            await LoadPoliciesAsync();
        }

        /// <summary>Saves a modified policy value to the system.</summary>
        public async Task<bool> SavePolicyAsync(SecurityPolicyValue policyValue)
        {
            if (_isStopped) return false;

            _logger.LogDebug("[{ViewModel}] Saving policy: {PolicyKey}", _logName, policyValue.Definition.Key);

            try
            {
                await Task.Run(() => _policyService.WritePolicy(policyValue));
                _logger.LogInformation("[{ViewModel}] Policy saved successfully: {PolicyKey}", _logName, policyValue.Definition.Key);

                // Refresh to show updated values.
                await LoadPoliciesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ViewModel}] Error saving policy: {PolicyKey}", _logName, policyValue.Definition.Key);
                if (_isStopped) return false;

                ReportError(ex);
                return false;
            }
        }

        private void ReportError(Exception ex)
        {
            if (_adminService.IsPermissionError(ex))
            {
                ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = ex.Message;
            }

            HasError = true;
        }
    }
}
