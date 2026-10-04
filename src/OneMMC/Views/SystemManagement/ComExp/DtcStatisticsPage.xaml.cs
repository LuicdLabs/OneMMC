using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace OneMMC.Views.ComExp;

public sealed partial class DtcStatisticsPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DtcStatisticsViewModel ViewModel { get; }

    /// <summary>
    /// Timer that keeps the displayed counters current. Live updating is not something the user turns
    /// on: MSDTC raises no change notification, so re-reading is simply part of how this page refreshes,
    /// and the timer runs for as long as the page is loaded.
    /// </summary>
    private DispatcherQueueTimer? _liveUpdateTimer;

    public DtcStatisticsPage()
    {
        ViewModel = App.GetRequiredService<DtcStatisticsViewModel>();
        InitializeComponent();
        Loaded += DtcStatisticsPage_Loaded;
        Unloaded += DtcStatisticsPage_Unloaded;
    }

    private async void DtcStatisticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcStatisticsPage] Loaded.");
        await ViewModel.LoadStatisticsAsync();
        StartLiveUpdates();
    }

    private void DtcStatisticsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopLiveUpdates();

        Loaded -= DtcStatisticsPage_Loaded;
        Unloaded -= DtcStatisticsPage_Unloaded;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcStatisticsPage] Refresh requested.");
        await ViewModel.LoadStatisticsAsync();
    }

    private void StartLiveUpdates()
    {
        _liveUpdateTimer ??= CreateLiveUpdateTimer();
        _liveUpdateTimer.Start();
    }

    private void StopLiveUpdates()
    {
        if (_liveUpdateTimer is null)
        {
            return;
        }

        _liveUpdateTimer.Stop();
        _liveUpdateTimer.Tick -= LiveUpdateTimer_Tick;
        _liveUpdateTimer = null;
    }

    /// <summary>
    /// Interval between reads. MSDTC raises no change notification for its counters, so each tick
    /// re-reads the statistics over WMI; the view model then publishes only the counters that actually
    /// moved, so a tick that reads identical numbers changes nothing on screen.
    /// </summary>
    private static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer CreateLiveUpdateTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = LiveUpdateInterval;
        timer.IsRepeating = true;
        timer.Tick += LiveUpdateTimer_Tick;
        return timer;
    }

    private async void LiveUpdateTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        await ViewModel.OnLiveUpdateTickAsync();
    }
}
