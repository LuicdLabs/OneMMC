using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OneMMC.Core.Infrastructure.Interop;
using OneMMC.Core.Infrastructure.Interop.Adsi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.PrintManagement.Models;

namespace OneMMC.Core.Features.PrintManagement.Services;

/// <summary>
/// Provides read/write access to printer connection deployments stored in GPOs.
/// </summary>
public sealed class GpoPrinterDeploymentService
{
    private readonly ILogger<GpoPrinterDeploymentService> _logger;

    public GpoPrinterDeploymentService()
        : this(NullLogger<GpoPrinterDeploymentService>.Instance)
    {
    }

    public GpoPrinterDeploymentService(ILogger<GpoPrinterDeploymentService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves all GPO printer deployments for a specific connection path.
    /// </summary>
    public Task<IReadOnlyList<GpoPrinterDeploymentEntry>> GetDeploymentsAsync(string connectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionPath);

        return Task.Run(() =>
        {
            string domainDn = GetDomainDistinguishedName();
            string? serverName = GetServerName(connectionPath);
            string? printerName = GetPrinterName(connectionPath);
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException("Invalid printer connection path.", nameof(connectionPath));
            }

            var results = new List<GpoPrinterDeploymentEntry>();
            var gpoNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string searchRoot = $"LDAP://CN=Policies,CN=System,{domainDn}";
            string escapedUnc = EscapeLdapFilterValue(connectionPath);
            string escapedPrinter = EscapeLdapFilterValue(printerName);
            string escapedServer = EscapeLdapFilterValue($"\\\\{serverName}");

            string filter =
                "(&" +
                "(objectClass=msPrint-ConnectionPolicy)" +
                "(|" +
                $"(uNCName={escapedUnc})" +
                $"(&" +
                $"(printerName={escapedPrinter})" +
                $"(serverName={escapedServer})" +
                ")" +
                ")" +
                ")";

            using var searcher = Adsi.BindSearcher(searchRoot);
            foreach (AdsiSearcher.Row result in searcher.Search(filter, ["uNCName", "serverName", "printerName", "distinguishedName"]))
            {
                string distinguishedName = result["distinguishedName"];
                string unc = result["uNCName"];
                string server = result["serverName"];
                string printer = result["printerName"];

                string gpoGuid = ExtractGpoGuid(distinguishedName);
                string gpoName = ResolveGpoName(gpoGuid, gpoNames, domainDn);

                GpoPrinterDeploymentScope? scope = GetScopeFromDistinguishedName(distinguishedName);
                if (scope is null)
                {
                    continue;
                }

                string resolvedUnc = string.IsNullOrWhiteSpace(unc)
                    ? BuildUnc(server, printer)
                    : unc;

                results.Add(new GpoPrinterDeploymentEntry
                {
                    PrinterName = resolvedUnc,
                    ConnectionPath = resolvedUnc,
                    GpoName = gpoName,
                    GpoGuid = gpoGuid,
                    ConnectionType = scope.Value,
                    DistinguishedName = distinguishedName
                });
            }

            return (IReadOnlyList<GpoPrinterDeploymentEntry>)results
                .OrderBy(entry => entry.GpoName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ConnectionType)
                .ToList();
        });
    }

    /// <summary>
    /// Retrieves all available Group Policy Objects in the current domain.
    /// </summary>
    public Task<IReadOnlyList<GroupPolicyObjectInfo>> GetGroupPolicyObjectsAsync()
    {
        return Task.Run(() =>
        {
            string domainDn = GetDomainDistinguishedName();
            string searchRoot = $"LDAP://CN=Policies,CN=System,{domainDn}";

            using var searcher = Adsi.BindSearcher(searchRoot);
            var gpos = new List<GroupPolicyObjectInfo>();
            foreach (AdsiSearcher.Row result in searcher.Search("(objectClass=groupPolicyContainer)", ["displayName", "name", "distinguishedName"]))
            {
                string guid = result["name"];
                string displayName = result["displayName"];
                string distinguishedName = result["distinguishedName"];

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = guid;
                }

                gpos.Add(new GroupPolicyObjectInfo
                {
                    Guid = guid,
                    DisplayName = displayName,
                    DistinguishedName = distinguishedName
                });
            }

            return (IReadOnlyList<GroupPolicyObjectInfo>)gpos
                .OrderBy(gpo => gpo.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    /// <summary>
    /// Adds a printer connection deployment entry to the specified GPO.
    /// </summary>
    public Task AddDeploymentAsync(string gpoGuid, string connectionPath, GpoPrinterDeploymentScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gpoGuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionPath);

        return Task.Run(() =>
        {
            string domainDn = GetDomainDistinguishedName();
            string normalizedGuid = NormalizeGuid(gpoGuid);
            string gpoPath = $"LDAP://CN={normalizedGuid},CN=Policies,CN=System,{domainDn}";
            string? serverName = GetServerName(connectionPath);
            string? printerName = GetPrinterName(connectionPath);

            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException("Invalid printer connection path.", nameof(connectionPath));
            }

            using var gpoEntry = Adsi.BindObject(gpoPath);
            using var scopeEntry = GetScopeEntry(gpoEntry, scope);
            using var printersEntry = EnsureChildContainer(scopeEntry, "Printers");

            string entryName = Guid.NewGuid().ToString("B");
            using var policyEntry = printersEntry.CreateChild("msPrint-ConnectionPolicy", $"CN={entryName}");

            PutString(policyEntry, "uNCName", connectionPath);
            PutString(policyEntry, "serverName", $"\\\\{serverName}");
            PutString(policyEntry, "printerName", printerName);
            policyEntry.SetInfo();

            _logger.LogInformation(
                "Added GPO printer deployment {ConnectionPath} to {GpoGuid} ({Scope})",
                connectionPath,
                normalizedGuid,
                scope);
        });
    }

    /// <summary>
    /// Removes a printer connection deployment entry from Active Directory.
    /// </summary>
    public Task RemoveDeploymentAsync(string distinguishedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName);

        return Task.Run(() =>
        {
            using var entry = Adsi.BindObject($"LDAP://{distinguishedName}");
            entry.DeleteTree();
            _logger.LogInformation("Removed GPO printer deployment {DistinguishedName}", distinguishedName);
        });
    }

    private static string GetDomainDistinguishedName()
    {
        try
        {
            string dn = Adsi.GetDefaultNamingContext();
            if (string.IsNullOrWhiteSpace(dn))
            {
                throw new InvalidOperationException("Unable to resolve the Active Directory domain.");
            }

            return dn;
        }
        catch (COMException ex) when (Adsi.IsDirectoryUnavailable(ex.ErrorCode))
        {
            throw new InvalidOperationException("Unable to connect to Active Directory.", ex);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException("Active Directory domain not found.", ex);
        }
    }

    private static AdsiObject GetScopeEntry(AdsiObject gpoEntry, GpoPrinterDeploymentScope scope)
    {
        string scopeName = scope == GpoPrinterDeploymentScope.PerUser ? "User" : "Machine";
        try
        {
            return gpoEntry.GetChild("container", $"CN={scopeName}");
        }
        catch (COMException)
        {
            return gpoEntry.GetChild(className: null, $"CN={scopeName}");
        }
    }

    private static AdsiObject EnsureChildContainer(AdsiObject parent, string name)
    {
        try
        {
            return parent.GetChild("container", $"CN={name}");
        }
        catch (COMException)
        {
            using var created = parent.CreateChild("container", $"CN={name}");
            created.SetInfo();
        }

        return parent.GetChild("container", $"CN={name}");
    }

    /// <summary>Puts one string attribute into the entry's property cache.</summary>
    private static void PutString(AdsiObject entry, string attribute, string value)
    {
        using Variant variant = Variant.FromString(value);
        entry.Put(attribute, in variant);
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
    }

    private static string ExtractGpoGuid(string distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return string.Empty;
        }

        Match match = Regex.Match(
            distinguishedName,
            @"CN=(\{[A-Fa-f0-9\-]+\}),CN=Policies",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ResolveGpoName(string gpoGuid, IDictionary<string, string> gpoNames, string domainDn)
    {
        if (string.IsNullOrWhiteSpace(gpoGuid))
        {
            return string.Empty;
        }

        if (gpoNames.TryGetValue(gpoGuid, out string? cached))
        {
            return cached;
        }

        try
        {
            using var gpoSearcher = Adsi.BindSearcher($"LDAP://CN=Policies,CN=System,{domainDn}");
            AdsiSearcher.Row? gpoResult = gpoSearcher.Search(
                $"(&(objectClass=groupPolicyContainer)(cn={EscapeLdapFilterValue(gpoGuid)}))",
                ["displayName"],
                firstOnly: true).FirstOrDefault();
            if (gpoResult is not null && gpoResult["displayName"].Length > 0)
            {
                string name = gpoResult["displayName"];
                gpoNames[gpoGuid] = name;
                return name;
            }
        }
        catch
        {
            // Ignore resolution errors and fall back to GUID.
        }

        gpoNames[gpoGuid] = gpoGuid;
        return gpoGuid;
    }

    private static GpoPrinterDeploymentScope? GetScopeFromDistinguishedName(string distinguishedName)
    {
        if (distinguishedName.IndexOf("CN=User,", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return GpoPrinterDeploymentScope.PerUser;
        }

        if (distinguishedName.IndexOf("CN=Machine,", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return GpoPrinterDeploymentScope.PerMachine;
        }

        return null;
    }

    private static string? GetServerName(string connectionPath)
    {
        if (!connectionPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        string trimmed = connectionPath.TrimStart('\\');
        int slashIndex = trimmed.IndexOf('\\');
        return slashIndex > 0 ? trimmed.Substring(0, slashIndex) : null;
    }

    private static string? GetPrinterName(string connectionPath)
    {
        if (!connectionPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        string trimmed = connectionPath.TrimStart('\\');
        int slashIndex = trimmed.IndexOf('\\');
        return slashIndex > 0 && slashIndex < trimmed.Length - 1
            ? trimmed[(slashIndex + 1)..]
            : null;
    }

    private static string BuildUnc(string serverName, string printerName)
    {
        string trimmedServer = serverName.TrimStart('\\');
        return $"\\\\{trimmedServer}\\{printerName}";
    }

    private static string NormalizeGuid(string gpoGuid)
    {
        string trimmed = gpoGuid.Trim();
        return trimmed.StartsWith("{", StringComparison.Ordinal) ? trimmed : $"{{{trimmed}}}";
    }
}


