// ============================================================================
// Authorization Manager ViewModels - Refactored Version
// ============================================================================
// Improved maintainability, reduced code duplication, and enhanced readability
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.UserSecurity.Services.AzMan;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.AzMan;

#region Base Classes

/// <summary>
/// Base class for ViewModels with INotifyPropertyChanged implementation
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Set property value and raise PropertyChanged event if value changed
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, Action? onChanged = null, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        onChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Raise PropertyChanged event
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Base class for ViewModels with loading and status management
/// </summary>
public abstract class LoadingViewModelBase : ViewModelBase
{
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private ILogger _logger = NullLogger.Instance;

    protected static ILocalizationProvider L => LocalizationProvider.Current;

    protected void SetLogger(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    /// <summary>
    /// Execute operation with standard error handling pattern
    /// </summary>
    protected async Task<T?> ExecuteWithErrorHandlingAsync<T>(
        Func<Task<T>> operation,
        string loadingMessage,
        Func<T, string>? successMessage = null,
        Action<T>? onSuccess = null)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            StatusMessage = loadingMessage;

            var result = await operation();

            if (successMessage != null)
                StatusMessage = successMessage(result);

            onSuccess?.Invoke(result);
            return result;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            _logger.LogDebug(ex, "[{ViewModelType}] Operation failed", GetType().Name);
            return default;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Execute operation with standard error handling pattern (boolean result)
    /// </summary>
    protected async Task<bool> ExecuteWithErrorHandlingAsync(
        Func<Task> operation,
        string loadingMessage,
        string successMessage)
    {
        try
        {
            IsLoading = true;
            HasError = false;
            StatusMessage = loadingMessage;

            await operation();

            StatusMessage = successMessage;
            return true;
        }
        catch (AzManException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            _logger.LogDebug(ex, "[{ViewModelType}] Operation failed", GetType().Name);
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

#endregion

#region Helper Classes

/// <summary>
/// Extension methods for ObservableCollection
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Replace all items in the collection with new items
    /// </summary>
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    /// <summary>
    /// Apply filter to source collection and update filtered collection
    /// </summary>
    public static void ApplyFilter<T>(
        this ObservableCollection<T> filteredCollection,
        IEnumerable<T> sourceCollection,
        string searchText,
        Func<T, string, bool> predicate)
    {
        filteredCollection.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? sourceCollection
            : sourceCollection.Where(item => predicate(item, searchText));

        foreach (var item in filtered)
            filteredCollection.Add(item);
    }
}

/// <summary>
/// Store protocol constants
/// </summary>
internal static class StoreProtocols
{
    public const string XmlPrefix = "msxml://";
    public const string LdapPrefix = "msldap://";
    public const string SqlPrefix = "mssql://";

    private static readonly (string Prefix, int Length)[] Prefixes = new[]
    {
        (XmlPrefix, 8),
        (LdapPrefix, 9),
        (SqlPrefix, 8)
    };

    /// <summary>
    /// Extract raw path by removing protocol prefix
    /// </summary>
    public static string ExtractRawPath(string storeUrl)
    {
        if (string.IsNullOrEmpty(storeUrl))
            return storeUrl;

        foreach (var (prefix, length) in Prefixes)
        {
            if (storeUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return storeUrl[length..];
        }

        return storeUrl;
    }
}

/// <summary>
/// Model for persisting store information
/// </summary>
internal class StorePersistenceModel
{
    public string Path { get; set; } = string.Empty;
    public AzStoreType StoreType { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion

#region Authorization Manager ViewModel

/// <summary>
/// Main ViewModel for managing authorization stores
/// </summary>
public class AuthorizationManagerViewModel : LoadingViewModelBase, IDisposable
{
    private const string StoresFileName = "AzManStores.json";

    private readonly AzManService _azManService;
    private readonly ILogger<AuthorizationManagerViewModel> _logger;
    private string _searchText = string.Empty;
    private bool _disposed;

    public ObservableCollection<AzAuthorizationStoreInfo> Stores { get; } = new();
    public ObservableCollection<AzAuthorizationStoreInfo> FilteredStores { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value, ApplyFilter);
    }

    public string StoreCountText => FilteredStores.Count == 1
        ? L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.StoreCount_Singular, FilteredStores.Count)
        : L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.StoreCount_Plural, FilteredStores.Count);

    public AzManService Service => _azManService;

    #region Constructor

    public AuthorizationManagerViewModel(AzManService service, ILogger<AuthorizationManagerViewModel> logger)
    {
        _azManService = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? NullLogger<AuthorizationManagerViewModel>.Instance;
        SetLogger(_logger);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadPersistedStoresAsync();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Create a new authorization store
    /// </summary>
    public async Task<AzAuthorizationStoreInfo?> CreateStoreAsync(CreateStoreParameters parameters)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var store = await _azManService.CreateStoreAsync(parameters);
                Stores.Add(store);
                return store;
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_CreatingStore),
            store => L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Status_CreateSuccess, store.Name),
            _ =>
            {
                ApplyFilter();
                SavePersistedStores();
            });
    }

    /// <summary>
    /// Open an existing authorization store
    /// </summary>
    public async Task<AzAuthorizationStoreInfo?> OpenStoreAsync(OpenStoreParameters parameters)
    {
        bool exists = false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var store = await _azManService.OpenStoreAsync(parameters);

                exists = Stores.Any(s =>
                    s.StorePath.Equals(store.StorePath, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    Stores.Add(store);
                }

                return store;
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_OpeningStore),
            store => L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Status_OpenSuccess, store.Name),
            store =>
            {
                ApplyFilter();

                if (!exists)
                {
                    SavePersistedStores();
                }
            });
    }

    /// <summary>
    /// Close a store
    /// </summary>
    public void CloseStore(AzAuthorizationStoreInfo store)
    {
        try
        {
            _azManService.CloseStore(store.StorePath);
            Stores.Remove(store);
            ApplyFilter();
            SavePersistedStores();
            StatusMessage = L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Status_CloseStore, store.Name);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to close store: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a store
    /// </summary>
    public async Task<bool> DeleteStoreAsync(AzAuthorizationStoreInfo store)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteStoreAsync(store.StorePath);
                Stores.Remove(store);
                SavePersistedStores();
                ApplyFilter();
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_DeletingStore),
            L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Status_DeleteSuccess, store.Name));
    }

    /// <summary>
    /// Refresh a store
    /// </summary>
    public async Task RefreshStoreAsync(AzAuthorizationStoreInfo store)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var updated = await _azManService.RefreshStoreAsync(store.StorePath);

                if (updated != null)
                {
                    int index = Stores.IndexOf(store);
                    if (index >= 0)
                    {
                        Stores[index] = updated;
                    }
                }
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_Refreshing),
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_RefreshComplete));

        ApplyFilter();
    }

    /// <summary>
    /// Refresh all stores
    /// </summary>
    public async Task RefreshAllAsync()
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                foreach (var store in Stores.ToList())
                {
                    await _azManService.RefreshStoreAsync(store.StorePath);
                }
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_RefreshingAll),
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Status_RefreshComplete));

        ApplyFilter();
    }

    /// <summary>
    /// Get details of a store
    /// </summary>
    public AzAuthorizationStoreInfo? GetStore(string storePath)
    {
        return Stores.FirstOrDefault(s =>
            s.StorePath.Equals(storePath, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Apply search filter
    /// </summary>
    private void ApplyFilter()
    {
        FilteredStores.ApplyFilter(
            Stores,
            SearchText,
            (store, search) =>
                store.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                store.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(StoreCountText));
    }

    /// <summary>
    /// Load persisted authorization stores
    /// </summary>
    private async Task LoadPersistedStoresAsync()
    {
        await Task.Yield();

        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneMMC");

            var filePath = System.IO.Path.Combine(folder, StoresFileName);

            if (!System.IO.File.Exists(filePath))
                return;

            string json = await System.IO.File.ReadAllTextAsync(filePath);
            var savedStores = System.Text.Json.JsonSerializer.Deserialize<List<StorePersistenceModel>>(json);

            if (savedStores == null)
                return;

            bool removedMissingStores = false;

            foreach (var savedStore in savedStores)
            {
                try
                {
                    string rawPath = StoreProtocols.ExtractRawPath(savedStore.Path);

                    // Skip XML store files that no longer exist on disk.
                    if (savedStore.StoreType == AzStoreType.Xml && !System.IO.File.Exists(rawPath))
                    {
                        _logger.LogDebug($"[AuthorizationManagerViewModel] Store file missing, removing from persistence: {rawPath}");
                        removedMissingStores = true;
                        continue;
                    }

                    var parameters = new OpenStoreParameters
                    {
                        Path = rawPath,
                        StoreType = savedStore.StoreType,
                        ReadOnly = false
                    };

                    var store = await _azManService.OpenStoreAsync(parameters);
                    if (store != null)
                    {
                        Stores.Add(store);
                    }
                }
                catch (AzManException azEx)
                {
                    // The store could not be opened (e.g. the AD object or XML file was
                    // deleted outside of this application).  Remove it from the persistence
                    // file so that the same error is not shown on every subsequent launch.
                    _logger.LogDebug(
                        "[AuthorizationManagerViewModel] Cannot restore store {Path}, removing from persistence: {Message}",
                        savedStore.Path, azEx.Message);
                    removedMissingStores = true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        "[AuthorizationManagerViewModel] Failed to restore store {Path}: {Message}",
                        savedStore.Path, ex.Message);
                    removedMissingStores = true;
                }
            }

            ApplyFilter();

            if (removedMissingStores)
            {
                SavePersistedStores();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[AuthorizationManagerViewModel] Failed to load persisted stores: {ex.Message}");
        }
    }

    /// <summary>
    /// Save the list of authorization stores
    /// </summary>
    private void SavePersistedStores()
    {
        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneMMC");

            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            var filePath = System.IO.Path.Combine(folder, StoresFileName);

            var persistenceList = Stores.Select(s => new StorePersistenceModel
            {
                Path = StoreProtocols.ExtractRawPath(s.StorePath),
                StoreType = s.StoreType,
                Name = s.Name
            }).ToList();

            string json = System.Text.Json.JsonSerializer.Serialize(persistenceList);
            System.IO.File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[AuthorizationManagerViewModel] Failed to save persistence stores: {ex.Message}");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _azManService.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}

#endregion

#region Authorization Store ViewModel

/// <summary>
/// ViewModel for managing a single authorization store
/// </summary>
public class AuthorizationStoreViewModel : LoadingViewModelBase
{
    private readonly AzManService _azManService;
    private AzAuthorizationStoreInfo? _store;
    private string _applicationSearchText = string.Empty;
    private string _groupSearchText = string.Empty;

    public AzAuthorizationStoreInfo? Store
    {
        get => _store;
        set => SetProperty(ref _store, value, () =>
        {
            OnPropertyChanged(nameof(StoreName));
            OnPropertyChanged(nameof(Applications));
            OnPropertyChanged(nameof(Groups));
            ApplyFilters();
        });
    }

    public string StoreName => _store?.Name ?? string.Empty;
    public IReadOnlyList<AzApplicationInfo> Applications => _store?.Applications ?? (IReadOnlyList<AzApplicationInfo>)Array.Empty<AzApplicationInfo>();
    public IReadOnlyList<AzApplicationGroupInfo> Groups => _store?.Groups ?? (IReadOnlyList<AzApplicationGroupInfo>)Array.Empty<AzApplicationGroupInfo>();

    public ObservableCollection<AzApplicationInfo> FilteredApplications { get; } = new();
    public ObservableCollection<AzApplicationGroupInfo> FilteredGroups { get; } = new();

    public string ApplicationSearchText
    {
        get => _applicationSearchText;
        set => SetProperty(ref _applicationSearchText, value, ApplyApplicationFilter);
    }

    public string GroupSearchText
    {
        get => _groupSearchText;
        set => SetProperty(ref _groupSearchText, value, ApplyGroupFilter);
    }

    public string ApplicationCountText => FilteredApplications.Count == 1
        ? L.GetFormattedString(ResourceFileNames.Common, CommonKeys.CountItem_Singular, FilteredApplications.Count)
        : L.GetFormattedString(ResourceFileNames.Common, CommonKeys.CountItem_Plural, FilteredApplications.Count);

    public string GroupCountText => FilteredGroups.Count == 1
        ? L.GetFormattedString(ResourceFileNames.Common, CommonKeys.CountItem_Singular, FilteredGroups.Count)
        : L.GetFormattedString(ResourceFileNames.Common, CommonKeys.CountItem_Plural, FilteredGroups.Count);

    #region Constructor

    public AuthorizationStoreViewModel(AzManService service)
    {
        _azManService = service ?? throw new ArgumentNullException(nameof(service));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Load store information
    /// </summary>
    public async Task LoadAsync(string storePath)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var updated = await _azManService.RefreshStoreAsync(storePath);
                Store = updated;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.LoadingData),
            L.GetString(ResourceFileNames.Common, CommonKeys.LoadedSuccessfully));
    }

    /// <summary>
    /// Create application
    /// </summary>
    public async Task<AzApplicationInfo?> CreateApplicationAsync(string name, string description)
    {
        if (_store == null) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var app = await _azManService.CreateApplicationAsync(_store.StorePath, name, description);
                await LoadAsync(_store.StorePath);
                return app;
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreatingApplication),
            app => L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreateApplicationSuccess, name));
    }

    /// <summary>
    /// Delete application
    /// </summary>
    public async Task<bool> DeleteApplicationAsync(string appName)
    {
        if (_store == null) return false;

        var result = await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteApplicationAsync(_store.StorePath, appName);
                await LoadAsync(_store.StorePath);
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeletingApplication),
            L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeleteApplicationSuccess, appName));

        return result;
    }

    /// <summary>
    /// Create group
    /// </summary>
    public async Task<AzApplicationGroupInfo?> CreateGroupAsync(
        string name,
        AzGroupType groupType,
        string description,
        string ldapQuery = "")
    {
        if (_store == null) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var group = await _azManService.CreateStoreGroupAsync(
                    _store.StorePath, name, groupType, description, ldapQuery);
                await LoadAsync(_store.StorePath);
                return group;
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreatingGroup),
            group => L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreateGroupSuccess, name));
    }

    /// <summary>
    /// Delete group
    /// </summary>
    public async Task<bool> DeleteGroupAsync(string groupName)
    {
        if (_store == null) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteStoreGroupAsync(_store.StorePath, groupName);
                await LoadAsync(_store.StorePath);
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeletingGroup),
            L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeleteGroupSuccess, groupName));
    }

    /// <summary>
    /// Update group
    /// </summary>
    public async Task<bool> UpdateGroupAsync(string groupName, string description, string ldapQuery = "")
    {
        if (_store == null) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.UpdateStoreGroupAsync(_store.StorePath, groupName, description, ldapQuery);
                await LoadAsync(_store.StorePath);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Updating),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    #endregion

    #region Private Methods

    private void ApplyFilters()
    {
        ApplyApplicationFilter();
        ApplyGroupFilter();
    }

    private void ApplyApplicationFilter()
    {
        FilteredApplications.ApplyFilter(
            Applications,
            ApplicationSearchText,
            (app, search) =>
                app.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                app.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(ApplicationCountText));
    }

    private void ApplyGroupFilter()
    {
        FilteredGroups.ApplyFilter(
            Groups,
            GroupSearchText,
            (group, search) =>
                group.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                group.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(GroupCountText));
    }

    #endregion
}

#endregion

#region Authorization Application ViewModel

/// <summary>
/// ViewModel for managing a single authorization application
/// </summary>
public class AuthApplicationViewModel : LoadingViewModelBase
{
    private readonly AzManService _azManService;
    private string _storePath = string.Empty;
    private AzApplicationInfo? _application;
    private string _groupSearchText = string.Empty;
    private string _roleAssignmentSearchText = string.Empty;
    private string _roleDefinitionSearchText = string.Empty;
    private string _taskSearchText = string.Empty;
    private string _operationSearchText = string.Empty;
    private string _scopeSearchText = string.Empty;

    #region Properties

    public string StorePath
    {
        get => _storePath;
        set => SetProperty(ref _storePath, value);
    }

    public AzApplicationInfo? Application
    {
        get => _application;
        set => SetProperty(ref _application, value, () => OnPropertyChanged(nameof(ApplicationName)));
    }

    public string ApplicationName => _application?.Name ?? string.Empty;

    private bool IsInitialized => !string.IsNullOrEmpty(StorePath) && _application != null;

    // Collections
    public ObservableCollection<AzApplicationGroupInfo> Groups { get; } = new();
    public ObservableCollection<AzRoleAssignmentInfo> RoleAssignments { get; } = new();
    public ObservableCollection<AzRoleDefinitionInfo> RoleDefinitions { get; } = new();
    public ObservableCollection<AzTaskInfo> Tasks { get; } = new();
    public ObservableCollection<AzOperationInfo> Operations { get; } = new();
    public ObservableCollection<AzScopeInfo> Scopes { get; } = new();

    // Filtered Collections
    public ObservableCollection<AzApplicationGroupInfo> FilteredGroups { get; } = new();
    public ObservableCollection<AzRoleAssignmentInfo> FilteredRoleAssignments { get; } = new();
    public ObservableCollection<AzRoleDefinitionInfo> FilteredRoleDefinitions { get; } = new();
    public ObservableCollection<AzTaskInfo> FilteredTasks { get; } = new();
    public ObservableCollection<AzOperationInfo> FilteredOperations { get; } = new();
    public ObservableCollection<AzScopeInfo> FilteredScopes { get; } = new();

    // Search Properties
    public string GroupSearchText
    {
        get => _groupSearchText;
        set => SetProperty(ref _groupSearchText, value, ApplyGroupFilter);
    }

    public string RoleAssignmentSearchText
    {
        get => _roleAssignmentSearchText;
        set => SetProperty(ref _roleAssignmentSearchText, value, ApplyRoleAssignmentFilter);
    }

    public string RoleDefinitionSearchText
    {
        get => _roleDefinitionSearchText;
        set => SetProperty(ref _roleDefinitionSearchText, value, ApplyRoleDefinitionFilter);
    }

    public string TaskSearchText
    {
        get => _taskSearchText;
        set => SetProperty(ref _taskSearchText, value, ApplyTaskFilter);
    }

    public string OperationSearchText
    {
        get => _operationSearchText;
        set => SetProperty(ref _operationSearchText, value, ApplyOperationFilter);
    }

    public string ScopeSearchText
    {
        get => _scopeSearchText;
        set => SetProperty(ref _scopeSearchText, value, ApplyScopeFilter);
    }

    // Count Text Properties
    public string GroupCountText => GetCountText(FilteredGroups.Count, CommonKeys.CountItem_Singular, CommonKeys.CountItem_Plural);
    public string RoleAssignmentCountText => GetCountText(FilteredRoleAssignments.Count, CommonKeys.CountItem_Singular, CommonKeys.CountItem_Plural);
    public string RoleDefinitionCountText => GetCountText(FilteredRoleDefinitions.Count, CommonKeys.CountRole_Singular, CommonKeys.CountRole_Plural);
    public string TaskCountText => GetCountText(FilteredTasks.Count, CommonKeys.CountTask_Singular, CommonKeys.CountTask_Plural);
    public string OperationCountText => GetCountText(FilteredOperations.Count, CommonKeys.CountOperation_Singular, CommonKeys.CountOperation_Plural);
    public string ScopeCountText => GetCountText(FilteredScopes.Count, CommonKeys.CountScope_Singular, CommonKeys.CountScope_Plural);

    private string GetCountText(int count, string singularKey, string pluralKey)
    {
        return count == 1
            ? L.GetFormattedString(ResourceFileNames.Common, singularKey, count)
            : L.GetFormattedString(ResourceFileNames.Common, pluralKey, count);
    }

    #endregion

    #region Constructor

    public AuthApplicationViewModel(AzManService service)
    {
        _azManService = service ?? throw new ArgumentNullException(nameof(service));
    }

    #endregion

    #region Public Methods - Load and CRUD

    /// <summary>
    /// Load application data
    /// </summary>
    public async Task LoadAsync(string storePath, string appName)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                StorePath = storePath;
                var app = await _azManService.GetApplicationAsync(storePath, appName);
                Application = app;

                Groups.ReplaceWith(app.Groups);
                RoleAssignments.ReplaceWith(app.RoleAssignments);
                RoleDefinitions.ReplaceWith(app.RoleDefinitions);
                Tasks.ReplaceWith(app.Tasks);
                Operations.ReplaceWith(app.Operations);
                Scopes.ReplaceWith(app.Scopes);

                ApplyAllFilters();
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.LoadingData),
            L.GetString(ResourceFileNames.Common, CommonKeys.LoadedSuccessfully));
    }

    /// <summary>
    /// Create group
    /// </summary>
    public async Task<AzApplicationGroupInfo?> CreateGroupAsync(
        string name, AzGroupType groupType, string description, string ldapQuery = "")
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var group = await _azManService.CreateAppGroupAsync(
                    StorePath, _application!.Name, name, groupType, description, ldapQuery);
                await LoadAsync(StorePath, _application.Name);
                return group;
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreatingGroup),
            group => L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_CreateGroupSuccess, name));
    }

    /// <summary>
    /// Delete group
    /// </summary>
    public async Task<bool> DeleteGroupAsync(string groupName)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteAppGroupAsync(StorePath, _application!.Name, groupName);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeletingGroup),
            L.GetFormattedString(ResourceFileNames.AzMan, AzManKeys.Store_Status_DeleteGroupSuccess, groupName));
    }

    /// <summary>
    /// Create role definition
    /// </summary>
    public async Task<AzRoleDefinitionInfo?> CreateRoleDefinitionAsync(string name, string description)
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var role = await _azManService.CreateRoleDefinitionAsync(
                    StorePath, _application!.Name, name, description);
                await LoadAsync(StorePath, _application.Name);
                return role;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Creating),
            _ => L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Delete role definition
    /// </summary>
    public async Task<bool> DeleteRoleDefinitionAsync(string roleName)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteRoleDefinitionAsync(StorePath, _application!.Name, roleName);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Deleting),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Create role assignment
    /// </summary>
    public async Task<AzRoleAssignmentInfo?> CreateRoleAssignmentAsync(string name, string description)
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var role = await _azManService.CreateRoleAssignmentAsync(
                    StorePath, _application!.Name, name, description);
                await LoadAsync(StorePath, _application.Name);
                return role;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Creating),
            _ => L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Delete role assignment
    /// </summary>
    public async Task<bool> DeleteRoleAssignmentAsync(string roleName)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteRoleAssignmentAsync(StorePath, _application!.Name, roleName);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Deleting),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Create task
    /// </summary>
    public async Task<AzTaskInfo?> CreateTaskAsync(string name, string description)
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var task = await _azManService.CreateTaskAsync(
                    StorePath, _application!.Name, name, description);
                await LoadAsync(StorePath, _application.Name);
                return task;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Creating),
            _ => L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Delete task
    /// </summary>
    public async Task<bool> DeleteTaskAsync(string taskName)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteTaskAsync(StorePath, _application!.Name, taskName);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Deleting),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Create operation
    /// </summary>
    public async Task<AzOperationInfo?> CreateOperationAsync(string name, string description, int operationId = -1)
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                int nextId = operationId >= 0
                    ? operationId
                    : await _azManService.GetNextOperationIdAsync(StorePath, _application!.Name);

                var operation = await _azManService.CreateOperationAsync(
                    StorePath, _application!.Name, name, nextId, description);
                await LoadAsync(StorePath, _application.Name);
                return operation;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Creating),
            _ => L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Delete operation
    /// </summary>
    public async Task<bool> DeleteOperationAsync(string operationName)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteOperationAsync(StorePath, _application!.Name, operationName);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Deleting),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Create scope
    /// </summary>
    public async Task<AzScopeInfo?> CreateScopeAsync(string name, string description)
    {
        if (!IsInitialized) return null;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var scope = await _azManService.CreateScopeAsync(StorePath, _application!.Name, name, description);
                await LoadAsync(StorePath, _application.Name);
                return scope;
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Creating),
            _ => L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    /// <summary>
    /// Delete scope
    /// </summary>
    public async Task<bool> DeleteScopeAsync(string name)
    {
        if (!IsInitialized) return false;

        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                await _azManService.DeleteScopeAsync(StorePath, _application!.Name, name);
                await LoadAsync(StorePath, _application.Name);
            },
            L.GetString(ResourceFileNames.Common, CommonKeys.Deleting),
            L.GetString(ResourceFileNames.Common, CommonKeys.OperationCompleted));
    }

    #endregion

    #region Public Methods - Member Management

    /// <summary>
    /// Add member to group
    /// </summary>
    public async Task AddGroupMemberAsync(string groupName, string memberSid)
    {
        if (!IsInitialized) return;
        await _azManService.AddGroupMemberAsync(StorePath, _application!.Name, groupName, memberSid);
    }

    /// <summary>
    /// Remove member from group
    /// </summary>
    public async Task RemoveGroupMemberAsync(string groupName, string memberSid)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveGroupMemberAsync(StorePath, _application!.Name, groupName, memberSid);
    }

    /// <summary>
    /// Add non-member to group
    /// </summary>
    public async Task AddGroupNonMemberAsync(string groupName, string memberSid)
    {
        if (!IsInitialized) return;
        await _azManService.AddGroupNonMemberAsync(StorePath, _application!.Name, groupName, memberSid);
    }

    /// <summary>
    /// Remove non-member from group
    /// </summary>
    public async Task RemoveGroupNonMemberAsync(string groupName, string memberSid)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveGroupNonMemberAsync(StorePath, _application!.Name, groupName, memberSid);
    }

    /// <summary>
    /// Add member to role assignment
    /// </summary>
    public async Task AddRoleAssignmentMemberAsync(string roleName, string memberSid)
    {
        if (!IsInitialized) return;
        await _azManService.AddRoleMemberAsync(StorePath, _application!.Name, roleName, memberSid);
    }

    #endregion

    #region Public Methods - Task Management

    /// <summary>
    /// Add task to role definition
    /// </summary>
    public async Task AddTaskToRoleDefinitionAsync(string roleDefinitionName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.AddTaskToRoleDefinitionAsync(StorePath, _application!.Name, roleDefinitionName, taskName);
    }

    /// <summary>
    /// Remove task from role definition
    /// </summary>
    public async Task RemoveTaskFromRoleDefinitionAsync(string roleDefinitionName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveTaskFromRoleDefinitionAsync(StorePath, _application!.Name, roleDefinitionName, taskName);
    }

    /// <summary>
    /// Add task to role assignment
    /// </summary>
    public async Task AddTaskToRoleAssignmentAsync(string roleName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.AddTaskToRoleAssignmentAsync(StorePath, _application!.Name, roleName, taskName);
    }

    /// <summary>
    /// Remove task from role assignment
    /// </summary>
    public async Task RemoveTaskFromRoleAssignmentAsync(string roleName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveTaskFromRoleAssignmentAsync(StorePath, _application!.Name, roleName, taskName);
    }

    /// <summary>
    /// Add operation to task
    /// </summary>
    public async Task AddOperationToTaskAsync(string taskName, string operationName)
    {
        if (!IsInitialized) return;
        await _azManService.AddOperationToTaskAsync(StorePath, _application!.Name, taskName, operationName);
    }

    /// <summary>
    /// Remove operation from task
    /// </summary>
    public async Task RemoveOperationFromTaskAsync(string taskName, string operationName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveOperationFromTaskAsync(StorePath, _application!.Name, taskName, operationName);
    }

    /// <summary>
    /// Add task link to a task.
    /// </summary>
    public async Task AddTaskLinkAsync(string taskName, string linkedTaskName)
    {
        if (!IsInitialized) return;
        await _azManService.AddTaskLinkAsync(StorePath, _application!.Name, taskName, linkedTaskName);
    }

    /// <summary>
    /// Remove task link from a task.
    /// </summary>
    public async Task RemoveTaskLinkAsync(string taskName, string linkedTaskName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveTaskLinkAsync(StorePath, _application!.Name, taskName, linkedTaskName);
    }

    /// <summary>
    /// Add operation to role definition.
    /// </summary>
    public async Task AddOperationToRoleDefinitionAsync(string roleDefinitionName, string operationName)
    {
        if (!IsInitialized) return;
        await _azManService.AddOperationToRoleDefinitionAsync(StorePath, _application!.Name, roleDefinitionName, operationName);
    }

    /// <summary>
    /// Remove operation from role definition.
    /// </summary>
    public async Task RemoveOperationFromRoleDefinitionAsync(string roleDefinitionName, string operationName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveOperationFromRoleDefinitionAsync(StorePath, _application!.Name, roleDefinitionName, operationName);
    }

    /// <summary>
    /// Backward compatibility - Add task to role (deprecated)
    /// </summary>
    [Obsolete("Use AddTaskToRoleDefinitionAsync or AddTaskToRoleAssignmentAsync")]
    public async Task AddTaskToRoleAsync(string roleName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.AddTaskToRoleAsync(StorePath, _application!.Name, roleName, taskName);
    }

    /// <summary>
    /// Backward compatibility - Remove task from role (deprecated)
    /// </summary>
    [Obsolete("Use RemoveTaskFromRoleDefinitionAsync or RemoveTaskFromRoleAssignmentAsync")]
    public async Task RemoveTaskFromRoleAsync(string roleName, string taskName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveTaskFromRoleAsync(StorePath, _application!.Name, roleName, taskName);
    }

    #endregion

    #region Public Methods - Update

    /// <summary>
    /// Update operation properties
    /// </summary>
    public async Task UpdateOperationAsync(string operationName, string description, string applicationData, int? operationId = null)
    {
        if (!IsInitialized) return;
        await _azManService.UpdateOperationAsync(StorePath, _application!.Name, operationName, description, applicationData, operationId);
    }

    /// <summary>
    /// Add an application group as member link.
    /// </summary>
    public async Task AddAppMemberToGroupAsync(string groupName, string appGroupName)
    {
        if (!IsInitialized) return;
        await _azManService.AddAppMemberToGroupAsync(StorePath, _application!.Name, groupName, appGroupName);
    }

    /// <summary>
    /// Remove an application group member link.
    /// </summary>
    public async Task RemoveAppMemberFromGroupAsync(string groupName, string appGroupName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveAppMemberFromGroupAsync(StorePath, _application!.Name, groupName, appGroupName);
    }

    /// <summary>
    /// Add an application group as non-member link.
    /// </summary>
    public async Task AddAppNonMemberToGroupAsync(string groupName, string appGroupName)
    {
        if (!IsInitialized) return;
        await _azManService.AddAppNonMemberToGroupAsync(StorePath, _application!.Name, groupName, appGroupName);
    }

    /// <summary>
    /// Remove an application group non-member link.
    /// </summary>
    public async Task RemoveAppNonMemberFromGroupAsync(string groupName, string appGroupName)
    {
        if (!IsInitialized) return;
        await _azManService.RemoveAppNonMemberFromGroupAsync(StorePath, _application!.Name, groupName, appGroupName);
    }

    /// <summary>
    /// Set business rule script on an application group.
    /// </summary>
    public async Task SetGroupBizRuleAsync(string groupName, string bizRule, string bizRuleLanguage)
    {
        if (!IsInitialized) return;
        await _azManService.SetAppGroupBizRuleAsync(StorePath, _application!.Name, groupName, bizRule, bizRuleLanguage);
    }

    /// <summary>
    /// Update role definition properties.
    /// </summary>
    public async Task UpdateRoleDefinitionAsync(string roleDefinitionName, string description)
    {
        if (!IsInitialized) return;
        await _azManService.UpdateRoleDefinitionAsync(StorePath, _application!.Name, roleDefinitionName, description);
    }

    /// <summary>
    /// Update task properties.
    /// </summary>
    public async Task UpdateTaskAsync(string taskName, string description)
    {
        if (!IsInitialized) return;
        await _azManService.UpdateTaskAsync(StorePath, _application!.Name, taskName, description);
    }

    /// <summary>
    /// Set business rule on a role definition.
    /// </summary>
    public async Task SetRoleDefinitionBizRuleAsync(string roleDefinitionName, string bizRule, string bizRuleLanguage)
    {
        if (!IsInitialized) return;
        if (string.IsNullOrWhiteSpace(bizRule))
        {
            await _azManService.ClearRoleDefinitionBizRuleAsync(StorePath, _application!.Name, roleDefinitionName);
            return;
        }

        await _azManService.SetRoleDefinitionBizRuleAsync(StorePath, _application!.Name, roleDefinitionName, bizRule, bizRuleLanguage);
    }

    /// <summary>
    /// Set business rule on a task.
    /// </summary>
    public async Task SetTaskBizRuleAsync(string taskName, string bizRule, string bizRuleLanguage)
    {
        if (!IsInitialized) return;
        if (string.IsNullOrWhiteSpace(bizRule))
        {
            await _azManService.ClearTaskBizRuleAsync(StorePath, _application!.Name, taskName);
            return;
        }

        await _azManService.SetTaskBizRuleAsync(StorePath, _application!.Name, taskName, bizRule, bizRuleLanguage);
    }

    /// <summary>
    /// Import business rule script for task.
    /// </summary>
    public async Task ImportTaskBizRuleAsync(string taskName, string scriptPath, string bizRuleLanguage)
    {
        if (!IsInitialized) return;
        await _azManService.ImportTaskBizRuleAsync(StorePath, _application!.Name, taskName, scriptPath, bizRuleLanguage);
    }

    /// <summary>
    /// Import business rule script for role definition.
    /// </summary>
    public async Task ImportRoleDefinitionBizRuleAsync(string roleDefinitionName, string scriptPath, string bizRuleLanguage)
    {
        if (!IsInitialized) return;
        await _azManService.ImportRoleDefinitionBizRuleAsync(StorePath, _application!.Name, roleDefinitionName, scriptPath, bizRuleLanguage);
    }

    /// <summary>
    /// Update application properties
    /// </summary>
    public async Task UpdateApplicationAsync(string description, string applicationData)
    {
        if (!IsInitialized) return;
        await _azManService.UpdateApplicationAsync(StorePath, _application!.Name, description, applicationData);
        await LoadAsync(StorePath, _application.Name);
    }

    /// <summary>
    /// Update scope properties
    /// </summary>
    public async Task UpdateScopeAsync(string name, string description)
    {
        if (!IsInitialized) return;
        await _azManService.UpdateScopeAsync(StorePath, _application!.Name, name, description);
    }

    #endregion

    #region Private Methods - Filtering

    private void ApplyAllFilters()
    {
        ApplyGroupFilter();
        ApplyRoleAssignmentFilter();
        ApplyRoleDefinitionFilter();
        ApplyTaskFilter();
        ApplyOperationFilter();
        ApplyScopeFilter();
    }

    private void ApplyGroupFilter()
    {
        FilteredGroups.ApplyFilter(
            Groups,
            GroupSearchText,
            (g, search) => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          g.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(GroupCountText));
    }

    private void ApplyRoleAssignmentFilter()
    {
        FilteredRoleAssignments.ApplyFilter(
            RoleAssignments,
            RoleAssignmentSearchText,
            (r, search) => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          r.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(RoleAssignmentCountText));
    }

    private void ApplyRoleDefinitionFilter()
    {
        FilteredRoleDefinitions.ApplyFilter(
            RoleDefinitions,
            RoleDefinitionSearchText,
            (r, search) => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          r.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(RoleDefinitionCountText));
    }

    private void ApplyTaskFilter()
    {
        FilteredTasks.ApplyFilter(
            Tasks,
            TaskSearchText,
            (t, search) => t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          t.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(TaskCountText));
    }

    private void ApplyOperationFilter()
    {
        FilteredOperations.ApplyFilter(
            Operations,
            OperationSearchText,
            (o, search) => o.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          o.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(OperationCountText));
    }

    private void ApplyScopeFilter()
    {
        FilteredScopes.ApplyFilter(
            Scopes,
            ScopeSearchText,
            (s, search) => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                          s.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(ScopeCountText));
    }

    #endregion
}

#endregion



