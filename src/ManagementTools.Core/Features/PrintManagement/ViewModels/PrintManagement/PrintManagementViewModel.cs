using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;
using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PrintManagement.ViewModels.PrintManagement;

/// <summary>
/// ViewModel for the Print Management page following MVVM pattern.
/// Uses manual property style for WinRT AOT compatibility.
/// </summary>
public partial class PrintManagementViewModel : ObservableObject
{
    private readonly PrintManagementService _printService;
    private readonly ILogger<PrintManagementViewModel> _logger;
    private readonly IAdminService _adminService;

    /// <summary>Collection of printers on the system</summary>
    private ObservableCollection<PrinterInfo> _printers = [];
    public ObservableCollection<PrinterInfo> Printers
    {
        get => _printers;
        set => SetProperty(ref _printers, value);
    }

    /// <summary>Collection of print drivers on the system</summary>
    private ObservableCollection<PrintDriverInfo> _drivers = [];
    public ObservableCollection<PrintDriverInfo> Drivers
    {
        get => _drivers;
        set => SetProperty(ref _drivers, value);
    }

    /// <summary>Collection of print ports on the system</summary>
    private ObservableCollection<PrintPortInfo> _ports = [];
    public ObservableCollection<PrintPortInfo> Ports
    {
        get => _ports;
        set => SetProperty(ref _ports, value);
    }

    /// <summary>Collection of print forms on the system</summary>
    private ObservableCollection<PrintFormInfo> _forms = [];
    public ObservableCollection<PrintFormInfo> Forms
    {
        get => _forms;
        set => SetProperty(ref _forms, value);
    }

    /// <summary>Collection of deployed printers on the system</summary>
    private ObservableCollection<PrinterInfo> _deployedPrinters = [];
    public ObservableCollection<PrinterInfo> DeployedPrinters
    {
        get => _deployedPrinters;
        set => SetProperty(ref _deployedPrinters, value);
    }

    /// <summary>Whether data is currently being loaded</summary>
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Status message displayed in the UI</summary>
    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Local computer name for display</summary>
    private string _computerName = string.Empty;
    public string ComputerName
    {
        get => _computerName;
        set => SetProperty(ref _computerName, value);
    }

    /// <summary>Number of printers for display in section header</summary>
    public string PrinterCountText => $"{Printers.Count}";

    /// <summary>Number of deployed printers for display</summary>
    public string DeployedPrinterCountText => $"{DeployedPrinters.Count}";

    /// <summary>Number of drivers for display in section header</summary>
    public string DriverCountText => $"{Drivers.Count}";

    /// <summary>Event raised when admin permissions are required</summary>
    public event EventHandler? AdminPermissionRequired;

    public PrintManagementViewModel(
        PrintManagementService printService,
        ILogger<PrintManagementViewModel> logger,
        IAdminService adminService)
    {
        _printService = printService;
        _logger = logger;
        _adminService = adminService;
        _computerName = _printService.GetComputerName();
    }

    /// <summary>
    /// Loads all print management data (printers, drivers, ports, forms).
    /// </summary>
    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading...";
        _logger.LogInformation("Loading print management data.");

        try
        {
            var printersTask = _printService.GetPrintersAsync();
            var driversTask = _printService.GetPrintDriversAsync();
            var portsTask = _printService.GetPrintPortsAsync();
            var formsTask = _printService.GetPrintFormsAsync();
            var deployedTask = _printService.GetDeployedPrintersAsync();

            await Task.WhenAll(printersTask, driversTask, portsTask, formsTask, deployedTask);

            Printers = new ObservableCollection<PrinterInfo>(await printersTask);
            Drivers = new ObservableCollection<PrintDriverInfo>(await driversTask);
            Ports = new ObservableCollection<PrintPortInfo>(await portsTask);
            Forms = new ObservableCollection<PrintFormInfo>(await formsTask);
            DeployedPrinters = new ObservableCollection<PrinterInfo>(await deployedTask);

            OnPropertyChanged(nameof(PrinterCountText));
            OnPropertyChanged(nameof(DeployedPrinterCountText));
            OnPropertyChanged(nameof(DriverCountText));

            StatusMessage = $"Loaded {Printers.Count} printers, {Drivers.Count} drivers, {Ports.Count} ports, {Forms.Count} forms.";
            _logger.LogInformation(
                "Loaded {PrinterCount} printers, {DriverCount} drivers, {PortCount} ports, {FormCount} forms.",
                Printers.Count, Drivers.Count, Ports.Count, Forms.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to load print management data.");

            if (_adminService.IsPermissionError(ex))
            {
                _logger.LogWarning("Admin permission is required for print management.");
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes all print management data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    public void ClearCachedData()
    {
        Printers.Clear();
        Drivers.Clear();
        Ports.Clear();
        Forms.Clear();
        DeployedPrinters.Clear();
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(PrinterCountText));
        OnPropertyChanged(nameof(DeployedPrinterCountText));
        OnPropertyChanged(nameof(DriverCountText));
    }
}


