using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Infrastructure.Wmi;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using WmiLight;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.DevMgmt
{
    public class DeviceManagerService
    {
        private readonly ILogger<DeviceManagerService> _logger;
        private readonly IAdminService _adminService;

        public DeviceManagerService(ILogger<DeviceManagerService> logger, IAdminService adminService)
        {
            _logger = logger;
            _adminService = adminService;
        }

        // Native device enumeration/installation runs through CsWin32-generated SETUPAPI.dll interop
        // (Win32PInvoke.SetupDi*, the SP_* structs and SETUP_DI_*/DI_FUNCTION enums). The HDEVINFO from
        // SetupDiGetClassDevs is wrapped in a SafeHandle that calls SetupDiDestroyDeviceInfoList on
        // dispose, so no handwritten [DllImport] remains here.
        private const int CR_SUCCESS = 0;

        /// <summary>
        /// Sets the full class-install parameter block for a device (SP_PROPCHANGE_PARAMS /
        /// SP_REMOVEDEVICE_PARAMS). CsWin32's in-header overload only marshals
        /// <c>sizeof(SP_CLASSINSTALL_HEADER)</c>; SetupDiSetClassInstallParams needs the whole block, so
        /// the parameter struct is handed over as raw bytes with its real size.
        /// </summary>
        private static bool SetClassInstallParams<T>(SafeHandle deviceInfoSet, in SP_DEVINFO_DATA deviceInfoData, in T parameters)
            where T : unmanaged
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in parameters));
            return Win32PInvoke.SetupDiSetClassInstallParams(deviceInfoSet, deviceInfoData, bytes);
        }

        /// <summary>
        /// Get all device categories
        /// </summary>
        public List<DeviceCategory> GetDeviceCategories()
        {
            var categories = new Dictionary<string, DeviceCategory>();

            try
            {
                using (var connection = new WmiConnection())
                {
                    // Project only the properties read below. Win32_PnPEntity exposes roughly thirty, and a
                    // typical machine has 1500-3000 instances, so SELECT * materialized tens of thousands
                    // of property values that were immediately discarded.
                    const string DeviceQuery =
                        "SELECT PNPClass, ClassGuid, Caption, DeviceID, Description, Manufacturer, " +
                        "Status, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity";

                    foreach (WmiObject device in connection.CreateQuery(DeviceQuery).DisposeItems())
                    {
                        var className = device["PNPClass"]?.ToString() ?? "Unknown";
                        var classGuid = device["ClassGuid"]?.ToString() ?? "";

                        if (!categories.ContainsKey(className))
                        {
                            categories[className] = new DeviceCategory
                            {
                                Name = className,
                                ClassGuid = classGuid,
                                Devices = new List<DeviceInfo>()
                            };
                        }

                        var deviceInfo = new DeviceInfo
                        {
                            Name = device["Caption"]?.ToString() ?? "Unknown Device",
                            DeviceId = device["DeviceID"]?.ToString() ?? "",
                            Description = device["Description"]?.ToString() ?? "",
                            Manufacturer = device["Manufacturer"]?.ToString() ?? "",
                            Status = device["Status"]?.ToString() ?? "Unknown",
                            PnpDeviceId = device["PNPDeviceID"]?.ToString() ?? "",
                            ConfigManagerErrorCode = Convert.ToUInt32(device["ConfigManagerErrorCode"] ?? 0),
                            ClassName = className,
                            ClassGuid = classGuid
                        };

                        // Determine device status
                        deviceInfo.IsEnabled = deviceInfo.ConfigManagerErrorCode == 0;
                        deviceInfo.HasProblem = deviceInfo.ConfigManagerErrorCode != 0;
                        deviceInfo.StatusDescription = GetDeviceStatusDescription(deviceInfo.ConfigManagerErrorCode);

                        categories[className].Devices.Add(deviceInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error getting device categories: {ex.Message}");
            }

            return categories.Values.OrderBy(c => c.Name).ToList();
        }

        /// <summary>
        /// Get detailed properties of a device
        /// </summary>
        public DeviceProperties GetDeviceProperties(string deviceId)
        {
            var properties = new DeviceProperties { DeviceId = deviceId };

            try
            {
                var query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'";
                using (var connection = new WmiConnection())
                {
                    foreach (WmiObject device in connection.CreateQuery(query).DisposeItems())
                    {
                        properties.Name = device["Caption"]?.ToString() ?? string.Empty;
                        properties.Description = device["Description"]?.ToString() ?? string.Empty;
                        properties.Manufacturer = device["Manufacturer"]?.ToString() ?? string.Empty;
                        properties.Status = device["Status"]?.ToString() ?? string.Empty;
                        properties.PnpDeviceId = device["PNPDeviceID"]?.ToString() ?? string.Empty;
                        properties.Service = device["Service"]?.ToString() ?? string.Empty;
                        properties.ClassName = device["PNPClass"]?.ToString() ?? string.Empty;
                        properties.ClassGuid = device["ClassGuid"]?.ToString() ?? string.Empty;

                        // Get hardware ID
                        if (device["HardwareID"] != null)
                        {
                            properties.HardwareIds = ((string[])device["HardwareID"]).ToList();
                        }

                        // Get compatible ID
                        if (device["CompatibleID"] != null)
                        {
                            properties.CompatibleIds = ((string[])device["CompatibleID"]).ToList();
                        }

                        properties.ConfigManagerErrorCode = Convert.ToUInt32(device["ConfigManagerErrorCode"] ?? 0);
                    }
                }

                // Get driver information
                properties.DriverInfo = GetDeviceDriverInfo(deviceId);

                
            }
            catch (Exception ex)
            {
                properties.ErrorMessage = $"DeviceManager_DevicePropertiesError:{ex.Message}";
            }

            return properties;
        }

        /// <summary>
        /// Get device driver information
        /// </summary>
        private DriverInfo GetDeviceDriverInfo(string deviceId)
        {
            var driverInfo = new DriverInfo();

            try
            {
                var query = $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'";
                using (var connection = new WmiConnection())
                {
                    foreach (WmiObject driver in connection.CreateQuery(query).DisposeItems())
                    {
                        driverInfo.DriverVersion = driver["DriverVersion"]?.ToString() ?? string.Empty;
                        
                        // Parse and format driver date
                        var rawDate = driver["DriverDate"]?.ToString();
                        driverInfo.DriverDate = ParseAndFormatDriverDate(rawDate) ?? string.Empty;
                        
                        driverInfo.DriverProviderName = driver["DriverProviderName"]?.ToString() ?? string.Empty;
                        driverInfo.InfName = driver["InfName"]?.ToString() ?? string.Empty;
                        driverInfo.IsSigned = Convert.ToBoolean(driver["IsSigned"] ?? false);
                        driverInfo.Signer = driver["Signer"]?.ToString() ?? string.Empty;
                        driverInfo.DeviceClass = driver["DeviceClass"]?.ToString() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error getting driver info: {ex.Message}");
            }

            return driverInfo;
        }

        
        public bool EnableDevice(string deviceId)
        {
            return ChangeDeviceState(deviceId, SETUP_DI_STATE_CHANGE.DICS_ENABLE);
        }

        /// <summary>
        /// Disable device
        /// </summary>
        public bool DisableDevice(string deviceId)
        {
            return ChangeDeviceState(deviceId, SETUP_DI_STATE_CHANGE.DICS_DISABLE);
        }

        /// <summary>
        /// Change device state
        /// </summary>
        private bool ChangeDeviceState(string deviceId, SETUP_DI_STATE_CHANGE newState)
        {
            try
            {
                using var deviceInfoSet = Win32PInvoke.SetupDiGetClassDevs(
                    ClassGuid: null,
                    Enumerator: null,
                    hwndParent: default,
                    Flags: SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES);

                if (deviceInfoSet.IsInvalid)
                {
                    _logger.LogDebug($"Failed to get device info set. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                var deviceInfoData = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                uint index = 0;
                while (Win32PInvoke.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData))
                {
                    string currentPnpDeviceId = GetPnpDeviceId(deviceInfoData.DevInst);
                    string currentHardwareId = GetDeviceId(deviceInfoSet, deviceInfoData);

                    _logger.LogDebug($"Checking device: PNP={currentPnpDeviceId}, HW={currentHardwareId}");

                    if (!string.IsNullOrEmpty(currentPnpDeviceId) && currentPnpDeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrEmpty(currentHardwareId) && currentHardwareId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug($"Found matching device: {currentPnpDeviceId}");

                        var parameters = new SP_PROPCHANGE_PARAMS
                        {
                            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                            {
                                cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                                InstallFunction = DI_FUNCTION.DIF_PROPERTYCHANGE,
                            },
                            StateChange = newState,
                            Scope = SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_GLOBAL,
                            HwProfile = 0,
                        };

                        if (SetClassInstallParams(deviceInfoSet, deviceInfoData, parameters))
                        {
                            bool result = Win32PInvoke.SetupDiCallClassInstaller(DI_FUNCTION.DIF_PROPERTYCHANGE, deviceInfoSet, deviceInfoData);
                            if (!result)
                            {
                                int error = Marshal.GetLastWin32Error();
                                _logger.LogDebug($"SetupDiCallClassInstaller failed. Error: {error}");
                            }
                            else
                            {
                                _logger.LogDebug("Device state changed successfully");
                            }

                            return result;
                        }

                        int lastError = Marshal.GetLastWin32Error();
                        _logger.LogDebug($"SetupDiSetClassInstallParams failed. Error: {lastError}");
                    }

                    index++;
                }

                _logger.LogDebug($"Device not found: {deviceId}");
            }
            catch (Exception ex)
            {
                if (_adminService.IsPermissionError(ex))
                {
                    _logger.LogDebug($"Error changing device state: insufficient administrator privileges.");
                }
                else
                {
                    _logger.LogDebug($"Error changing device state: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Get device ID
        /// </summary>
        private string GetDeviceId(SafeHandle deviceInfoSet, in SP_DEVINFO_DATA deviceInfoData)
        {
            Span<byte> buffer = new byte[1024];

            if (Win32PInvoke.SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                in deviceInfoData,
                SETUP_DI_REGISTRY_PROPERTY.SPDRP_HARDWAREID,
                out _,
                buffer,
                out _))
            {
                return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            }

            return string.Empty;
        }

        /// <summary>
        /// Get PNP Device ID
        /// </summary>
        private string GetPnpDeviceId(uint devInst)
        {
            Span<char> buffer = stackalloc char[256];
            var result = Win32PInvoke.CM_Get_Device_IDW(devInst, buffer, 0);

            if ((int)result == CR_SUCCESS)
            {
                return buffer.ToString().TrimEnd('\0');
            }

            return string.Empty;
        }

        /// <summary>
        /// Uninstall device
        /// </summary>
        public bool UninstallDevice(string deviceId)
        {
            try
            {
                using var deviceInfoSet = Win32PInvoke.SetupDiGetClassDevs(
                    ClassGuid: null,
                    Enumerator: null,
                    hwndParent: default,
                    Flags: SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES);

                if (deviceInfoSet.IsInvalid)
                {
                    _logger.LogDebug($"Failed to get device info set for uninstall. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                var deviceInfoData = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                uint index = 0;
                while (Win32PInvoke.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData))
                {
                    string currentPnpDeviceId = GetPnpDeviceId(deviceInfoData.DevInst);
                    string currentHardwareId = GetDeviceId(deviceInfoSet, deviceInfoData);

                    if (!string.IsNullOrEmpty(currentPnpDeviceId) && currentPnpDeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrEmpty(currentHardwareId) && currentHardwareId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug($"Found device to uninstall: {currentPnpDeviceId}");

                        var removeParams = new SP_REMOVEDEVICE_PARAMS
                        {
                            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                            {
                                cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                                InstallFunction = DI_FUNCTION.DIF_REMOVE,
                            },
                            Scope = SETUP_DI_REMOVE_DEVICE_SCOPE.DI_REMOVEDEVICE_GLOBAL,
                            HwProfile = 0,
                        };

                        if (SetClassInstallParams(deviceInfoSet, deviceInfoData, removeParams))
                        {
                            bool result = Win32PInvoke.SetupDiCallClassInstaller(DI_FUNCTION.DIF_REMOVE, deviceInfoSet, deviceInfoData);
                            if (!result)
                            {
                                int error = Marshal.GetLastWin32Error();
                                _logger.LogDebug($"SetupDiCallClassInstaller (DIF_REMOVE) failed. Error: {error}");
                            }
                            else
                            {
                                _logger.LogDebug("Device uninstalled successfully");
                            }

                            return result;
                        }

                        int lastError = Marshal.GetLastWin32Error();
                        _logger.LogDebug($"SetupDiSetClassInstallParams (DIF_REMOVE) failed. Error: {lastError}");
                    }

                    index++;
                }

                _logger.LogDebug($"Device not found for uninstall: {deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error uninstalling device: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Scan for hardware changes
        /// </summary>
        public bool ScanForHardwareChanges()
        {
            try
            {
                // First try to locate root node (pass null to get root devnode), then perform re-enumeration on that node
                uint rootDevInst;
                var locateResult = Win32PInvoke.CM_Locate_DevNode(out rootDevInst, default(PWSTR), (CM_LOCATE_DEVNODE_FLAGS)0);
                if ((int)locateResult == CR_SUCCESS)
                {
                    var result = Win32PInvoke.CM_Reenumerate_DevNode(rootDevInst, (CM_REENUMERATE_FLAGS)0);
                    if ((int)result == CR_SUCCESS)
                    {
                        _logger.LogDebug("CM_Reenumerate_DevNode succeeded on root devnode");
                        return true;
                    }

                    _logger.LogDebug($"CM_Reenumerate_DevNode failed on root devnode. Return: {result}");
                    if ((int)result == 5)
                    {
                        _logger.LogDebug("CM_Reenumerate_DevNode returned 5 (likely invalid devnode or insufficient privileges).");
                    }
                }
                else
                {
                    _logger.LogDebug($"CM_Locate_DevNode failed. Return: {locateResult}");
                }

                // Try fallback: use special value 0xFFFFFFFF for all device trees
                uint allDevices = 0xFFFFFFFF;
                var fallback = Win32PInvoke.CM_Reenumerate_DevNode(allDevices, (CM_REENUMERATE_FLAGS)0);
                if ((int)fallback == CR_SUCCESS)
                {
                    _logger.LogDebug("CM_Reenumerate_DevNode succeeded using fallback (0xFFFFFFFF)");
                    return true;
                }

                _logger.LogDebug($"CM_Reenumerate_DevNode fallback failed. Return: {fallback}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error scanning for hardware changes: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Open Device Manager console
        /// </summary>
        public bool OpenDeviceManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse and format driver date
        /// </summary>
        private string? ParseAndFormatDriverDate(string? rawDate)
        {
            if (string.IsNullOrEmpty(rawDate))
                return null;

            try
            {
                // WMI CIM_DateTime format: yyyyMMddHHmmss.ffffff+UUU
                // Example: 20231127120000.000000+000
                if (rawDate.Length >= 8)
                {
                    // Extract date part: yyyyMMdd
                    string year = rawDate.Substring(0, 4);
                    string month = rawDate.Substring(4, 2);
                    string day = rawDate.Substring(6, 2);

                    // Validate if date is valid
                    if (int.TryParse(year, out int y) && int.TryParse(month, out int m) && int.TryParse(day, out int d))
                    {
                        if (y >= 1900 && y <= 2100 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
                        {
                            // Format as yyyy/MM/dd
                            return $"{y:D4}/{m:D2}/{d:D2}";
                        }
                    }
                }

                // If parsing fails, return original value
                return rawDate;
            }
            catch
            {
                // If an exception occurs during parsing, return original value
                return rawDate;
            }
        }
        
        private string GetDeviceStatusDescription(uint errorCode)
        {
            var L = LocalizationProvider.Current;
            
            string resourceKey;
            bool isKnownError = true;
            
            switch (errorCode)
            {
                case 0: resourceKey = DeviceManagerKeys.StatusWorking; break;
                case 1: resourceKey = DeviceManagerKeys.StatusConfigError; break;
                case 3: resourceKey = DeviceManagerKeys.StatusDriverCorrupt; break;
                case 10: resourceKey = DeviceManagerKeys.StatusCannotStart; break;
                case 12: resourceKey = DeviceManagerKeys.StatusNoResources; break;
                case 14: resourceKey = DeviceManagerKeys.StatusRestartRequired; break;
                case 18: resourceKey = DeviceManagerKeys.StatusReinstallDriver; break;
                case 19: resourceKey = DeviceManagerKeys.StatusRegistryCorrupt; break;
                case 21: resourceKey = DeviceManagerKeys.StatusRemoving; break;
                case 22: resourceKey = DeviceManagerKeys.StatusDisabled; break;
                case 24: resourceKey = DeviceManagerKeys.StatusNotPresent; break;
                case 28: resourceKey = DeviceManagerKeys.StatusNoDriver; break;
                case 29: resourceKey = DeviceManagerKeys.StatusDisabledFirmware; break;
                case 31: resourceKey = DeviceManagerKeys.StatusCannotLoadDriver; break;
                case 32: resourceKey = DeviceManagerKeys.StatusDriverDisabled; break;
                case 33: resourceKey = DeviceManagerKeys.StatusCannotDetermineResources; break;
                case 34: resourceKey = DeviceManagerKeys.StatusCannotDetermineConfig; break;
                case 35: resourceKey = DeviceManagerKeys.StatusInsufficientFirmware; break;
                case 36: resourceKey = DeviceManagerKeys.StatusInterruptConflict; break;
                case 37: resourceKey = DeviceManagerKeys.StatusCannotInitializeDriver; break;
                case 38: resourceKey = DeviceManagerKeys.StatusDriverInMemory; break;
                case 39: resourceKey = DeviceManagerKeys.StatusDriverCorruptOrMissing; break;
                case 40: resourceKey = DeviceManagerKeys.StatusRegistryMissing; break;
                case 41: resourceKey = DeviceManagerKeys.StatusHardwareNotFound; break;
                case 42: resourceKey = DeviceManagerKeys.StatusDuplicateDevice; break;
                case 43: resourceKey = DeviceManagerKeys.StatusDeviceReportedProblem; break;
                case 44: resourceKey = DeviceManagerKeys.StatusApplicationClosed; break;
                case 45: resourceKey = DeviceManagerKeys.StatusNotConnected; break;
                case 46: resourceKey = DeviceManagerKeys.StatusSystemShutdown; break;
                case 47: resourceKey = DeviceManagerKeys.StatusSafeRemoval; break;
                case 48: resourceKey = DeviceManagerKeys.StatusSoftwareBlocked; break;
                case 49: resourceKey = DeviceManagerKeys.StatusRegistryTooBig; break;
                case 52: resourceKey = DeviceManagerKeys.StatusInvalidSignature; break;
                default:
                    resourceKey = DeviceManagerKeys.StatusUnknownError;
                    isKnownError = false;
                    break;
            }
            
            // For unknown errors, format with error code
            if (!isKnownError)
            {
                return L.GetFormattedString(ResourceFileNames.DeviceManager, resourceKey, errorCode);
            }
            
            return L.GetString(ResourceFileNames.DeviceManager, resourceKey);
        }

        /// <summary>
        /// Get hidden devices
        /// </summary>
        public List<DeviceInfo> GetHiddenDevices()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                using (var connection = new WmiConnection())
                {
                    foreach (WmiObject device in connection.CreateQuery(
                        "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 22").DisposeItems())
                    {
                        var deviceInfo = new DeviceInfo
                        {
                            Name = device["Caption"]?.ToString() ?? "Unknown Device",
                            DeviceId = device["DeviceID"]?.ToString() ?? "",
                            Description = device["Description"]?.ToString() ?? "",
                            Manufacturer = device["Manufacturer"]?.ToString() ?? "",
                            Status = "Disabled",
                            IsEnabled = false,
                            HasProblem = true,
                            ConfigManagerErrorCode = 22,
                            StatusDescription = "DeviceManager_StatusDisabled"
                        };

                        devices.Add(deviceInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error getting hidden devices: {ex.Message}");
            }

            return devices;
        }
    }

    #region Data Models

    public class DeviceCategory
    {
        public string Name { get; set; } = string.Empty;
        public string ClassGuid { get; set; } = string.Empty;
        public List<DeviceInfo> Devices { get; set; } = new();
        public int DeviceCount => Devices?.Count ?? 0;
        public bool IsExpanded { get; set; }
    }

    public class DeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PnpDeviceId { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ClassGuid { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool HasProblem { get; set; }
        public uint ConfigManagerErrorCode { get; set; }
        public string StatusDescription { get; set; } = string.Empty;
    }

    public class DeviceProperties
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PnpDeviceId { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ClassGuid { get; set; } = string.Empty;
        public List<string> HardwareIds { get; set; } = new List<string>();
        public List<string> CompatibleIds { get; set; } = new List<string>();
        public uint ConfigManagerErrorCode { get; set; }
        public DriverInfo DriverInfo { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class DriverInfo
    {
        public string DriverVersion { get; set; } = string.Empty;
        public string DriverDate { get; set; } = string.Empty;
        public string DriverProviderName { get; set; } = string.Empty;
        public string InfName { get; set; } = string.Empty;
        public bool IsSigned { get; set; }
        public string Signer { get; set; } = string.Empty;
        public string DeviceClass { get; set; } = string.Empty;
    }

    #endregion
}
