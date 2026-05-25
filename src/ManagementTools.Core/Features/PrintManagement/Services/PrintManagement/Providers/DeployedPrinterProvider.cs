using System;
using System.Collections.Generic;
using System.Linq;
using ManagementTools.Core.Features.PrintManagement.Models.PrintManagement;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PrintManagement.Services.PrintManagement.Providers;

/// <summary>
/// Provides deployed printer information from Active Directory and local registry.
/// </summary>
internal class DeployedPrinterProvider
{
    private readonly ILogger _logger;
    private readonly PrinterEnumerator _printerEnumerator;

    public DeployedPrinterProvider(ILogger logger, PrinterEnumerator printerEnumerator)
    {
        _logger = logger;
        _printerEnumerator = printerEnumerator;
    }

    public List<PrinterInfo> GetDeployedPrinters()
    {
        var printers = new Dictionary<string, PrinterInfo>(StringComparer.OrdinalIgnoreCase);

        // Get deployed printers from Active Directory
        GetDeployedPrintersFromAD(printers);

        // Supplement with local registry checks
        SupplementWithLocalDeployedPrinters(printers);

        return printers.Values.OrderBy(p => p.Name).ToList();
    }

    private void GetDeployedPrintersFromAD(Dictionary<string, PrinterInfo> printers)
    {
        try
        {
            var domain = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
            if (domain == null)
                return;

            string dn = domain.GetDirectoryEntry().Properties["distinguishedName"]?.Value?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(dn))
                return;

            using var searcher = new System.DirectoryServices.DirectorySearcher();
            searcher.SearchRoot = new System.DirectoryServices.DirectoryEntry($"LDAP://CN=Policies,CN=System,{dn}");
            searcher.Filter = "(objectClass=msPrint-ConnectionPolicy)";
            searcher.SearchScope = System.DirectoryServices.SearchScope.Subtree;
            searcher.PropertiesToLoad.Add("uNCName");
            searcher.PropertiesToLoad.Add("serverName");
            searcher.PropertiesToLoad.Add("printerName");
            searcher.PropertiesToLoad.Add("distinguishedName");

            using var results = searcher.FindAll();
            var gpoNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.DirectoryServices.SearchResult result in results)
            {
                ProcessADPrinterResult(result, printers, gpoNames, dn);
            }
        }
        catch (System.DirectoryServices.ActiveDirectory.ActiveDirectoryObjectNotFoundException)
        {
            _logger.LogInformation("Active Directory domain not found, cannot fetch deployed printers from AD.");
        }
        catch (System.DirectoryServices.ActiveDirectory.ActiveDirectoryOperationException)
        {
            _logger.LogInformation("Unable to connect to Active Directory. This computer may not be joined to a domain.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch deployed printers via Active Directory LDAP.");
        }
    }

    private void ProcessADPrinterResult(
        System.DirectoryServices.SearchResult result,
        Dictionary<string, PrinterInfo> printers,
        Dictionary<string, string> gpoNames,
        string dn)
    {
        var props = result.Properties;
        string distinguishedName = props["distinguishedName"].Count > 0 ? props["distinguishedName"][0]?.ToString() ?? "" : "";
        string unc = props["uNCName"].Count > 0 ? props["uNCName"][0]?.ToString() ?? "" : "";
        string server = props["serverName"].Count > 0 ? props["serverName"][0]?.ToString() ?? "" : "";
        string printerName = props["printerName"].Count > 0 ? props["printerName"][0]?.ToString() ?? "" : "";

        if (string.IsNullOrEmpty(printerName) && !string.IsNullOrEmpty(unc))
        {
            int lastSlash = unc.LastIndexOf('\\');
            if (lastSlash >= 0 && lastSlash < unc.Length - 1)
            {
                printerName = unc.Substring(lastSlash + 1);
            }
            else
            {
                printerName = unc;
            }
        }

        string serverDisplay = server.TrimStart('\\');
        bool isPerUser = distinguishedName.IndexOf("CN=User,", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isPerMachine = distinguishedName.IndexOf("CN=Machine,", StringComparison.OrdinalIgnoreCase) >= 0;

        string gpoGuid = ExtractGPOGuid(distinguishedName);
        string gpoName = ResolveGPOName(gpoGuid, gpoNames, dn);

        string key = $"{serverDisplay}\\{printerName}";
        if (!printers.TryGetValue(key, out var info))
        {
            info = new PrinterInfo
            {
                Name = printerName,
                ServerName = string.IsNullOrEmpty(serverDisplay) ? string.Empty : serverDisplay,
                IsDeployedViaGPO = true
            };
            printers[key] = info;
        }

        if (isPerUser && !string.IsNullOrEmpty(gpoName))
        {
            if (string.IsNullOrEmpty(info.PerUserGPO))
                info.PerUserGPO = gpoName;
            else if (!info.PerUserGPO.Contains(gpoName))
                info.PerUserGPO += $", {gpoName}";
        }

        if (isPerMachine && !string.IsNullOrEmpty(gpoName))
        {
            if (string.IsNullOrEmpty(info.PerComputerGPO))
                info.PerComputerGPO = gpoName;
            else if (!info.PerComputerGPO.Contains(gpoName))
                info.PerComputerGPO += $", {gpoName}";
        }
    }

    private void SupplementWithLocalDeployedPrinters(Dictionary<string, PrinterInfo> printers)
    {
        var allPrinters = _printerEnumerator.EnumeratePrinters(
            Native.PrinterConstants.PRINTER_ENUM_LOCAL | Native.PrinterConstants.PRINTER_ENUM_CONNECTIONS);

        foreach (var p in allPrinters)
        {
            if (p.IsPushedMachine || p.IsPushedUser)
            {
                string serverDisplay = (p.ServerName ?? string.Empty).TrimStart('\\');
                string key = $"{serverDisplay}\\{p.Name}";

                if (!printers.TryGetValue(key, out var existingInfo))
                {
                    p.IsDeployedViaGPO = true;
                    if (p.IsPushedUser) p.PerUserGPO = "Local/Registry Deployment";
                    if (p.IsPushedMachine) p.PerComputerGPO = "Local/Registry Deployment";
                    printers[key] = p;
                }
                else
                {
                    if (p.IsPushedUser && string.IsNullOrEmpty(existingInfo.PerUserGPO))
                        existingInfo.PerUserGPO = "Local/Registry Deployment";
                    if (p.IsPushedMachine && string.IsNullOrEmpty(existingInfo.PerComputerGPO))
                        existingInfo.PerComputerGPO = "Local/Registry Deployment";
                }
            }
        }
    }

    private static string ExtractGPOGuid(string distinguishedName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            distinguishedName,
            @"CN=(\{[A-Fa-f0-9\-]+\}),CN=Policies",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private string ResolveGPOName(string gpoGuid, Dictionary<string, string> gpoNames, string dn)
    {
        if (string.IsNullOrEmpty(gpoGuid))
            return string.Empty;

        if (gpoNames.TryGetValue(gpoGuid, out string? cachedName))
        {
            return cachedName;
        }

        try
        {
            using var gpoSearcher = new System.DirectoryServices.DirectorySearcher();
            gpoSearcher.SearchRoot = new System.DirectoryServices.DirectoryEntry($"LDAP://CN=Policies,CN=System,{dn}");
            gpoSearcher.Filter = $"(&(objectClass=groupPolicyContainer)(cn={gpoGuid}))";
            gpoSearcher.PropertiesToLoad.Add("displayName");

            var gpoResult = gpoSearcher.FindOne();
            if (gpoResult != null && gpoResult.Properties["displayName"].Count > 0)
            {
                string name = gpoResult.Properties["displayName"][0]?.ToString() ?? gpoGuid;
                gpoNames[gpoGuid] = name;
                return name;
            }
        }
        catch
        {
            // Ignore errors
        }

        gpoNames[gpoGuid] = gpoGuid;
        return gpoGuid;
    }
}


