using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.PCManagement.Models.EventViewer;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Infrastructure.Admin;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PCManagement.ViewModels.EventViewer;

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
    private CancellationTokenSource? _loadCts;
    private List<EventLogEntry> _allEvents = [];
    private bool _disposed;

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

    /// <summary>Filtered events displayed in the list.</summary>
    [ObservableProperty]
    public partial ObservableCollection<EventLogEntry> Events { get; set; } = [];

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
    /// Loads the first batch of events from the selected log.
    /// </summary>
    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        StatusMessage = string.Empty;
        _allEvents.Clear();
        Events.Clear();
        SelectedEvent = null;

        try
        {
            // Use existing filter when loading events
            var xpathQuery = BuildXPathQuery();
            var events = await _eventViewerService.ReadEventsAsync(SelectedLogName, xpathQuery, 200, ct);
            _allEvents = events;
            CanLoadMore = events.Count >= 200;
            ApplyFilter();
            StatusMessage = events.Count > 0
                ? $"{_allEvents.Count} events loaded"
                : string.Empty;
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
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the next batch of events for pagination.
    /// </summary>
    [RelayCommand]
    public async Task LoadMoreEventsAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogName) || !CanLoadMore || _allEvents.Count == 0) return;

        IsLoading = true;
        try
        {
            var lastId = _allEvents[^1].RecordId;
            var xpathQuery = BuildXPathQuery();
            var more = await _eventViewerService.ReadMoreEventsAsync(SelectedLogName, xpathQuery, lastId, 200);
            _allEvents.AddRange(more);
            CanLoadMore = more.Count >= 200;
            ApplyFilter();
            StatusMessage = $"{_allEvents.Count} events loaded";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more events.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ========================================================================
    // Filtering
    // ========================================================================

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedLevelFilterChanged(byte? value)
    {
        // When the level filter changes, always reload from the server with XPath
        // to ensure we get fresh data and avoid empty list issues
        if (!string.IsNullOrEmpty(SelectedLogName))
        {
            _ = ReloadWithFilterAsync();
        }
    }

    private async Task ReloadWithFilterAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogName)) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        try
        {
            var xpathQuery = BuildXPathQuery();
            var events = await _eventViewerService.ReadEventsAsync(SelectedLogName, xpathQuery, 200, ct);
            _allEvents = events;
            CanLoadMore = events.Count >= 200;
            ApplyFilter();
            StatusMessage = $"{_allEvents.Count} events loaded";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload events with filter.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<EventLogEntry> filtered = _allEvents;

        // Level filter (client-side, for when all events are already loaded)
        if (SelectedLevelFilter.HasValue)
        {
            filtered = filtered.Where(e => e.Level == SelectedLevelFilter.Value);
        }

        // Text filter
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var text = FilterText;
            filtered = filtered.Where(e =>
                (e.Source?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                e.EventId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ||
                (e.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.TaskCategory?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Events = new ObservableCollection<EventLogEntry>(filtered);
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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _eventViewerService.Dispose();
        GC.SuppressFinalize(this);
    }
}


