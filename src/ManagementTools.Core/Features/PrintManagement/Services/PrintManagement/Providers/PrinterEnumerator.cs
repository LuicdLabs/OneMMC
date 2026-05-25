using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;
using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Native;
using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Helpers;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Providers;

/// <summary>
/// Handles printer enumeration via P/Invoke.
/// </summary>
internal class PrinterEnumerator
{
    private readonly ILogger _logger;
    private readonly DriverInfoProvider _driverInfoProvider;

    public PrinterEnumerator(ILogger logger, DriverInfoProvider driverInfoProvider)
    {
        _logger = logger;
        _driverInfoProvider = driverInfoProvider;
    }

    public List<PrinterInfo> EnumeratePrinters(uint flags)
    {
        var printers = new List<PrinterInfo>();
        IntPtr pPrinters = IntPtr.Zero;

        try
        {
            PrinterNativeMethods.EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out uint pcbNeeded, out uint pcReturned);

            if (pcbNeeded == 0 || pcbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
            {
                if (pcbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
                {
                    _logger.LogError("EnumPrinters requested buffer size {Size} exceeds maximum allowed {MaxSize}", 
                        pcbNeeded, PrinterConstants.MAX_BUFFER_SIZE);
                }
                return printers;
            }

            pPrinters = Marshal.AllocHGlobal((int)pcbNeeded);

            if (!PrinterNativeMethods.EnumPrinters(flags, null, 2, pPrinters, pcbNeeded, out pcbNeeded, out pcReturned))
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogError("EnumPrinters failed with error: {Error}", error);
                return printers;
            }

            var structSize = Marshal.SizeOf<PrinterNativeStructures.PRINTER_INFO_2>();
            IntPtr current = pPrinters;

            for (uint i = 0; i < pcReturned; i++)
            {
                var infoStruct = Marshal.PtrToStructure<PrinterNativeStructures.PRINTER_INFO_2>(current);
                var info = MapPrinterInfo(infoStruct);
                printers.Add(info);
                current = IntPtr.Add(current, structSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate printers via EnumPrinters.");
        }
        finally
        {
            if (pPrinters != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pPrinters);
            }
        }

        return printers;
    }

    private PrinterInfo MapPrinterInfo(PrinterNativeStructures.PRINTER_INFO_2 infoStruct)
    {
        var info = new PrinterInfo
        {
            Name = Marshal.PtrToStringUni(infoStruct.pPrinterName) ?? string.Empty,
            Status = PrinterStatusMapper.MapPrinterStatus(infoStruct.Status),
            IsPaused = (infoStruct.Status & PrinterConstants.PRINTER_STATUS_PAUSED) != 0,
            JobCount = (int)infoStruct.cJobs,
            DriverName = Marshal.PtrToStringUni(infoStruct.pDriverName) ?? string.Empty,
            PortName = Marshal.PtrToStringUni(infoStruct.pPortName) ?? string.Empty,
            ShareName = Marshal.PtrToStringUni(infoStruct.pShareName) ?? string.Empty,
            Comment = Marshal.PtrToStringUni(infoStruct.pComment) ?? string.Empty,
            IsDefault = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_DEFAULT) != 0,
            IsShared = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_SHARED) != 0,
            IsNetwork = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_NETWORK) != 0,
            IsPerUser = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_PER_USER) != 0,
            IsPushedUser = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_PUSHED_USER) != 0,
            IsPushedMachine = (infoStruct.Attributes & PrinterConstants.PRINTER_ATTRIBUTE_PUSHED_MACHINE) != 0,
            ServerName = Marshal.PtrToStringUni(infoStruct.pServerName) ?? string.Empty,
            PrintProcessor = Marshal.PtrToStringUni(infoStruct.pPrintProcessor) ?? string.Empty,
            Location = Marshal.PtrToStringUni(infoStruct.pLocation) ?? string.Empty,
        };

        info.DriverVersion = _driverInfoProvider.GetDriverVersion(info.DriverName);
        info.IsolationMode = _driverInfoProvider.GetDriverIsolationMode(info.DriverName);
        info.IsDeployedViaGPO = GPODeploymentChecker.IsDeployedByGroupPolicy(info.Name, info.ServerName);

        return info;
    }

    public Dictionary<string, string> BuildPrinterPortMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var printers = EnumeratePrinters(PrinterConstants.PRINTER_ENUM_LOCAL | PrinterConstants.PRINTER_ENUM_CONNECTIONS);

        foreach (var printer in printers)
        {
            var portNames = printer.PortName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var portName in portNames)
            {
                if (map.TryGetValue(portName, out var existing))
                {
                    map[portName] = $"{existing}, {printer.Name}";
                }
                else
                {
                    map[portName] = printer.Name;
                }
            }
        }

        return map;
    }
}


