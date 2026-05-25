using System;
using Microsoft.Win32;

namespace ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Providers;

/// <summary>
/// Checks if a printer is deployed by Group Policy.
/// </summary>
internal static class GPODeploymentChecker
{
    public static bool IsDeployedByGroupPolicy(string printerName, string? serverName)
    {
        if (string.IsNullOrEmpty(printerName))
            return false;

        try
        {
            // Check Deployed Printer Connections
            if (CheckDeployedConnections(printerName))
                return true;

            // Check Client Side Rendering Print Provider
            if (CheckClientSideRenderingProvider(printerName))
                return true;

            // Check for per-user deployed printers
            if (CheckPerUserDeployedPrinters(printerName))
                return true;
        }
        catch (Exception)
        {
            // Ignore registry access errors
        }

        return false;
    }

    private static bool CheckDeployedConnections(string printerName)
    {
        string connectionsPath = @"SYSTEM\CurrentControlSet\Control\Print\Connections";
        using var connectionsKey = Registry.LocalMachine.OpenSubKey(connectionsPath);
        if (connectionsKey == null)
            return false;

        foreach (string conn in connectionsKey.GetSubKeyNames())
        {
            using var connKey = connectionsKey.OpenSubKey(conn);
            string? printer = connKey?.GetValue("Printer")?.ToString();

            if (!string.IsNullOrEmpty(printer) &&
                printer.Equals(printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CheckClientSideRenderingProvider(string printerName)
    {
        string csrpPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Providers\Client Side Rendering Print Provider\Servers";
        using var csrpKey = Registry.LocalMachine.OpenSubKey(csrpPath);
        if (csrpKey == null)
            return false;

        foreach (string server in csrpKey.GetSubKeyNames())
        {
            string printersPath = $@"{csrpPath}\{server}\Printers";
            using var printersKey = Registry.LocalMachine.OpenSubKey(printersPath);
            if (printersKey == null) continue;

            foreach (string guid in printersKey.GetSubKeyNames())
            {
                using var printerKey = printersKey.OpenSubKey(guid);
                string? name = printerKey?.GetValue("Name")?.ToString();
                if (name?.Equals(printerName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CheckPerUserDeployedPrinters(string printerName)
    {
        using var userPrintersKey = Registry.CurrentUser.OpenSubKey(@"Printers\Connections");
        if (userPrintersKey == null)
            return false;

        foreach (string connName in userPrintersKey.GetSubKeyNames())
        {
            // Connection names are in format: ,,server,printer
            if (connName.Contains(printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}


