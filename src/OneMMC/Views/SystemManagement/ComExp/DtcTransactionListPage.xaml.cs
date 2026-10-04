using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace OneMMC.Views.ComExp;

public sealed partial class DtcTransactionListPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public DtcTransactionListViewModel ViewModel { get; }

    /// <summary>
    /// Timer that keeps the displayed transactions current. Live updating is not something the user
    /// turns on: MSDTC raises no change notification, so re-reading is simply part of how this page
    /// refreshes, and the timer runs for as long as the page is loaded.
    /// </summary>
    private DispatcherQueueTimer? _liveUpdateTimer;

    public DtcTransactionListPage()
    {
        ViewModel = App.GetRequiredService<DtcTransactionListViewModel>();
        InitializeComponent();
        Loaded += DtcTransactionListPage_Loaded;
        Unloaded += DtcTransactionListPage_Unloaded;
    }

    private async void DtcTransactionListPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcTransactionListPage] Loaded.");
        await ViewModel.LoadTransactionsAsync();
        StartLiveUpdates();
    }

    private void DtcTransactionListPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopLiveUpdates();

        Loaded -= DtcTransactionListPage_Loaded;
        Unloaded -= DtcTransactionListPage_Unloaded;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[DtcTransactionListPage] Refresh requested.");
        await ViewModel.LoadTransactionsAsync();
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
    /// Interval between reads. MSDTC raises no change notification for its transaction table, so each
    /// tick re-reads the list over WMI; the view model then applies only the differences, so a tick that
    /// reads an unchanged list leaves the displayed rows untouched.
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
