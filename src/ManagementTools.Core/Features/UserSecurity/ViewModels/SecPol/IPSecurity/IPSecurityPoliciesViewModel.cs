using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.IPSecurity;

/// <summary>
/// View model for IP Security Policies on Local Computer.
/// </summary>
public sealed partial class IPSecurityPoliciesViewModel : ObservableObject
{
    private readonly IPSecurityPolicyService _policyService;
    private readonly IPSecurityStaticPolicyMutationService _mutationService;
    private readonly ILogger<IPSecurityPoliciesViewModel> _logger;
    private readonly IAdminService _adminService;
    private IPSecurityPolicySnapshot _snapshot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityPoliciesViewModel"/> class.
    /// </summary>
    /// <param name="policyService">The IP Security Policies service.</param>
    /// <param name="mutationService">The legacy static policy mutation service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator service.</param>
    public IPSecurityPoliciesViewModel(
        IPSecurityPolicyService policyService,
        IPSecurityStaticPolicyMutationService mutationService,
        ILogger<IPSecurityPoliciesViewModel> logger,
        IAdminService adminService)
    {
        _policyService = policyService;
        _mutationService = mutationService;
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Raised when a policy operation requires administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the visible policy rows.
    /// </summary>
    public ObservableCollection<IPSecurityPolicyRow> Items { get; } = [];

    /// <summary>
    /// Gets or sets the selected policy row.
    /// </summary>
    [ObservableProperty]
    public partial IPSecurityPolicyRow? SelectedPolicy { get; set; }

    /// <summary>
    /// Gets a value indicating whether the selected row has read-only details.
    /// </summary>
    public bool CanViewSelectedPolicyDetails => SelectedPolicy?.CanViewDetails == true;

    /// <summary>
    /// Gets a value indicating whether a new item can be created in the selected section.
    /// </summary>
    public bool CanCreateItem => !IsLoading && !HasError;

    /// <summary>
    /// Gets a value indicating whether the selected item can be edited.
    /// </summary>
    public bool CanEditSelectedItem => CanCreateItem && SelectedPolicy is not null;

    /// <summary>
    /// Gets a value indicating whether the selected item can be deleted.
    /// </summary>
    public bool CanDeleteSelectedItem => CanEditSelectedItem;

    /// <summary>
    /// Gets a value indicating whether the selected policy can be assigned or unassigned.
    /// </summary>
    public bool CanAssignSelectedPolicy => CanCreateItem && SelectedPolicy?.Policy is { IsAssigned: false };

    /// <summary>
    /// Gets a value indicating whether the selected policy can be unassigned.
    /// </summary>
    public bool CanUnassignSelectedPolicy => CanCreateItem && SelectedPolicy?.Policy is { IsAssigned: true };

    /// <summary>
    /// Gets a value indicating whether the selected policy's rules can be managed.
    /// </summary>
    public bool CanManageSelectedPolicyRules => CanCreateItem && SelectedPolicy?.Policy is not null;

    /// <summary>
    /// Gets the shared filter-list names available to policy rules.
    /// </summary>
    public IReadOnlyList<string> FilterListNames =>
        _snapshot.FilterLists.Select(static row => row.Name).ToArray();

    /// <summary>
    /// Gets the shared filter-action names available to policy rules.
    /// </summary>
    public IReadOnlyList<string> FilterActionNames =>
        _snapshot.FilterActions.Select(static row => row.Name).ToArray();

    /// <summary>
    /// Gets the shared filter-list definitions, for the manage filter lists and filter actions dialog.
    /// </summary>
    public IReadOnlyList<IPSecurityFilterListDefinition> FilterLists =>
        _snapshot.FilterLists.Select(static row => row.FilterList).OfType<IPSecurityFilterListDefinition>().ToArray();

    /// <summary>
    /// Gets the shared filter-action definitions, for the manage filter lists and filter actions dialog.
    /// </summary>
    public IReadOnlyList<IPSecurityFilterActionDefinition> FilterActions =>
        _snapshot.FilterActions.Select(static row => row.FilterAction).OfType<IPSecurityFilterActionDefinition>().ToArray();

    /// <summary>
    /// Gets a value indicating whether the policy list has no matching rows.
    /// </summary>
    public bool IsEmpty => !IsLoading && !HasError && Items.Count == 0;

    /// <summary>
    /// Gets the localized empty-state message.
    /// </summary>
    public string EmptyMessage => GetString(IPSecurityPolicyKeys.EmptyPolicies);

    /// <summary>
    /// Gets or sets a value indicating whether policies are loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an error is visible.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the current error message.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// Loads IP Security Policies.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        NotifyEmptyStateChanged();

        try
        {
            await LoadSnapshotCoreAsync();
        }
        catch (Exception ex)
        {
            HandleOperationFailure(ex, clearSnapshot: true);
        }
        finally
        {
            IsLoading = false;
            NotifyEmptyStateChanged();
        }
    }

    /// <summary>
    /// Refreshes IP Security Policies.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>Adds a policy and reloads the store.</summary>
    public Task<bool> AddPolicyAsync(IPSecurityPolicyCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.AddPolicyAsync(options));

    /// <summary>Updates a policy and reloads the store.</summary>
    public Task<bool> SetPolicyAsync(IPSecurityPolicyCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.SetPolicyAsync(options));

    /// <summary>Assigns or unassigns a policy and reloads the store.</summary>
    public Task<bool> AssignPolicyAsync(string policyName, bool isAssigned)
        => ExecuteMutationAsync(() => _mutationService.AssignPolicyAsync(policyName, isAssigned));

    /// <summary>Deletes a policy and reloads the store.</summary>
    public Task<bool> DeletePolicyAsync(string policyName)
        => ExecuteMutationAsync(() => _mutationService.DeletePolicyAsync(policyName));

    /// <summary>Adds a shared filter list and reloads the store.</summary>
    public Task<bool> AddFilterListAsync(IPSecurityFilterListCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.AddFilterListAsync(options));

    /// <summary>Adds a shared filter list with its filters and reloads the store.</summary>
    public Task<bool> AddFilterListWithFiltersAsync(
        IPSecurityFilterListCommandOptions options,
        IReadOnlyList<IPSecurityFilterCommandOptions> filters)
        => ExecuteMutationAsync(() => _mutationService.AddFilterListWithFiltersAsync(options, filters));

    /// <summary>Updates a shared filter list and reloads the store.</summary>
    public Task<bool> SetFilterListAsync(IPSecurityFilterListCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.SetFilterListAsync(options));

    /// <summary>Updates a shared filter list with its filters and reloads the store.</summary>
    public Task<bool> SetFilterListWithFiltersAsync(
        IPSecurityFilterListDefinition original,
        IPSecurityFilterListCommandOptions options,
        IReadOnlyList<IPSecurityFilterCommandOptions> filters)
        => ExecuteMutationAsync(() => _mutationService.SetFilterListWithFiltersAsync(original, options, filters));

    /// <summary>Deletes a shared filter list and reloads the store.</summary>
    public Task<bool> DeleteFilterListAsync(string filterListName)
        => ExecuteMutationAsync(() => _mutationService.DeleteFilterListAsync(filterListName));

    /// <summary>Adds a filter and reloads the store.</summary>
    public Task<bool> AddFilterAsync(IPSecurityFilterCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.AddFilterAsync(options));

    /// <summary>Replaces a filter and reloads the store.</summary>
    public Task<bool> ReplaceFilterAsync(
        IPSecurityFilterCommandOptions original,
        IPSecurityFilterCommandOptions replacement)
        => ExecuteMutationAsync(() => _mutationService.ReplaceFilterAsync(original, replacement));

    /// <summary>Deletes a filter and reloads the store.</summary>
    public Task<bool> DeleteFilterAsync(IPSecurityFilterCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.DeleteFilterAsync(options));

    /// <summary>Adds a shared filter action and reloads the store.</summary>
    public Task<bool> AddFilterActionAsync(IPSecurityFilterActionCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.AddFilterActionAsync(options));

    /// <summary>Updates a shared filter action and reloads the store.</summary>
    public Task<bool> SetFilterActionAsync(IPSecurityFilterActionCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.SetFilterActionAsync(options));

    /// <summary>Deletes a shared filter action and reloads the store.</summary>
    public Task<bool> DeleteFilterActionAsync(string filterActionName)
        => ExecuteMutationAsync(() => _mutationService.DeleteFilterActionAsync(filterActionName));

    /// <summary>Adds a rule and reloads the store.</summary>
    public Task<bool> AddRuleAsync(IPSecurityRuleCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.AddRuleAsync(options));

    /// <summary>Updates a rule and reloads the store.</summary>
    public Task<bool> SetRuleAsync(IPSecurityRuleCommandOptions options)
        => ExecuteMutationAsync(() => _mutationService.SetRuleAsync(options));

    /// <summary>Deletes a named rule and reloads the store.</summary>
    public Task<bool> DeleteRuleAsync(string policyName, string ruleName)
        => ExecuteMutationAsync(() => _mutationService.DeleteRuleAsync(policyName, ruleName));

    private async Task<bool> ExecuteMutationAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (IsLoading || HasError)
        {
            return false;
        }

        IsLoading = true;
        try
        {
            await operation();
            await LoadSnapshotCoreAsync();
            return true;
        }
        catch (Exception ex)
        {
            HandleOperationFailure(ex, clearSnapshot: false);
            return false;
        }
        finally
        {
            IsLoading = false;
            NotifyEmptyStateChanged();
        }
    }

    private async Task LoadSnapshotCoreAsync()
    {
        _snapshot = await Task.Run(() => _policyService.LoadSnapshot());
        ApplyFilter();
    }

    private void HandleOperationFailure(Exception ex, bool clearSnapshot)
    {
        bool accessDenied = _adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException;
        _logger.LogError(ex, "[IPSecurityPoliciesViewModel] The legacy static IPsec policy operation failed.");
        ErrorMessage = accessDenied
            ? GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common)
            : GetString(IPSecurityPolicyKeys.LegacyStoreReadFailed);
        HasError = true;
        if (clearSnapshot)
        {
            _snapshot = new IPSecurityPolicySnapshot();
        }

        ApplyFilter();

        if (accessDenied)
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        SelectedPolicy = null;

        IEnumerable<IPSecurityPolicyRow> rows = _snapshot.Policies;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            rows = rows.Where(row =>
                row.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Description.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.PolicyAssigned.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.LastModified.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Summary.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || row.Details.Any(detail =>
                    detail.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                    || detail.Value.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)));
        }

        foreach (IPSecurityPolicyRow row in rows)
        {
            Items.Add(row);
        }

        NotifyEmptyStateChanged();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedPolicyChanged(IPSecurityPolicyRow? value)
    {
        OnPropertyChanged(nameof(CanViewSelectedPolicyDetails));
        NotifyMutationStateChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyMutationStateChanged();
    }

    partial void OnHasErrorChanged(bool value)
    {
        NotifyMutationStateChanged();
        NotifyEmptyStateChanged();
    }

    private void NotifyEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void NotifyMutationStateChanged()
    {
        OnPropertyChanged(nameof(CanCreateItem));
        OnPropertyChanged(nameof(CanEditSelectedItem));
        OnPropertyChanged(nameof(CanDeleteSelectedItem));
        OnPropertyChanged(nameof(CanAssignSelectedPolicy));
        OnPropertyChanged(nameof(CanUnassignSelectedPolicy));
        OnPropertyChanged(nameof(CanManageSelectedPolicyRules));
    }

    private static string GetString(string key, string resourceFileName = ResourceFileNames.SecPol)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
