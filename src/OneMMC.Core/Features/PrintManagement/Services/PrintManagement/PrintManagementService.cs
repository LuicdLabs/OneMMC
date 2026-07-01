using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Native;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Providers;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.PrintManagement.Services.PrintManagement;

/// <summary>
/// Service for managing printers, drivers, ports, and forms using WMI and P/Invoke.
/// Refactored version with separated concerns.
/// </summary>
public class PrintManagementService
{
    private const int ErrorInsufficientBuffer = 122;
    private const int DefaultPrinterInfoBufferSize = 4 * 1024;
    private const int HrAccessDenied = unchecked((int)0x80070005);
    private const int HrDriverPackageInUseLegacy = unchecked((int)0x80070BC4);
    private const int HrDriverPackageInUse = unchecked((int)0x80070BC7);

    private static readonly string[] DriverEnvironments = { "Windows x64", "Windows NT x86", "Windows ARM64" };
    private static readonly string[] DriverVersions = { "Version-4", "Version-3", "Version-2" };

    private readonly ILogger<PrintManagementService> _logger;
    private readonly DriverInfoProvider _driverInfoProvider;
    private readonly PrinterEnumerator _printerEnumerator;
    private readonly DeployedPrinterProvider _deployedPrinterProvider;
    private readonly PortProvider _portProvider;
    private readonly FormProvider _formProvider;

    public PrintManagementService(ILogger<PrintManagementService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverInfoProvider = new DriverInfoProvider(_logger);
        _printerEnumerator = new PrinterEnumerator(_logger, _driverInfoProvider);
        _deployedPrinterProvider = new DeployedPrinterProvider(_logger, _printerEnumerator);
        _portProvider = new PortProvider(_logger, _printerEnumerator);
        _formProvider = new FormProvider(_logger);
    }

    /// <summary>
    /// Gets the local computer name for display.
    /// </summary>
    public string GetComputerName() => Environment.MachineName;

    /// <summary>
    /// Retrieves all printers installed on the system via P/Invoke.
    /// </summary>
    public async Task<List<PrinterInfo>> GetPrintersAsync()
    {
        return await Task.Run(() =>
        {
            return _printerEnumerator.EnumeratePrinters(
                PrinterConstants.PRINTER_ENUM_LOCAL | PrinterConstants.PRINTER_ENUM_CONNECTIONS);
        });
    }

    /// <summary>
    /// Retrieves all deployed/connected printers including Group Policy deployed printers.
    /// </summary>
    public async Task<List<PrinterInfo>> GetDeployedPrintersAsync()
    {
        return await Task.Run(() => _deployedPrinterProvider.GetDeployedPrinters());
    }

    /// <summary>
    /// Retrieves all print drivers installed on the system via P/Invoke.
    /// </summary>
    public async Task<List<PrintDriverInfo>> GetPrintDriversAsync()
    {
        return await Task.Run(() => _driverInfoProvider.EnumerateDrivers());
    }

    /// <summary>
    /// Retrieves all print ports on the system via P/Invoke.
    /// </summary>
    public async Task<List<PrintPortInfo>> GetPrintPortsAsync()
    {
        return await Task.Run(() => _portProvider.GetPrintPorts());
    }

    /// <summary>
    /// Retrieves all print forms on the system using the EnumForms P/Invoke API.
    /// </summary>
    public async Task<List<PrintFormInfo>> GetPrintFormsAsync()
    {
        return await Task.Run(() => _formProvider.GetPrintForms());
    }

    /// <summary>
    /// Opens the native printer queue window.
    /// </summary>
    public Task OpenPrinterQueueAsync(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        return RunPrintUiEntryAsync($"/o /n \"{printerName}\"");
    }

    /// <summary>
    /// Prints a test page using the native print UI entry point.
    /// </summary>
    public Task PrintTestPageAsync(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        return RunPrintUiEntryAsync($"/k /n \"{printerName}\"");
    }

    /// <summary>
    /// Shows the global printing defaults dialog and saves the resulting DEVMODE when confirmed.
    /// </summary>
    public Task ShowPrintingDefaultsAsync(IntPtr ownerWindowHandle, string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        IntPtr printerHandle = IntPtr.Zero;
        IntPtr devMode = IntPtr.Zero;
        IntPtr printerInfoBuffer = IntPtr.Zero;

        try
        {
            printerHandle = OpenPrinterHandle(printerName, PrinterConstants.PRINTER_ACCESS_USE | PrinterConstants.PRINTER_ACCESS_ADMINISTER);

            int size = PrinterNativeMethods.DocumentProperties(ownerWindowHandle, printerHandle, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size < 0)
            {
                throw CreateWin32Exception("Unable to query printer default settings.");
            }

            devMode = Marshal.AllocHGlobal(size);

            if (PrinterNativeMethods.DocumentProperties(
                    ownerWindowHandle,
                    printerHandle,
                    printerName,
                    devMode,
                    IntPtr.Zero,
                    PrinterConstants.DM_OUT_BUFFER) < 0)
            {
                throw CreateWin32Exception("Unable to load current printer default settings.");
            }

            int result = PrinterNativeMethods.DocumentProperties(
                ownerWindowHandle,
                printerHandle,
                printerName,
                devMode,
                devMode,
                PrinterConstants.DM_IN_BUFFER | PrinterConstants.DM_OUT_BUFFER | PrinterConstants.DM_PROMPT);

            if (result != PrinterConstants.IDOK)
            {
                return Task.CompletedTask;
            }

            var printerInfo = new PrinterNativeStructures.PRINTER_INFO_8 { pDevMode = devMode };
            printerInfoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<PrinterNativeStructures.PRINTER_INFO_8>());
            Marshal.StructureToPtr(printerInfo, printerInfoBuffer, false);

            if (!PrinterNativeMethods.SetPrinter(printerHandle, 8, printerInfoBuffer, 0))
            {
                throw CreateWin32Exception("Unable to save printer default settings.");
            }

            _logger.LogInformation("Updated printing defaults for printer {PrinterName}", printerName);
            return Task.CompletedTask;
        }
        finally
        {
            if (printerInfoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(printerInfoBuffer);
            }

            if (devMode != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(devMode);
            }

            ClosePrinterHandle(printerHandle);
        }
    }

    /// <summary>
    /// Shows the native printer properties dialog using PrintUIEntry.
    /// </summary>
    public Task ShowPrinterPropertiesAsync(IntPtr ownerWindowHandle, string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        return RunPrintUiEntryAsync($"/p /n \"{printerName}\"");
    }

    /// <summary>
    /// Pauses the specified printer queue.
    /// </summary>
    public Task PausePrinterAsync(string printerName) =>
        ExecutePrinterCommandAsync(printerName, PrinterConstants.PRINTER_CONTROL_PAUSE, "Paused printer queue {PrinterName}");

    /// <summary>
    /// Resumes the specified printer queue.
    /// </summary>
    public Task ResumePrinterAsync(string printerName) =>
        ExecutePrinterCommandAsync(printerName, PrinterConstants.PRINTER_CONTROL_RESUME, "Resumed printer queue {PrinterName}");

    /// <summary>
    /// Deletes a printer queue.
    /// </summary>
    public async Task DeletePrinterAsync(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        await Task.Run(() =>
        {
            IntPtr printerHandle = IntPtr.Zero;

            try
            {
                printerHandle = OpenPrinterHandle(printerName, PrinterConstants.PRINTER_ALL_ACCESS);
                if (!PrinterNativeMethods.DeletePrinter(printerHandle))
                {
                    throw CreateWin32Exception("Unable to delete printer.");
                }

                _logger.LogInformation("Deleted printer {PrinterName}", printerName);
            }
            finally
            {
                ClosePrinterHandle(printerHandle);
            }
        });
    }

    /// <summary>
    /// Renames a printer queue.
    /// </summary>
    public async Task RenamePrinterAsync(string currentName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        if (string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await Task.Run(() =>
        {
            IntPtr printerHandle = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            IntPtr newNamePtr = IntPtr.Zero;

            try
            {
                printerHandle = OpenPrinterHandle(currentName, PrinterConstants.PRINTER_ACCESS_USE | PrinterConstants.PRINTER_ACCESS_ADMINISTER);
                buffer = GetPrinterInfoBuffer(printerHandle, 2, out _);

                var printerInfo = Marshal.PtrToStructure<PrinterNativeStructures.PRINTER_INFO_2>(buffer);
                printerInfo.pSecurityDescriptor = IntPtr.Zero;
                newNamePtr = Marshal.StringToHGlobalUni(newName);
                printerInfo.pPrinterName = newNamePtr;
                Marshal.StructureToPtr(printerInfo, buffer, false);

                if (!PrinterNativeMethods.SetPrinter(printerHandle, 2, buffer, 0))
                {
                    throw CreateWin32Exception("Unable to rename printer.");
                }

                _logger.LogInformation("Renamed printer {OldPrinterName} to {NewPrinterName}", currentName, newName);
            }
            finally
            {
                if (newNamePtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(newNamePtr);
                }

                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                ClosePrinterHandle(printerHandle);
            }
        });
    }

    /// <summary>
    /// Adds or removes a current-user printer connection.
    /// </summary>
    public async Task SetCurrentUserDeploymentAsync(string connectionPath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionPath);

        await Task.Run(() =>
        {
            bool result = enabled
                ? PrinterNativeMethods.AddPrinterConnection(connectionPath)
                : PrinterNativeMethods.DeletePrinterConnection(connectionPath);

            if (!result)
            {
                throw CreateWin32Exception(enabled
                    ? "Unable to add the current-user printer connection."
                    : "Unable to remove the current-user printer connection.");
            }

            _logger.LogInformation(
                enabled
                    ? "Added current-user printer deployment {ConnectionPath}"
                    : "Removed current-user printer deployment {ConnectionPath}",
                connectionPath);
        });
    }

    /// <summary>
    /// Adds or removes an all-users printer connection using PrintUIEntry.
    /// </summary>
    public Task SetComputerDeploymentAsync(string connectionPath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionPath);

        string arguments = enabled
            ? $"/ga /n \"{connectionPath}\""
            : $"/gd /n \"{connectionPath}\"";

        return RunPrintUiEntryAsync(arguments);
    }

    /// <summary>
    /// Removes a driver package from the driver store.
    /// </summary>
    public async Task RemoveDriverPackageAsync(PrintDriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        await Task.Run(() =>
        {
            // Resolve the INF path before deleting the registration, because
            // TryDeleteDriverRegistration removes the registry entry we need to read.
            string? infPath = ResolveDriverStoreInfPath(driver.Name);

            // Removing the driver registration first reduces false "package in use" failures
            // when the queue has already been deleted but the driver is still registered.
            TryDeleteDriverRegistration(driver);

            if (string.IsNullOrWhiteSpace(infPath))
            {
                _logger.LogError(
                    "Cannot find the driver store INF path for driver '{DriverName}'. The driver package may already have been removed.",
                    driver.Name);
                return;
            }

            string? environment = string.IsNullOrWhiteSpace(driver.EnvironmentName) ? null : driver.EnvironmentName;
            int hr = PrinterNativeMethods.DeletePrinterDriverPackage(null, infPath, environment);
            if (hr < 0)
            {
                if (hr == HrAccessDenied) // E_ACCESSDENIED
                {
                    throw new InvalidOperationException(LocalizationProvider.Current.GetString("PrintManagement", "PrintMgmt_ErrorDriverInBox"));
                }
                if (hr == HrDriverPackageInUseLegacy || hr == HrDriverPackageInUse)
                {
                    throw new InvalidOperationException(LocalizationProvider.Current.GetString("PrintManagement", "PrintMgmt_ErrorDriverInUse"));
                }
                Marshal.ThrowExceptionForHR(hr);
            }

            _logger.LogInformation("Removed driver package {InfPath} for driver {DriverName}", infPath, driver.Name);
        });
    }

    /// <summary>
    /// Deletes the printer driver registration from the print server.
    /// </summary>
    public async Task DeleteDriverAsync(PrintDriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        await Task.Run(() =>
        {
            if (!PrinterNativeMethods.DeletePrinterDriverEx(
                    null,
                    string.IsNullOrWhiteSpace(driver.EnvironmentName) ? null : driver.EnvironmentName,
                    driver.Name,
                    0,
                    0))
            {
                throw CreateWin32Exception("Unable to delete printer driver.");
            }

            _logger.LogInformation("Deleted printer driver {DriverName}", driver.Name);
        });
    }

    private void TryDeleteDriverRegistration(PrintDriverInfo driver)
    {
        if (!PrinterNativeMethods.DeletePrinterDriverEx(
                null,
                string.IsNullOrWhiteSpace(driver.EnvironmentName) ? null : driver.EnvironmentName,
                driver.Name,
                0,
                0))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogError(
                "DeletePrinterDriverEx before package removal did not succeed for {DriverName}. Win32Error={Win32Error}",
                driver.Name,
                error);
            return;
        }

        _logger.LogInformation("Deleted driver registration {DriverName} before package removal", driver.Name);
    }

    /// <summary>
    /// Updates the driver isolation mode for the specified driver.
    /// </summary>
    public async Task SetDriverIsolationModeAsync(string driverName, string isolationMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(isolationMode);

        await Task.Run(() =>
        {
            UpdateDriverIsolationRegistry(driverName, isolationMode);
            UpdateDriverIsolationGroups(driverName, isolationMode);

            _logger.LogInformation("Set driver isolation mode for {DriverName} to {IsolationMode}", driverName, isolationMode);
        });
    }

    private async Task ExecutePrinterCommandAsync(string printerName, uint command, string successMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        await Task.Run(() =>
        {
            IntPtr printerHandle = IntPtr.Zero;

            try
            {
                printerHandle = OpenPrinterHandle(printerName, PrinterConstants.PRINTER_ACCESS_ADMINISTER);
                if (!PrinterNativeMethods.SetPrinter(printerHandle, 0, IntPtr.Zero, command))
                {
                    throw CreateWin32Exception(command == PrinterConstants.PRINTER_CONTROL_PAUSE
                        ? "Unable to pause printer."
                        : "Unable to resume printer.");
                }

                _logger.LogInformation(successMessage, printerName);
            }
            finally
            {
                ClosePrinterHandle(printerHandle);
            }
        });
    }

    private Task RunPrintUiEntryAsync(string arguments)
    {
        return Task.Run(() =>
        {
            PrinterNativeMethods.PrintUIEntryW(IntPtr.Zero, IntPtr.Zero, arguments, 1);
        });
    }

    private IntPtr OpenPrinterHandle(string? printerName, uint desiredAccess)
    {
        var defaults = new PrinterNativeStructures.PRINTER_DEFAULTS
        {
            pDatatype = IntPtr.Zero,
            pDevMode = IntPtr.Zero,
            DesiredAccess = desiredAccess
        };

        if (!PrinterNativeMethods.OpenPrinter(printerName, out IntPtr printerHandle, ref defaults))
        {
            throw CreateWin32Exception(printerName is null
                ? "Unable to open the print server."
                : $"Unable to open printer '{printerName}'.");
        }

        return printerHandle;
    }

    private static void ClosePrinterHandle(IntPtr printerHandle)
    {
        if (printerHandle != IntPtr.Zero)
        {
            PrinterNativeMethods.ClosePrinter(printerHandle);
        }
    }

    private static IntPtr GetPrinterInfoBuffer(IntPtr printerHandle, uint level, out uint bytesNeeded)
    {
        bytesNeeded = 0;
        if (PrinterNativeMethods.GetPrinter(printerHandle, level, IntPtr.Zero, 0, out uint requiredBytes))
        {
            bytesNeeded = requiredBytes;
        }

        int lastError = Marshal.GetLastWin32Error();
        if (requiredBytes > PrinterConstants.MAX_BUFFER_SIZE)
        {
            throw new InvalidOperationException("Invalid printer information buffer size returned by the spooler.");
        }

        if (requiredBytes == 0 && lastError != 0 && lastError != ErrorInsufficientBuffer)
        {
            throw CreateWin32Exception("Unable to query printer information size.");
        }

        uint bufferSize = requiredBytes > 0 ? requiredBytes : DefaultPrinterInfoBufferSize;

        while (bufferSize <= PrinterConstants.MAX_BUFFER_SIZE)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            if (PrinterNativeMethods.GetPrinter(printerHandle, level, buffer, bufferSize, out bytesNeeded))
            {
                return buffer;
            }

            lastError = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(buffer);

            if (bytesNeeded > PrinterConstants.MAX_BUFFER_SIZE)
            {
                break;
            }

            if (lastError != ErrorInsufficientBuffer)
            {
                throw CreateWin32Exception("Unable to query printer information.");
            }

            bufferSize = bytesNeeded > bufferSize
                ? bytesNeeded
                : checked(bufferSize * 2);
        }

        throw new InvalidOperationException("Invalid printer information buffer size returned by the spooler.");
    }

    private void UpdateDriverIsolationRegistry(string driverName, string isolationMode)
    {
        uint? isolationValue = isolationMode switch
        {
            "None" => PrinterConstants.DRIVER_ISOLATION_NONE,
            "Shared" => PrinterConstants.DRIVER_ISOLATION_SHARED,
            "Isolated" => PrinterConstants.DRIVER_ISOLATION_ISOLATED,
            "System Default" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(isolationMode))
        };

        foreach (string environmentName in DriverEnvironments)
        {
            foreach (string versionName in DriverVersions)
            {
                string keyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{environmentName}\Drivers\{versionName}\{driverName}";
                using RegistryKey? driverKey = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
                if (driverKey is null)
                {
                    continue;
                }

                if (isolationValue.HasValue)
                {
                    driverKey.SetValue("IsolationMode", isolationValue.Value, RegistryValueKind.DWord);
                }
                else if (driverKey.GetValue("IsolationMode") is not null)
                {
                    driverKey.DeleteValue("IsolationMode", throwOnMissingValue: false);
                }
            }
        }
    }

    private void UpdateDriverIsolationGroups(string driverName, string isolationMode)
    {
        string[] groups = ReadDriverIsolationGroups();
        List<HashSet<string>> parsedGroups = ParseDriverIsolationGroups(groups);

        foreach (HashSet<string> group in parsedGroups)
        {
            group.RemoveWhere(name => string.Equals(name, driverName, StringComparison.OrdinalIgnoreCase));
        }

        parsedGroups = parsedGroups.Where(group => group.Count > 0).ToList();

        if (parsedGroups.Count == 0)
        {
            parsedGroups.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        switch (isolationMode)
        {
            case "None":
                parsedGroups[0].Add(driverName);
                break;

            case "Shared":
                while (parsedGroups.Count < 2)
                {
                    parsedGroups.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                parsedGroups[1].Add(driverName);
                break;

            case "Isolated":
                while (parsedGroups.Count < 2)
                {
                    parsedGroups.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                parsedGroups.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { driverName });
                break;

            case "System Default":
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(isolationMode));
        }

        string isolationGroups = SerializeDriverIsolationGroups(parsedGroups);
        byte[] data = Encoding.Unicode.GetBytes(isolationGroups + "\0");

        IntPtr printServerHandle = IntPtr.Zero;
        try
        {
            printServerHandle = OpenPrinterHandle(null, PrinterConstants.SERVER_ACCESS_ADMINISTER);
            uint result = PrinterNativeMethods.SetPrinterDataEx(
                printServerHandle,
                null,
                PrinterConstants.PrintDriverIsolationGroupsValueName,
                (uint)RegistryValueKind.String,
                data,
                (uint)data.Length);

            if (result != 0)
            {
                throw new Win32Exception((int)result, "Unable to update print driver isolation groups.");
            }
        }
        finally
        {
            ClosePrinterHandle(printServerHandle);
        }
    }

    private static string[] ReadDriverIsolationGroups()
    {
        using RegistryKey? printKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print");
        if (printKey?.GetValue(PrinterConstants.PrintDriverIsolationGroupsValueName) is not string value || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(new[] { "\\\\" }, StringSplitOptions.None);
    }

    private static List<HashSet<string>> ParseDriverIsolationGroups(IEnumerable<string> groups)
    {
        var parsedGroups = new List<HashSet<string>>();

        foreach (string group in groups)
        {
            parsedGroups.Add(new HashSet<string>(
                group.Split(new[] { PrinterConstants.PrintDriverIsolationSeparator }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase));
        }

        return parsedGroups;
    }

    private static string SerializeDriverIsolationGroups(IEnumerable<HashSet<string>> groups)
    {
        string[] normalizedGroups = groups
            .Select(group => string.Join(PrinterConstants.PrintDriverIsolationSeparator, group.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return string.Join("\\\\", normalizedGroups);
    }

    /// <summary>
    /// Resolves the full driver store INF path for a driver by reading the registry.
    /// <c>DeletePrinterDriverPackage</c> requires the full path inside
    /// <c>%windir%\System32\DriverStore\FileRepository</c>.
    /// </summary>
    private string? ResolveDriverStoreInfPath(string driverName)
    {
        foreach (string env in DriverEnvironments)
        {
            foreach (string ver in DriverVersions)
            {
                string keyPath = $@"SYSTEM\CurrentControlSet\Control\Print\Environments\{env}\Drivers\{ver}\{driverName}";
                using RegistryKey? driverKey = Registry.LocalMachine.OpenSubKey(keyPath);
                if (driverKey?.GetValue("InfPath") is string infPath && !string.IsNullOrWhiteSpace(infPath))
                {
                    if (Path.IsPathRooted(infPath))
                    {
                        return infPath;
                    }

                    // Relative paths are relative to the driver directory
                    if (driverKey.GetValue("DriverPath") is string driverPath && !string.IsNullOrWhiteSpace(driverPath))
                    {
                        string? driverDir = Path.GetDirectoryName(driverPath);
                        if (!string.IsNullOrWhiteSpace(driverDir))
                        {
                            return Path.Combine(driverDir, infPath);
                        }
                    }
                }
            }
        }

        _logger.LogError("Could not resolve driver store INF path for driver {DriverName}", driverName);
        return null;
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        int error = Marshal.GetLastWin32Error();
        return error == 0
            ? new Win32Exception(message)
            : new Win32Exception(error, message);
    }
}


