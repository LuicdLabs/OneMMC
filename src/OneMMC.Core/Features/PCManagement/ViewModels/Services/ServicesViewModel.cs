using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.PCManagement.Models.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.PCManagement.Services.WindowsServices;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Infrastructure.Collections;

namespace OneMMC.Core.Features.PCManagement.ViewModels.Services
{
    public partial class ServicesViewModel : ObservableObject
    {
        private readonly WindowsServiceManager _serviceManager;
        private readonly ILogger<ServicesViewModel> _logger;
        private readonly IAdminService _adminService;

        private ObservableCollection<ServiceInfo> services = new();
        public ObservableCollection<ServiceInfo> Services
        {
            get => services;
            set => SetProperty(ref services, value);
        }

        private ServiceInfo? selectedService;
        public ServiceInfo? SelectedService
        {
            get => selectedService;
            set
            {
                if (SetProperty(ref selectedService, value))
                {
                    OnPropertyChanged(nameof(CanStartService));
                    OnPropertyChanged(nameof(CanStopService));
                    OnPropertyChanged(nameof(CanRestartService));
                }
            }
        }

        private bool isLoading;
        public bool IsLoading
        {
            get => isLoading;
            set => SetProperty(ref isLoading, value);
        }

        private string statusMessage = string.Empty;
        public string StatusMessage
        {
            get => statusMessage;
            set => SetProperty(ref statusMessage, value);
        }

        private string filterText = string.Empty;
        public string FilterText
        {
            get => filterText;
            set
            {
                if (SetProperty(ref filterText, value))
                {
                    FilterServices();
                }
            }
        }

        public bool CanStartService => SelectedService != null && (SelectedService.Status == "Stopped" || SelectedService.Status == "Paused");
        public bool CanStopService => SelectedService != null && (SelectedService.Status == "Running" || SelectedService.Status == "Paused");
        public bool CanRestartService => SelectedService != null && SelectedService.Status == "Running";

        private List<ServiceInfo> _allServices;
        
        // Event to notify UI about permission errors
        public event EventHandler? AdminPermissionRequired;

        public ServicesViewModel(WindowsServiceManager serviceManager, ILogger<ServicesViewModel> logger, IAdminService adminService)
        {
            _serviceManager = serviceManager;
            _logger = logger;
            _adminService = adminService;
            _allServices = new List<ServiceInfo>();
        }

        [RelayCommand]
        public async Task LoadServicesAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading services...";
            _logger.LogInformation("Loading Windows services.");
            try
            {
                var services = await _serviceManager.GetAllServicesAsync();
                _allServices = services;
                FilterServices();
                StatusMessage = $"Loaded {Services.Count} services.";
                _logger.LogInformation("Loaded {ServiceCount} services.", Services.Count);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading services: {ex.Message}";
                _logger.LogError(ex, "Failed to load services.");
                
                // Check if the error is related to permission issues
                if (_adminService.IsPermissionError(ex))
                {
                    _logger.LogWarning("Admin permission is required to complete service enumeration.");
                    AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        

        private void FilterServices()
        {
            if (_allServices == null) return;

            if (string.IsNullOrWhiteSpace(FilterText))
            {
                Services.ReplaceAll(_allServices);
                OnPropertyChanged(nameof(Services));
            }
            else
            {
                var filtered = _allServices.Where(s => 
                    (s.DisplayName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) || 
                    (s.Name?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false));
                Services.ReplaceAll(filtered);
                OnPropertyChanged(nameof(Services));
            }
        }

        [RelayCommand]
        public async Task StartServiceAsync()
        {
            if (SelectedService == null) return;
            string selectedName = SelectedService.Name;
            IsLoading = true;
            try
            {
                await _serviceManager.StartServiceAsync(SelectedService.Name);
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error starting service: {ex.Message}";
                _logger.LogError(ex, "Failed to start service {ServiceName}.", selectedName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task StopServiceAsync()
        {
            if (SelectedService == null) return;
            string selectedName = SelectedService.Name;
            IsLoading = true;
            try
            {
                await _serviceManager.StopServiceAsync(SelectedService.Name);
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error stopping service: {ex.Message}";
                _logger.LogError(ex, "Failed to stop service {ServiceName}.", selectedName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task RestartServiceAsync()
        {
            if (SelectedService == null) return;
            string selectedName = SelectedService.Name;
            IsLoading = true;
            try
            {
                await _serviceManager.RestartServiceAsync(SelectedService.Name);
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error restarting service: {ex.Message}";
                _logger.LogError(ex, "Failed to restart service {ServiceName}.", selectedName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<OperationResult> UpdateServiceStartupTypeAsync(string serviceName, string startupType)
        {
            try
            {
                // Map UI string to backend value if necessary
                string mode = startupType;
                if (startupType == "Automatic (Delayed Start)")
                {
                    mode = "Delayed-Auto";
                }

                await _serviceManager.SetStartupTypeAsync(serviceName, mode);
                string? selectedName = SelectedService?.Name;
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }
                return OperationResult.Ok($"Startup type for {serviceName} updated.");
            }
            catch (Exception ex)
            {
                bool accessDenied = _adminService.IsPermissionError(ex);
                StatusMessage = $"Error setting startup type: {ex.Message}";
                _logger.LogError(ex, "Failed to update startup type for service {ServiceName} to {StartupType}.", serviceName, startupType);
                return new OperationResult(false, ex.Message, isAccessDenied: accessDenied);
            }
        }

        public async Task<OperationResult> UpdateServiceLogOnAsync(string serviceName, string username, string? password)
        {
            try
            {
                await _serviceManager.SetLogOnAccountAsync(serviceName, username, password);
                string? selectedName = SelectedService?.Name;
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }
                return OperationResult.Ok($"Log on account for {serviceName} updated.");
            }
            catch (Exception ex)
            {
                bool accessDenied = _adminService.IsPermissionError(ex);
                StatusMessage = $"Error setting log on account: {ex.Message}";
                _logger.LogError(ex, "Failed to update logon account for service {ServiceName}.", serviceName);
                return new OperationResult(false, ex.Message, isAccessDenied: accessDenied);
            }
        }

        public async Task LoadServiceDetailsAsync()
        {
            if (SelectedService == null) return;

            try
            {
                var recovery = await _serviceManager.GetServiceRecoveryInfoAsync(SelectedService.Name);
                SelectedService.FirstFailureAction = recovery.First;
                SelectedService.SecondFailureAction = recovery.Second;
                SelectedService.SubsequentFailureAction = recovery.Subsequent;
                SelectedService.ResetFailCountDays = recovery.ResetDays;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading details: {ex.Message}";
                _logger.LogError(ex, "Failed to load service recovery details for {ServiceName}.", SelectedService.Name);
            }
        }

        public async Task<OperationResult> UpdateServiceRecoveryAsync(string serviceName, string firstAction, string secondAction, string subsequentAction, double resetDays)
        {
            try
            {
                // Convert days to seconds
                int resetSeconds = (int)(resetDays * 86400);

                await _serviceManager.SetRecoveryOptionsAsync(serviceName, firstAction, secondAction, subsequentAction, resetSeconds);

                // Reload service details to reflect the changes
                string? selectedName = SelectedService?.Name;
                await LoadServicesAsync();
                SelectedService = Services.FirstOrDefault(s => s.Name == selectedName);
                if (SelectedService != null)
                {
                    await LoadServiceDetailsAsync();
                }

                StatusMessage = "Recovery options updated.";
                _logger.LogInformation("Updated recovery options for service {ServiceName}.", serviceName);
                return OperationResult.Ok($"Recovery options for {serviceName} updated.");
            }
            catch (Exception ex)
            {
                bool accessDenied = _adminService.IsPermissionError(ex);
                StatusMessage = $"Error setting recovery options: {ex.Message}";
                _logger.LogError(ex, "Failed to update recovery options for service {ServiceName}.", serviceName);
                return new OperationResult(false, ex.Message, isAccessDenied: accessDenied);
            }
        }

    }
}


