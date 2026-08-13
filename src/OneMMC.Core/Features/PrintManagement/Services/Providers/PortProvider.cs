using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.PrintManagement.Services.Native;
using OneMMC.Core.Features.PrintManagement.Services.Helpers;
using OneMMC.Core.Features.PrintManagement.Models;

namespace OneMMC.Core.Features.PrintManagement.Services.Providers;

/// <summary>
/// Provides printer port information.
/// </summary>
internal class PortProvider
{
    private readonly ILogger _logger;
    private readonly PrinterEnumerator _printerEnumerator;

    public PortProvider(ILogger logger, PrinterEnumerator printerEnumerator)
    {
        _logger = logger;
        _printerEnumerator = printerEnumerator;
    }

    public List<PrintPortInfo> GetPrintPorts()
    {
        var ports = new List<PrintPortInfo>();
        var printerPortMap = _printerEnumerator.BuildPrinterPortMap();
        IntPtr pPorts = IntPtr.Zero;

        try
        {
            PrinterNativeMethods.EnumPorts(null, 2, IntPtr.Zero, 0, out uint pcbNeeded, out uint pcReturned);

            if (pcbNeeded == 0 || pcbNeeded > PrinterConstants.MAX_BUFFER_SIZE)
            {
                return ports;
            }

            pPorts = Marshal.AllocHGlobal((int)pcbNeeded);

            if (!PrinterNativeMethods.EnumPorts(null, 2, pPorts, pcbNeeded, out pcbNeeded, out pcReturned))
            {
                return ports;
            }

            var structSize = Marshal.SizeOf<PrinterNativeStructures.PORT_INFO_2>();
            IntPtr current = pPorts;

            for (uint i = 0; i < pcReturned; i++)
            {
                var portInfo = Marshal.PtrToStructure<PrinterNativeStructures.PORT_INFO_2>(current);
                var portName = Marshal.PtrToStringUni(portInfo.pPortName) ?? string.Empty;
                var monitorName = Marshal.PtrToStringUni(portInfo.pMonitorName) ?? string.Empty;
                var description = Marshal.PtrToStringUni(portInfo.pDescription) ?? string.Empty;

                var portType = monitorName.Contains("TCP/IP", StringComparison.OrdinalIgnoreCase)
                    ? "Standard TCP/IP Port"
                    : monitorName;

                if (string.IsNullOrEmpty(portType))
                {
                    portType = PortHelper.DeterminePortType(portName);
                }

                if (string.IsNullOrEmpty(description))
                {
                    description = PortHelper.GetPortDescription(portName);
                }

                ports.Add(new PrintPortInfo
                {
                    PortName = portName,
                    Description = description,
                    PortType = portType,
                    PrinterNames = printerPortMap.GetValueOrDefault(portName, string.Empty),
                });

                current = IntPtr.Add(current, structSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate printer ports via EnumPorts.");
        }
        finally
        {
            if (pPorts != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pPorts);
            }
        }

        return ports.OrderBy(p => p.PortName).ToList();
    }
}


