using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Management;
using System.ComponentModel;
using OneMMC.Core.Features.PCManagement.Models.Services;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using OneMMC.Core.Infrastructure.Wmi;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Services;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.WindowsServices
{
    public class WindowsServiceManager
    {
        private readonly ILogger<WindowsServiceManager> _logger;

        public WindowsServiceManager(ILogger<WindowsServiceManager> logger)
        {
            _logger = logger;
        }

        #region Service Constants
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SERVICE_QUERY_CONFIG = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_ALL_ACCESS = 0xF01FF;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_AUTO_START = 0x00000002;
        private const uint SERVICE_DEMAND_START = 0x00000003;
        private const uint SERVICE_DISABLED = 0x00000004;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_FAILURE_ACTIONS
        {
            public int dwResetPeriod;
            public IntPtr lpRebootMsg;
            public IntPtr lpCommand;
            public int cActions;
            public IntPtr lpsaActions;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SC_ACTION
        {
            public int Type;
            public int Delay;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_DELAYED_AUTO_START_INFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fDelayedAutostart;
        }

        private const int SC_ACTION_NONE = 0;
        private const int SC_ACTION_RESTART = 1;
        private const int SC_ACTION_REBOOT = 2;
        private const int SC_ACTION_RUN_COMMAND = 3;
        #endregion

        public async Task<List<ServiceInfo>> GetAllServicesAsync()
        {
            return await Task.Run(() =>
            {
                _logger.LogInformation("Enumerating Windows services and WMI metadata.");
                var services = new List<ServiceInfo>();
                var controllers = ServiceController.GetServices();
                
                var wmiData = new Dictionary<string, ServiceWmiData>();
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, StartMode, DelayedAutoStart, StartName, PathName, Description, ProcessId FROM Win32_Service"))
                    {
                        foreach (ManagementObject obj in searcher.GetAndDispose())
                        {
                            var name = obj["Name"]?.ToString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                wmiData[name] = new ServiceWmiData(
                                    obj["StartMode"]?.ToString(),
                                    obj["DelayedAutoStart"] is not null && Convert.ToBoolean(obj["DelayedAutoStart"]),
                                    obj["StartName"]?.ToString() ?? string.Empty,
                                    obj["PathName"]?.ToString() ?? string.Empty,
                                    obj["Description"]?.ToString() ?? string.Empty,
                                    obj["ProcessId"] is not null ? Convert.ToInt32(obj["ProcessId"]) : null);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Handle WMI errors
                }

                foreach (var controller in controllers)
                {
                    using (controller)
                    {
                        var info = new ServiceInfo
                        {
                            Name = controller.ServiceName,
                            DisplayName = controller.DisplayName,
                            Status = GetServiceStatus(controller),
                            Dependencies = GetServiceDependencies(controller),
                            Dependents = GetServiceDependents(controller)
                        };

                        if (wmiData.TryGetValue(controller.ServiceName, out var wmiObj))
                        {
                            info.Description = wmiObj.Description;
                            string? startMode = wmiObj.StartMode;
                            if (startMode == "Auto")
                            {
                                if (wmiObj.DelayedAutoStart)
                                {
                                    info.StartupType = "Automatic (Delayed Start)";
                                }
                                else
                                {
                                    info.StartupType = "Auto";
                                }
                            }
                            else if (startMode == "Manual")
                            {
                                info.StartupType = "Manual";
                            }
                            else if (startMode == "Disabled")
                            {
                                info.StartupType = "Disabled";
                            }
                            else
                            {
                                info.StartupType = startMode ?? string.Empty;
                            }
                            info.LogOnAs = wmiObj.StartName;
                            info.BinaryPath = wmiObj.PathName;
                            if (wmiObj.ProcessId is not null)
                            {
                                 info.ProcessId = wmiObj.ProcessId.Value;
                            }
                        }
                    
                        services.Add(info);
                    }
                }

                _logger.LogInformation("Service enumeration completed. Count={ServiceCount}", services.Count);
                return services.OrderBy(s => s.DisplayName).ToList();
            });
        }

        private string GetServiceStatus(ServiceController controller)
        {
            try
            {
                return controller.Status.ToString();
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read status for service {ServiceName}.", controller.ServiceName);
                return "Unknown";
            }
        }

        private List<string> GetServiceDependencies(ServiceController controller)
        {
            try
            {
                ServiceController[] dependencies = controller.ServicesDependedOn;
                try
                {
                    return dependencies.Select(s => s.ServiceName).ToList();
                }
                finally
                {
                    foreach (ServiceController dependency in dependencies)
                    {
                        dependency.Dispose();
                    }
                }
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read dependencies for service {ServiceName}.", controller.ServiceName);
                return new List<string>();
            }
        }

        private List<string> GetServiceDependents(ServiceController controller)
        {
            try
            {
                ServiceController[] dependents = controller.DependentServices;
                try
                {
                    return dependents.Select(s => s.ServiceName).ToList();
                }
                finally
                {
                    foreach (ServiceController dependent in dependents)
                    {
                        dependent.Dispose();
                    }
                }
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read dependents for service {ServiceName}.", controller.ServiceName);
                return new List<string>();
            }
        }

        public async Task StartServiceAsync(string serviceName)
        {
            await Task.Run(() =>
            {
                using (var controller = new ServiceController(serviceName))
                {
                    if (controller.Status != ServiceControllerStatus.Running)
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                }
            });
        }

        public async Task StopServiceAsync(string serviceName)
        {
            await Task.Run(() =>
            {
                using (var controller = new ServiceController(serviceName))
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }
            });
        }

        public async Task RestartServiceAsync(string serviceName)
        {
            await StopServiceAsync(serviceName);
            await StartServiceAsync(serviceName);
        }

        public async Task SetStartupTypeAsync(string serviceName, string startupType)
        {
            await Task.Run(() =>
            {
                uint startType = SERVICE_DEMAND_START;
                bool delayedAuto = false;

                switch (startupType.ToLower())
                {
                    case "auto":
                    case "automatic":
                        startType = SERVICE_AUTO_START;
                        break;
                    case "manual":
                        startType = SERVICE_DEMAND_START;
                        break;
                    case "disabled":
                        startType = SERVICE_DISABLED;
                        break;
                    case "delayed-auto":
                        startType = SERVICE_AUTO_START;
                        delayedAuto = true;
                        break;
                }

                using var hSCManager = Win32PInvoke.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
                if (hSCManager.IsInvalid) throw new Exception("Failed to open SC Manager.");

                using var hService = Win32PInvoke.OpenService(hSCManager, serviceName, SERVICE_CHANGE_CONFIG);
                if (hService.IsInvalid) throw new Exception("Failed to open service.");

                if (!Win32PInvoke.ChangeServiceConfig(
                    hService,
                    unchecked((ENUM_SERVICE_TYPE)SERVICE_NO_CHANGE),
                    (SERVICE_START_TYPE)startType,
                    unchecked((SERVICE_ERROR)SERVICE_NO_CHANGE),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null))
                {
                    throw new Exception($"Failed to set startup type. Error: {Marshal.GetLastWin32Error()}");
                }

                if (startType == SERVICE_AUTO_START)
                {
                    var delayedInfo = new SERVICE_DELAYED_AUTO_START_INFO
                    {
                        fDelayedAutostart = delayedAuto
                    };

                    unsafe
                    {
                        if (!Win32PInvoke.ChangeServiceConfig2W(
                            hService,
                            SERVICE_CONFIG.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                            &delayedInfo))
                        {
                            throw new Exception($"Failed to set delayed auto-start. Error: {Marshal.GetLastWin32Error()}");
                        }
                    }
                }
            });
        }

        public async Task SetLogOnAccountAsync(string serviceName, string username, string? password)
        {
             await Task.Run(() =>
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'"))
                {
                    foreach (ManagementObject service in searcher.GetAndDispose())
                    {
                        var parameters = service.GetMethodParameters("Change");
                        parameters["StartName"] = username;
                        if (!string.IsNullOrEmpty(password))
                        {
                            parameters["StartPassword"] = password;
                        }
                        service.InvokeMethod("Change", parameters, null);
                    }
                }
            });
        }
        
        public async Task SetRecoveryOptionsAsync(string serviceName, string first, string second, string subsequent, int resetSeconds)
        {
            await SetRecoveryOptionsInternalAsync(serviceName, first, second, subsequent, resetSeconds);
        }

        private async Task SetRecoveryOptionsInternalAsync(string serviceName, string first, string second, string subsequent, int resetSeconds)
        {
             await Task.Run(() =>
             {
                 if (RequiresShutdownPrivilege(first, second, subsequent))
                 {
                     EnablePrivilege("SeShutdownPrivilege");
                 }

                 using var hSCManager = Win32PInvoke.OpenSCManager(null, null, SC_MANAGER_CONNECT);
                 if (hSCManager.IsInvalid) throw new Exception("Failed to open SC Manager.");

                 using var hService = Win32PInvoke.OpenService(hSCManager, serviceName, SERVICE_ALL_ACCESS);
                 if (hService.IsInvalid) throw new Exception("Failed to open service.");

                 int count = 3;
                 int actionSize = Marshal.SizeOf(typeof(SC_ACTION));
                 IntPtr actionsPtr = Marshal.AllocHGlobal(actionSize * count);

                 try
                 {
                     var actions = new SC_ACTION[count];
                     actions[0] = new SC_ACTION { Type = GetActionType(first), Delay = 60000 };
                     actions[1] = new SC_ACTION { Type = GetActionType(second), Delay = 60000 };
                     actions[2] = new SC_ACTION { Type = GetActionType(subsequent), Delay = 60000 };

                     for (int i = 0; i < count; i++)
                     {
                         Marshal.StructureToPtr(actions[i], actionsPtr + (i * actionSize), false);
                     }

                     var failureActions = new SERVICE_FAILURE_ACTIONS
                     {
                         dwResetPeriod = resetSeconds,
                         lpRebootMsg = IntPtr.Zero,
                         lpCommand = IntPtr.Zero,
                         cActions = count,
                         lpsaActions = actionsPtr
                     };

                     unsafe
                     {
                         if (!Win32PInvoke.ChangeServiceConfig2W(
                             hService,
                             SERVICE_CONFIG.SERVICE_CONFIG_FAILURE_ACTIONS,
                             &failureActions))
                         {
                             throw new Exception($"Failed to set recovery options. Error: {Marshal.GetLastWin32Error()}");
                         }
                     }
                 }
                 finally
                 {
                     Marshal.FreeHGlobal(actionsPtr);
                 }
             });
        }

        private static bool RequiresShutdownPrivilege(string first, string second, string subsequent)
        {
            return string.Equals(first, "reboot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(second, "reboot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subsequent, "reboot", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnablePrivilege(string privilegeName)
        {
            using var processHandle = Win32PInvoke.GetCurrentProcess_SafeHandle();
            if (!Win32PInvoke.OpenProcessToken(
                processHandle,
                TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                out var tokenHandle))
            {
                throw new Exception($"Failed to open process token. Error: {Marshal.GetLastWin32Error()}");
            }

            using (tokenHandle)
            {
                if (!Win32PInvoke.LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    throw new Exception($"Failed to lookup privilege {privilegeName}. Error: {Marshal.GetLastWin32Error()}");
                }

                unsafe
                {
                    TOKEN_PRIVILEGES privileges = default;
                    privileges.PrivilegeCount = 1;
                    privileges.Privileges[0] = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED
                    };

                    if (!Win32PInvoke.AdjustTokenPrivileges(tokenHandle, false, &privileges, Span<byte>.Empty))
                    {
                        throw new Exception($"Failed to enable privilege {privilegeName}. Error: {Marshal.GetLastWin32Error()}");
                    }

                    int error = Marshal.GetLastWin32Error();
                    if (error != 0)
                    {
                        throw new Exception($"Failed to enable privilege {privilegeName}. Error: {error}");
                    }
                }
            }
        }

        public async Task<(string First, string Second, string Subsequent, double ResetDays)> GetServiceRecoveryInfoAsync(string serviceName)
        {
             return await Task.Run(() =>
             {
                 using var hSCManager = Win32PInvoke.OpenSCManager(null, null, SC_MANAGER_CONNECT);
                 if (hSCManager.IsInvalid) throw new Exception("Failed to open SC Manager.");

                 using var hService = Win32PInvoke.OpenService(hSCManager, serviceName, SERVICE_QUERY_CONFIG);
                 if (hService.IsInvalid) throw new Exception("Failed to open service.");

                 uint bytesNeeded = 0;
                 Win32PInvoke.QueryServiceConfig2W(
                     hService,
                     SERVICE_CONFIG.SERVICE_CONFIG_FAILURE_ACTIONS,
                     Span<byte>.Empty,
                     out bytesNeeded);

                 if (bytesNeeded == 0) return ("none", "none", "none", 0.0);

                 byte[] buffer = new byte[bytesNeeded];

                 unsafe
                 {
                     if (!Win32PInvoke.QueryServiceConfig2W(
                         hService,
                         SERVICE_CONFIG.SERVICE_CONFIG_FAILURE_ACTIONS,
                         buffer,
                         out bytesNeeded))
                     {
                         throw new Exception($"Failed to query service config. Error: {Marshal.GetLastWin32Error()}");
                     }

                     fixed (byte* bufferPtr = buffer)
                     {
                         var failureActions = Marshal.PtrToStructure<SERVICE_FAILURE_ACTIONS>((IntPtr)bufferPtr);

                         string first = "none";
                         string second = "none";
                         string subsequent = "none";
                         double resetDays = failureActions.dwResetPeriod / 86400.0;

                         if (failureActions.cActions > 0 && failureActions.lpsaActions != IntPtr.Zero)
                         {
                             int actionSize = Marshal.SizeOf(typeof(SC_ACTION));

                             if (failureActions.cActions > 0)
                             {
                                 var action = Marshal.PtrToStructure<SC_ACTION>(failureActions.lpsaActions);
                                 first = GetActionString(action.Type);
                             }

                             if (failureActions.cActions > 1)
                             {
                                 var action = Marshal.PtrToStructure<SC_ACTION>(failureActions.lpsaActions + actionSize);
                                 second = GetActionString(action.Type);
                             }

                             if (failureActions.cActions > 2)
                             {
                                 var action = Marshal.PtrToStructure<SC_ACTION>(failureActions.lpsaActions + (2 * actionSize));
                                 subsequent = GetActionString(action.Type);
                             }
                             else if (failureActions.cActions > 0)
                             {
                                 var action = Marshal.PtrToStructure<SC_ACTION>(failureActions.lpsaActions + ((failureActions.cActions - 1) * actionSize));
                                 subsequent = GetActionString(action.Type);
                             }
                         }

                         return (first, second, subsequent, resetDays);
                     }
                 }
             });
        }

        private int GetActionType(string action)
        {
            if (string.Equals(action, "restart", StringComparison.OrdinalIgnoreCase)) return SC_ACTION_RESTART;
            if (string.Equals(action, "reboot", StringComparison.OrdinalIgnoreCase)) return SC_ACTION_REBOOT;
            if (string.Equals(action, "run", StringComparison.OrdinalIgnoreCase)) return SC_ACTION_RUN_COMMAND;
            return SC_ACTION_NONE;
        }

        private string GetActionString(int type)
        {
            switch (type)
            {
                case SC_ACTION_RESTART: return "restart";
                case SC_ACTION_REBOOT: return "reboot";
                case SC_ACTION_RUN_COMMAND: return "run";
                default: return "none";
            }
        }

        private sealed record ServiceWmiData(
            string? StartMode,
            bool DelayedAutoStart,
            string StartName,
            string PathName,
            string Description,
            int? ProcessId);
    }
}
