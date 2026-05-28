using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.Certificates.Models;
using ManagementTools.Core.Features.Certificates.Services;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.Certificates.ViewModels;

/// <summary>
/// Shared view-model logic for the current-user and local-machine certificate pages.
/// </summary>
public abstract partial class CertificateStoresViewModelBase : ObservableObject
{
    private readonly CertificateStoreService _certificateStoreService;
    private readonly IAdminService _adminService;
    private readonly ILogger _logger;
    private readonly StoreLocation _storeLocation;
    private IReadOnlyList<CertificateStoreNode> _allStores = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateStoresViewModelBase"/> class.
    /// </summary>
    /// <param name="certificateStoreService">The certificate store enumeration service.</param>
    /// <param name="adminService">The administrator permission service.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <param name="storeLocation">The logical store location shown by the page.</param>
    protected CertificateStoresViewModelBase(
        CertificateStoreService certificateStoreService,
        IAdminService adminService,
        ILogger logger,
        StoreLocation storeLocation)
    {
        _certificateStoreService = certificateStoreService;
        _adminService = adminService;
        _logger = logger;
        _storeLocation = storeLocation;
    }

    /// <summary>
    /// Raised when the page should prompt the user for administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the currently visible logical stores.
    /// </summary>
    public ObservableCollection<CertificateStoreNode> Stores { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether stores are currently loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an error is currently shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the error message displayed by the page.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page-wide filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether destructive or import operations can run without elevation.
    /// </summary>
    public bool CanWriteToStores => _storeLocation == StoreLocation.CurrentUser || _adminService.IsRunningAsAdmin;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Loads all logical stores for the current page.
    /// </summary>
    [RelayCommand]
    private async Task LoadStoresAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            IReadOnlyDictionary<string, bool> expansionState = Stores.ToDictionary(
                store => store.StoreName,
                store => store.IsExpanded,
                StringComparer.OrdinalIgnoreCase);

            _allStores = await _certificateStoreService.GetStoresAsync(_storeLocation);

            foreach (CertificateStoreNode store in _allStores)
            {
                if (expansionState.TryGetValue(store.StoreName, out bool isExpanded))
                {
                    store.IsExpanded = isExpanded;
                }
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            HandleException(ex, "Failed to load certificate stores.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes a single logical store card.
    /// </summary>
    /// <param name="storeName">The store name to refresh.</param>
    public async Task RefreshStoreAsync(string storeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        try
        {
            CertificateStoreNode? existing = _allStores.FirstOrDefault(store =>
                string.Equals(store.StoreName, storeName, StringComparison.OrdinalIgnoreCase));
            CertificateStoreNode? refreshed = await _certificateStoreService.GetStoreAsync(_storeLocation, storeName);
            if (refreshed is null)
            {
                return;
            }

            if (existing is not null)
            {
                refreshed.IsExpanded = existing.IsExpanded;
            }

            _allStores = _allStores
                .Where(store => !string.Equals(store.StoreName, storeName, StringComparison.OrdinalIgnoreCase))
                .Append(refreshed)
                .OrderBy(store => store.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            HandleException(ex, "Failed to refresh a certificate store.");
        }
    }

    /// <summary>
    /// Deletes the specified entry and refreshes its parent store on success.
    /// </summary>
    /// <param name="entry">The entry to delete.</param>
    /// <returns><see langword="true"/> when the entry was deleted; otherwise <see langword="false"/>.</returns>
    public async Task<bool> DeleteEntryAsync(CertificateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!CanWriteToStores)
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            return false;
        }

        try
        {
            await _certificateStoreService.DeleteEntryAsync(entry);
            await RefreshStoreAsync(entry.StoreName);
            return true;
        }
        catch (Exception ex)
        {
            HandleException(ex, "Failed to delete a certificate store entry.");
            return false;
        }
    }

    /// <summary>
    /// Clears loaded certificate store data when the owning page leaves the visual tree.
    /// </summary>
    public void ClearCachedData()
    {
        _allStores = [];
        Stores.Clear();
        FilterText = string.Empty;
        ErrorMessage = string.Empty;
        HasError = false;
        IsLoading = false;
    }

    private void ApplyFilter()
    {
        IReadOnlyDictionary<string, bool> expansionState = Stores.ToDictionary(
            store => store.StoreName,
            store => store.IsExpanded,
            StringComparer.OrdinalIgnoreCase);

        Stores.Clear();

        foreach (CertificateStoreNode store in GetVisibleStores())
        {
            if (expansionState.TryGetValue(store.StoreName, out bool isExpanded))
            {
                store.IsExpanded = isExpanded;
            }

            Stores.Add(store);
        }
    }

    private IEnumerable<CertificateStoreNode> GetVisibleStores()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            foreach (CertificateStoreNode store in _allStores)
            {
                yield return store;
            }

            yield break;
        }

        string filter = FilterText.Trim();

        foreach (CertificateStoreNode store in _allStores)
        {
            bool storeMatches = store.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || store.StoreName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
            var filteredSections = new List<CertificateSection>(store.Sections.Count);
            bool anyEntryMatch = false;

            foreach (CertificateSection section in store.Sections)
            {
                IReadOnlyList<CertificateEntry> entries = storeMatches
                    ? section.Entries
                    : section.Entries.Where(entry => entry.Matches(filter)).ToList();

                anyEntryMatch |= entries.Count > 0;
                filteredSections.Add(section.WithEntries(entries));
            }

            if (storeMatches || anyEntryMatch)
            {
                yield return store.WithSections(filteredSections);
            }
        }
    }

    private void HandleException(Exception ex, string logMessage)
    {
        _logger.LogError(ex, logMessage);

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
