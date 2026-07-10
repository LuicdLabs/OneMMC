// ============================================================================
// Performance Monitor Data Models
// ============================================================================
// File Description:
//   This file defines all data model classes required for performance monitor functionality, including:
//   - PerfMonColors: Chart color palette
//   - PerformanceCounterInfo: Performance counter information (core model)
//   - PerformanceCounterCategoryInfo: Counter category information
//   - CounterInfo: Basic counter information
//   - DataCollectorSetInfo: Data collector set information
//   - PerformanceReportInfo: Performance report information
//
// Architecture Location: Model Layer (Data layer in MVVM architecture)
// Dependencies: CommunityToolkit.Mvvm
// ============================================================================

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OneMMC.Core.Features.PCManagement.Models.PerfMon
{
    // ========================================================================
    // Chart Color Palette
    // ========================================================================
    /// <summary>
    /// Shared color palette for performance monitor charts.
    /// Provides predefined color arrays to ensure good visual distinction between multiple counter lines.
    /// </summary>
    /// <remarks>
    /// Color selection principles:
    /// 1. High contrast - Ensures clear visibility on both dark/light backgrounds
    /// 2. Color-blind friendly - Avoids relying solely on red-green distinction
    /// 3. Visual balance - Even color distribution, not overly harsh
    /// </remarks>
    public static class PerfMonColors
    {
        /// <summary>
        /// Predefined color palette (hexadecimal format).
        /// Contains 8 colors, will cycle when more than 8 counters are used.
        /// </summary>
        public static readonly string[] Palette = 
        { 
            "#F7B500",  // Golden yellow - Default preferred color, high visibility
            "#0F7B0F",  // Green - Commonly used to indicate normal status
            "#0078D4",  // Blue - Windows theme color
            "#E74856",  // Red - Commonly used for warnings or high load
            "#8764B8",  // Purple - Neutral tone
            "#00B7C3",  // Cyan - Fresh tone
            "#FF8C00",  // Orange - Warm tone
            "#107C10"   // Dark green - Alternative green
        };
        
        /// <summary>
        /// Get the corresponding color based on index.
        /// Uses modulo operation to ensure cycling when index exceeds range.
        /// </summary>
        /// <param name="index">Index position of the counter</param>
        /// <returns>Hexadecimal color string (e.g., "#F7B500")</returns>
        public static string GetColor(int index) => Palette[index % Palette.Length];
    }

    /// <summary>
    /// Represents one timestamped performance counter value.
    /// </summary>
    /// <param name="Timestamp">Time at which the value was collected.</param>
    /// <param name="Value">Collected and scaled counter value.</param>
    public readonly record struct PerformanceCounterSample(DateTime Timestamp, double Value);

    // ========================================================================
    // Performance Counter Information Model
    // ========================================================================
    /// <summary>
    /// Represents a complete information model for a single performance counter.
    /// This is the core data model for the performance monitor, containing counter identification, display settings, statistical data, etc.
    /// </summary>
    /// <remarks>
    /// Thread-safe design:
    /// - Uses lock to protect access to data point collections
    /// - Ensures UI property updates execute on main thread via DispatcherQueue
    /// - Sample history spans 100 one-second intervals and removes oldest data when exceeded (FIFO)
    /// 
    /// Usage example:
    /// <code>
    /// var counter = new PerformanceCounterInfo
    /// {
    ///     CategoryName = "Processor",
    ///     CounterName = "% Processor Time",
    ///     InstanceName = "_Total",
    ///     ColorHex = "#F7B500"
    /// };
    /// counter.AddDataPoint(45.5); // Add data point
    /// </code>
    /// </remarks>
    public partial class PerformanceCounterInfo : ObservableObject
    {
        /// <summary>
        /// Maximum samples retained for chart history.
        /// Includes both endpoints of the default 100-second graph window.
        /// </summary>
        public const int HistoryCapacity = 101;

        // --------------------------------------------------------------------
        // Private Fields
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Thread synchronization lock for protecting data point collections.
        /// Ensures data consistency in multi-threaded environments.
        /// </summary>
        private readonly object _lockObject = new();
        
        /// <summary>
        /// Internal queue storing historical samples.
        /// Maintains enough values to render both endpoints of the chart time window.
        /// </summary>
        private readonly Queue<PerformanceCounterSample> _dataPointsQueue = new(HistoryCapacity);
        
        /// <summary>
        /// UI dispatcher callback injected by the UI layer.
        /// Core stays framework-agnostic by storing only a delegate.
        /// </summary>
        private static Action<Action>? _uiDispatcher;

        // --------------------------------------------------------------------
        // Static Methods
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Initialize UI dispatch callback.
        /// Must be called by the UI project at application startup.
        /// </summary>
        /// <param name="uiDispatcher">A callback that schedules work on the UI thread.</param>
        public static void InitializeDispatcher(Action<Action> uiDispatcher) => _uiDispatcher = uiDispatcher;

        // --------------------------------------------------------------------
        // Counter Identification Properties
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Category name the counter belongs to (e.g., "Processor", "Memory").
        /// Corresponds to Windows performance counter Category.
        /// </summary>
        [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
        
        /// <summary>
        /// Counter name (e.g., "% Processor Time", "Available MBytes").
        /// Corresponds to Windows performance counter Counter Name.
        /// </summary>
        [ObservableProperty] public partial string CounterName { get; set; } = string.Empty;
        
        /// <summary>
        /// Instance name for multi-instance counters (e.g., "_Total", "0", "1").
        /// Empty string for single-instance counters.
        /// </summary>
        [ObservableProperty] public partial string InstanceName { get; set; } = string.Empty;
        
        /// <summary>
        /// Target computer name.
        /// "." indicates local machine, can also specify remote computer name for remote monitoring.
        /// </summary>
        [ObservableProperty] public partial string MachineName { get; set; } = ".";

        // --------------------------------------------------------------------
        // Display Settings Properties
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Value scaling ratio.
        /// Used to adjust counter value display range, e.g., scaling 0-1000 values to 0-100.
        /// Default value is 1.0 (no scaling).
        /// </summary>
        [ObservableProperty] public partial double Scale { get; set; } = 1.0;
        
        /// <summary>
        /// Hexadecimal color code for chart line (e.g., "#F7B500").
        /// Used to distinguish different counters in charts.
        /// </summary>
        [ObservableProperty] public partial string ColorHex { get; set; } = "#F7B500";
        
        /// <summary>
        /// Whether to display this counter in the chart.
        /// When set to false, counter still collects data but doesn't draw lines.
        /// </summary>
        [ObservableProperty] public partial bool IsVisible { get; set; } = true;
        
        /// <summary>
        /// Chart line width (pixels).
        /// Default value is 1, can be set between 1-5.
        /// </summary>
        [ObservableProperty] public partial int Width { get; set; } = 1;
        
        /// <summary>
        /// Line style.
        /// 0: Solid
        /// 1: Dash
        /// 2: Dot
        /// </summary>
        [ObservableProperty] public partial int LineStyle { get; set; } // 0: Solid, 1: Dash, 2: Dot

        // --------------------------------------------------------------------
        // Statistics Properties
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Latest counter value.
        /// Updated each time AddDataPoint is called.
        /// </summary>
        [ObservableProperty] public partial double LastValue { get; set; }
        
        /// <summary>
        /// Average value of all data points.
        /// Calculated based on all retained values in the sample queue.
        /// </summary>
        [ObservableProperty] public partial double AverageValue { get; set; }
        
        /// <summary>
        /// Minimum value among all data points.
        /// Initial value is double.MaxValue to ensure first data point sets correctly.
        /// </summary>
        [ObservableProperty] public partial double MinimumValue { get; set; } = double.MaxValue;
        
        /// <summary>
        /// Maximum value among all data points.
        /// Initial value is double.MinValue to ensure first data point sets correctly.
        /// </summary>
        [ObservableProperty] public partial double MaximumValue { get; set; } = double.MinValue;

        // --------------------------------------------------------------------
        // Read-only Properties
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Get read-only copy of data points collection.
        /// Uses lock to ensure thread safety, returns array copy to prevent external modification.
        /// </summary>
        public IReadOnlyList<double> DataPoints
        {
            get
            {
                lock (_lockObject)
                {
                    var values = new double[_dataPointsQueue.Count];
                    var index = 0;
                    foreach (var sample in _dataPointsQueue)
                    {
                        values[index++] = sample.Value;
                    }

                    return values;
                }
            }
        }

        /// <summary>
        /// Gets a timestamped snapshot of retained samples for time-based chart rendering.
        /// </summary>
        public IReadOnlyList<PerformanceCounterSample> Samples
        {
            get { lock (_lockObject) { return _dataPointsQueue.ToArray(); } }
        }

        /// <summary>
        /// Get counter display name.
        /// Format: "CounterName (InstanceName)" if instance exists, otherwise just "CounterName".
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(InstanceName) 
            ? CounterName 
            : $"{CounterName} ({InstanceName})";

        /// <summary>
        /// Get counter full path.
        /// Format: \CategoryName\CounterName or \CategoryName(InstanceName)\CounterName
        /// Follows Windows performance counter standard path format.
        /// </summary>
        public string FullPath => string.IsNullOrEmpty(InstanceName)
            ? $"\\{CategoryName}\\{CounterName}"
            : $"\\{CategoryName}({InstanceName})\\{CounterName}";

        /// <summary>
        /// Get counter detailed description information.
        /// Includes scaling ratio, instance, category, target computer, etc.
        /// </summary>
        public string Description => $"Scale: {Scale:F1} | Instance: {(string.IsNullOrEmpty(InstanceName) ? "---" : InstanceName)} | Parent: --- | Object: {CategoryName} | Target PC: \\\\{(MachineName == "." ? Environment.MachineName : MachineName)}";

        // --------------------------------------------------------------------
        // Public Methods
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Add a data point and update statistical information.
        /// This method is thread-safe and can be called from any thread.
        /// </summary>
        /// <param name="value">Value to add (scaling already applied)</param>
        /// <remarks>
        /// Processing flow:
        /// 1. Add a timestamped value within the lock block and maintain the history limit
        /// 2. Calculate statistical values (minimum, maximum, average)
        /// 3. Update observable properties via UI thread
        /// </remarks>
        public void AddDataPoint(double value)
        {
            double last, avg, min = double.MaxValue, max = double.MinValue;

            // Execute data operations within lock block to ensure thread safety
            lock (_lockObject)
            {
                _dataPointsQueue.Enqueue(new PerformanceCounterSample(DateTime.Now, value));

                while (_dataPointsQueue.Count > HistoryCapacity)
                    _dataPointsQueue.Dequeue();

                last = value;
                double sum = 0;
                foreach (var sample in _dataPointsQueue)
                {
                    if (sample.Value < min) min = sample.Value;
                    if (sample.Value > max) max = sample.Value;
                    sum += sample.Value;
                }
                avg = _dataPointsQueue.Count > 0 ? sum / _dataPointsQueue.Count : 0;
            }

            // Update observable properties on UI thread
            RunOnUIThread(() =>
            {
                LastValue = last;
                MinimumValue = min == double.MaxValue ? MinimumValue : min;
                MaximumValue = max == double.MinValue ? MaximumValue : max;
                AverageValue = avg;
            });
        }

        /// <summary>
        /// Reset all statistical data and data points.
        /// Used to clear historical data and restart monitoring.
        /// </summary>
        public void ResetStatistics()
        {
            // Clear data points collection
            lock (_lockObject) { _dataPointsQueue.Clear(); }
            
            // Reset statistical properties to initial values
            RunOnUIThread(() =>
            {
                MinimumValue = double.MaxValue;
                MaximumValue = double.MinValue;
                AverageValue = 0;
                LastValue = 0;
            });
        }

        // --------------------------------------------------------------------
        // Private Methods
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Execute specified action on UI thread.
        /// The UI layer provides the dispatch strategy through InitializeDispatcher.
        /// </summary>
        /// <param name="action">Action to execute</param>
        private static void RunOnUIThread(Action action)
        {
            if (_uiDispatcher is null)
            {
                action();
                return;
            }

            _uiDispatcher(action);
        }
    }

    // ========================================================================
    // Performance Counter Category Information
    // ========================================================================
    /// <summary>
    /// Represents performance counter category information.
    /// Corresponds to Windows performance counter Category (e.g., Processor, Memory, PhysicalDisk).
    /// </summary>
    public class PerformanceCounterCategoryInfo
    {
        /// <summary>
        /// Category name (e.g., "Processor", "Memory").
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Category description text.
        /// Empty with the PDH backend - PDH object enumeration exposes no help text
        /// (the System.Diagnostics CategoryHelp had no PDH equivalent).
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Target computer name.
        /// "." indicates local machine.
        /// </summary>
        public string MachineName { get; set; } = ".";

        /// <summary>
        /// Whether it's a multi-instance category.
        /// Not pre-populated by the PDH backend (probing every category upfront would be
        /// costly); instance presence is determined per selection via
        /// PerformanceMonitorService.GetInstancesAsync, which returns an empty list for
        /// single-instance categories.
        /// </summary>
        public bool IsMultiInstance { get; set; }

        /// <summary>
        /// Returns <see cref="Name"/> so list controls can display the category without a
        /// reflection-based DisplayMemberPath (AOT/trimming compatibility).
        /// </summary>
        public override string ToString() => Name;
    }

    // ========================================================================
    // Counter Basic Information
    // ========================================================================
    /// <summary>
    /// Represents basic information for a single counter within a category.
    /// Used to display available counter list in add counter dialog.
    /// </summary>
    public class CounterInfo
    {
        /// <summary>
        /// Counter name (e.g., "% Processor Time").
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Counter description text.
        /// From Windows performance counter CounterHelp.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Returns <see cref="Name"/> so list controls can display the counter without a
        /// reflection-based DisplayMemberPath (AOT/trimming compatibility).
        /// </summary>
        public override string ToString() => Name;
    }

    // ========================================================================
    // Data Collector Set Information
    // ========================================================================
    /// <summary>
    /// Represents Windows Data Collector Set information.
    /// Data Collector Sets are used to schedule performance data collection and save as log files.
    /// </summary>
    /// <remarks>
    /// Data Collector Set functionality is implemented via Windows PLA (Performance Logs and Alerts) COM API.
    /// Administrator privileges are required to create, start, or delete Data Collector Sets.
    /// </remarks>
    public partial class DataCollectorSetInfo : ObservableObject
    {
        /// <summary>
        /// Data Collector Set name.
        /// Used to identify and manage Data Collector Sets.
        /// </summary>
        [ObservableProperty] public partial string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Current status.
        /// Possible values: Stopped, Running, Compiling, Pending, Unknown
        /// </summary>
        [ObservableProperty] public partial string Status { get; set; } = "Stopped";
        
        /// <summary>
        /// Output file storage location.
        /// Default is %systemdrive%\PerfLogs\Admin\{Name}
        /// </summary>
        [ObservableProperty] public partial string OutputLocation { get; set; } = string.Empty;
        
        /// <summary>
        /// Last run time.
        /// null indicates never executed.
        /// </summary>
        [ObservableProperty] public partial DateTime? LastRunTime { get; set; }
        
        /// <summary>
        /// Whether it's a user-defined Data Collector Set.
        /// true: User created
        /// false: System built-in
        /// </summary>
        [ObservableProperty] public partial bool IsUserDefined { get; set; }
        
        /// <summary>
        /// Data Collector Set description text.
        /// </summary>
        [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    }

    // ========================================================================
    // Performance Report Information
    // ========================================================================
    /// <summary>
    /// Represents performance monitor generated report file information.
    /// Report files are typically in .blg (binary log) or .csv format.
    /// </summary>
    public partial class PerformanceReportInfo : ObservableObject
    {
        /// <summary>
        /// Report name (typically filename without extension).
        /// </summary>
        [ObservableProperty] public partial string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Full path of the report file.
        /// </summary>
        [ObservableProperty] public partial string Path { get; set; } = string.Empty;
        
        /// <summary>
        /// Report creation date and time.
        /// </summary>
        [ObservableProperty] public partial DateTime CreatedDate { get; set; }
        
        /// <summary>
        /// Report file size (bytes).
        /// </summary>
        [ObservableProperty] public partial long SizeBytes { get; set; }
    }
}
