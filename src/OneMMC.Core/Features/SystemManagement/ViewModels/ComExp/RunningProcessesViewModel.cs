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

public partial class RunningProcessesViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<RunningProcessesViewModel> _logger;

    public ObservableCollection<ProcessInfo> Processes { get; } = new();

    private ProcessInfo? _selectedProcess;
    public ProcessInfo? SelectedProcess
    {
        get => _selectedProcess;
        set => SetProperty(ref _selectedProcess, value);
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

    public RunningProcessesViewModel(ComponentServicesManager service)
                : this(service, NullLogger<RunningProcessesViewModel>.Instance)
        {
        }

        public RunningProcessesViewModel(ComponentServicesManager service, ILogger<RunningProcessesViewModel> logger)
    {
        _service = service;
		_logger = logger;
		SelectedProcess = new ProcessInfo { ProcessId = 0, Name = string.Empty };
    }

    [RelayCommand]
    public async Task LoadProcessesAsync()
    {
        _logger.LogInformation("Loading COM+ running processes");
		IsLoading = true;
		StatusMessage = "Loading COM+ running processes...";

        try
        {
            Processes.Clear();
			var processes = await _service.GetComPlusRunningProcessesAsync();
            foreach (var process in processes)
            {
                Processes.Add(process);
            }

			SelectedProcess = Processes.FirstOrDefault() ?? SelectedProcess;
			StatusMessage = $"Loaded {Processes.Count} COM+ processes.";
        }
        catch (Exception ex)
        {
			StatusMessage = $"Failed to load COM+ processes: {ex.Message}";
            _logger.LogError(ex, "Failed to load COM+ running processes");
        }
        finally
        {
            IsLoading = false;
        }
    }
}


