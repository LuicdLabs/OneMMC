using System;

namespace OneMMC.Core.Features.PrintManagement.Services.PrintManagement.Helpers;

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
            return "Local Port";

        if (portName.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            return "Local Port";

        if (portName.StartsWith("nul", StringComparison.OrdinalIgnoreCase))
            return "Local Port";

        if (portName.Contains("IP_", StringComparison.OrdinalIgnoreCase) ||
            portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
            return "Standard TCP/IP Port";

        return "Local Port";
    }

    /// <summary>
    /// Gets a short description for a port based on its name.
    /// </summary>
    internal static string GetPortDescription(string portName)
    {
        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            return "USB Virtual Printer Port";

        if (portName.StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase))
            return "Local Port";

        if (portName.Equals("FILE:", StringComparison.OrdinalIgnoreCase))
            return "Print to File";

        if (portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            return "Printer Port";

        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return "Serial Port";

        if (portName.StartsWith("nul", StringComparison.OrdinalIgnoreCase))
            return "Null Port";

        if (portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
            return "WS Discovery Port";

        return "Local Port";
    }
}


