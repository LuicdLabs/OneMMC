using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

/// <summary>
/// ViewModel for the DTC Transaction Statistics page (Component Services \ Distributed Transaction
/// Coordinator \ Local DTC \ Transaction Statistics).
/// </summary>
/// <remarks>
/// MSDTC publishes no change notification for its counters, so the page re-reads the snapshot on a
/// timer for as long as it is loaded — live updating is not an option the user turns on, it is simply
/// how this page refreshes. Each counter is exposed as its own observable property and assigned from
/// the new snapshot; the generated setters compare before raising a notification, so a poll that
/// returns identical numbers updates nothing and re-renders nothing. Only counters that actually moved
/// change on screen, which is how the comexp.msc Transaction Statistics view behaves.
/// </remarks>
public partial class DtcStatisticsViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DtcStatisticsViewModel> _logger;
    private bool _isPolling;

    /// <summary>Gets or sets the number of transactions currently open.</summary>
    [ObservableProperty]
    public partial uint Open { get; set; }

    /// <summary>Gets or sets the highest number of concurrently open transactions seen so far.</summary>
    [ObservableProperty]
    public partial uint OpenMax { get; set; }

    /// <summary>Gets or sets the number of transactions whose outcome is currently unknown.</summary>
    [ObservableProperty]
    public partial uint InDoubt { get; set; }

    /// <summary>Gets or sets the aggregate number of committed transactions.</summary>
    [ObservableProperty]
    public partial uint Committed { get; set; }

    /// <summary>Gets or sets the aggregate number of aborted transactions.</summary>
    [ObservableProperty]
    public partial uint Aborted { get; set; }

    /// <summary>Gets or sets the aggregate number of transactions forced to commit.</summary>
    [ObservableProperty]
    public partial uint ForcedCommit { get; set; }

    /// <summary>Gets or sets the aggregate number of transactions forced to abort.</summary>
    [ObservableProperty]
    public partial uint ForcedAbort { get; set; }

    /// <summary>Gets or sets the aggregate number of heuristically resolved transactions.</summary>
    [ObservableProperty]
    public partial uint Heuristic { get; set; }

    /// <summary>Gets or sets the aggregate total of all resolved transactions.</summary>
    [ObservableProperty]
    public partial uint Total { get; set; }

    /// <summary>Gets or sets the minimum transaction response time, in milliseconds.</summary>
    [ObservableProperty]
    public partial uint ResponseTimeMin { get; set; }

    /// <summary>Gets or sets the average transaction response time, in milliseconds.</summary>
    [ObservableProperty]
    public partial uint ResponseTimeAverage { get; set; }

    /// <summary>Gets or sets the maximum transaction response time, in milliseconds.</summary>
    [ObservableProperty]
    public partial uint ResponseTimeMax { get; set; }

    /// <summary>Gets or sets whether an explicit load is in flight.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Gets or sets the localized status shown at the bottom of the page.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public DtcStatisticsViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DtcStatisticsViewModel>.Instance)
    {
    }

    public DtcStatisticsViewModel(ComponentServicesManager service, ILogger<DtcStatisticsViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Loads the statistics, showing the loading indicator. Used for the page's initial load and for an
    /// explicit refresh.
    /// </summary>
    [RelayCommand]
    public Task LoadStatisticsAsync() => RefreshAsync(showLoading: true);

    /// <summary>
    /// Re-reads the statistics on each live update tick and publishes only the counters that changed,
    /// without flashing the loading indicator. Skipped while a tick is still running or while an
    /// explicit load is in flight.
    /// </summary>
    public async Task OnLiveUpdateTickAsync()
    {
        if (_isPolling || IsLoading)
        {
            return;
        }

        _isPolling = true;
        try
        {
            await RefreshAsync(showLoading: false);
        }
        finally
        {
            _isPolling = false;
        }
    }

    private async Task RefreshAsync(bool showLoading)
    {
        var L = LocalizationProvider.Current;

        if (showLoading)
        {
            // Quiet ticks skip the log as well as the indicator; otherwise the page would write an
            // entry every interval for state that usually has not changed.
            _logger.LogInformation("Loading DTC statistics");
            IsLoading = true;
            StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.LoadingStatistics);
        }

        try
        {
            DtcTransactionsStatistics? statistics = await _service.GetDtcTransactionsStatisticsAsync();

            if (statistics is null)
            {
                // Keep the last known counters rather than zeroing them: an unreadable snapshot is not
                // the same as MSDTC reporting zero, and blanking the page on a transient WMI failure
                // would be more misleading than leaving the previous values with an explanatory status.
                StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.DtcStatisticsUnavailable);
                return;
            }

            Apply(statistics);
            StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.LoadedSuccess);
        }
        catch (Exception ex)
        {
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadFailed, ex.Message);
            _logger.LogError(ex, "Failed to load DTC statistics");
        }
        finally
        {
            if (showLoading)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// Publishes a snapshot to the bound properties. Every assignment is a no-op when the value is
    /// unchanged, so a poll that returns identical counters raises no change notification at all and
    /// the page is left untouched.
    /// </summary>
    /// <param name="statistics">The snapshot just read from MSDTC.</param>
    private void Apply(DtcTransactionsStatistics statistics)
    {
        Open = statistics.Open;
        OpenMax = statistics.OpenMax;
        InDoubt = statistics.InDoubt;

        Committed = statistics.Committed;
        Aborted = statistics.Aborted;
        ForcedCommit = statistics.ForcedCommit;
        ForcedAbort = statistics.ForcedAbort;
        Heuristic = statistics.Heuristic;
        Total = statistics.Total;

        ResponseTimeMin = statistics.ResponseTimeMin;
        ResponseTimeAverage = statistics.ResponseTimeAverage;
        ResponseTimeMax = statistics.ResponseTimeMax;
    }
}
