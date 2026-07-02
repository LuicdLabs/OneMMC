using System;
using System.Diagnostics;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using WmiLight;

namespace OneMMC.Core.Features.SystemManagement.Services.TPM
{
    public class TPMService
    {
        private const string TpmNamespace = @"root\CIMV2\Security\MicrosoftTpm";
        private const string TpmQuery = "SELECT * FROM Win32_Tpm";

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
                using (var connection = new WmiConnection(TpmNamespace))
                {
                    foreach (WmiObject obj in connection.CreateQuery(TpmQuery))
                    {
                        using (obj)
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
            }
            catch (WmiException)
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
                using var connection = new WmiConnection(TpmNamespace);
                var tpmObjects = connection.CreateQuery(TpmQuery).ToList();
                if (tpmObjects.Count == 0)
                {
                    result.Success = false;
                    result.Status = ClearStatus.NotFoundObject;
                    result.ErrorMessage = "Win32_Tpm object not found (WMI does not provide TPM information).";
                    return result;
                }

                try
                {
                    foreach (WmiObject obj in tpmObjects)
                    {
                        // Method 1: Try using Clear method (TPM 2.0 doesn't need OwnerAuth, pass empty string)
                        try
                        {
                            using WmiMethod clearMethod = obj.GetMethod("Clear");
                            using WmiMethodParameters clearParams = clearMethod.CreateInParameters();
                            // For TPM 2.0, OwnerAuth parameter can be empty string
                            clearParams.SetPropertyValue("OwnerAuth", "");
                            uint returnCode = obj.ExecuteMethod<uint>(clearMethod, clearParams, out WmiMethodParameters clearOutParams);
                            clearOutParams?.Dispose();
                            if (returnCode == 0)
                            {
                                result.Success = true;
                                result.Status = ClearStatus.Success;
                                return result;
                            }
                            _logger.LogDebug($"Clear method returned error code: {returnCode}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Clear method failed: {ex.Message}");
                        }

                        // Method 2: Try Disable + Clear combination (some systems need to disable first then clear)
                        try
                        {
                            // Try to disable first
                            using WmiMethod disableMethod = obj.GetMethod("Disable");
                            using WmiMethodParameters disableParams = disableMethod.CreateInParameters();
                            disableParams.SetPropertyValue("OwnerAuth", "");
                            obj.ExecuteMethod<uint>(disableMethod, disableParams, out WmiMethodParameters disableOutParams);
                            disableOutParams?.Dispose();
                        }
                        catch
                        {
                            // Ignore disable failure
                        }

                        // Method 3: Try SetPhysicalPresenceRequest(22) - TPM 2.0 Clear request
                        // 22 = PP_ClearControl(FALSE) + PP_Clear (Set physical presence request to clear TPM)
                        try
                        {
                            using WmiMethod pprMethod = obj.GetMethod("SetPhysicalPresenceRequest");
                            using WmiMethodParameters pprParams = pprMethod.CreateInParameters();
                            // 22 is TPM 2.0 clear operation code
                            pprParams.SetPropertyValue("Request", (uint)22);
                            uint returnCode = obj.ExecuteMethod<uint>(pprMethod, pprParams, out WmiMethodParameters pprOutParams);
                            pprOutParams?.Dispose();
                            if (returnCode == 0)
                            {
                                result.Success = true;
                                result.Status = ClearStatus.Success;
                                result.ErrorMessage = "Clear request set. Please restart the computer to complete TPM clearing.";
                                return result;
                            }
                            _logger.LogDebug($"SetPhysicalPresenceRequest(22) returned error code: {returnCode}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"SetPhysicalPresenceRequest(22) failed: {ex.Message}");
                        }

                        // Method 4: Try legacy SetPhysicalPresenceRequest(5) - TPM 1.2 Clear request
                        try
                        {
                            using WmiMethod pprMethod = obj.GetMethod("SetPhysicalPresenceRequest");
                            using WmiMethodParameters pprParams = pprMethod.CreateInParameters();
                            pprParams.SetPropertyValue("Request", (uint)5);
                            uint returnCode = obj.ExecuteMethod<uint>(pprMethod, pprParams, out WmiMethodParameters pprOutParams);
                            pprOutParams?.Dispose();
                            if (returnCode == 0)
                            {
                                result.Success = true;
                                result.Status = ClearStatus.Success;
                                result.ErrorMessage = "Clear request set. Please restart the computer to complete TPM clearing.";
                                return result;
                            }
                            result.ErrorMessage = $"SetPhysicalPresenceRequest(5) returned error code: {returnCode}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"SetPhysicalPresenceRequest(5) failed: {ex.Message}");
                        }

                        // Method 5: Try ClearTpm method (some OEM implementations)
                        try
                        {
                            using WmiMethod clearTpmMethod = obj.GetMethod("ClearTpm");
                            uint returnCode = obj.ExecuteMethod<uint>(clearTpmMethod, out WmiMethodParameters clearTpmOutParams);
                            clearTpmOutParams?.Dispose();
                            if (returnCode == 0)
                            {
                                result.Success = true;
                                result.Status = ClearStatus.Success;
                                return result;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"ClearTpm method failed: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    foreach (WmiObject obj in tpmObjects)
                    {
                        obj.Dispose();
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
            catch (Exception ex) when (_adminService.IsPermissionError(ex))
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


