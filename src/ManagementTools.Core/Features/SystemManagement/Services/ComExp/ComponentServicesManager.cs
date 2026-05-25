using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.ServiceProcess;
using ManagementTools.Core.Features.SystemManagement.Models.ComExp;
using ManagementTools.Core.DependencyInjection;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.SystemManagement.Services.ComExp;

public sealed class ComponentServicesManager
{
    private const string ComAdminProgId = "COMAdmin.COMAdminCatalog";
    private const string DcomRegistryPath = @"SOFTWARE\Classes\AppID";
    private const string DtcServiceName = "MSDTC";
	private static readonly Dictionary<Type, HashSet<string>> MissingComProperties = new();
    private readonly ILogger<ComponentServicesManager> _logger;

    public ComponentServicesManager()
        : this(NullLogger<ComponentServicesManager>.Instance)
    {
    }

    public ComponentServicesManager(ILogger<ComponentServicesManager> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<ComPlusApplicationInfo>> GetComPlusApplicationsAsync()
    {
        return Task.Run<IReadOnlyList<ComPlusApplicationInfo>>(() =>
        {
            var results = new List<ComPlusApplicationInfo>();
            _logger.LogDebug("[ComponentServicesManager] Loading COM+ applications...");

            try
            {
                var catalogType = Type.GetTypeFromProgID(ComAdminProgId, throwOnError: false);
                if (catalogType == null)
                {
                    _logger.LogDebug("[ComponentServicesManager] COMAdmin catalog type not found.");
                    return results;
                }

                dynamic catalog = Activator.CreateInstance(catalogType) ?? throw new InvalidOperationException("COMAdmin catalog instance creation failed.");
                dynamic applications = catalog.GetCollection("Applications");
                try
                {
                    applications.Populate();

                    foreach (var app in applications)
                    {
                        try
                        {
                            var activationValue = ReadComProperty(app, "Activation");
                            var activationDisplay = GetLocalizedActivation(activationValue);

                            var authValue = ReadComProperty(app, "Authentication");
                            var authDisplay = GetLocalizedAuthentication(authValue);

                            var info = new ComPlusApplicationInfo
                            {
                                Name = ReadComProperty(app, "Name") ?? "(Unknown)",
                                Id = ReadComProperty(app, "ID"),
                                Description = ReadComProperty(app, "Description"),
                                Activation = activationDisplay,
                                AuthenticationLevel = authDisplay
                            };
                            results.Add(info);
                        }
                        finally
                        {
                            ReleaseComObject(app);
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(applications);
                    ReleaseComObject(catalog);
                }

                _logger.LogDebug($"[ComponentServicesManager] COM+ applications loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load COM+ applications: {ex}");
            }

            return results.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public Task<IReadOnlyList<DcomApplicationInfo>> GetDcomApplicationsAsync()
    {
        return Task.Run<IReadOnlyList<DcomApplicationInfo>>(() =>
        {
            var results = new List<DcomApplicationInfo>();
            _logger.LogDebug("[ComponentServicesManager] Loading DCOM applications...");

            try
            {
                using var appIdKey = Registry.LocalMachine.OpenSubKey(DcomRegistryPath);
                if (appIdKey == null)
                {
                    _logger.LogDebug("[ComponentServicesManager] DCOM registry key not found.");
                    return results;
                }

                foreach (var subKeyName in appIdKey.GetSubKeyNames())
                {
                    using var subKey = appIdKey.OpenSubKey(subKeyName);
                    if (subKey == null)
                    {
                        continue;
                    }

                    string name = Convert.ToString(subKey.GetValue(null), CultureInfo.InvariantCulture) ?? subKeyName;
                    var info = new DcomApplicationInfo
                    {
                        Name = name,
                        AppId = subKeyName,
                        LocalService = Convert.ToString(subKey.GetValue("LocalService"), CultureInfo.InvariantCulture),
                        RunAs = Convert.ToString(subKey.GetValue("RunAs"), CultureInfo.InvariantCulture),
                        DllSurrogate = Convert.ToString(subKey.GetValue("DllSurrogate"), CultureInfo.InvariantCulture),
                        ServiceParameters = Convert.ToString(subKey.GetValue("ServiceParameters"), CultureInfo.InvariantCulture)
                    };

                    results.Add(info);
                }

                _logger.LogDebug($"[ComponentServicesManager] DCOM applications loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load DCOM applications: {ex}");
            }

            return results.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync()
    {
        return Task.Run<IReadOnlyList<ProcessInfo>>(() =>
        {
            var results = new List<ProcessInfo>();
            _logger.LogDebug("[ComponentServicesManager] Loading running processes...");

            try
            {
                // First, try to enable SeDebugPrivilege to access protected processes
                TryEnableDebugPrivilege();

                // Build a WMI lookup table for process details (fallback for protected processes)
                var wmiProcessData = LoadProcessDataFromWmi();

                foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    string? filePath = null;
                    string? description = null;
                    DateTime? startTime = null;

                    try
                    {
                        // Try direct access first (fastest)
                        filePath = process.MainModule?.FileName;
                        description = process.MainModule?.FileVersionInfo.FileDescription;
                        startTime = process.StartTime;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Fall back to WMI for protected processes
                        if (wmiProcessData.TryGetValue(process.Id, out var wmiInfo))
                        {
                            filePath = wmiInfo.FilePath;
                            description = wmiInfo.Description;
                            startTime = wmiInfo.StartTime;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process has exited, use WMI data if available
                        if (wmiProcessData.TryGetValue(process.Id, out var wmiInfo))
                        {
                            filePath = wmiInfo.FilePath;
                            description = wmiInfo.Description;
                            startTime = wmiInfo.StartTime;
                        }
                    }

                    // Get FileDescription from file if WMI provided path but no description
                    if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(filePath))
                    {
                        try
                        {
                            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                            description = versionInfo.FileDescription;
                        }
                        catch
                        {
                            // Ignore - file may not exist or be accessible
                        }
                    }

                    results.Add(new ProcessInfo
                    {
                        ProcessId = process.Id,
                        Name = process.ProcessName,
                        FilePath = filePath,
                        Description = description,
                        StartTime = startTime
                    });
                }

                _logger.LogDebug($"[ComponentServicesManager] Running processes loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load running processes: {ex}");
            }

            return results;
        });
    }

    public Task<IReadOnlyList<ProcessInfo>> GetComPlusRunningProcessesAsync()
    {
        return Task.Run<IReadOnlyList<ProcessInfo>>(() =>
        {
            var results = new List<ProcessInfo>();
            _logger.LogDebug("[ComponentServicesManager] Loading COM+ running processes...");

            try
            {
                var catalogType = Type.GetTypeFromProgID(ComAdminProgId, throwOnError: false);
                if (catalogType == null)
                {
                    _logger.LogDebug("[ComponentServicesManager] COMAdmin catalog type not found.");
                    return results;
                }

                dynamic catalog = Activator.CreateInstance(catalogType) ?? throw new InvalidOperationException("COMAdmin catalog instance creation failed.");
                dynamic applications = catalog.GetCollection("Applications");
                try
                {
                    applications.Populate();

                    var appIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var app in applications)
                    {
                        try
                        {
                            var appId = ReadComProperty(app, "ID");
                            var name = ReadComProperty(app, "Name");
                            if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(name))
                            {
                                appIdToName[appId] = name;
                            }
                        }
                        finally
                        {
                            ReleaseComObject(app);
                        }
                    }

                    dynamic applicationInstances = catalog.GetCollection("ApplicationInstances");
                    try
                    {
                        applicationInstances.Populate();

                        foreach (var instance in applicationInstances)
                        {
                            try
                            {
                                var appId = ReadComProperty(instance, "Application");
                                var processIdStr = ReadComProperty(instance, "ProcessID");

                                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(processIdStr))
                                {
                                    continue;
                                }

                                if (!int.TryParse(processIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
                                {
                                    continue;
                                }

                                appIdToName.TryGetValue(appId, out string? appName);
                                
                                string? executableName = null;
                                try
                                {
                                    var process = Process.GetProcessById(processId);
                                    executableName = process.ProcessName + ".exe";
                                }
                                catch
                                {
                                    executableName = "dllhost.exe";
                                }

                                bool isNTService = false;
                                bool isPaused = false;
                                bool isRecycling = false;
                                
                                if (!string.IsNullOrWhiteSpace(appId))
                                {
                                    foreach (var app in applications)
                                    {
                                        try
                                        {
                                            var checkAppId = ReadComProperty(app, "ID");
                                            if (string.Equals(checkAppId, appId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                var runAsNTService = ReadComProperty(app, "RunForever");
                                                isNTService = runAsNTService == "1" || string.Equals(runAsNTService, "true", StringComparison.OrdinalIgnoreCase);
                                                break;
                                            }
                                        }
                                        finally
                                        {
                                            ReleaseComObject(app);
                                        }
                                    }
                                }
                                
                                results.Add(new ProcessInfo
                                {
                                    ProcessId = processId,
                                    Name = appName ?? "(Unknown COM+ Application)",
                                    Description = appId,
                                    ExecutableName = executableName,
                                    IsPaused = isPaused,
                                    IsRecycling = isRecycling,
                                    IsNTService = isNTService
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug($"[ComponentServicesManager] Failed to process application instance: {ex.GetType().Name} - {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    _logger.LogDebug($"[ComponentServicesManager] Inner exception: {ex.InnerException.Message}");
                                }
                            }
                            finally
                            {
                                ReleaseComObject(instance);
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(applicationInstances);
                    }
                }
                finally
                {
                    ReleaseComObject(applications);
                    ReleaseComObject(catalog);
                }

                _logger.LogDebug($"[ComponentServicesManager] COM+ running processes loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load COM+ running processes: {ex}");
            }

            return results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public Task<DtcStatusInfo> GetDtcStatusAsync()
    {
        return Task.Run(() =>
        {
            _logger.LogDebug("[ComponentServicesManager] Loading MSDTC status...");
            string status = "Unknown";
            int? processId = null;
            DateTime? startTime = null;

            try
            {
                using var controller = new ServiceController(DtcServiceName);
                status = controller.Status.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to read MSDTC service status: {ex.Message}");
            }

            try
            {
                var msdtc = Process.GetProcessesByName("msdtc").FirstOrDefault();
                if (msdtc != null)
                {
                    processId = msdtc.Id;
                    startTime = msdtc.StartTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to read MSDTC process info: {ex.Message}");
            }

            return new DtcStatusInfo
            {
                ServiceStatus = status,
                ProcessId = processId,
                StartTime = startTime
            };
        });
    }

    public Task<IReadOnlyList<DtcTransactionItem>> GetDtcTransactionListAsync()
    {
        return Task.Run<IReadOnlyList<DtcTransactionItem>>(() =>
        {
            var results = new List<DtcTransactionItem>();
            _logger.LogDebug("[ComponentServicesManager] Loading DTC transaction list...");

            try
            {
                // Query WMI for MSFT_DtcTransactionTask
                using var searcher = new ManagementObjectSearcher(
                    @"root\MsDTC",
                    "SELECT * FROM MSFT_DtcTransactionTask");

                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        var inParams = obj.GetMethodParameters("Get");
                        inParams["DtcName"] = "Local";

                        var outParams = obj.InvokeMethod("Get", inParams, null);
                        if (outParams != null && outParams["cmdletOutput"] is ManagementBaseObject[] transactions)
                        {
                            foreach (var txn in transactions)
                            {
                                var state = txn["State"]?.ToString() ?? "Unknown";
                                var transactionId = txn["TransactionId"]?.ToString() ?? string.Empty;

                                results.Add(new DtcTransactionItem
                                {
                                    Status = state,
                                    UnitOfWorkId = transactionId
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[ComponentServicesManager] Failed to invoke Get method: {ex.Message}");
                    }
                }

                _logger.LogDebug($"[ComponentServicesManager] DTC transaction list loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load DTC transaction list: {ex}");
            }

            return results;
        });
    }

    public Task<DtcTransactionsStatistics?> GetDtcTransactionsStatisticsAsync()
    {
        return Task.Run<DtcTransactionsStatistics?>(() =>
        {
            _logger.LogDebug("[ComponentServicesManager] Loading MSDTC statistics...");

            try
            {
                // Query WMI for transaction statistics
                using var searcher = new ManagementObjectSearcher(
                    @"root\MsDTC",
                    "SELECT * FROM MSFT_DtcTransactionsStatisticsTask");

                // Call the Get method to retrieve statistics
                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        var inParams = obj.GetMethodParameters("Get");
                        inParams["DtcName"] = "Local";

                        var outParams = obj.InvokeMethod("Get", inParams, null);
                        if (outParams != null && outParams["Statistics"] is ManagementBaseObject statsObj)
                        {
                            return new DtcTransactionsStatistics
                            {
                                Open = Convert.ToUInt32(statsObj["Open"]),
                                OpenMax = Convert.ToUInt32(statsObj["OpenMax"]),
                                InDoubt = Convert.ToUInt32(statsObj["InDoubt"]),
                                Committed = Convert.ToUInt32(statsObj["Committed"]),
                                Aborted = Convert.ToUInt32(statsObj["Aborted"]),
                                ForcedCommit = Convert.ToUInt32(statsObj["ForcedCommit"]),
                                ForcedAbort = Convert.ToUInt32(statsObj["ForcedAbort"]),
                                Heuristic = Convert.ToUInt32(statsObj["Heuristic"]),
                                ResponseTimeMin = 0, // Not available in WMI
                                ResponseTimeAverage = 0,
                                ResponseTimeMax = 0
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[ComponentServicesManager] Failed to invoke Get method: {ex.Message}");
                    }
                }

                // Fallback: Try to query DtcTransactionsStatistics directly
                using var statSearcher = new ManagementObjectSearcher(
                    @"root\MsDTC",
                    "SELECT * FROM DtcTransactionsStatistics");

                foreach (ManagementObject obj in statSearcher.Get())
                {
                    return new DtcTransactionsStatistics
                    {
                        Open = Convert.ToUInt32(obj["Open"]),
                        OpenMax = Convert.ToUInt32(obj["OpenMax"]),
                        InDoubt = Convert.ToUInt32(obj["InDoubt"]),
                        Committed = Convert.ToUInt32(obj["Committed"]),
                        Aborted = Convert.ToUInt32(obj["Aborted"]),
                        ForcedCommit = Convert.ToUInt32(obj["ForcedCommit"]),
                        ForcedAbort = Convert.ToUInt32(obj["ForcedAbort"]),
                        Heuristic = Convert.ToUInt32(obj["Heuristic"]),
                        ResponseTimeMin = 0,
                        ResponseTimeAverage = 0,
                        ResponseTimeMax = 0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load MSDTC statistics: {ex}");
            }

            return null;
        });
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            Marshal.ReleaseComObject(obj);
        }
    }

    private int? ReadComIntProperty(object comObject, string propertyName)
    {
        var value = ReadComProperty(comObject, propertyName);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    private string? ReadComProperty(object comObject, string propertyName)
    {
        if (comObject == null)
        {
            return null;
        }

		var comType = comObject.GetType();
		if (IsMissingComProperty(comType, propertyName))
		{
			return null;
		}

        try
        {
            var result = comType.InvokeMember(
                "Value",
                System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.IgnoreCase,
                null,
                comObject,
                new object[] { propertyName });
            if (result != null)
            {
                return Convert.ToString(result, CultureInfo.InvariantCulture);
            }
        }
		catch (System.Reflection.TargetInvocationException ex)
        {
			_logger.LogDebug($"[ComponentServicesManager] Property '{propertyName}' not available: {ex.InnerException?.Message ?? ex.Message}");
			MarkMissingComProperty(comType, propertyName);
        }
        catch (MissingMemberException ex)
        {
			_logger.LogDebug($"[ComponentServicesManager] Property '{propertyName}' missing: {ex.Message}");
			MarkMissingComProperty(comType, propertyName);
        }
        catch (COMException ex)
        {
			_logger.LogDebug($"[ComponentServicesManager] COM error reading '{propertyName}': {ex.Message}");
			MarkMissingComProperty(comType, propertyName);
        }

        return null;
    }

	private static bool IsMissingComProperty(Type comType, string propertyName)
	{
		lock (MissingComProperties)
		{
			return MissingComProperties.TryGetValue(comType, out var missing)
				&& missing.Contains(propertyName);
		}
	}



	private static void MarkMissingComProperty(Type comType, string propertyName)
	{
		lock (MissingComProperties)
		{
			if (!MissingComProperties.TryGetValue(comType, out var missing))
			{
				missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				MissingComProperties[comType] = missing;
			}
			missing.Add(propertyName);
		}
	}

    #region Process Helper Methods

    private sealed record WmiProcessInfo(string? FilePath, string? Description, DateTime? StartTime);

    private Dictionary<int, WmiProcessInfo> LoadProcessDataFromWmi()
    {
        var result = new Dictionary<int, WmiProcessInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, Description, CreationDate FROM Win32_Process");

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var processId = Convert.ToInt32(obj["ProcessId"], CultureInfo.InvariantCulture);
                    var executablePath = obj["ExecutablePath"]?.ToString();
                    var description = obj["Description"]?.ToString();
                    DateTime? creationDate = null;

                    var creationDateStr = obj["CreationDate"]?.ToString();
                    if (!string.IsNullOrEmpty(creationDateStr))
                    {
                        creationDate = ManagementDateTimeConverter.ToDateTime(creationDateStr);
                    }

                    result[processId] = new WmiProcessInfo(executablePath, description, creationDate);
                }
                catch
                {
                    // Skip processes that can't be parsed
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[ComponentServicesManager] WMI process query failed: {ex.Message}");
        }

        return result;
    }

    private static void TryEnableDebugPrivilege()
    {
        try
        {
            using var processHandle = Win32PInvoke.GetCurrentProcess_SafeHandle();
            if (!Win32PInvoke.OpenProcessToken(
                processHandle,
                TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                out var tokenHandle))
            {
                return;
            }

            using (tokenHandle)
            {
                if (!Win32PInvoke.LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                {
                    return;
                }

                unsafe
                {
                    TOKEN_PRIVILEGES tp = default;
                    tp.PrivilegeCount = 1;
                    tp.Privileges[0] = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED
                    };

                    Win32PInvoke.AdjustTokenPrivileges(tokenHandle, false, &tp, Span<byte>.Empty);
                }
            }
        }
        catch
        {
            // Ignore - privilege elevation is best-effort
        }
    }

    #endregion

    #region Localization Helpers

    private static string GetLocalizedActivation(string? activationValue)
    {
        var L = LocalizationProvider.Current;
        return activationValue switch
        {
            "0" => L.GetString(ResourceFileNames.ComExp, "ComExp_Activation_Library"),
            "1" => L.GetString(ResourceFileNames.ComExp, "ComExp_Activation_Server"),
            _ => activationValue ?? L.GetString(ResourceFileNames.ComExp, "ComExp_Format_Unknown")
        };
    }

    private static string GetLocalizedAuthentication(string? authValue)
    {
        var L = LocalizationProvider.Current;
        return authValue switch
        {
            "1" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_None"),
            "2" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Connect"),
            "3" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Call"),
            "4" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Packet"),
            "5" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_PacketIntegrity"),
            "6" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_PacketPrivacy"),
            _ => authValue ?? L.GetString(ResourceFileNames.ComExp, "ComExp_Format_Default")
        };
    }

    #endregion
}






