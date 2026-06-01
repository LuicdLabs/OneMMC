using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.PCManagement.Models.FsMgmt;
using ManagementTools.Core.Features.PCManagement.Services.FsMgmt;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PCManagement.ViewModels.FsMgmt;

/// <summary>
/// View model for the Shared Folders management page.
/// </summary>
public sealed partial class SharedFoldersViewModel : ObservableObject
{
    private readonly SharedFoldersService _sharedFoldersService;
    private readonly IAdminService _adminService;
    private readonly ILogger<SharedFoldersViewModel> _logger;
    private readonly List<SharedFolderShare> _allShares = [];
    private readonly List<SharedFolderSession> _allSessions = [];
    private readonly List<SharedFolderOpenFile> _allOpenFiles = [];
    [ObservableProperty]
    private ObservableCollection<SharedFolderShare> _shares = [];

    [ObservableProperty]
    private ObservableCollection<SharedFolderSession> _sessions = [];

    [ObservableProperty]
    private ObservableCollection<SharedFolderOpenFile> _openFiles = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedFoldersViewModel"/> class.
    /// </summary>
    /// <param name="sharedFoldersService">Shared Folders service.</param>
    /// <param name="adminService">Administrator permission helper.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public SharedFoldersViewModel(
        SharedFoldersService sharedFoldersService,
        IAdminService adminService,
        ILogger<SharedFoldersViewModel> logger)
    {
        _sharedFoldersService = sharedFoldersService;
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// Raised when an operation requires administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>Gets the localized share section description.</summary>
    public string SharesDescription => string.Format(GetString(FsMgmtKeys.SharesCountFormat), Shares.Count);

    /// <summary>Gets the localized sessions section description.</summary>
    public string SessionsDescription => string.Format(GetString(FsMgmtKeys.SessionsCountFormat), Sessions.Count);

    /// <summary>Gets the localized open files section description.</summary>
    public string OpenFilesDescription => string.Format(GetString(FsMgmtKeys.OpenFilesCountFormat), OpenFiles.Count);

    /// <summary>Gets whether at least one share is displayed.</summary>
    public bool HasShares => Shares.Count > 0;

    /// <summary>Gets whether at least one session is displayed.</summary>
    public bool HasSessions => Sessions.Count > 0;

    /// <summary>Gets whether at least one open file is displayed.</summary>
    public bool HasOpenFiles => OpenFiles.Count > 0;

    /// <summary>
    /// Loads shares, sessions, and open files from the local Server service.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        _logger.LogInformation("Loading Shared Folders state.");

        try
        {
            IReadOnlyList<SharedFolderShare> shares = await _sharedFoldersService.GetSharesAsync();
            IReadOnlyList<SharedFolderSession> sessions = await LoadOptionalAsync(
                _sharedFoldersService.GetSessionsAsync,
                nameof(_sharedFoldersService.GetSessionsAsync));
            IReadOnlyList<SharedFolderOpenFile> openFiles = await LoadOptionalAsync(
                _sharedFoldersService.GetOpenFilesAsync,
                nameof(_sharedFoldersService.GetOpenFilesAsync));

            _allShares.Clear();
            _allShares.AddRange(shares);
            _allSessions.Clear();
            _allSessions.AddRange(sessions);
            _allOpenFiles.Clear();
            _allOpenFiles.AddRange(openFiles);

            ApplyFilter();
            _logger.LogInformation(
                "Loaded Shared Folders state. Shares={ShareCount}, Sessions={SessionCount}, OpenFiles={OpenFileCount}",
                Shares.Count,
                Sessions.Count,
                OpenFiles.Count);
        }
        catch (Exception ex)
        {
            HandleOperationException(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the page data.
    /// </summary>
    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    /// <summary>
    /// Creates a new share and refreshes the page.
    /// </summary>
    /// <param name="definition">The share definition.</param>
    public async Task CreateShareAsync(SharedFolderShareDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await RunMutatingOperationAsync(() => _sharedFoldersService.CreateShareAsync(definition));
    }

    /// <summary>
    /// Updates an existing share and refreshes the page.
    /// </summary>
    /// <param name="shareName">The share name.</param>
    /// <param name="definition">The updated definition.</param>
    public async Task UpdateShareAsync(string shareName, SharedFolderShareDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareName);
        ArgumentNullException.ThrowIfNull(definition);

        await RunMutatingOperationAsync(() => _sharedFoldersService.UpdateShareAsync(shareName, definition));
    }

    /// <summary>
    /// Deletes an existing share and refreshes the page.
    /// </summary>
    /// <param name="shareName">The share name.</param>
    public async Task DeleteShareAsync(string shareName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareName);

        await RunMutatingOperationAsync(() => _sharedFoldersService.DeleteShareAsync(shareName));
    }

    /// <summary>
    /// Disconnects one session and refreshes the page.
    /// </summary>
    /// <param name="session">The session to disconnect.</param>
    public async Task DisconnectSessionAsync(SharedFolderSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        await RunMutatingOperationAsync(() => _sharedFoldersService.DisconnectSessionAsync(session));
    }

    /// <summary>
    /// Disconnects all sessions and refreshes the page.
    /// </summary>
    public async Task DisconnectAllSessionsAsync()
    {
        await RunMutatingOperationAsync(_sharedFoldersService.DisconnectAllSessionsAsync);
    }

    /// <summary>
    /// Closes one open file and refreshes the page.
    /// </summary>
    /// <param name="openFile">The open file to close.</param>
    public async Task CloseOpenFileAsync(SharedFolderOpenFile openFile)
    {
        ArgumentNullException.ThrowIfNull(openFile);

        await RunMutatingOperationAsync(() => _sharedFoldersService.CloseOpenFileAsync(openFile.Id));
    }

    /// <summary>
    /// Closes all open files and refreshes the page.
    /// </summary>
    public async Task CloseAllOpenFilesAsync()
    {
        await RunMutatingOperationAsync(_sharedFoldersService.CloseAllOpenFilesAsync);
    }

    /// <summary>
    /// Clears cached page state.
    /// </summary>
    public void ClearCachedData()
    {
        _allShares.Clear();
        _allSessions.Clear();
        _allOpenFiles.Clear();
        Shares.Clear();
        Sessions.Clear();
        OpenFiles.Clear();
        FilterText = string.Empty;
        NotifySectionDescriptions();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private async Task RunMutatingOperationAsync(Func<Task> operation)
    {
        IsLoading = true;

        try
        {
            await operation();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            HandleOperationException(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        string filter = FilterText.Trim();

        Shares = new ObservableCollection<SharedFolderShare>(
            string.IsNullOrWhiteSpace(filter)
                ? _allShares
                : _allShares.Where(share =>
                    Contains(share.Name, filter)
                    || Contains(share.Path, filter)
                    || Contains(share.Description, filter)));

        Sessions = new ObservableCollection<SharedFolderSession>(
            string.IsNullOrWhiteSpace(filter)
                ? _allSessions
                : _allSessions.Where(session =>
                    Contains(session.ClientName, filter)
                    || Contains(session.UserName, filter)
                    || Contains(session.ClientType, filter)));

        OpenFiles = new ObservableCollection<SharedFolderOpenFile>(
            string.IsNullOrWhiteSpace(filter)
                ? _allOpenFiles
                : _allOpenFiles.Where(file =>
                    Contains(file.Path, filter)
                    || Contains(file.UserName, filter)
                    || file.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)));

        NotifySectionDescriptions();
    }

    private void NotifySectionDescriptions()
    {
        OnPropertyChanged(nameof(SharesDescription));
        OnPropertyChanged(nameof(SessionsDescription));
        OnPropertyChanged(nameof(OpenFilesDescription));
        OnPropertyChanged(nameof(HasShares));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasOpenFiles));
    }

    private void HandleOperationException(Exception ex)
    {
        _logger.LogError(ex, "Shared Folders operation failed.");

        if (IsPermissionError(ex))
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<IReadOnlyList<T>> LoadOptionalAsync<T>(Func<Task<IReadOnlyList<T>>> load, string operationName)
    {
        try
        {
            return await load();
        }
        catch (Exception ex) when (IsPermissionError(ex))
        {
            _logger.LogWarning(ex, "Shared Folders optional load failed due to insufficient permissions. Operation={Operation}", operationName);
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shared Folders optional load failed. Operation={Operation}", operationName);
            return [];
        }
    }

    private bool IsPermissionError(Exception ex)
    {
        if (_adminService.IsPermissionError(ex))
        {
            return true;
        }

        return ex is Win32Exception { NativeErrorCode: 5 };
    }

    private static bool Contains(string? value, string filter) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string GetString(string key) =>
        LocalizationProvider.Current.GetString(ResourceFileNames.FsMgmt, key);
}
