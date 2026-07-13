using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using OneMMC.Core.Features.PCManagement.Services.EventViewer;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.ViewModels.EventViewer;

/// <summary>
/// ViewModel for the Event Viewer page, orchestrating log tree navigation,
/// event loading with pagination, and filtering.
/// </summary>
public partial class EventViewerViewModel : ObservableObject, IDisposable
{
    private readonly EventViewerService _eventViewerService;
    private readonly ILogger<EventViewerViewModel> _logger;
    private readonly IAdminService _adminService;
    private readonly SynchronizationContext? _syncContext;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private CancellationTokenSource? _loadCts;
    private List<EventLogEntry> _allEvents = [];
    private int _queryGeneration;
    private bool _disposed;

    /// <summary>Number of events requested per read batch.</summary>
    private const int BatchSize = 200;

    /// <summary>
    /// Upper bound on events retained per query. Keeps memory bounded and stops the
    /// automatic filter scan from walking an arbitrarily large log to its end.
    /// </summary>
    private const int MaxLoadedEvents = 20_000;

    // ========================================================================
    // Observable Properties
    // ========================================================================

    /// <summary>Root nodes for the event log tree.</summary>
    [ObservableProperty]
    public partial ObservableCollection<EventLogTreeNode> RootNodes { get; set; } = [];

    /// <summary>Currently selected log name (null if no leaf node selected).</summary>
    [ObservableProperty]
    public partial string? SelectedLogName { get; set; }

    /// <summary>Currently selected tree node.</summary>
    [ObservableProperty]
    public partial EventLogTreeNode? SelectedNode { get; set; }

    /// <summary>
    /// Events visible in the list — the client-side filtered projection of everything
    /// loaded so far. The instance never changes: batches are appended in place so the
    /// ListView keeps its scroll position and selection, and the ListView pulls further
    /// batches itself through incremental loading whenever its viewport is unfilled.
    /// </summary>
    public IncrementalEventLogCollection Events { get; }

    /// <summary>Currently selected event for the details panel.</summary>
    [ObservableProperty]
    public partial EventLogEntry? SelectedEvent { get; set; }

    /// <summary>Text filter for searching events.</summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>Level filter: null = all levels, 1-5 = specific level.</summary>
    [ObservableProperty]
    public partial byte? SelectedLevelFilter { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>Whether more events can be loaded (pagination).</summary>
    [ObservableProperty]
    public partial bool CanLoadMore { get; set; }


    /// <summary>Log properties for the current log.</summary>
    [ObservableProperty]
    public partial EventLogInfo? CurrentLogInfo { get; set; }

    /// <summary>
    /// Event to notify the UI layer about permission errors.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    // ========================================================================
    // Constructors
    // ========================================================================

    public EventViewerViewModel(
        EventViewerService eventViewerService,
        ILogger<EventViewerViewModel> logger,
        IAdminService adminService)
    {
        _eventViewerService = eventViewerService;
        _logger = logger;
        _adminService = adminService;
        _syncContext = SynchronizationContext.Current;
        Events = new IncrementalEventLogCollection(HasMoreEvents, FetchNextBatchAsync);
    }

    // ========================================================================
    // Initialization
    // ========================================================================

    /// <summary>
    /// Builds the log tree on first load.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var roots = await _eventViewerService.BuildLogTreeAsync();
            RootNodes = new ObservableCollection<EventLogTreeNode>(roots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build event log tree.");
            StatusMessage = $"Error: {ex.Message}";
            if (_adminService.IsPermissionError(ex))
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ========================================================================
    // Log Selection
    // ========================================================================

    /// <summary>
    /// Called when user selects a tree node. If it's a leaf (actual log), loads events.
    /// </summary>
    public async Task SelectLogAsync(EventLogTreeNode node)
    {
        SelectedNode = node;
        if (!node.IsLog) return;

        SelectedLogName = node.LogName;
        await LoadEventsAsync();
    }

    // ========================================================================
    // Event Loading
    // ========================================================================

    /// <summary>
    /// Loads the first batch of events from the selected log. Subsequent batches are
    /// pulled automatically by the ListView through <see cref="FetchNextBatchAsync"/>.
    /// </summary>
    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;

        // Invalidate any in-flight incremental batch before taking over the query state.
        _queryGeneration++;
        var generation = _queryGeneration;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        StatusMessage = string.Empty;
        SelectedEvent = null;

        await _fetchGate.WaitAsync(CancellationToken.None);
        try
        {
            _allEvents = [];
            CanLoadMore = false;
            Events.ResetWith([]);

            var events = await _eventViewerService.ReadEventsAsync(SelectedLogName, BuildXPathQuery(), BatchSize, ct);
            if (generation != _queryGeneration) return;

            _allEvents = events;
            CanLoadMore = events.Count >= BatchSize;
            Events.ResetWith(events.Where(MatchesClientFilter));
            UpdateStatusMessage();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load events from {LogName}.", SelectedLogName);
            StatusMessage = $"Error: {ex.Message}";
            if (_adminService.IsPermissionError(ex))
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _fetchGate.Release();

            // Only the query that still owns the state may clear the loading flag;
            // a newer query started meanwhile has already set it for itself.
            if (generation == _queryGeneration)
            {
                IsLoading = false;
            }
        }
    }

    // ========================================================================
    // Incremental Loading
    // ========================================================================

    /// <summary>
    /// Whether the ListView may pull another batch: a log is selected and loaded, the
    /// last batch was full, and the retention cap has not been reached.
    /// </summary>
    private bool HasMoreEvents() =>
        !_disposed
        && !string.IsNullOrEmpty(SelectedLogName)
        && CanLoadMore
        && _allEvents.Count > 0
        && _allEvents.Count < MaxLoadedEvents;

    /// <summary>
    /// Reads the next batch behind the pagination cursor, retains it, and appends the
    /// entries matching the current client-side filter to <see cref="Events"/>.
    /// The ListView keeps calling this while its viewport is unfilled, which is what
    /// makes a text search continue into older events until enough matches are visible,
    /// the end of the log is reached, or <see cref="MaxLoadedEvents"/> is hit.
    /// </summary>
    /// <returns>The number of entries appended to the visible collection.</returns>
    private async Task<int> FetchNextBatchAsync(CancellationToken viewCt)
    {
        if (!HasMoreEvents()) return 0;

        try
        {
            await _fetchGate.WaitAsync(viewCt);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        try
        {
            var generation = _queryGeneration;
            var logName = SelectedLogName;
            if (logName is null || !HasMoreEvents()) return 0;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                viewCt, _loadCts?.Token ?? CancellationToken.None);

            // Keep reading until at least one entry becomes visible or the log/cap is
            // exhausted: the ListView stops re-requesting after a zero-progress result,
            // which would otherwise stall a text search whose matches lie further back.
            var appended = 0;
            do
            {
                var lastId = _allEvents[^1].RecordId;
                var batch = await _eventViewerService.ReadMoreEventsAsync(
                    logName, BuildXPathQuery(), lastId, BatchSize, linkedCts.Token);

                // The query changed while the batch was in flight — discard silently.
                if (generation != _queryGeneration) return 0;

                _allEvents.AddRange(batch);
                CanLoadMore = batch.Count >= BatchSize;

                var visible = batch.Where(MatchesClientFilter).ToList();
                Events.AppendRange(visible);
                appended += visible.Count;
                UpdateStatusMessage();
            }
            while (appended == 0 && HasMoreEvents());

            return appended;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more events from {LogName}.", SelectedLogName);
            StatusMessage = $"Error: {ex.Message}";

            // Stop further automatic pulls; otherwise the ListView would retry in a loop.
            CanLoadMore = false;
            return 0;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    // ========================================================================
    // Filtering
    // ========================================================================

    partial void OnFilterTextChanged(string value) => RebuildVisibleEvents();

    partial void OnSelectedLevelFilterChanged(byte? value)
    {
        // Level filtering happens server-side via XPath, so a fresh query is required.
        if (!string.IsNullOrEmpty(SelectedLogName))
        {
            _ = LoadEventsAsync();
        }
    }

    /// <summary>
    /// Rebuilds the visible collection from the retained events after a text-filter
    /// change. This resets the view to the top; when too few rows match to fill the
    /// viewport, the ListView automatically pulls older batches to continue the search.
    /// </summary>
    private void RebuildVisibleEvents()
    {
        Events.ResetWith(_allEvents.Where(MatchesClientFilter));
        UpdateStatusMessage();
    }

    /// <summary>
    /// Client-side filter for a single entry. The level check is normally redundant
    /// (levels are filtered server-side) but keeps retained batches consistent when
    /// the level filter changes while a batch is in flight.
    /// </summary>
    private bool MatchesClientFilter(EventLogEntry entry)
    {
        if (SelectedLevelFilter.HasValue && entry.Level != SelectedLevelFilter.Value)
            return false;

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        var text = FilterText;
        return (entry.Source?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || entry.EventId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
            || (entry.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.TaskCategory?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Updates the status bar with loaded/matching counts and, when applicable, the
    /// retention-cap notice.
    /// </summary>
    private void UpdateStatusMessage()
    {
        if (_allEvents.Count == 0)
        {
            StatusMessage = LocalizationProvider.Current.GetString(
                ResourceFileNames.EventViewer, EventViewerKeys.StatusNoEvents);
            return;
        }

        var status = string.IsNullOrWhiteSpace(FilterText)
            ? string.Format(
                LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.StatusLoadedFormat),
                _allEvents.Count)
            : string.Format(
                LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.StatusLoadedFilteredFormat),
                _allEvents.Count,
                Events.Count);

        if (_allEvents.Count >= MaxLoadedEvents)
        {
            var capNotice = LocalizationProvider.Current.GetString(
                ResourceFileNames.EventViewer, EventViewerKeys.StatusLoadLimitReached);
            status = $"{status} — {capNotice}";
        }

        StatusMessage = status;
    }

    private string BuildXPathQuery()
    {
        if (SelectedLevelFilter.HasValue)
        {
            return $"*[System[Level={SelectedLevelFilter.Value}]]";
        }
        return "*";
    }

    // ========================================================================
    // Actions
    // ========================================================================

    /// <summary>
    /// Refreshes the current log.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadEventsAsync();
    }

    /// <summary>
    /// Clears all events from the selected log.
    /// </summary>
    public async Task ClearLogAsync(string? backupPath = null)
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;

        try
        {
            await _eventViewerService.ClearLogAsync(SelectedLogName, backupPath);

            // Invalidate any in-flight incremental batch so it cannot repopulate the list.
            _queryGeneration++;
            _allEvents.Clear();
            Events.Clear();
            SelectedEvent = null;
            CanLoadMore = false;
            StatusMessage = "Log cleared";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear log {LogName}.", SelectedLogName);
            StatusMessage = $"Error clearing log: {ex.Message}";
            if (_adminService.IsPermissionError(ex))
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Export current log to .evtx file at specified path.
    /// </summary>
    public async Task ExportLogAsync(string targetPath)
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;

        IsLoading = true;
        StatusMessage = "Exporting...";
        try
        {
            await _eventViewerService.ExportLogAsync(SelectedLogName, targetPath);
            StatusMessage = "Export completed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export log {LogName}.", SelectedLogName);
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads log properties for the Log Properties dialog.
    /// </summary>
    [RelayCommand]
    public async Task LoadLogPropertiesAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;
        try
        {
            CurrentLogInfo = await _eventViewerService.GetLogInfoAsync(SelectedLogName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load log properties for {LogName}.", SelectedLogName);
        }
    }






    // ========================================================================
    // Dispose
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queryGeneration++;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _eventViewerService.Dispose();
        _allEvents.Clear();
        RootNodes.Clear();
        Events.Clear();
        SelectedEvent = null;
        SelectedNode = null;
        CurrentLogInfo = null;
        AdminPermissionRequired = null;
        GC.SuppressFinalize(this);
    }
}


