using System;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PrintManagement.Services.Helpers;

/// <summary>
/// Helper methods for port type detection and description.
/// </summary>
internal static class PortHelper
{
    /// <summary>
    /// Determines the port type based on the port name pattern.
    /// </summary>
    internal static string DeterminePortType(string portName)
    {
        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            return "USB";

        if (portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            return "LPT";

        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return "COM";

        if (portName.StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortTypeLocal);

        if (portName.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortTypeLocal);

        if (portName.StartsWith("nul", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortTypeLocal);

        if (portName.Contains("IP_", StringComparison.OrdinalIgnoreCase) ||
            portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortTypeStandardTcpIp);

        return L(PrintMgmtKeys.PortTypeLocal);
    }

    /// <summary>
    /// Gets a short description for a port based on its name.
    /// </summary>
    internal static string GetPortDescription(string portName)
    {
        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescUsbVirtual);

        if (portName.StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortTypeLocal);

        if (portName.Equals("FILE:", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescPrintToFile);

        if (portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescPrinterPort);

        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescSerial);

        if (portName.StartsWith("nul", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescNull);

        if (portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
            return L(PrintMgmtKeys.PortDescWsDiscovery);

        return L(PrintMgmtKeys.PortTypeLocal);
    }

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.PrintManagement, key);
}


