// ============================================================================
// Performance Monitor Page
// ============================================================================
// File Description:
//   This page is the main UI for the performance monitor, responsible for:
//   - Real-time chart rendering (using Canvas + Polyline)
//   - User interaction handling (button clicks, menu operations)
//   - Dialog management (add counter, property settings)
//   - File operations (save/load configuration)
//
// Architecture Position: View Layer (View layer in MVVM architecture)
// Chart Technology: Canvas + Polyline for real-time line charts
// File Dialogs: Supports WinRT FilePicker and Win32 dialogs (fallback)
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PCManagement.Models.PerfMon;
using ManagementTools.Core.Features.PCManagement.ViewModels.PerfMon;
using ManagementTools.Localization;
using Microsoft.UI;
using WinRT.Interop;

using ManagementTools.Helpers;

namespace ManagementTools.Views.PerfMon;

/// <summary>
/// Performance monitor page, providing real-time performance monitoring UI interface.
/// </summary>
/// <remarks>
/// Main features:
/// 1. Real-time charts - Use Canvas and Polyline to draw line charts of counter values
/// 2. Counter management - Add, remove, configure counters
/// 3. Data collector sets - Create, start, stop, delete
/// 4. Configuration management - Save and load counter settings
/// </remarks>
public sealed partial class PerformanceMonitorPage : Page
{
    // ========================================================================
    // Public Properties
    // ========================================================================
    
    /// <summary>Performance monitor ViewModel</summary>
    public PerformanceMonitorViewModel ViewModel { get; }
    
    /// <summary>Localized string resources</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    // ========================================================================
    // Private Fields
    // ========================================================================
    
    /// <summary>Chart update timer, updates chart once per second</summary>
    private readonly System.Timers.Timer _chartUpdateTimer;
    
    /// <summary>Mapping table of counters to their corresponding Polylines</summary>
    private readonly Dictionary<PerformanceCounterInfo, Polyline> _counterLines = new();

    /// <summary>Retained portion of each counter line after the circular write cursor wraps.</summary>
    private readonly Dictionary<PerformanceCounterInfo, Polyline> _counterRetainedLines = new();
    
    /// <summary>Chart start time, used to calculate time axis</summary>
    private DateTime _chartStartTime;

    /// <summary>Whether the chart viewport is anchored to collected sample data.</summary>
    private bool _hasChartSamples;
    
    /// <summary>Native Performance Monitor's default graph range.</summary>
    private const double ChartMaximumValue = 100;
    
    /// <summary>Chart time window, default 100 seconds</summary>
    private TimeSpan _chartWindow = TimeSpan.FromSeconds(100);

    // ========================================================================
    // Constructor
    // ========================================================================
    
    /// <summary>
    /// Initialize the performance monitor page.
    /// </summary>
    /// <remarks>
    /// Initialization process:
    /// 1. Create ViewModel
    /// 2. Initialize XAML components
    /// 3. Set data context
    /// 4. Create and start chart update timer
    /// 5. Register page load/unload events
    /// </remarks>
    public PerformanceMonitorPage()
    {
        ViewModel = App.GetRequiredService<PerformanceMonitorViewModel>();
        
        // Initialize XAML components
        InitializeComponent();
        DataContext = ViewModel;
        _chartStartTime = DateTime.Now;

        // Create chart update timer (update every second)
        _chartUpdateTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _chartUpdateTimer.Elapsed += (_, _) => DispatcherQueue.TryEnqueue(() => { try { UpdateChart(); } catch { } });
        _chartUpdateTimer.Start();

        // Initialize ViewModel when page loads
        Loaded += async (_, _) => { await ViewModel.InitializeAsync(); UpdateChart(); };
        
        // Clean up resources when page unloads
        Unloaded += (_, _) =>
        {
            _chartUpdateTimer.Stop();
            _chartUpdateTimer.Dispose();
            ViewModel.Dispose();
        };
    }


    // ========================================================================
    // Chart Rendering
    // ========================================================================
    #region Chart Rendering

    /// <summary>
    /// When chart Canvas size changes, redraw grid lines and chart.
    /// </summary>
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var (w, h) = (ChartCanvas.ActualWidth, ChartCanvas.ActualHeight);
        if (w <= 0 || h <= 0) return;

        // Update horizontal grid line positions (20%, 50%, 80%)
        double[] yPercents = { 0.2, 0.5, 0.8 };
        Line[] gridLines = { GridLine1, GridLine2, GridLine3 };
        for (int i = 0; i < gridLines.Length; i++)
        {
            gridLines[i].X1 = 0; gridLines[i].Y1 = yPercents[i] * h;
            gridLines[i].X2 = w; gridLines[i].Y2 = yPercents[i] * h;
        }
        UpdateChart();
    }

    /// <summary>
    /// Update chart, redraw all counter lines.
    /// </summary>
    /// <remarks>
    /// Drawing process:
    /// 1. Remove lines for counters that no longer exist
    /// 2. Create/update Polyline for each visible counter
    /// 3. Calculate data point coordinates and set to Polyline
    /// 4. Update Y-axis labels and time window
    /// </remarks>
    private void UpdateChart()
    {
        var (w, h) = (ChartCanvas.ActualWidth, ChartCanvas.ActualHeight);
        if (w <= 0 || h <= 0) return;

        var counters = ViewModel.Counters.ToList();
        var visible = counters.Where(c => c.IsVisible).ToList();
        var samplesByCounter = counters.ToDictionary(counter => counter, counter => counter.Samples);
        DateTime? earliestSampleTime = null;
        DateTime? latestSampleTime = null;

        foreach (var samples in samplesByCounter.Values)
        {
            foreach (var sample in samples)
            {
                if (earliestSampleTime is null || sample.Timestamp < earliestSampleTime.Value)
                {
                    earliestSampleTime = sample.Timestamp;
                }

                if (latestSampleTime is null || sample.Timestamp > latestSampleTime.Value)
                {
                    latestSampleTime = sample.Timestamp;
                }
            }
        }

        UpdateTimeWindow(earliestSampleTime, latestSampleTime);
        UpdateTimeMarker(latestSampleTime, w, h);

        // Remove lines for counters that no longer exist
        foreach (var c in _counterLines.Keys.Where(c => !counters.Contains(c)).ToList())
        {
            RemoveCounterLines(c);
        }

        // Update/create lines for each visible counter
        foreach (var counter in visible)
        {
            // If line doesn't exist, create new Polyline
            if (!_counterLines.TryGetValue(counter, out var polyline))
            {
                polyline = CreateCounterLine();
                _counterLines[counter] = polyline;
                ChartLinesCanvas.Children.Add(polyline);

                var retainedPolyline = CreateCounterLine();
                _counterRetainedLines[counter] = retainedPolyline;
                ChartLinesCanvas.Children.Add(retainedPolyline);
            }

            var retainedLine = _counterRetainedLines[counter];

            // Set line color
            try
            {
                var brush = new SolidColorBrush(ParseColor(counter.ColorHex));
                polyline.Stroke = brush;
                retainedLine.Stroke = brush;
            }
            catch
            {
                var brush = new SolidColorBrush(Colors.Yellow);
                polyline.Stroke = brush;
                retainedLine.Stroke = brush;
            }

            // Native Performance Monitor overwrites a fixed graph from left to right.
            // Keep a break at the write cursor so retained and newly written values are not joined.
            var currentPoints = new PointCollection();
            var retainedPoints = new PointCollection();
            var activePoints = currentPoints;
            double? previousX = null;
            foreach (var sample in samplesByCounter[counter])
            {
                double x = GetCircularChartX(sample.Timestamp, w);
                if (previousX is not null && x < previousX.Value)
                {
                    retainedPoints = currentPoints;
                    currentPoints = new PointCollection();
                    activePoints = currentPoints;
                }

                double y = (1 - NormalizeValue(sample.Value)) * h * 0.9 + h * 0.05;
                activePoints.Add(new Point(x, y));
                previousX = x;
            }

            // Sampling jitter can keep overwritten retained points in the history for one refresh.
            while (retainedPoints.Count > 0 && previousX is not null && retainedPoints[0].X <= previousX.Value)
            {
                retainedPoints.RemoveAt(0);
            }

            polyline.Points = currentPoints;
            retainedLine.Points = retainedPoints;
        }

        // Hide lines for invisible counters
        foreach (var c in counters.Where(c => !c.IsVisible && _counterLines.ContainsKey(c)))
        {
            RemoveCounterLines(c);
        }

        // Keep the default graph scale aligned with native Performance Monitor.
        UpdateYAxis();
    }

    /// <summary>
    /// Create a line segment used to render counter history.
    /// </summary>
    private static Polyline CreateCounterLine() => new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };

    /// <summary>
    /// Remove all rendered segments belonging to a counter.
    /// </summary>
    /// <param name="counter">Counter whose rendered segments should be removed.</param>
    private void RemoveCounterLines(PerformanceCounterInfo counter)
    {
        if (_counterLines.Remove(counter, out var line))
        {
            ChartLinesCanvas.Children.Remove(line);
        }

        if (_counterRetainedLines.Remove(counter, out var retainedLine))
        {
            ChartLinesCanvas.Children.Remove(retainedLine);
        }
    }

    /// <summary>
    /// Calculate the fixed-position circular X coordinate used by native Performance Monitor.
    /// </summary>
    /// <param name="timestamp">Timestamp of the collected sample.</param>
    /// <param name="width">Available chart width.</param>
    /// <returns>Horizontal chart coordinate.</returns>
    private double GetCircularChartX(DateTime timestamp, double width)
    {
        double windowMilliseconds = Math.Max(1, _chartWindow.TotalMilliseconds);
        double elapsedMilliseconds = Math.Max(0, (timestamp - _chartStartTime).TotalMilliseconds);

        if (elapsedMilliseconds <= windowMilliseconds)
        {
            return Math.Clamp(width * elapsedMilliseconds / windowMilliseconds, 0, width);
        }

        double circularMilliseconds = elapsedMilliseconds % windowMilliseconds;
        return Math.Clamp(width * circularMilliseconds / windowMilliseconds, 0, width);
    }

    /// <summary>
    /// Normalize value to 0-1 range.
    /// </summary>
    /// <param name="value">Original value</param>
    /// <returns>Normalized value (0-1)</returns>
    private static double NormalizeValue(double value) => Math.Clamp(value / ChartMaximumValue, 0, 1);

    /// <summary>
    /// Update default fixed Y-axis labels.
    /// </summary>
    private void UpdateYAxis()
    {
        YAxisTopLabel.Text = FormatAxisValue(ChartMaximumValue);
        YAxisMidLabel.Text = FormatAxisValue(ChartMaximumValue * 0.5);
        YAxisBottomLabel.Text = "0";
    }

    /// <summary>
    /// Update time window.
    /// </summary>
    /// <param name="earliestSampleTime">Earliest retained sample timestamp.</param>
    /// <param name="latestSampleTime">Latest retained sample timestamp.</param>
    private void UpdateTimeWindow(DateTime? earliestSampleTime, DateTime? latestSampleTime)
    {
        _chartWindow = TimeSpan.FromMilliseconds(
            (PerformanceCounterInfo.HistoryCapacity - 1) * Math.Max(1, ViewModel.UpdateInterval));

        if (latestSampleTime is null)
        {
            if (_hasChartSamples)
            {
                ResetChartViewport();
            }

            UpdateTimeLabels();
            return;
        }

        if (!_hasChartSamples)
        {
            _chartStartTime = earliestSampleTime ?? latestSampleTime.Value;
            _hasChartSamples = true;
        }

        UpdateTimeLabels(latestSampleTime);
    }

    /// <summary>
    /// Move the graph write-position marker with the latest sample.
    /// </summary>
    /// <param name="latestSampleTime">Latest retained sample timestamp.</param>
    /// <param name="width">Available chart width.</param>
    /// <param name="height">Available chart height.</param>
    private void UpdateTimeMarker(DateTime? latestSampleTime, double width, double height)
    {
        if (ViewModel.CounterCount == 0)
        {
            VerticalMarker.Visibility = Visibility.Collapsed;
            return;
        }

        double x = GetCircularChartX(latestSampleTime ?? _chartStartTime, width);

        VerticalMarker.X1 = x;
        VerticalMarker.X2 = x;
        VerticalMarker.Y1 = 0;
        VerticalMarker.Y2 = height;
        VerticalMarker.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Reset the visible timeline so new samples start at the left edge.
    /// </summary>
    private void ResetChartViewport()
    {
        _chartStartTime = DateTime.Now;
        _hasChartSamples = false;
    }

    /// <summary>
    /// Update time axis labels.
    /// </summary>
    private void UpdateTimeLabels(DateTime? latestSampleTime = null)
    {
        TimeLabel1.Text = GetCircularTickTime(0, latestSampleTime).ToString("HH:mm:ss");
        TimeLabel2.Text = GetCircularTickTime(0.5, latestSampleTime).ToString("HH:mm:ss");
        TimeLabel3.Text = GetCircularTickTime(1, latestSampleTime).ToString("HH:mm:ss");
    }

    /// <summary>
    /// Resolve the timestamp currently displayed at a fixed circular graph tick.
    /// </summary>
    /// <param name="position">Position in the graph window, from 0 to 1.</param>
    /// <param name="latestSampleTime">Latest retained sample timestamp.</param>
    /// <returns>Time displayed at the requested fixed graph tick.</returns>
    private DateTime GetCircularTickTime(double position, DateTime? latestSampleTime)
    {
        var initialTickTime = _chartStartTime.AddMilliseconds(_chartWindow.TotalMilliseconds * position);
        if (latestSampleTime is null || latestSampleTime.Value < initialTickTime)
        {
            return initialTickTime;
        }

        double elapsedMilliseconds = (latestSampleTime.Value - initialTickTime).TotalMilliseconds;
        double completedWindows = Math.Floor(elapsedMilliseconds / _chartWindow.TotalMilliseconds);
        return initialTickTime.AddMilliseconds(completedWindows * _chartWindow.TotalMilliseconds);
    }

    /// <summary>
    /// Format axis label value.
    /// </summary>
    /// <param name="v">Value</param>
    /// <returns>Formatted string</returns>
    private static string FormatAxisValue(double v) => v >= 10 ? v.ToString("0") : v.ToString("0.00");
    
    /// <summary>
    /// Parse hexadecimal color string.
    /// </summary>
    /// <param name="hex">Hexadecimal color string (e.g. "#F7B500")</param>
    /// <returns>Windows.UI.Color object</returns>
    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length == 6 ? Windows.UI.Color.FromArgb(255,
            byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber)) : Colors.Yellow;
    }

    #endregion

    // ========================================================================
    // Event Handlers - Toolbar Buttons
    // ========================================================================
    #region Event Handlers

    /// <summary>Pause/Resume button click</summary>
    private void PauseButton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleMonitoring();
    
    /// <summary>Refresh button click</summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();
    
    /// <summary>Clear data button click</summary>
    private void ClearButton_Click(object sender, RoutedEventArgs e) { ViewModel.ClearData(); ResetChartViewport(); UpdateChart(); }
    
    /// <summary>Switch to graph view</summary>
    private void GraphViewMenuItem_Click(object sender, RoutedEventArgs e) => ViewModel.SwitchToGraphViewCommand.Execute(null);
    
    /// <summary>Switch to histogram view</summary>
    private void HistogramViewMenuItem_Click(object sender, RoutedEventArgs e) => ViewModel.SwitchToHistogramViewCommand.Execute(null);
    
    /// <summary>Switch to report view</summary>
    private void ReportViewMenuItem_Click(object sender, RoutedEventArgs e) => ViewModel.SwitchToReportViewCommand.Execute(null);

    /// <summary>
    /// Update interval menu item click.
    /// </summary>
    private void UpdateIntervalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string tag } &&
            int.TryParse(tag, out int interval) &&
            interval != ViewModel.UpdateInterval)
        {
            ViewModel.UpdateInterval = interval;
            _chartUpdateTimer.Interval = interval;
            ViewModel.ClearData();
            ResetChartViewport();
            UpdateChart();
        }
    }

    /// <summary>Add counter button click</summary>
    private async void AddCounterButton_Click(object sender, RoutedEventArgs e) => await ShowAddCounterDialogAsync();

    /// <summary>Remove all counters</summary>
    private void RemoveAllCountersMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in ViewModel.Counters.ToList()) ViewModel.RemoveCounterCommand.Execute(c);
    }

    /// <summary>Show all counters</summary>
    private void ShowAllCountersMenuItem_Click(object sender, RoutedEventArgs e) { foreach (var c in ViewModel.Counters) c.IsVisible = true; UpdateChart(); }

    /// <summary>Hide all counters</summary>
    private void HideAllCountersMenuItem_Click(object sender, RoutedEventArgs e) { foreach (var c in ViewModel.Counters) c.IsVisible = false; UpdateChart(); }

    /// <summary>
    /// Counter options button click, show operation menu.
    /// </summary>
    private void CounterOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PerformanceCounterInfo counter } button) return;
        
        // Create flyout menu
        var flyout = new MenuFlyout();
        
        flyout.Items.Add(CreateMenuItem(LocalizedStrings.PerfMon_Properties, Symbol.Setting, () => ShowCounterPropertiesDialog(counter)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem(LocalizedStrings.Common_RemoveButton, Symbol.Delete, () => ViewModel.RemoveCounterCommand.Execute(counter)));
        flyout.ShowAt(button);
    }

    /// <summary>
    /// Quick counter button click, add predefined counter.
    /// </summary>
    private async void QuickCounterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PerformanceCounterInfo c })
        {
            var counter = new PerformanceCounterInfo { CategoryName = c.CategoryName, CounterName = c.CounterName, InstanceName = c.InstanceName, ColorHex = c.ColorHex, Scale = c.Scale };
            if (!ViewModel.TryAddCounter(counter))
            {
                await ShowCounterUnavailableDialogAsync(new[] { counter.DisplayName });
            }
        }
    }

    #endregion

    // ========================================================================
    // Dialogs
    // ========================================================================
    #region Dialogs

    /// <summary>
    /// Show add counter dialog.
    /// </summary>
    /// <remarks>
    /// Dialog contains:
    /// 1. Category dropdown menu
    /// 2. Counter list (supports multi-selection)
    /// 3. Instance dropdown menu (multi-instance categories)
    /// </remarks>
    private async Task ShowAddCounterDialogAsync()
    {
        // Load category list
        await ViewModel.LoadCategoriesAsync();

        // Create UI components
        var categoryCombo = new ComboBox { ItemsSource = ViewModel.Categories, DisplayMemberPath = "Name", PlaceholderText = LocalizedStrings.PerfMon_SelectCategory, Width = 400, Margin = new Thickness(0, 0, 0, 16) };
        var counterList = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 300, Width = 400 };
        var instancePanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0), Visibility = Visibility.Collapsed };
        var instanceCombo = new ComboBox { PlaceholderText = LocalizedStrings.PerfMon_SelectInstance, Width = 400 };
        instancePanel.Children.Add(new TextBlock { Text = LocalizedStrings.PerfMon_InstanceLabel, Margin = new Thickness(0, 0, 0, 8) });
        instancePanel.Children.Add(instanceCombo);

        // Create layout
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = LocalizedStrings.PerfMon_SelectCategoryLabel, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(label, 0); Grid.SetRow(categoryCombo, 1); Grid.SetRow(counterList, 2); Grid.SetRow(instancePanel, 3);
        content.Children.Add(label); content.Children.Add(categoryCombo); content.Children.Add(counterList); content.Children.Add(instancePanel);

        // Load counters when category selection changes
        categoryCombo.SelectionChanged += async (_, _) =>
        {
            if (categoryCombo.SelectedItem is not PerformanceCounterCategoryInfo cat) return;
            ViewModel.SelectedCategory = cat;
            await ViewModel.LoadCountersForCategoryAsync();
            counterList.ItemsSource = ViewModel.AvailableCounters;
            counterList.DisplayMemberPath = "Name";
            instanceCombo.ItemsSource = ViewModel.Instances;
            instancePanel.Visibility = ViewModel.Instances.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ViewModel.Instances.Count > 0) instanceCombo.SelectedIndex = 0;
        };

        // Show dialog and handle result
        if (await ShowDialogAsync(LocalizedStrings.PerfMon_AddCountersTitle, content, LocalizedStrings.Common_AddButton) == ContentDialogResult.Primary)
        {
            var instance = instanceCombo.SelectedItem as string ?? "";
            var unavailableCounters = new List<string>();
            foreach (var ci in counterList.SelectedItems.Cast<CounterInfo>())
            {
                var counter = new PerformanceCounterInfo { CategoryName = ViewModel.SelectedCategory?.Name ?? "", CounterName = ci.Name, InstanceName = instance, ColorHex = PerfMonColors.GetColor(ViewModel.Counters.Count), Scale = 1.0 };
                if (!ViewModel.TryAddCounter(counter))
                {
                    unavailableCounters.Add(counter.DisplayName);
                }
            }

            if (unavailableCounters.Count > 0)
            {
                await ShowCounterUnavailableDialogAsync(unavailableCounters);
            }
        }
    }

    /// <summary>
    /// Show counter properties dialog.
    /// </summary>
    /// <param name="counter">Counter to edit</param>
    private async void ShowCounterPropertiesDialog(PerformanceCounterInfo counter)
    {
        // Create property editing controls
        var colorPicker = new ColorPicker { Color = ParseColor(counter.ColorHex), IsColorSliderVisible = true, IsColorChannelTextInputVisible = false, IsHexInputVisible = true, IsAlphaEnabled = false };

        // Create layout
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(CreateLabeledPanel(LocalizedStrings.PerfMon_CounterLabel, new TextBlock { Text = counter.FullPath, Foreground = new SolidColorBrush(Colors.Gray) }));
        content.Children.Add(CreateLabeledPanel(LocalizedStrings.PerfMon_ColorLabel, colorPicker));

        // Show dialog and apply changes
        if (await ShowDialogAsync(LocalizedStrings.PerfMon_CounterPropertiesTitle, new ScrollViewer { Content = content }, LocalizedStrings.Common_OKButton) == ContentDialogResult.Primary)
        {
            counter.ColorHex = $"#{colorPicker.Color.R:X2}{colorPicker.Color.G:X2}{colorPicker.Color.B:X2}";
            UpdateChart();
        }
    }

    /// <summary>
    /// Explains why one or more selected counters could not be sampled.
    /// </summary>
    /// <param name="counterNames">Display names of counters that failed to open.</param>
    private async Task ShowCounterUnavailableDialogAsync(IEnumerable<string> counterNames)
    {
        var names = string.Join(Environment.NewLine, counterNames);
        var message = string.Format(LocalizedStrings.PerfMon_CounterUnavailableMessage, names);

        await new ContentDialog
        {
            Title = LocalizedStrings.PerfMon_CounterUnavailableTitle,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    /// <summary>
    /// Show generic dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="content">Dialog content</param>
    /// <param name="primaryButton">Primary button text</param>
    /// <returns>Dialog result</returns>
    private async Task<ContentDialogResult> ShowDialogAsync(string title, object content, string primaryButton)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync();
    }

    /// <summary>
    /// Show confirmation dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Confirmation message</param>
    /// <returns>true if user confirms</returns>
    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        return await new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        }.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Create labeled panel.
    /// </summary>
    /// <param name="label">Label text</param>
    /// <param name="content">Content element</param>
    /// <returns>StackPanel containing label and content</returns>
    private static StackPanel CreateLabeledPanel(string label, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(content);
        return panel;
    }

    /// <summary>
    /// Create menu item (using Symbol icon).
    /// </summary>
    private static MenuFlyoutItem CreateMenuItem(string text, Symbol icon, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Create menu item (using font icon).
    /// </summary>
    private static MenuFlyoutItem CreateMenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Try to open path using File Explorer.
    /// </summary>
    /// <param name="path">Path to open</param>
    private static void TryOpenPath(string path) { try { System.Diagnostics.Process.Start("explorer.exe", path); } catch { } }

    #endregion

    // ========================================================================
    // File Operations
    // ========================================================================
    #region File Operations

    /// <summary>Save configuration menu item click</summary>
    private async void SaveConfigurationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var path = await SaveFileAsync($"{LocalizedStrings.PerfMon_PmcfgConfigurationFilter}\0{LocalizedStrings.PerfMon_PmcfgExtension}\0All Files\0*.*\0", LocalizedStrings.PerfMon_PmcfgExtension, LocalizedStrings.PerfMon_ConfigurationSaved);
        if (!string.IsNullOrEmpty(path)) { await ViewModel.SaveConfigurationCommand.ExecuteAsync(path); ViewModel.StatusMessage = LocalizedStrings.PerfMon_ConfigurationSaved; }
    }

    /// <summary>Load configuration menu item click</summary>
    private async void LoadConfigurationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var path = await OpenFileAsync($"{LocalizedStrings.PerfMon_PmcfgFilesFilter}\0{LocalizedStrings.PerfMon_PmcfgExtension}\0All Files\0*.*\0", LocalizedStrings.PerfMon_ConfigurationLoaded);
        if (!string.IsNullOrEmpty(path)) { await ViewModel.LoadConfigurationCommand.ExecuteAsync(path); ViewModel.StatusMessage = LocalizedStrings.PerfMon_ConfigurationLoaded; UpdateChart(); }
    }

/// <summary>
    /// Show save file dialog using Windows App SDK picker.
    /// </summary>
    /// <param name="filter">File filter</param>
    /// <param name="defaultExt">Default extension</param>
    /// <param name="title">Dialog title</param>
    /// <returns>Selected file path, returns null when cancelled</returns>
    private async Task<string?> SaveFileAsync(string filter, string defaultExt, string? title = null)
    {
        if (App.MainWindowInstance == null) return null;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        return await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            filter,
            title,
            initialDirectory: null,
            defaultExtension: defaultExt,
            suggestedFileName: "PerformanceMonitor");
    }

    /// <summary>
    /// Show open file dialog using Windows App SDK picker.
    /// </summary>
    /// <param name="filter">File filter</param>
    /// <param name="title">Dialog title</param>
    /// <returns>Selected file path, returns null when cancelled</returns>
    private async Task<string?> OpenFileAsync(string filter, string? title = null)
    {
        if (App.MainWindowInstance == null) return null;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        return await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(hwnd, filter, title);
    }

    #endregion
}


