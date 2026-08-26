using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Services.DevMgmt;
using OneMMC.Core.Infrastructure.Collections;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.ViewModels.DevMgmt
{
    public partial class DeviceManagerViewModel : INotifyPropertyChanged
    {
        private readonly DeviceManagerService _deviceManagerService;
        private readonly ILogger<DeviceManagerViewModel> _logger;
        private static ILocalizationProvider L => LocalizationProvider.Current;
        private ObservableCollection<DeviceCategory> _deviceCategories;

        /// <summary>
        /// The unfiltered device categories from the last load. Filtering reads from here so it never
        /// narrows a previously filtered result and never has to re-run the WMI enumeration.
        /// </summary>
        private List<DeviceCategory> _allCategories = [];

        private DeviceCategory? _selectedCategory;
        private DeviceInfo? _selectedDevice;
        private DeviceProperties? _selectedDeviceProperties;
        private bool _isLoading;
        private bool _showHiddenDevices;
        private string _searchText = string.Empty;

        public DeviceManagerViewModel(DeviceManagerService deviceManagerService, ILogger<DeviceManagerViewModel> logger)
        {
            _deviceManagerService = deviceManagerService;
            _logger = logger;
            _deviceCategories = new ObservableCollection<DeviceCategory>();
        }

        public ObservableCollection<DeviceCategory> DeviceCategories
        {
            get => _deviceCategories;
            set
            {
                _deviceCategories = value;
                OnPropertyChanged();
            }
        }

        public DeviceCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
            }
        }

        public DeviceInfo? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
                if (value != null)
                {
                    _ = LoadDevicePropertiesAsync(value.DeviceId);
                }
            }
        }

        public DeviceProperties? SelectedDeviceProperties
        {
            get => _selectedDeviceProperties;
            set
            {
                _selectedDeviceProperties = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool ShowHiddenDevices
        {
            get => _showHiddenDevices;
            set
            {
                _showHiddenDevices = value;
                OnPropertyChanged();
                _ = LoadDevicesAsync();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterDevices();
            }
        }

        public async Task LoadDevicesAsync()
        {
            IsLoading = true;

            try
            {
                var categories = await Task.Run(() =>
                {
                    var cats = _deviceManagerService.GetDeviceCategories();
                    
                    // If showing hidden devices, add them
                    if (ShowHiddenDevices)
                    {
                        var hiddenDevices = _deviceManagerService.GetHiddenDevices();
                        if (hiddenDevices.Any())
                        {
                            var hiddenCategory = new DeviceCategory
                            {
                                Name = L.GetString(ResourceFileNames.DeviceManager, DeviceManagerKeys.HiddenDevices),
                                ClassGuid = "",
                                Devices = hiddenDevices
                            };
                            cats.Insert(0, hiddenCategory);
                        }
                    }

                    return cats;
                });

                // Keep the unfiltered result so filtering never has to re-query, and so it always filters
                // the full set rather than the previous filter's output.
                _allCategories = categories;

                // Update on UI thread
                ReplaceCategories(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load device categories.");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadDevicePropertiesAsync(string deviceId)
        {
            IsLoading = true;

            try
            {
                var properties = await Task.Run(() =>
                {
                    return _deviceManagerService.GetDeviceProperties(deviceId);
                });

                // Update on UI thread
                SelectedDeviceProperties = properties;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load device properties for {DeviceId}.", deviceId);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> EnableDeviceAsync()
        {
            if (SelectedDevice == null) return false;
            var selectedDeviceId = SelectedDevice.DeviceId;

            IsLoading = true;

            try
            {
                var result = await Task.Run(() => _deviceManagerService.EnableDevice(selectedDeviceId));
                
                if (result)
                {
                    await LoadDevicesAsync();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable selected device {DeviceId}.", selectedDeviceId);
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> DisableDeviceAsync()
        {
            if (SelectedDevice == null) return false;
            var selectedDeviceId = SelectedDevice.DeviceId;

            IsLoading = true;

            try
            {
                var result = await Task.Run(() => _deviceManagerService.DisableDevice(selectedDeviceId));
                
                if (result)
                {
                    await LoadDevicesAsync();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disable selected device {DeviceId}.", selectedDeviceId);
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> UninstallDeviceAsync()
        {
            if (SelectedDevice == null) return false;

            IsLoading = true;

            try
            {
                var result = await Task.Run(() => _deviceManagerService.UninstallDevice(SelectedDevice.DeviceId));
                
                if (result)
                {
                    await LoadDevicesAsync();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to uninstall selected device {DeviceId}.", SelectedDevice.DeviceId);
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> ScanForHardwareChangesAsync()
        {
            IsLoading = true;

            try
            {
                var result = await Task.Run(() => _deviceManagerService.ScanForHardwareChanges());
                
                if (result)
                {
                    await Task.Delay(2000); // Wait for system to complete scan
                    await LoadDevicesAsync();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan for hardware changes.");
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public string GetDeviceCountText(DeviceCategory category)
        {
            return $"{L.GetString(ResourceFileNames.DeviceManager, DeviceManagerKeys.DeviceCountPrefix)}{category.DeviceCount}{L.GetString(ResourceFileNames.DeviceManager, DeviceManagerKeys.DeviceCountSuffix)}";
        }

        public void OpenDeviceManager()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open system device manager.");
            }
        }

        private void FilterDevices()
        {
            // Always filter the unfiltered master list. Filtering DeviceCategories (the previous result)
            // narrowed monotonically, so deleting characters from the search box could not bring
            // previously excluded devices back.
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                if (_allCategories.Count > 0)
                {
                    ReplaceCategories(_allCategories);
                }
                else
                {
                    _ = LoadDevicesAsync();
                }

                return;
            }

            var filteredCategories = new List<DeviceCategory>();
            foreach (var category in _allCategories)
            {
                var filteredDevices = category.Devices
                    .Where(d => 
                        d.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        d.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        d.Manufacturer.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (filteredDevices.Any())
                {
                    filteredCategories.Add(new DeviceCategory
                    {
                        Name = category.Name,
                        ClassGuid = category.ClassGuid,
                        Devices = filteredDevices
                    });
                }
            }

            ReplaceCategories(filteredCategories);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Mirrors <paramref name="categories"/> into the already-bound collection instead of
        /// assigning a fresh <see cref="ObservableCollection{T}"/>.
        /// </summary>
        /// <remarks>
        /// Assigning a new collection made the list view drop every item container and build new
        /// element trees; the native side of the discarded trees is never released, so each load,
        /// filter keystroke and page visit cost several MB for the life of the process. Mutating the
        /// bound instance lets the list view recycle the containers it already has. See
        /// <c>doc/MemoryManagement.md</c>.
        /// </remarks>
        private void ReplaceCategories(IList<DeviceCategory> categories)
        {
            _deviceCategories.ReplaceAll(categories);

            // The collection instance is unchanged, so raise the notification the view listens to for
            // its device-count header.
            OnPropertyChanged(nameof(DeviceCategories));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
