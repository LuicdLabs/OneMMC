// ============================================================================
// Performance Monitor ViewModel
// ============================================================================
// File Description:
//   This ViewModel manages all business logic for the performance monitor, including:
//   - Counter management (add, remove, toggle visibility)
//   - Monitor control (start, pause, update interval)
//   - Statistics calculation (latest value, average, minimum, maximum)
//   - Configuration save/load
//
// Architecture Position: ViewModel Layer (View Model layer in MVVM architecture)
// Design Pattern: Uses CommunityToolkit.Mvvm's ObservableObject and RelayCommand
// Threading Model: Uses SynchronizationContext to ensure UI updates on main thread
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.PCManagement.Models.PerfMon;
using OneMMC.Core.Features.PCManagement.Services.PerfMon;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.ViewModels.PerfMon
{
    // ========================================================================
    // View Mode Enumeration
    // ========================================================================
    /// <summary>
    /// View modes for the performance monitor.
    /// </summary>
    public enum PerformanceViewMode 
    { 
        /// <summary>Real-time line chart mode</summary>
        Graph, 
        /// <summary>Bar chart mode</summary>
        Histogram, 
        /// <summary>Report mode</summary>
        Report 
    }

    // ========================================================================
    // Performance Monitor ViewModel Class
    // ========================================================================
    /// <summary>
    /// ViewModel for the performance monitor, managing all performance monitoring related business logic.
    /// </summary>
    /// <remarks>
    /// Main responsibilities:
    /// 1. Manage performance counter lifecycle
    /// 2. Control monitoring start/pause
    /// 3. Periodically update counter values
    /// 4. Calculate and update statistics
    /// 
    /// Threading model:
    /// - Timer triggers on background thread
    /// - Counter value reading executes in Task
    /// - UI property updates return to main thread via SynchronizationContext
    /// </remarks>
    public partial class PerformanceMonitorViewModel : ObservableObject, IDisposable
    {
        // ====================================================================
        // Private Fields
        // ====================================================================
        
        /// <summary>Performance monitor service instance, responsible for interacting with Windows performance counters</summary>
        private readonly PerformanceMonitorService _performanceService;
        private readonly ILogger<PerformanceMonitorViewModel> _logger;
        
        /// <summary>Timer for periodic updates, used to regularly read counter values</summary>
        private readonly System.Timers.Timer _updateTimer;
        
        /// <summary>UI thread synchronization context, used for cross-thread UI updates</summary>
        private readonly SynchronizationContext _uiContext;
        
        /// <summary>Monitoring start time, used to calculate duration</summary>
        private DateTime _monitoringStartTime;
        
        /// <summary>Flag indicating whether the ViewModel has been disposed</summary>
        private bool _disposed;

        // ====================================================================
        // Observable Properties - Collections
        // ====================================================================
        #region Observable Properties

        /// <summary>Collection of currently monitored counters</summary>
        [ObservableProperty] public partial ObservableCollection<PerformanceCounterInfo> Counters { get; set; } = new();
        
        /// <summary>All available performance counter categories</summary>
        [ObservableProperty] public partial ObservableCollection<PerformanceCounterCategoryInfo> Categories { get; set; } = new();
        
        /// <summary>Available counters in the selected category</summary>
        [ObservableProperty] public partial ObservableCollection<CounterInfo> AvailableCounters { get; set; } = new();
        
        /// <summary>Instance names of selected category (multi-instance categories)</summary>
        [ObservableProperty] public partial ObservableCollection<string> Instances { get; set; } = new();
        
        /// <summary>Counter search results</summary>
        [ObservableProperty] public partial ObservableCollection<PerformanceCounterInfo> SearchResults { get; set; } = new();
        
        /// <summary>Predefined common counter list</summary>
        [ObservableProperty] public partial ObservableCollection<PerformanceCounterInfo> CommonCounters { get; set; } = new();
        
        /// <summary>Filtered counter list (based on search text)</summary>
        [ObservableProperty] public partial ObservableCollection<PerformanceCounterInfo> FilteredCounters { get; set; } = new();

        // ====================================================================
        // Observable Properties - Selected Items
        // ====================================================================
        
        /// <summary>Currently selected counter category</summary>
        [ObservableProperty] public partial PerformanceCounterCategoryInfo? SelectedCategory { get; set; }
        
        /// <summary>Currently selected available counter</summary>
        [ObservableProperty] public partial CounterInfo? SelectedAvailableCounter { get; set; }
        
        /// <summary>Currently selected instance name</summary>
        [ObservableProperty] public partial string? SelectedInstance { get; set; }

        // ====================================================================
        // Observable Properties - State
        // ====================================================================
        
        /// <summary>Whether data is being loaded</summary>
        [ObservableProperty] public partial bool IsLoading { get; set; }
        
        /// <summary>Status message displayed in status bar</summary>
        [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
        
        /// <summary>Search text</summary>
        [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

        // ====================================================================
        // Observable Properties - Statistics
        // ====================================================================
        
        /// <summary>Latest value of selected counter</summary>
        [ObservableProperty] public partial double LastValue { get; set; }
        
        /// <summary>Average value of selected counter</summary>
        [ObservableProperty] public partial double AverageValue { get; set; }
        
        /// <summary>Minimum value of selected counter</summary>
        [ObservableProperty] public partial double MinimumValue { get; set; }
        
        /// <summary>Maximum value of selected counter</summary>
        [ObservableProperty] public partial double MaximumValue { get; set; }
        
        /// <summary>Monitoring duration (format: m:ss or h:mm:ss)</summary>
        [ObservableProperty] public partial string Duration { get; set; } = "0:00";
        
        /// <summary>Current view mode</summary>
        [ObservableProperty] public partial PerformanceViewMode CurrentViewMode { get; set; } = PerformanceViewMode.Graph;

        // ====================================================================
        // Manually Implemented Observable Properties
        // ====================================================================
        
        /// <summary>
        /// Currently selected counter (used to display statistics).
        /// Automatically updates statistics when changed.
        /// </summary>
        private PerformanceCounterInfo? selectedCounter;
        public PerformanceCounterInfo? SelectedCounter
        {
            get => selectedCounter;
            set { if (SetProperty(ref selectedCounter, value)) UpdateStatistics(); }
        }

        /// <summary>
        /// Whether monitoring is in progress.
        /// Automatically starts/stops timer when changed.
        /// </summary>
        private bool isMonitoring;
        public bool IsMonitoring
        {
            get => isMonitoring;
            set
            {
                if (SetProperty(ref isMonitoring, value))
                {
                    // Start or stop timer based on monitoring state
                    if (value) _updateTimer.Start(); else _updateTimer.Stop();
                    OnPropertyChanged(nameof(MonitoringStatus));
                }
            }
        }

        /// <summary>
        /// Update interval (milliseconds).
        /// Automatically adjusts timer interval when changed.
        /// </summary>
        private int updateInterval = 1000;
        public int UpdateInterval
        {
            get => updateInterval;
            set { if (SetProperty(ref updateInterval, value)) _updateTimer.Interval = value; }
        }

        // ====================================================================
        // Property Changed Handlers
        // ====================================================================
        
        /// <summary>When selected category changes, load counters for that category</summary>
        partial void OnSelectedCategoryChanged(PerformanceCounterCategoryInfo? value) => _ = LoadCountersForCategoryAsync();
        
        /// <summary>When selected available counter changes, update CanAddSelectedCounter</summary>
        partial void OnSelectedAvailableCounterChanged(CounterInfo? value) => OnPropertyChanged(nameof(CanAddSelectedCounter));
        
        /// <summary>When search text changes, perform filtering</summary>
        partial void OnSearchTextChanged(string value) => FilterCounters();
        
        /// <summary>When view mode changes, update related boolean properties</summary>
        partial void OnCurrentViewModeChanged(PerformanceViewMode value)
        {
            OnPropertyChanged(nameof(IsGraphView));
            OnPropertyChanged(nameof(IsHistogramView));
            OnPropertyChanged(nameof(IsReportView));
        }

        #endregion

        // ====================================================================
        // Computed Properties
        // ====================================================================
        #region Computed Properties

        /// <summary>Monitoring status text (Running or Paused)</summary>
        public string MonitoringStatus => IsMonitoring ? "Running" : "Paused";
        
        /// <summary>Total counter count</summary>
        public int CounterCount => Counters.Count;
        
        /// <summary>Visible counter count</summary>
        public int VisibleCounterCount => Counters.Count(c => c.IsVisible);
        
        /// <summary>Total data points count for all counters</summary>
        public int DataPointCount => Counters.Sum(c => c.DataPoints?.Count ?? 0);
        
        /// <summary>Whether the selected counter can be added</summary>
        public bool CanAddSelectedCounter => SelectedAvailableCounter != null && SelectedCategory != null;
        
        /// <summary>Whether in graph view mode</summary>
        public bool IsGraphView => CurrentViewMode == PerformanceViewMode.Graph;
        
        /// <summary>Whether in histogram view mode</summary>
        public bool IsHistogramView => CurrentViewMode == PerformanceViewMode.Histogram;
        
        /// <summary>Whether in report view mode</summary>
        public bool IsReportView => CurrentViewMode == PerformanceViewMode.Report;

        #endregion

        // ====================================================================
        // Constructor
        // ====================================================================
        
        /// <summary>
        /// Initialize a new instance of PerformanceMonitorViewModel.
        /// </summary>
        /// <remarks>
        /// Initialization process:
        /// 1. Create performance monitor service
        /// 2. Get UI thread synchronization context
        /// 3. Create and configure update timer
        /// 4. Load common counter list
        /// </remarks>
        public PerformanceMonitorViewModel(PerformanceMonitorService performanceService, ILogger<PerformanceMonitorViewModel> logger)
        {
            _performanceService = performanceService;
            _logger = logger;
            
            // Get UI thread synchronization context for cross-thread updates
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
            
            // Create update timer
            _updateTimer = new System.Timers.Timer(UpdateInterval) { AutoReset = true };
            _updateTimer.Elapsed += OnUpdateTimerElapsed;
            
            // Record monitoring start time
            _monitoringStartTime = DateTime.Now;
            
            // Load predefined common counters
            CommonCounters = new ObservableCollection<PerformanceCounterInfo>(_performanceService.GetCommonCounters());
            
            // Initialize filtered counter list
            FilteredCounters = new ObservableCollection<PerformanceCounterInfo>(Counters);
            
            // Subscribe to Counters collection change events to update filter results
            Counters.CollectionChanged += OnCountersCollectionChanged;
        }

        // ====================================================================
        // Commands - Initialization & Control
        // ====================================================================
        #region Commands

        /// <summary>
        /// Asynchronously initialize performance monitor.
        /// Load default counters, data collector sets and reports, then start monitoring.
        /// </summary>
        [RelayCommand]
        public async Task InitializeAsync()
        {
            IsLoading = true;
            StatusMessage = "Initializing Performance Monitor...";
            try
            {
                // Add default counters (CPU and Memory)
                await AddDefaultCountersAsync();
                // Reset monitoring start time
                _monitoringStartTime = DateTime.Now;
                // Start monitoring
                IsMonitoring = true;
                StatusMessage = $"Monitoring {CounterCount} counters";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error initializing: {ex.Message}";
                _logger.LogError(ex, "Failed to initialize performance monitor view model.");
            }
            finally { IsLoading = false; }
        }

        /// <summary>Toggle monitoring state (pause/resume)</summary>
        [RelayCommand] public void ToggleMonitoring() { IsMonitoring = !IsMonitoring; StatusMessage = IsMonitoring ? "Monitoring resumed" : "Monitoring paused"; }
        
        /// <summary>Manually refresh counter values</summary>
        [RelayCommand] public void Refresh() { _ = Task.Run(UpdateCounterValues); StatusMessage = "Data refreshed"; }
        
        /// <summary>Clear all counter history data</summary>
        [RelayCommand] public void ClearData() { foreach (var c in Counters) c.ResetStatistics(); _monitoringStartTime = DateTime.Now; UpdateStatistics(); StatusMessage = "Data cleared"; }
        
        /// <summary>Switch to graph view mode</summary>
        [RelayCommand] public void SwitchToGraphView() { CurrentViewMode = PerformanceViewMode.Graph; StatusMessage = "Switched to graph view"; }
        
        /// <summary>Switch to histogram view mode</summary>
        [RelayCommand] public void SwitchToHistogramView() { CurrentViewMode = PerformanceViewMode.Histogram; StatusMessage = "Switched to histogram view"; }
        
        /// <summary>Switch to report view mode</summary>
        [RelayCommand] public void SwitchToReportView() { CurrentViewMode = PerformanceViewMode.Report; StatusMessage = "Switched to report view"; }

        // ====================================================================
        // Commands - Category & Counter Loading
        // ====================================================================

        /// <summary>
        /// Asynchronously load all performance counter categories.
        /// </summary>
        [RelayCommand]
        public async Task LoadCategoriesAsync()
        {
            IsLoading = true;
            try { Categories = new ObservableCollection<PerformanceCounterCategoryInfo>(await _performanceService.GetCategoriesAsync()); }
            catch (Exception ex) { StatusMessage = $"Error loading categories: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        /// <summary>
        /// Asynchronously load counters and instances for selected category.
        /// </summary>
        [RelayCommand]
        public async Task LoadCountersForCategoryAsync()
        {
            if (SelectedCategory == null) return;
            IsLoading = true;
            try
            {
                // Load counter list
                AvailableCounters = new ObservableCollection<CounterInfo>(await _performanceService.GetCountersAsync(SelectedCategory.Name));
                // Load instance list; single-instance categories yield an empty list
                Instances = new ObservableCollection<string>(await _performanceService.GetInstancesAsync(SelectedCategory.Name));
            }
            catch (Exception ex) { StatusMessage = $"Error loading counters: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        // ====================================================================
        // Commands - Counter Management
        // ====================================================================

        /// <summary>
        /// Add currently selected counter to monitoring list.
        /// </summary>
        [RelayCommand]
        public void AddSelectedCounter()
        {
            if (SelectedCategory == null || SelectedAvailableCounter == null) return;
            TryAddCounter(new PerformanceCounterInfo
            {
                CategoryName = SelectedCategory.Name,
                CounterName = SelectedAvailableCounter.Name,
                InstanceName = SelectedInstance ?? string.Empty,
                ColorHex = PerfMonColors.GetColor(Counters.Count),
                Scale = 1.0
            });
        }

        /// <summary>
        /// Add counter to monitoring list.
        /// </summary>
        /// <param name="counter">Counter information to add</param>
        /// <remarks>
        /// Processing flow:
        /// 1. Check if same counter already exists
        /// 2. Create counter instance through service
        /// 3. Add counter to collection
        /// </remarks>
        [RelayCommand]
        public void AddCounter(PerformanceCounterInfo counter) => TryAddCounter(counter);

        /// <summary>
        /// Attempts to add a counter and reports whether Windows can open it for sampling.
        /// </summary>
        /// <param name="counter">Counter information to add.</param>
        /// <returns>
        /// <see langword="true"/> when the counter is already monitored or is added successfully;
        /// otherwise, <see langword="false"/> when Windows cannot create the counter.
        /// </returns>
        public bool TryAddCounter(PerformanceCounterInfo counter)
        {
            // Check if already exists
            if (Counters.Any(c => c.CategoryName == counter.CategoryName && c.CounterName == counter.CounterName && c.InstanceName == counter.InstanceName))
            { StatusMessage = "Counter already exists"; return true; }

            // Create counter instance
            if (!_performanceService.CreateCounter(counter))
            { StatusMessage = $"Failed to create counter: {counter.DisplayName}"; return false; }

            AddCounterToCollection(counter, $"Added counter: {counter.DisplayName}");
            return true;
        }

        /// <summary>
        /// Remove counter from monitoring list.
        /// </summary>
        /// <param name="counter">Counter to remove</param>
        [RelayCommand]
        public void RemoveCounter(PerformanceCounterInfo counter)
        {
            _performanceService.RemoveCounter(counter);
            Counters.Remove(counter);
            OnPropertyChanged(nameof(CounterCount));
            OnPropertyChanged(nameof(VisibleCounterCount));
            StatusMessage = $"Removed counter: {counter.DisplayName}";
        }

        /// <summary>
        /// Toggle counter visibility.
        /// </summary>
        /// <param name="counter">Counter to toggle</param>
        [RelayCommand]
        public void ToggleCounterVisibility(PerformanceCounterInfo counter)
        {
            counter.IsVisible = !counter.IsVisible;
            OnPropertyChanged(nameof(VisibleCounterCount));
        }

        /// <summary>
        /// Asynchronously search counters.
        /// </summary>
        /// <param name="searchTerm">Search keyword</param>
        [RelayCommand]
        public async Task SearchCountersAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) { SearchResults.Clear(); return; }
            IsLoading = true;
            try
            {
                var results = await _performanceService.SearchCountersAsync(searchTerm);
                SearchResults = new ObservableCollection<PerformanceCounterInfo>(results);
                StatusMessage = $"Found {results.Count} counters";
            }
            catch (Exception ex) { StatusMessage = $"Error searching: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        // ====================================================================
        // Commands - Configuration Management
        // ====================================================================

        /// <summary>
        /// Asynchronously save counter configuration to file.
        /// </summary>
        /// <param name="filePath">File path</param>
        [RelayCommand]
        public async Task SaveConfigurationAsync(string filePath)
        {
            IsLoading = true;
            StatusMessage = "Saving configuration...";
            try
            {
                var success = await _performanceService.SaveConfigurationAsync(filePath, Counters.ToList());
                StatusMessage = success ? $"Configuration saved to {filePath}" : "Failed to save configuration";
            }
            catch (Exception ex) { StatusMessage = $"Error saving configuration: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        /// <summary>
        /// Asynchronously load counter configuration from file.
        /// </summary>
        /// <param name="filePath">File path</param>
        [RelayCommand]
        public async Task LoadConfigurationAsync(string filePath)
        {
            IsLoading = true;
            StatusMessage = "Loading configuration...";
            try
            {
                var loaded = await _performanceService.LoadConfigurationAsync(filePath);
                if (loaded.Count > 0)
                {
                    // Clear existing counters
                    DisposeAllCounters();
                    Counters.Clear();
                    // Add loaded counters
                    foreach (var c in loaded) AddCounter(c);
                    StatusMessage = $"Loaded {loaded.Count} counters from configuration";
                }
                else StatusMessage = "Failed to load configuration or no counters found";
            }
            catch (Exception ex) { StatusMessage = $"Error loading configuration: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        #endregion

        // ====================================================================
        // Private Methods
        // ====================================================================
        #region Private Methods

        /// <summary>
        /// Add default counters (CPU and Memory usage).
        /// </summary>
        private async Task AddDefaultCountersAsync()
        {
            // CPU usage
            var counter = new PerformanceCounterInfo { CategoryName = "Processor", CounterName = "% Processor Time", InstanceName = "_Total", ColorHex = "#F7B500", Scale = 1.0 };
            if (Counters.Any(c => c.CategoryName == counter.CategoryName && c.CounterName == counter.CounterName && c.InstanceName == counter.InstanceName))
            {
                return;
            }
            var created = await Task.Run(() => _performanceService.CreateCounter(counter));
            if (!created)
            {
                StatusMessage = $"Failed to create counter: {counter.DisplayName}";
                return;
            }

            AddCounterToCollection(counter, $"Added counter: {counter.DisplayName}");
        }

        private void AddCounterToCollection(PerformanceCounterInfo counter, string statusMessage)
        {
            Counters.Add(counter);
            OnPropertyChanged(nameof(CounterCount));
            OnPropertyChanged(nameof(VisibleCounterCount));
            StatusMessage = statusMessage;
        }

        /// <summary>
        /// Timer elapsed event handler.
        /// Updates counter values and duration in background thread.
        /// </summary>
        private void OnUpdateTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    UpdateCounterValues();
                    UpdateDuration();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Performance monitor timer update failed.");
                }
            });
        }

        /// <summary>
        /// Update all counter values.
        /// </summary>
        /// <remarks>
        /// Processing flow:
        /// 1. Iterate through all counters
        /// 2. Read counter value and apply scaling
        /// 3. Add value to counter's data point collection
        /// 4. Update statistics
        /// </remarks>
        private void UpdateCounterValues()
        {
            foreach (var counter in Counters.ToList())
            {
                try
                {
                    // Read counter value and apply scaling
                    var value = _performanceService.ReadCounterValue(counter);
                    counter.AddDataPoint(value * counter.Scale);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update counter value for {CounterDisplayName}.", counter.DisplayName);
                }
            }
            UpdateStatistics();
        }

        /// <summary>
        /// Update statistics (latest value, average, minimum, maximum).
        /// </summary>
        /// <remarks>
        /// Statistics source:
        /// - Prioritize selected counter
        /// - If no selected counter, use first visible counter
        /// </remarks>
        private void UpdateStatistics()
        {
            // Get counter to display statistics for
            var active = SelectedCounter ?? Counters.FirstOrDefault(c => c.IsVisible);
            
            void Apply()
            {
                if (active != null)
                {
                    LastValue = active.LastValue;
                    AverageValue = active.AverageValue;
                    // Handle initial values (double.MaxValue/MinValue)
                    MinimumValue = active.MinimumValue == double.MaxValue ? 0 : active.MinimumValue;
                    MaximumValue = active.MaximumValue == double.MinValue ? 0 : active.MaximumValue;
                }
                else { LastValue = AverageValue = MinimumValue = MaximumValue = 0; }
                OnPropertyChanged(nameof(DataPointCount));
            }
            
            // Ensure update on UI thread
            if (SynchronizationContext.Current == _uiContext) Apply(); else _uiContext.Post(_ => Apply(), null);
        }

        /// <summary>
        /// Update monitoring duration.
        /// </summary>
        private void UpdateDuration()
        {
            var elapsed = DateTime.Now - _monitoringStartTime;
            // Choose format based on time length
            var newDuration = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
            // Ensure update on UI thread
            if (SynchronizationContext.Current == _uiContext) Duration = newDuration; else _uiContext.Post(_ => Duration = newDuration, null);
        }

        /// <summary>
        /// Filter counters (filter Counters collection based on search text).
        /// </summary>
        private void FilterCounters()
        {
            FilteredCounters.Clear();

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                foreach (var c in Counters)
                    FilteredCounters.Add(c);
            }
            else
            {
                foreach (var c in Counters)
                {
                    if (c.CounterName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        c.InstanceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        c.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        c.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredCounters.Add(c);
                    }
                }
            }

            OnPropertyChanged(nameof(CounterCount));
            OnPropertyChanged(nameof(VisibleCounterCount));
        }

        /// <summary>
        /// Dispose all counters.
        /// </summary>
        private void DisposeAllCounters() { foreach (var c in Counters.ToList()) _performanceService.RemoveCounter(c); }

        #endregion

        // ====================================================================
        // IDisposable Implementation
        // ====================================================================
        
        /// <summary>
        /// Dispose all resources used by the ViewModel.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _performanceService.DisposeAllCounters();
            Counters.CollectionChanged -= OnCountersCollectionChanged;
            Counters.Clear();
            Categories.Clear();
            AvailableCounters.Clear();
            Instances.Clear();
            SearchResults.Clear();
            CommonCounters.Clear();
            FilteredCounters.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void OnCountersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => FilterCounters();
    }
}


