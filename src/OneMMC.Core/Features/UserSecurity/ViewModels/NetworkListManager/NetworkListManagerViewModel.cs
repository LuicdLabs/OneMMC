using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.NetworkListManager;
using OneMMC.Core.Features.UserSecurity.Services.NetworkListManager;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.NetworkListManager;

/// <summary>
/// View model for the Network List Manager Policies page.
/// </summary>
public sealed partial class NetworkListManagerViewModel : ObservableObject
{
    private readonly NetworkListPolicyService _policyService;
    private readonly ILogger<NetworkListManagerViewModel> _logger;
    private readonly IAdminService _adminService;
    private List<NetworkListPolicyNodeViewModel> _allNodes = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkListManagerViewModel"/> class.
    /// </summary>
    /// <param name="policyService">The backing policy service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator detection service.</param>
    public NetworkListManagerViewModel(
        NetworkListPolicyService policyService,
        ILogger<NetworkListManagerViewModel> logger,
        IAdminService adminService)
    {
        _policyService = policyService;
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Raised when a write operation fails due to missing administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the currently visible nodes.
    /// </summary>
    public ObservableCollection<NetworkListPolicyNodeViewModel> Nodes { get; } = [];

    /// <summary>
    /// Gets or sets whether the page is currently loading data.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets the current error message.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether an error is currently displayed.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the current filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Loads all Network List Manager policy nodes.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var nodes = await Task.Run(() => _policyService.LoadNodes());
            _allNodes = nodes.Select(node => new NetworkListPolicyNodeViewModel(node)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NetworkListManagerViewModel] Failed to load policy nodes.");
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the page contents.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Saves the configured network name for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="hasCustomName"><see langword="true"/> to save a custom name; otherwise clear it.</param>
    /// <param name="networkName">The network name to persist.</param>
    public async Task SaveNetworkNameAsync(NetworkListPolicyNodeViewModel node, bool hasCustomName, string? networkName)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveNetworkName(node.SignatureId, hasCustomName, networkName),
            "network name");
    }

    /// <summary>
    /// Saves the configured icon payload for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="payload">The new icon payload, or <see langword="null"/> to clear it.</param>
    public async Task SaveNetworkIconAsync(NetworkListPolicyNodeViewModel node, NetworkListIconPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveNetworkIcon(node.SignatureId, payload),
            "network icon");
    }

    /// <summary>
    /// Saves the name permission state for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="selectedIndex">The selected combo index.</param>
    public async Task SaveNamePermissionAsync(NetworkListPolicyNodeViewModel node, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveNamePermission(node.SignatureId, ToPermissionMode(selectedIndex)),
            "name permission");
    }

    /// <summary>
    /// Saves the icon permission state for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="selectedIndex">The selected combo index.</param>
    public async Task SaveIconPermissionAsync(NetworkListPolicyNodeViewModel node, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveIconPermission(node.SignatureId, ToPermissionMode(selectedIndex)),
            "icon permission");
    }

    /// <summary>
    /// Saves the location type for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="selectedIndex">The selected combo index.</param>
    public async Task SaveLocationTypeAsync(NetworkListPolicyNodeViewModel node, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveLocationType(node.SignatureId, ToLocationMode(selectedIndex)),
            "location type");
    }

    /// <summary>
    /// Saves the location permission state for a node.
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="selectedIndex">The selected combo index.</param>
    public async Task SaveLocationPermissionAsync(NetworkListPolicyNodeViewModel node, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        await SaveAsync(
            () => _policyService.SaveLocationPermission(node.SignatureId, ToPermissionMode(selectedIndex)),
            "location permission");
    }

    private async Task SaveAsync(Action action, string operationName)
    {
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(action);
            await LoadAsync();
            _logger.LogInformation("[NetworkListManagerViewModel] Saved {OperationName}.", operationName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NetworkListManagerViewModel] Failed to save {OperationName}.", operationName);
            HasError = true;

            if (_adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException)
            {
                ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = ex.Message;
            }

            await LoadAsync();
        }
    }

    private void ApplyFilter()
    {
        Nodes.Clear();

        foreach (var node in _allNodes.Where(node => node.MatchesFilter(FilterText)))
        {
            Nodes.Add(node);
        }
    }

    private static NetworkListPermissionMode ToPermissionMode(int selectedIndex) => selectedIndex switch
    {
        1 => NetworkListPermissionMode.Allow,
        2 => NetworkListPermissionMode.Deny,
        _ => NetworkListPermissionMode.NotConfigured
    };

    private static NetworkListLocationMode ToLocationMode(int selectedIndex) => selectedIndex switch
    {
        1 => NetworkListLocationMode.Private,
        2 => NetworkListLocationMode.Public,
        _ => NetworkListLocationMode.NotConfigured
    };
}
