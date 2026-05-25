using System;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.Management;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.SystemManagement.Services.TPM
{
    public class TPMService
    {
        private readonly ILogger<TPMService> _logger;
        private readonly IAdminService _adminService;

        public TPMService(ILogger<TPMService> logger, IAdminService adminService)
        {
            _logger = logger;
            _adminService = adminService;
        }

        public enum ClearStatus
        {
            Success,
            NotFoundObject,
            NoSuitableMethod,
            NeedsParameters,
            RequiresAdmin,
            InvocationFailed,
            Unknown
        }

        public class ClearResult
        {
            public bool Success { get; set; }
            public ClearStatus Status { get; set; } = ClearStatus.Unknown;
            public string ErrorMessage { get; set; } = string.Empty;
        }

        public TPMInfo GetTPMInformation()
        {
            var info = new TPMInfo();

            try
            {
                // Query TPM using WMI
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT * FROM Win32_Tpm"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        // Get TPM version
                        info.SpecVersion = obj["SpecVersion"]?.ToString() ?? "Unknown";
                        info.ManufacturerVersion = obj["ManufacturerVersion"]?.ToString() ?? "Unknown";
                        info.ManufacturerId = obj["ManufacturerId"]?.ToString() ?? "Unknown";
                        info.ManufacturerName = GetManufacturerName(obj["ManufacturerId"]);

                        // Get TPM status
                        info.IsEnabled = Convert.ToBoolean(obj["IsEnabled_InitialValue"]);
                        info.IsActivated = Convert.ToBoolean(obj["IsActivated_InitialValue"]);
                        info.IsOwned = Convert.ToBoolean(obj["IsOwned_InitialValue"]);

                        // Check if TPM is ready (all indicators are true)
                        info.IsReady = info.IsEnabled && info.IsActivated && info.IsOwned;

                        info.IsAvailable = true;
                    }
                }
            }
            catch (ManagementException)
            {
                // TPM might not be available or accessible
                info.IsAvailable = false;
                info.ErrorMessage = _adminService.IsRunningAsAdmin
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.TPM, TPMKeys.NotAvailable)
                    : LocalizationProvider.Current.GetString(ResourceFileNames.TPM, TPMKeys.AccessDenied);
            }
            catch (Exception ex)
            {
                info.IsAvailable = false;
                info.ErrorMessage = $"Error: {ex.Message}";
            }

            return info;
        }

        private static string GetManufacturerName(object? manufacturerId)
        {
            if (manufacturerId is null)
            {
                return "Unknown";
            }

            var id = Convert.ToUInt32(manufacturerId);
            if (id == 0)
            {
                return "Unknown";
            }

            var chars = new[]
            {
                (char)((id >> 24) & 0xFF),
                (char)((id >> 16) & 0xFF),
                (char)((id >> 8) & 0xFF),
                (char)(id & 0xFF)
            };

            var manufacturerName = new string(chars).TrimEnd('\0', ' ');
            return string.IsNullOrWhiteSpace(manufacturerName) ? "Unknown" : manufacturerName;
        }

        public bool OpenTPMConsole()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tpm.msc",
                    UseShellExecute = true,
                    Verb = "runas" // Run as administrator
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public ClearResult ClearTPM()
        {
            var result = new ClearResult();

            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT * FROM Win32_Tpm"))
                {
                    var collection = searcher.Get();
                    if (collection.Count == 0)
                    {
                        result.Success = false;
                        result.Status = ClearStatus.NotFoundObject;
                        result.ErrorMessage = "Win32_Tpm object not found (WMI does not provide TPM information).";
                        return result;
                    }

                    foreach (ManagementObject obj in collection)
                    {
                        // Method 1: Try using Clear method (TPM 2.0 doesn't need OwnerAuth, pass empty string)
                        try
                        {
                            var clearParams = obj.GetMethodParameters("Clear");
                            if (clearParams != null)
                            {
                                // For TPM 2.0, OwnerAuth parameter can be empty string
                                clearParams["OwnerAuth"] = "";
                                var outParams = obj.InvokeMethod("Clear", clearParams, null);

                                if (outParams != null && outParams["ReturnValue"] != null)
                                {
                                    var returnCode = Convert.ToUInt32(outParams["ReturnValue"]);
                                    if (returnCode == 0)
                                    {
                                        result.Success = true;
                                        result.Status = ClearStatus.Success;
                                        return result;
                                    }
                                    _logger.LogDebug($"Clear method returned error code: {returnCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Clear method failed: {ex.Message}");
                        }

                        // Method 2: Try Disable + Clear combination (some systems need to disable first then clear)
                        try
                        {
                            // Try to disable first
                            var disableParams = obj.GetMethodParameters("Disable");
                            if (disableParams != null)
                            {
                                disableParams["OwnerAuth"] = "";
                                obj.InvokeMethod("Disable", disableParams, null);
                            }
                        }
                        catch
                        {
                            // Ignore disable failure
                        }

                        // Method 3: Try SetPhysicalPresenceRequest(22) - TPM 2.0 Clear request
                        // 22 = PP_ClearControl(FALSE) + PP_Clear (Set physical presence request to clear TPM)
                        try
                        {
                            var methodParams = obj.GetMethodParameters("SetPhysicalPresenceRequest");
                            if (methodParams != null)
                            {
                                // 22 is TPM 2.0 clear operation code
                                methodParams["Request"] = (uint)22;
                                var outParams = obj.InvokeMethod("SetPhysicalPresenceRequest", methodParams, null);

                                if (outParams != null && outParams["ReturnValue"] != null)
                                {
                                    var returnCode = Convert.ToUInt32(outParams["ReturnValue"]);
                                    if (returnCode == 0)
                                    {
                                        result.Success = true;
                                        result.Status = ClearStatus.Success;
                                        result.ErrorMessage = "Clear request set. Please restart the computer to complete TPM clearing.";
                                        return result;
                                    }
                                    _logger.LogDebug($"SetPhysicalPresenceRequest(22) returned error code: {returnCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"SetPhysicalPresenceRequest(22) failed: {ex.Message}");
                        }

                        // Method 4: Try legacy SetPhysicalPresenceRequest(5) - TPM 1.2 Clear request
                        try
                        {
                            var methodParams = obj.GetMethodParameters("SetPhysicalPresenceRequest");
                            if (methodParams != null)
                            {
                                methodParams["Request"] = (uint)5;
                                var outParams = obj.InvokeMethod("SetPhysicalPresenceRequest", methodParams, null);

                                if (outParams != null && outParams["ReturnValue"] != null)
                                {
                                    var returnCode = Convert.ToUInt32(outParams["ReturnValue"]);
                                    if (returnCode == 0)
                                    {
                                        result.Success = true;
                                        result.Status = ClearStatus.Success;
                                        result.ErrorMessage = "Clear request set. Please restart the computer to complete TPM clearing.";
                                        return result;
                                    }
                                    result.ErrorMessage = $"SetPhysicalPresenceRequest(5) returned error code: {returnCode}";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"SetPhysicalPresenceRequest(5) failed: {ex.Message}");
                        }

                        // Method 5: Try ClearTpm method (some OEM implementations)
                        try
                        {
                            var outParams = obj.InvokeMethod("ClearTpm", null, null);
                            if (outParams != null && outParams["ReturnValue"] != null)
                            {
                                var returnCode = Convert.ToUInt32(outParams["ReturnValue"]);
                                if (returnCode == 0)
                                {
                                    result.Success = true;
                                    result.Status = ClearStatus.Success;
                                    return result;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"ClearTpm method failed: {ex.Message}");
                        }
                    }
                }

                // If we reached here, we failed to clear
                result.Success = false;
                result.Status = ClearStatus.InvocationFailed;
                if (string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.TPM, TPMKeys.ClearAllMethodsFailed);
                }
                return result;
            }
            catch (UnauthorizedAccessException)
            {
                return new ClearResult { Success = false, Status = ClearStatus.RequiresAdmin, ErrorMessage = LocalizationProvider.Current.GetString(ResourceFileNames.TPM, TPMKeys.WmiAccessDenied) };
            }
            catch (Exception ex)
            {
                return new ClearResult { Success = false, Status = ClearStatus.Unknown, ErrorMessage = $"An error occurred during execution: {ex.Message}" };
            }
        }
    }

    public class TPMInfo
    {
        public bool IsAvailable { get; set; }
        public bool IsReady { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsActivated { get; set; }
        public bool IsOwned { get; set; }
        public string SpecVersion { get; set; } = "Unknown";
        public string ManufacturerVersion { get; set; } = "Unknown";
        public string ManufacturerId { get; set; } = "Unknown";
        public string ManufacturerName { get; set; } = "Unknown";
        public string ErrorMessage { get; set; } = "";
    }
}


