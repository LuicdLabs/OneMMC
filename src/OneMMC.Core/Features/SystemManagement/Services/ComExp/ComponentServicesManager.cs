using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.ServiceProcess;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp.Native;
using OneMMC.Core.DependencyInjection;
using OneMMC.Core.Infrastructure.Interop;
using OneMMC.Core.Infrastructure.Wmi;
using OneMMC.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using WmiLight;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.SystemManagement.Services.ComExp;

public sealed class ComponentServicesManager
{
    private const string DcomRegistryPath = @"SOFTWARE\Classes\AppID";
    private const string DtcServiceName = "MSDTC";
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

            ICOMAdminCatalog? catalog = null;
            ICatalogCollection? applications = null;
            try
            {
                catalog = ComActivator.CreateInstance<ICOMAdminCatalog>(ComAdminCatalogClsid.ComAdminCatalog);
                catalog.GetCollection("Applications", out applications);
                applications.Populate();

                int count = applications.get_Count();
                for (int i = 0; i < count; i++)
                {
                    applications.get_Item(i, out ICatalogObject app);
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
                        ComActivator.Release(app);
                    }
                }

                _logger.LogDebug($"[ComponentServicesManager] COM+ applications loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load COM+ applications: {ex}");
            }
            finally
            {
                ComActivator.Release(applications);
                ComActivator.Release(catalog);
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

                // Resolve executable paths up front with a single CLSID sweep so per-app
                // lookups below stay dictionary lookups (HKCR\CLSID holds 7000+ keys).
                var localPaths = BuildAppIdLocalPathMap();

                foreach (var subKeyName in appIdKey.GetSubKeyNames())
                {
                    // Named <ExeName> keys are just AppID mappings, not DCOM entries.
                    // Component Services lists only {GUID} application identities.
                    if (!IsAppIdGuid(subKeyName))
                    {
                        continue;
                    }

                    using var subKey = appIdKey.OpenSubKey(subKeyName);
                    if (subKey == null)
                    {
                        continue;
                    }

                    string? name = Convert.ToString(subKey.GetValue(null), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = subKeyName;
                    }

                    string? localService = Convert.ToString(subKey.GetValue("LocalService"), CultureInfo.InvariantCulture);
                    string? runAs = Convert.ToString(subKey.GetValue("RunAs"), CultureInfo.InvariantCulture);
                    string? serviceParameters = Convert.ToString(subKey.GetValue("ServiceParameters"), CultureInfo.InvariantCulture);
                    string? remoteServerName = Convert.ToString(subKey.GetValue("RemoteServerName"), CultureInfo.InvariantCulture);

                    var valueNames = new HashSet<string>(subKey.GetValueNames(), StringComparer.OrdinalIgnoreCase);
                    bool hasDllSurrogate = valueNames.Contains("DllSurrogate");
                    string? dllSurrogate = hasDllSurrogate
                        ? Convert.ToString(subKey.GetValue("DllSurrogate"), CultureInfo.InvariantCulture)
                        : null;

                    uint? authenticationLevel = ReadDword(subKey, "AuthenticationLevel");

                    if (!localPaths.TryGetValue(subKeyName, out string? localPath))
                    {
                        localPath = null;
                    }

                    if (string.IsNullOrEmpty(localPath) && LooksLikePath(name))
                    {
                        localPath = name;
                    }

                    bool isService = !string.IsNullOrEmpty(localService);
                    var info = new DcomApplicationInfo
                    {
                        Name = name,
                        AppId = subKeyName,
                        LocalService = localService,
                        RunAs = runAs,
                        DllSurrogate = dllSurrogate,
                        HasDllSurrogate = hasDllSurrogate,
                        ServiceParameters = serviceParameters,
                        AuthenticationLevel = authenticationLevel,
                        AuthenticationLevelDisplay = GetLocalizedAuthentication(authenticationLevel?.ToString(CultureInfo.InvariantCulture)),
                        ApplicationType = GetLocalizedApplicationType(isService, hasDllSurrogate),
                        IsService = isService,
                        LocalPath = localPath,
                        RemoteServerName = remoteServerName,
                        RunOnThisComputer = !valueNames.Contains("_LocalService"),
                        HasCustomLaunchPermissions = valueNames.Contains("LaunchPermission"),
                        HasCustomAccessPermissions = valueNames.Contains("AccessPermission"),
                        IdentityDisplay = GetLocalizedIdentity(localService, runAs)
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

    /// <summary>
    /// Checks whether an AppID subkey name is a GUID identity (<c>{...}</c>).
    /// Plain executable names are Exe-to-AppID mappings and are not DCOM entries.
    /// </summary>
    private static bool IsAppIdGuid(string subKeyName)
    {
        return subKeyName.Length == 38
            && subKeyName[0] == '{'
            && subKeyName[^1] == '}'
            && Guid.TryParse(subKeyName, out _);
    }

    /// <summary>
    /// Reads a REG_DWORD value, returning <see langword="null"/> when absent or non-numeric.
    /// </summary>
    private static uint? ReadDword(RegistryKey key, string valueName)
    {
        try
        {
            object? raw = key.GetValue(valueName);
            return raw switch
            {
                int i => unchecked((uint)i),
                uint u => u,
                long l => unchecked((uint)l),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an AppID-to-executable map with a single sweep over <c>HKCR\CLSID</c>,
    /// preferring <c>LocalServer32</c> and falling back to <c>InprocServer32</c>.
    /// </summary>
    private Dictionary<string, string> BuildAppIdLocalPathMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inprocFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var clsidKey = Registry.ClassesRoot.OpenSubKey("CLSID");
            if (clsidKey == null)
            {
                return map;
            }

            foreach (var clsidName in clsidKey.GetSubKeyNames())
            {
                string? appId = null;
                try
                {
                    using var clsidSubKey = clsidKey.OpenSubKey(clsidName);
                    if (clsidSubKey == null)
                    {
                        continue;
                    }

                    appId = Convert.ToString(clsidSubKey.GetValue("AppID"), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(appId))
                    {
                        continue;
                    }

                    if (map.ContainsKey(appId))
                    {
                        continue;
                    }

                    string? localServer = ReadDefaultValue(clsidSubKey, "LocalServer32");
                    if (!string.IsNullOrWhiteSpace(localServer))
                    {
                        map[appId] = localServer;
                        continue;
                    }

                    if (!inprocFallback.ContainsKey(appId))
                    {
                        string? inproc = ReadDefaultValue(clsidSubKey, "InprocServer32");
                        if (!string.IsNullOrWhiteSpace(inproc))
                        {
                            inprocFallback[appId] = inproc;
                        }
                    }
                }
                catch
                {
                    // Skip unreadable CLSID keys; best-effort enrichment only.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[ComponentServicesManager] CLSID sweep for DCOM paths failed: {ex.Message}");
        }

        foreach (var entry in inprocFallback)
        {
            map.TryAdd(entry.Key, entry.Value);
        }

        return map;
    }

    private static string? ReadDefaultValue(RegistryKey parent, string subKeyName)
    {
        try
        {
            using var subKey = parent.OpenSubKey(subKeyName);
            return Convert.ToString(subKey?.GetValue(null), CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains('\\', StringComparison.Ordinal)
            || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ocx", StringComparison.OrdinalIgnoreCase);
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

    public Task<IReadOnlyList<ComPlusRunningProcess>> GetComPlusRunningProcessesAsync()
    {
        return Task.Run<IReadOnlyList<ComPlusRunningProcess>>(() =>
        {
            var results = new List<ComPlusRunningProcess>();
            _logger.LogDebug("[ComponentServicesManager] Loading COM+ running processes...");

            ICOMAdminCatalog? catalog = null;
            ICatalogCollection? applications = null;
            ICatalogCollection? applicationInstances = null;
            try
            {
                catalog = ComActivator.CreateInstance<ICOMAdminCatalog>(ComAdminCatalogClsid.ComAdminCatalog);

                // Build appId lookups from the Applications collection up front so
                // the ApplicationInstances loop is a pure dictionary lookup (no nested COM enumeration).
                catalog.GetCollection("Applications", out applications);
                applications.Populate();

                var appIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var appIdToActivation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var appIdToDescription = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                int appCount = applications.get_Count();
                for (int i = 0; i < appCount; i++)
                {
                    applications.get_Item(i, out ICatalogObject app);
                    try
                    {
                        var appId = ReadComProperty(app, "ID");
                        if (string.IsNullOrWhiteSpace(appId))
                        {
                            continue;
                        }

                        var name = ReadComProperty(app, "Name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            appIdToName[appId] = name;
                        }

                        var activation = ReadComProperty(app, "Activation");
                        if (!string.IsNullOrWhiteSpace(activation))
                        {
                            appIdToActivation[appId] = activation;
                        }

                        var description = ReadComProperty(app, "Description");
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            appIdToDescription[appId] = description;
                        }
                    }
                    finally
                    {
                        ComActivator.Release(app);
                    }
                }

                // A process is an NT-service host when the SCM reports a service on its PID
                // (e.g. COMSysApp hosting System Application) — verified against MMC output.
                var servicePids = LoadServiceProcessIds();

                catalog.GetCollection("ApplicationInstances", out applicationInstances);
                applicationInstances.Populate();

                int instanceCount = applicationInstances.get_Count();
                for (int i = 0; i < instanceCount; i++)
                {
                    applicationInstances.get_Item(i, out ICatalogObject instance);
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
                        appName ??= appId;
                        appIdToActivation.TryGetValue(appId, out string? activation);
                        appIdToDescription.TryGetValue(appId, out string? appDescription);

                        string? executableName;
                        string? filePath = null;
                        try
                        {
                            var process = Process.GetProcessById(processId);
                            executableName = process.ProcessName + ".exe";
                            try
                            {
                                filePath = process.MainModule?.FileName;
                            }
                            catch
                            {
                                // Protected process; name is enough.
                            }
                        }
                        catch
                        {
                            executableName = "dllhost.exe";
                        }

                        results.Add(new ComPlusRunningProcess
                        {
                            ProcessId = processId,
                            Name = appName,
                            ExecutableName = executableName,
                            FilePath = filePath,
                            IsPaused = ParseComBool(ReadComProperty(instance, "IsPaused")),
                            IsRecycling = ParseComBool(ReadComProperty(instance, "HasRecycled")),
                            IsNTService = servicePids.Contains(processId),
                            Instance = new ComPlusApplicationInstance
                            {
                                ApplicationId = appId,
                                ApplicationName = appName,
                                Description = appDescription,
                                PartitionId = ReadComProperty(instance, "PartitionID"),
                                InstanceId = ReadComProperty(instance, "InstanceID"),
                                ActivationType = GetLocalizedActivation(activation),
                                Components = LoadComponents(applications, appId)
                            }
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
                        ComActivator.Release(instance);
                    }
                }

                _logger.LogDebug($"[ComponentServicesManager] COM+ running processes loaded: {results.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Failed to load COM+ running processes: {ex}");
            }
            finally
            {
                ComActivator.Release(applicationInstances);
                ComActivator.Release(applications);
                ComActivator.Release(catalog);
            }

            return results
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId)
                .ToList();
        });
    }

    /// <summary>
    /// Loads the hosted components of a COM+ application via the related
    /// <c>Components</c> collection (keyed by application ID).
    /// </summary>
    private List<ComPlusComponentInfo> LoadComponents(ICatalogCollection applications, string appId)
    {
        var results = new List<ComPlusComponentInfo>();
        ICatalogCollection? components = null;
        Variant key = Variant.FromString(appId);
        try
        {
            try
            {
                applications.GetCollection("Components", key, out components);
                components.Populate();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ComponentServicesManager] Components collection unavailable for {appId}: {ex.Message}");
                return results;
            }

            int count = components.get_Count();
            for (int i = 0; i < count; i++)
            {
                components.get_Item(i, out ICatalogObject component);
                try
                {
                    var clsid = ReadComProperty(component, "CLSID");
                    var progId = ReadComProperty(component, "ProgID");
                    var description = ReadComProperty(component, "Description");
                    results.Add(new ComPlusComponentInfo
                    {
                        DisplayName = progId ?? description ?? clsid ?? appId,
                        Clsid = clsid,
                        ProgId = progId,
                        DllPath = ReadComProperty(component, "DLL"),
                        Description = description
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[ComponentServicesManager] Failed to read component: {ex.Message}");
                }
                finally
                {
                    ComActivator.Release(component);
                }
            }
        }
        finally
        {
            key.Clear();
            ComActivator.Release(components);
        }

        return results.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Parses a COM catalog boolean rendered by <see cref="Variant.ToInvariantString"/>
    /// (<c>"True"/"False"</c>) plus the numeric forms some providers return.
    /// </summary>
    private static bool ParseComBool(string? value)
    {
        return string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "-1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the set of PIDs hosting an SCM service (single WMI query),
    /// used to reproduce the classic "NT Service" column.
    /// </summary>
    private HashSet<int> LoadServiceProcessIds()
    {
        var result = new HashSet<int>();
        try
        {
            using var connection = new WmiConnection();
            foreach (WmiObject obj in connection.CreateQuery("SELECT ProcessId FROM Win32_Service").DisposeItems())
            {
                try
                {
                    var raw = obj["ProcessId"];
                    if (raw is not null && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) && pid != 0)
                    {
                        result.Add(pid);
                    }
                }
                catch
                {
                    // Skip unparsable entries.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[ComponentServicesManager] Service PID query failed: {ex.Message}");
        }

        return result;
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
                using var connection = new WmiConnection(@"root\MsDTC");

                foreach (WmiObject obj in connection.CreateQuery("SELECT * FROM MSFT_DtcTransactionTask").DisposeItems())
                {
                    try
                    {
                        using WmiMethod getMethod = obj.GetMethod("Get");
                        using WmiMethodParameters inParams = getMethod.CreateInParameters();
                        inParams.SetPropertyValue("DtcName", "Local");

                        obj.ExecuteMethod(getMethod, inParams, out WmiMethodParameters outParams);
                        using (outParams)
                        {
                            if (outParams?["cmdletOutput"] is WmiObject[] transactions)
                            {
                                foreach (var txn in transactions)
                                {
                                    using (txn)
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
                using var connection = new WmiConnection(@"root\MsDTC");

                // Call the Get method to retrieve statistics
                foreach (WmiObject obj in connection.CreateQuery("SELECT * FROM MSFT_DtcTransactionsStatisticsTask").DisposeItems())
                {
                    try
                    {
                        using WmiMethod getMethod = obj.GetMethod("Get");
                        using WmiMethodParameters inParams = getMethod.CreateInParameters();
                        inParams.SetPropertyValue("DtcName", "Local");

                        obj.ExecuteMethod(getMethod, inParams, out WmiMethodParameters outParams);
                        using (outParams)
                        {
                            if (outParams?["Statistics"] is WmiObject statsObj)
                            {
                                using (statsObj)
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
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[ComponentServicesManager] Failed to invoke Get method: {ex.Message}");
                    }
                }

                // Fallback: Try to query DtcTransactionsStatistics directly
                foreach (WmiObject obj in connection.CreateQuery("SELECT * FROM DtcTransactionsStatistics").DisposeItems())
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

    /// <summary>
    /// Reads a COM+ catalog object's named property as an invariant-culture string, or
    /// <see langword="null"/> if the value is empty or the property does not exist on the object
    /// (<see cref="ICatalogObject.get_Value"/> throws <see cref="COMException"/> in that case).
    /// </summary>
    private string? ReadComProperty(ICatalogObject catalogObject, string propertyName)
    {
        try
        {
            catalogObject.get_Value(propertyName, out Variant value);
            try
            {
                return value.ToInvariantString();
            }
            finally
            {
                value.Clear();
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug($"[ComponentServicesManager] Property '{propertyName}' not available: {ex.Message}");
            return null;
        }
    }

    #region Process Helper Methods

    private sealed record WmiProcessInfo(string? FilePath, string? Description, DateTime? StartTime);

    private Dictionary<int, WmiProcessInfo> LoadProcessDataFromWmi()
    {
        var result = new Dictionary<int, WmiProcessInfo>();

        try
        {
            using var connection = new WmiConnection();

            foreach (WmiObject obj in connection.CreateQuery("SELECT ProcessId, ExecutablePath, Description, CreationDate FROM Win32_Process").DisposeItems())
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
                        creationDate = DmtfDateTimeConverter.ToDateTime(creationDateStr);
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
            null or "" or "0" => L.GetString(ResourceFileNames.ComExp, ComExpKeys.FormatDefault),
            "1" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_None"),
            "2" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Connect"),
            "3" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Call"),
            "4" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_Packet"),
            "5" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_PacketIntegrity"),
            "6" => L.GetString(ResourceFileNames.ComExp, "ComExp_Auth_PacketPrivacy"),
            _ => authValue ?? L.GetString(ResourceFileNames.ComExp, ComExpKeys.FormatDefault)
        };
    }

    private static string GetLocalizedApplicationType(bool isService, bool hasDllSurrogate)
    {
        var L = LocalizationProvider.Current;
        if (isService)
        {
            return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomTypeLocalService);
        }

        if (hasDllSurrogate)
        {
            return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomTypeSurrogate);
        }

        return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomTypeLocalServer);
    }

    private static string GetLocalizedIdentity(string? localService, string? runAs)
    {
        var L = LocalizationProvider.Current;
        if (!string.IsNullOrEmpty(localService))
        {
            return L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.DcomIdentityServiceFormat, localService);
        }

        if (string.Equals(runAs, "Interactive User", StringComparison.OrdinalIgnoreCase))
        {
            return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomIdentityInteractive);
        }

        if (!string.IsNullOrWhiteSpace(runAs))
        {
            return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomIdentityThisUser);
        }

        return L.GetString(ResourceFileNames.ComExp, ComExpKeys.DcomIdentityLaunching);
    }

    #endregion
}






