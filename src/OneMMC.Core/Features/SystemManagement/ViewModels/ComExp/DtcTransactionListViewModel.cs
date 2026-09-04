using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using OneMMC.Core.Infrastructure.Collections;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

/// <summary>
/// ViewModel for the DTC Transaction List page (Component Services \ Distributed Transaction
/// Coordinator \ Local DTC \ Transaction List).
/// </summary>
/// <remarks>
/// MSDTC publishes no change notification for its transaction table, so the page re-reads the list on a
/// timer for as long as it is loaded — live updating is not an option the user turns on, it is simply
/// how this page refreshes. Each read applies only the differences to <see cref="Transactions"/>: a
/// transaction that appeared is inserted, one that resolved is removed, and one whose state changed is
/// replaced. Rows that did not change keep their instance, so the list view keeps its containers,
/// selection, and scroll position instead of rebuilding on every tick. The page therefore changes only
/// when the DTC transaction table changed, which is how the comexp.msc Transaction List view behaves.
/// </remarks>
public partial class DtcTransactionListViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DtcTransactionListViewModel> _logger;
    private bool _isPolling;

    /// <summary>Gets the transactions currently tracked by the local DTC.</summary>
    public ObservableCollection<DtcTransactionItem> Transactions { get; } = new();

    /// <summary>Gets or sets whether an explicit load is in flight.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Gets or sets the localized status shown at the bottom of the page.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public DtcTransactionListViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DtcTransactionListViewModel>.Instance)
    {
    }

    public DtcTransactionListViewModel(ComponentServicesManager service, ILogger<DtcTransactionListViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Loads the transaction list, showing the loading indicator. Used for the page's initial load and
    /// for an explicit refresh.
    /// </summary>
    [RelayCommand]
    public Task LoadTransactionsAsync() => RefreshAsync(showLoading: true);

    /// <summary>
    /// Re-reads the transaction list on each live update tick and applies only the differences, without
    /// flashing the loading indicator. Skipped while a tick is still running or while an explicit load
    /// is in flight.
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
            // entry every interval for a list that usually has not changed.
            _logger.LogInformation("Loading DTC transaction list");
            IsLoading = true;
            StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.DtcLoadingTransactions);
        }

        try
        {
            IReadOnlyList<DtcTransactionItem> items = await _service.GetDtcTransactionListAsync();

            // Merge in place instead of clearing and refilling, so unchanged rows are never re-rendered
            // and the user keeps their selection and scroll position across ticks.
            Transactions.Reconcile(items, TransactionIdentityEquals, TransactionValueEquals);

            StatusMessage = L.GetFormattedString(
                ResourceFileNames.ComExp,
                ComExpKeys.DtcActiveTransactionsCount,
                Transactions.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadFailed, ex.Message);
            _logger.LogError(ex, "Failed to load DTC transaction list");
        }
        finally
        {
            if (showLoading)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Two rows describe the same transaction when their unit-of-work identifiers match.</summary>
    private static bool TransactionIdentityEquals(DtcTransactionItem a, DtcTransactionItem b) =>
        string.Equals(a.UnitOfWorkId, b.UnitOfWorkId, StringComparison.OrdinalIgnoreCase);

    /// <summary>A row only needs replacing when the displayed transaction state changed.</summary>
    private static bool TransactionValueEquals(DtcTransactionItem a, DtcTransactionItem b) =>
        string.Equals(a.Status, b.Status, StringComparison.Ordinal);
}
