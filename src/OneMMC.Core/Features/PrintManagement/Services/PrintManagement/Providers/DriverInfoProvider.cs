using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Providers;

/// <summary>
/// Provides driver information including version and isolation mode.
/// </summary>
internal class DriverInfoProvider
{
    private readonly ILogger _logger;
    private static readonly string[] Environments = { "Windows x64", "Windows NT x86", "Windows ARM64" };
    private static readonly string[] Versions = { "Version-4", "Version-3", "Version-2" };

    public DriverInfoProvider(ILogger logger)
    {
        _logger = logger;
    }

    public string GetDriverVersion(string driverName)
    {
        if (string.IsNullOrEmpty(driverName))
            return string.Empty;

        try
        {
            foreach (var env in Environments)
            {
                foreach (var version in Versions)
                {
                    string driverKeyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{env}\Drivers\{version}\{driverName}";

                    using var driverKey = Registry.LocalMachine.OpenSubKey(driverKeyPath);
                    if (driverKey == null) continue;

                    object? driverVersion = driverKey.GetValue("DriverVersion");
                    if (driverVersion != null)
                    {
                        if (driverVersion is string strVersion)
                        {
                            return strVersion;
                        }

                        try
                        {
                            ulong versionNum = Convert.ToUInt64(driverVersion);
                            return FormatDriverVersion(versionNum);
                        }
                        catch
                        {
                            return driverVersion.ToString() ?? string.Empty;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get driver version for {DriverName}", driverName);
        }

        return string.Empty;
    }

    public string GetDriverIsolationMode(string driverName)
    {
        if (string.IsNullOrEmpty(driverName))
            return "None";

        try
        {
            // Check registry for explicit isolation mode
            foreach (var env in Environments)
            {
                foreach (var version in Versions)
                {
                    string driverKeyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{env}\Drivers\{version}\{driverName}";

                    using var driverKey = Registry.LocalMachine.OpenSubKey(driverKeyPath);
                    if (driverKey == null) continue;

                    object? isolationMode = driverKey.GetValue("IsolationMode");
                    if (isolationMode != null)
                    {
                        uint mode = Convert.ToUInt32(isolationMode);
                        string result = mode switch
                        {
                            PrinterConstants.DRIVER_ISOLATION_SHARED => "Shared",
                            PrinterConstants.DRIVER_ISOLATION_ISOLATED => "Isolated",
                            _ => "None"
                        };
                        _logger.LogDebug("Driver {DriverName} has IsolationMode={Mode} from registry", driverName, result);
                        return result;
                    }
                }
            }

            // Check spooler isolation groups
            if (TryGetDriverIsolationModeFromSpoolerGroups(driverName, out string groupedMode))
            {
                _logger.LogDebug("Driver {DriverName} has isolation mode {Mode} from spooler groups", driverName, groupedMode);
                return groupedMode;
            }

            // Check DriverIsolation compatibility flag
            foreach (var env in Environments)
            {
                foreach (var version in Versions)
                {
                    string driverKeyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{env}\Drivers\{version}\{driverName}";

                    using var driverKey = Registry.LocalMachine.OpenSubKey(driverKeyPath);
                    if (driverKey == null) continue;

                    object? driverIsolation = driverKey.GetValue("DriverIsolation");
                    if (driverIsolation != null && Convert.ToUInt32(driverIsolation) == PrinterConstants.DRIVER_ISOLATION_ISOLATED)
                    {
                        _logger.LogDebug("Driver {DriverName} defaults to Shared because DriverIsolation=2", driverName);
                        return "Shared";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get driver isolation mode for {DriverName}", driverName);
        }

        return "None";
    }

    public string GetDriverInfName(string driverName)
    {
        if (string.IsNullOrEmpty(driverName))
            return string.Empty;

        try
        {
            foreach (var env in Environments)
            {
                foreach (var version in Versions)
                {
                    string driverKeyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{env}\Drivers\{version}\{driverName}";

                    using var driverKey = Registry.LocalMachine.OpenSubKey(driverKeyPath);
                    if (driverKey == null) continue;

                    object? infPath = driverKey.GetValue("InfPath");
                    if (infPath != null)
                    {
                        var path = infPath.ToString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            return System.IO.Path.GetFileName(path);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get INF name for {DriverName}", driverName);
        }

        return string.Empty;
    }

    public List<PrintDriverInfo> EnumerateDrivers()
    {
        var drivers = EnumerateDriversLevel8();
        if (drivers.Count == 0)
        {
            drivers = EnumerateDriversLevel6();
        }
        return drivers.OrderBy(d => d.Name).ToList();
    }

    private List<PrintDriverInfo> EnumerateDriversLevel8()
    {
        var drivers = new List<PrintDriverInfo>();
        IntPtr pDrivers = IntPtr.Zero;

        try
        {
            PrinterNativeMethods.EnumPrinterDrivers(null, null, 8, IntPtr.Zero, 0, out uint pcbNeeded, out uint pcReturned);

            if (pcbNeeded == 0 || pcbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
            {
                return drivers;
            }

            pDrivers = Marshal.AllocHGlobal((int)pcbNeeded);

            if (!PrinterNativeMethods.EnumPrinterDrivers(null, null, 8, pDrivers, pcbNeeded, out pcbNeeded, out pcReturned))
            {
                return drivers;
            }

            var structSize = Marshal.SizeOf<PrinterNativeStructures.DRIVER_INFO_8>();
            IntPtr current = pDrivers;

            for (uint i = 0; i < pcReturned; i++)
            {
                var infoStruct = Marshal.PtrToStructure<PrinterNativeStructures.DRIVER_INFO_8>(current);
                var info = MapDriverInfo8(infoStruct);
                drivers.Add(info);
                current = IntPtr.Add(current, structSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate drivers via DRIVER_INFO_8.");
        }
        finally
        {
            if (pDrivers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pDrivers);
            }
        }

        return drivers;
    }

    private List<PrintDriverInfo> EnumerateDriversLevel6()
    {
        var drivers = new List<PrintDriverInfo>();
        IntPtr pDrivers = IntPtr.Zero;

        try
        {
            PrinterNativeMethods.EnumPrinterDrivers(null, null, 6, IntPtr.Zero, 0, out uint pcbNeeded, out uint pcReturned);

            if (pcbNeeded == 0 || pcbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
            {
                return drivers;
            }

            pDrivers = Marshal.AllocHGlobal((int)pcbNeeded);

            if (!PrinterNativeMethods.EnumPrinterDrivers(null, null, 6, pDrivers, pcbNeeded, out pcbNeeded, out pcReturned))
            {
                return drivers;
            }

            var structSize = Marshal.SizeOf<PrinterNativeStructures.DRIVER_INFO_6>();
            IntPtr current = pDrivers;

            for (uint i = 0; i < pcReturned; i++)
            {
                var infoStruct = Marshal.PtrToStructure<PrinterNativeStructures.DRIVER_INFO_6>(current);
                var info = MapDriverInfo6(infoStruct);
                drivers.Add(info);
                current = IntPtr.Add(current, structSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate drivers via DRIVER_INFO_6.");
        }
        finally
        {
            if (pDrivers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pDrivers);
            }
        }

        return drivers;
    }

    private PrintDriverInfo MapDriverInfo8(PrinterNativeStructures.DRIVER_INFO_8 infoStruct)
    {
        var fullName = Marshal.PtrToStringUni(infoStruct.pName) ?? string.Empty;
        var driverName = fullName.Contains(',') ? fullName.Split(',')[0].Trim() : fullName;

        var info = new PrintDriverInfo
        {
            Name = driverName,
            EnvironmentName = Marshal.PtrToStringUni(infoStruct.pEnvironment) ?? string.Empty,
            DriverPath = Marshal.PtrToStringUni(infoStruct.pDriverPath) ?? string.Empty,
            DataFile = Marshal.PtrToStringUni(infoStruct.pDataFile) ?? string.Empty,
            ConfigFile = Marshal.PtrToStringUni(infoStruct.pConfigFile) ?? string.Empty,
            MonitorName = Marshal.PtrToStringUni(infoStruct.pMonitorName) ?? string.Empty,
            DriverVersion = FormatDriverVersion(infoStruct.dwlDriverVersion),
            SupportsIsolation = (infoStruct.dwPrinterDriverAttributes & PrinterConstants.PRINTER_DRIVER_SANDBOX_ENABLED) != 0,
        };

        if (info.DriverVersion == "0.0.0.0")
        {
            var regVersion = GetDriverVersion(driverName);
            if (!string.IsNullOrEmpty(regVersion))
            {
                info.DriverVersion = regVersion;
            }
        }

        // Determine isolation mode
        string registryMode = GetDriverIsolationMode(driverName);
        bool supportsIsolation = info.SupportsIsolation;

        if (registryMode != "None")
        {
            info.IsolationMode = registryMode;
        }
        else if (supportsIsolation)
        {
            info.IsolationMode = "Shared";
        }
        else
        {
            info.IsolationMode = "None";
        }

        info.InfName = Marshal.PtrToStringUni(infoStruct.pszInfPath) ?? string.Empty;
        if (string.IsNullOrEmpty(info.InfName) || !info.InfName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
        {
            info.InfName = GetDriverInfName(driverName);
        }
        else
        {
            info.InfName = System.IO.Path.GetFileName(info.InfName);
        }

        return info;
    }

    private PrintDriverInfo MapDriverInfo6(PrinterNativeStructures.DRIVER_INFO_6 infoStruct)
    {
        var fullName = Marshal.PtrToStringUni(infoStruct.pName) ?? string.Empty;
        var driverName = fullName.Contains(',') ? fullName.Split(',')[0].Trim() : fullName;
        string isolationMode = GetDriverIsolationMode(driverName);

        var info = new PrintDriverInfo
        {
            Name = driverName,
            EnvironmentName = Marshal.PtrToStringUni(infoStruct.pEnvironment) ?? string.Empty,
            DriverPath = Marshal.PtrToStringUni(infoStruct.pDriverPath) ?? string.Empty,
            DataFile = Marshal.PtrToStringUni(infoStruct.pDataFile) ?? string.Empty,
            ConfigFile = Marshal.PtrToStringUni(infoStruct.pConfigFile) ?? string.Empty,
            MonitorName = Marshal.PtrToStringUni(infoStruct.pMonitorName) ?? string.Empty,
            DriverVersion = FormatDriverVersion(infoStruct.dwlDriverVersion),
            IsolationMode = isolationMode,
            InfName = GetDriverInfName(driverName),
            SupportsIsolation = string.Equals(isolationMode, "Shared", StringComparison.OrdinalIgnoreCase)
                || string.Equals(isolationMode, "Isolated", StringComparison.OrdinalIgnoreCase),
        };

        if (info.DriverVersion == "0.0.0.0")
        {
            var regVersion = GetDriverVersion(driverName);
            if (!string.IsNullOrEmpty(regVersion))
            {
                info.DriverVersion = regVersion;
            }
        }

        return info;
    }

    private static string FormatDriverVersion(ulong version)
    {
        ushort v1 = (ushort)(version >> 48);
        ushort v2 = (ushort)((version >> 32) & 0xFFFF);
        ushort v3 = (ushort)((version >> 16) & 0xFFFF);
        ushort v4 = (ushort)(version & 0xFFFF);
        return $"{v1}.{v2}.{v3}.{v4}";
    }

    private bool TryGetDriverIsolationModeFromSpoolerGroups(string driverName, out string isolationMode)
    {
        isolationMode = "None";

        try
        {
            using var printKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print");
            if (printKey?.GetValue("PrintDriverIsolationGroups") is not string groupsValue || string.IsNullOrWhiteSpace(groupsValue))
            {
                return false;
            }

            string[] groups = groupsValue.Split(new[] { "\\\\" }, StringSplitOptions.None);
            for (int index = 0; index < groups.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(groups[index]))
                {
                    continue;
                }

                string[] drivers = groups[index].Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string configuredDriver in drivers)
                {
                    if (!string.Equals(configuredDriver, driverName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    isolationMode = index switch
                    {
                        0 => "None",
                        1 => "Shared",
                        _ => "Isolated"
                    };

                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read spooler isolation groups for {DriverName}", driverName);
        }

        return false;
    }
}


