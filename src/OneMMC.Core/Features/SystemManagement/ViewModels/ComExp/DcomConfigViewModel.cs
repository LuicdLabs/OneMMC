using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

public partial class DcomConfigViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DcomConfigViewModel> _logger;

    public ObservableCollection<DcomApplicationInfo> Applications { get; } = new();

    private DcomApplicationInfo? _selectedApplication;
    public DcomApplicationInfo? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                OnPropertyChanged(nameof(SelectedApplicationName));
                OnPropertyChanged(nameof(SelectedApplicationAppId));
                OnPropertyChanged(nameof(SelectedApplicationLocalService));
                OnPropertyChanged(nameof(SelectedApplicationRunAs));
                OnPropertyChanged(nameof(SelectedApplicationDllSurrogate));
                OnPropertyChanged(nameof(SelectedApplicationServiceParameters));
            }
        }
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

    public string SelectedApplicationName => SelectedApplication?.Name ?? string.Empty;
    public string SelectedApplicationAppId => SelectedApplication?.AppId ?? string.Empty;
    public string SelectedApplicationLocalService => SelectedApplication?.LocalService ?? string.Empty;
    public string SelectedApplicationRunAs => SelectedApplication?.RunAs ?? string.Empty;
    public string SelectedApplicationDllSurrogate => SelectedApplication?.DllSurrogate ?? string.Empty;
    public string SelectedApplicationServiceParameters => SelectedApplication?.ServiceParameters ?? string.Empty;

    public DcomConfigViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DcomConfigViewModel>.Instance)
    {
    }

    public DcomConfigViewModel(ComponentServicesManager service, ILogger<DcomConfigViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadApplicationsAsync()
    {
        _logger.LogInformation("Loading DCOM applications");
        IsLoading = true;
        StatusMessage = "Loading DCOM applications...";

        try
        {
            Applications.Clear();
            var apps = await _service.GetDcomApplicationsAsync();
            foreach (var app in apps)
            {
                Applications.Add(app);
            }

            SelectedApplication = Applications.FirstOrDefault();
            StatusMessage = $"Loaded {Applications.Count} applications.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load DCOM applications: {ex.Message}";
            _logger.LogError(ex, "Failed to load DCOM applications");
        }
        finally
        {
            IsLoading = false;
        }
    }
}


