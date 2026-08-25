using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.RSoP;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP
{
    /// <summary>
    /// ViewModel for the Resultant Set of Policy (RSoP) page.
    /// Only shows policies that have been actively configured (Enabled or Disabled).
    /// Categories with no configured policies are hidden from the tree.
    /// </summary>
    public sealed partial class ResultantSetOfPolicyViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<ResultantSetOfPolicyViewModel> _logger;
        private readonly RSoPService _rsopService;
        private SynchronizationContext? _syncContext;
        private bool _disposed;

        private List<RSoPPolicyItem> _allPoliciesForCurrentNode = new();
        private string _currentFilter = string.Empty;

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial RSoPTreeItem? SelectedNode { get; set; }

        [ObservableProperty]
        public partial RSoPPolicyItem? SelectedPolicy { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial int ConfiguredPoliciesCount { get; set; }

        /// <summary>
        /// Gets the root nodes of the policy tree.
        /// </summary>
        public ObservableCollection<RSoPTreeItem> RootNodes { get; } = new();

        /// <summary>
        /// Gets the current list of policies to display (all are configured).
        /// </summary>
        public ObservableCollection<RSoPPolicyItem> CurrentPolicies { get; } = new();

        public ResultantSetOfPolicyViewModel(RSoPService rsopService, ILogger<ResultantSetOfPolicyViewModel> logger)
        {
            _rsopService = rsopService;
            _logger = logger;
            _syncContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Initializes the ViewModel by loading real policy data asynchronously.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || cancellationToken.IsCancellationRequested || RootNodes.Count > 0) return;

            IsLoading = true;
            StatusMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Policy, RSoPKeys.Loading);
            HasError = false;

            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_rsopService.Initialize())
                    {
                        _syncContext?.Post(_ =>
                        {
                            if (_disposed || cancellationToken.IsCancellationRequested) return;

                            HasError = true;
                            ErrorMessage = LocalizationProvider.Current.GetString(
                                ResourceFileNames.Policy, RSoPKeys.ErrorLoadFailed);
                            IsLoading = false;
                            StatusMessage = string.Empty;
                        }, null);
                        return;
                    }

                    _syncContext?.Post(_ =>
                    {
                        if (_disposed || cancellationToken.IsCancellationRequested) return;

                        BuildPolicyTree();
                        IsLoading = false;
                        StatusMessage = string.Empty;
                    }, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize RSoP ViewModel");
                    _syncContext?.Post(_ =>
                    {
                        if (_disposed || cancellationToken.IsCancellationRequested) return;

                        HasError = true;
                        ErrorMessage = $"{LocalizationProvider.Current.GetString(ResourceFileNames.Policy, RSoPKeys.ErrorLoadFailed)}: {ex.Message}";
                        IsLoading = false;
                        StatusMessage = string.Empty;
                    }, null);
                }
            });
        }

        /// <summary>
        /// Builds the policy tree from real ADMX categories.
        /// Only includes categories that contain at least one configured policy.
        /// </summary>
        private void BuildPolicyTree()
        {
            if (_disposed) return;

            RootNodes.Clear();

            var categories = _rsopService.GetTopLevelCategories();

            // Computer Configuration - only add if there are configured policies
            var computerAdmin = new RSoPTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeAdministrativeTemplates),
                null, true, this);

            foreach (var cat in categories.OrderBy(c => c.DisplayName))
            {
                if (_rsopService.HasConfiguredPoliciesInSection(cat, AdmxPolicySection.Machine))
                {
                    computerAdmin.Children.Add(CreateCategoryTreeItem(cat, true));
                }
            }

            if (computerAdmin.Children.Count > 0)
            {
                var computerRoot = new RSoPTreeItem(
                    LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeComputerConfiguration),
                    null, true, this);
                computerRoot.Children.Add(computerAdmin);
                RootNodes.Add(computerRoot);
            }

            // User Configuration - only add if there are configured policies
            var userAdmin = new RSoPTreeItem(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeAdministrativeTemplates),
                null, false, this);

            foreach (var cat in categories.OrderBy(c => c.DisplayName))
            {
                if (_rsopService.HasConfiguredPoliciesInSection(cat, AdmxPolicySection.User))
                {
                    userAdmin.Children.Add(CreateCategoryTreeItem(cat, false));
                }
            }

            if (userAdmin.Children.Count > 0)
            {
                var userRoot = new RSoPTreeItem(
                    LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.TreeUserConfiguration),
                    null, false, this);
                userRoot.Children.Add(userAdmin);
                RootNodes.Add(userRoot);
            }

            _logger.LogInformation("RSoP policy tree built — only categories with configured policies are shown");
        }

        /// <summary>
        /// Creates a tree item for a category, only including child categories
        /// that have configured policies.
        /// </summary>
        private RSoPTreeItem CreateCategoryTreeItem(PolicyManagerCategory category, bool isComputer)
        {
            var section = isComputer ? AdmxPolicySection.Machine : AdmxPolicySection.User;
            var item = new RSoPTreeItem(category.DisplayName, category, isComputer, this);

            foreach (var child in category.Children.OrderBy(c => c.DisplayName))
            {
                if (_rsopService!.HasConfiguredPoliciesInSection(child, section))
                {
                    item.Children.Add(CreateCategoryTreeItem(child, isComputer));
                }
            }

            return item;
        }

        /// <summary>
        /// When the selected tree node changes, load configured policies for that category.
        /// </summary>
        partial void OnSelectedNodeChanged(RSoPTreeItem? value)
        {
            if (value?.Category is not null)
            {
                UpdatePolicyList(value.Category, value.IsComputerConfiguration);
            }
            else
            {
                _allPoliciesForCurrentNode.Clear();
                CurrentPolicies.Clear();
                ConfiguredPoliciesCount = 0;
            }
        }

        /// <summary>
        /// Updates the policy list for the specified category.
        /// Only configured (Enabled/Disabled) policies are shown.
        /// </summary>
        public void UpdatePolicyList(PolicyManagerCategory category, bool isComputer)
        {
            if (_rsopService is null) return;

            IsLoading = true;

            try
            {
                var results = _rsopService.GetPoliciesForCategory(category, isComputer);
                _allPoliciesForCurrentNode = results.Select(r => new RSoPPolicyItem(r)).ToList();

                ConfiguredPoliciesCount = _allPoliciesForCurrentNode.Count;
                ApplyFilter();

                _logger.LogDebug("Updated RSoP policy list for category: {Category}, {Count} configured policies",
                    category.DisplayName, _allPoliciesForCurrentNode.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update policy list for category: {Category}", category.DisplayName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Filters policies by name.
        /// </summary>
        public void FilterPoliciesByName(string filter)
        {
            _currentFilter = filter ?? string.Empty;
            ApplyFilter();
        }

        /// <summary>
        /// Applies text filter to the current policy list.
        /// </summary>
        private void ApplyFilter()
        {
            CurrentPolicies.Clear();

            IEnumerable<RSoPPolicyItem> filtered = _allPoliciesForCurrentNode;

            // Apply text filter
            if (!string.IsNullOrWhiteSpace(_currentFilter))
            {
                filtered = filtered.Where(p =>
                    p.DisplayName.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var policy in filtered)
            {
                CurrentPolicies.Add(policy);
            }
        }

        /// <summary>
        /// Gets detailed policy options for the currently selected policy.
        /// </summary>
        public Dictionary<string, object> GetSelectedPolicyOptions()
        {
            if (_rsopService is null || SelectedPolicy is null) return new Dictionary<string, object>();

            return _rsopService.GetPolicyOptions(
                SelectedPolicy.UnderlyingResult.UnderlyingPolicy,
                SelectedPolicy.IsComputerPolicy);
        }


        /// <summary>
        /// Refreshes the current view by re-reading policy states.
        /// </summary>
        public void Refresh()
        {
            if (SelectedNode?.Category is not null)
            {
                UpdatePolicyList(SelectedNode.Category, SelectedNode.IsComputerConfiguration);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _rsopService.Dispose();
        }

    }

    /// <summary>
    /// Represents a tree item in the RSoP tree view.
    /// Declared at namespace level so XAML compiled bindings (x:Bind casts) can reference it.
    /// </summary>
    public partial class RSoPTreeItem
    {
        /// <summary>Gets the display name of this tree node.</summary>
        public string Name { get; }

        /// <summary>Gets the underlying ADMX category, if any.</summary>
        public PolicyManagerCategory? Category { get; }

        /// <summary>Gets whether this is under computer configuration.</summary>
        public bool IsComputerConfiguration { get; }

        /// <summary>Gets the child tree items.</summary>
        public ObservableCollection<RSoPTreeItem> Children { get; } = new();

        private readonly ResultantSetOfPolicyViewModel _viewModel;

        public RSoPTreeItem(string name, PolicyManagerCategory? category, bool isComputer, ResultantSetOfPolicyViewModel viewModel)
        {
            Name = name;
            Category = category;
            IsComputerConfiguration = isComputer;
            _viewModel = viewModel;
        }
    }
}



