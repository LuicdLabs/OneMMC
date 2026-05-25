using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.SystemManagement.Models.ComExp;
using ManagementTools.Core.Features.SystemManagement.Services.ComExp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.SystemManagement.ViewModels.ComExp;

public partial class DtcStatisticsViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DtcStatisticsViewModel> _logger;

    private DtcTransactionsStatistics? _statistics;
    public DtcTransactionsStatistics? Statistics
    {
        get => _statistics;
        set => SetProperty(ref _statistics, value);
    }

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

    public DtcStatisticsViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DtcStatisticsViewModel>.Instance)
    {
    }

    public DtcStatisticsViewModel(ComponentServicesManager service, ILogger<DtcStatisticsViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadStatisticsAsync()
    {
        _logger.LogInformation("Loading DTC statistics");
        IsLoading = true;
        StatusMessage = "Loading DTC statistics...";

        try
        {
            Statistics = await _service.GetDtcTransactionsStatisticsAsync();

            if (Statistics != null)
            {
                StatusMessage = "Statistics loaded successfully.";
            }
            else
            {
                StatusMessage = "Failed to load statistics. MSDTC may not be available.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load statistics: {ex.Message}";
            _logger.LogError(ex, "Failed to load DTC statistics");
        }
        finally
        {
            IsLoading = false;
        }
    }
}


