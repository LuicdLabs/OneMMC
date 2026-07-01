using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

public partial class DtcTransactionListViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DtcTransactionListViewModel> _logger;

    public ObservableCollection<DtcTransactionItem> Transactions { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DtcTransactionListViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DtcTransactionListViewModel>.Instance)
    {
    }

    public DtcTransactionListViewModel(ComponentServicesManager service, ILogger<DtcTransactionListViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadTransactionsAsync()
    {
        _logger.LogInformation("Loading DTC transaction list");
        IsLoading = true;
        StatusMessage = "Loading DTC transaction list...";

        try
        {
            Transactions.Clear();
            var items = await _service.GetDtcTransactionListAsync();
            foreach (var item in items)
            {
                Transactions.Add(item);
            }

            StatusMessage = $"Loaded {Transactions.Count} active transaction(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load transaction list: {ex.Message}";
            _logger.LogError(ex, "Failed to load DTC transaction list");
        }
        finally
        {
            IsLoading = false;
        }
    }
}


