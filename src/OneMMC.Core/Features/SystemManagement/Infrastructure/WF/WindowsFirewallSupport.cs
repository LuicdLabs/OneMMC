using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF;

public static partial class WindowsFirewallSupport
{
    internal const string StandardCimNamespace = @"root\StandardCimv2";
    internal const int NetFwRuleDirIn = 1;
    internal const int NetFwRuleDirOut = 2;
    internal const int NetFwActionBlock = 0;
    internal const int NetFwActionAllow = 1;
    internal const int NetFwIpProtocolAny = 256;
    internal const int NetFwProfile2Domain = 1;
    internal const int NetFwProfile2Private = 2;
    internal const int NetFwProfile2Public = 4;
    internal const int NetFwProfile2All = int.MaxValue;
    internal const string AllApplicationPackagesSid = "S-1-15-2-1";
    internal const string ApplicationPackagesOnlyWildcard = "*";

    private static readonly (string Sid, string Name)[] WellKnownCapabilityEntries =
    [
        ("S-1-15-2-1", "ALL APPLICATION PACKAGES"),
        ("S-1-15-3-1", "internetClient"),
        ("S-1-15-3-2", "internetClientServer"),
        ("S-1-15-3-3", "privateNetworkClientServer"),
        ("S-1-15-3-4", "picturesLibrary"),
        ("S-1-15-3-5", "videosLibrary"),
        ("S-1-15-3-6", "musicLibrary"),
        ("S-1-15-3-7", "documentsLibrary"),
        ("S-1-15-3-8", "enterpriseAuthentication"),
        ("S-1-15-3-9", "sharedUserCertificates"),
        ("S-1-15-3-10", "removableStorage")
    ];

    private static readonly Dictionary<string, string> WellKnownCapabilitySidByName = BuildCapabilitySidLookup();
    private static readonly Dictionary<string, string> WellKnownCapabilityNameBySid = BuildCapabilityNameLookup();

    internal static INetFwPolicy2 CreatePolicy2()
    {
        Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
            ?? throw new InvalidOperationException("Windows Firewall COM policy object is unavailable.");

        return (INetFwPolicy2)(Activator.CreateInstance(fwPolicy2Type)
            ?? throw new InvalidOperationException("Failed to create Windows Firewall COM policy object."));
    }

    internal static INetFwRule3 CreateRule()
    {
        Type fwRuleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
            ?? throw new InvalidOperationException("Windows Firewall COM rule object is unavailable.");

        return (INetFwRule3)(Activator.CreateInstance(fwRuleType)
            ?? throw new InvalidOperationException("Failed to create Windows Firewall COM rule object."));
    }

    internal static FirewallRuleProtocol ResolveProtocol(int protocolNumber)
    {
        return protocolNumber switch
        {
            0 => FirewallRuleProtocol.HOPOPT,
            1 => FirewallRuleProtocol.ICMPv4,
            2 => FirewallRuleProtocol.IGMP,
            6 => FirewallRuleProtocol.TCP,
            17 => FirewallRuleProtocol.UDP,
            41 => FirewallRuleProtocol.IPv6,
            43 => FirewallRuleProtocol.IPv6Route,
            44 => FirewallRuleProtocol.IPv6Frag,
            47 => FirewallRuleProtocol.GRE,
            58 => FirewallRuleProtocol.ICMPv6,
            59 => FirewallRuleProtocol.IPv6NoNxt,
            60 => FirewallRuleProtocol.IPv6Opts,
            112 => FirewallRuleProtocol.VRRP,
            113 => FirewallRuleProtocol.PGM,
            115 => FirewallRuleProtocol.L2TP,
            NetFwIpProtocolAny => FirewallRuleProtocol.Any,
            _ => FirewallRuleProtocol.Custom
        };
    }

    internal static int ResolveProtocolNumber(FirewallRuleProtocol protocol, int customProtocolNumber)
    {
        return protocol switch
        {
            FirewallRuleProtocol.HOPOPT => 0,
            FirewallRuleProtocol.ICMPv4 => 1,
            FirewallRuleProtocol.IGMP => 2,
            FirewallRuleProtocol.TCP => 6,
            FirewallRuleProtocol.UDP => 17,
            FirewallRuleProtocol.IPv6 => 41,
            FirewallRuleProtocol.IPv6Route => 43,
            FirewallRuleProtocol.IPv6Frag => 44,
            FirewallRuleProtocol.GRE => 47,
            FirewallRuleProtocol.ICMPv6 => 58,
            FirewallRuleProtocol.IPv6NoNxt => 59,
            FirewallRuleProtocol.IPv6Opts => 60,
            FirewallRuleProtocol.VRRP => 112,
            FirewallRuleProtocol.PGM => 113,
            FirewallRuleProtocol.L2TP => 115,
            FirewallRuleProtocol.Custom => customProtocolNumber,
            _ => NetFwIpProtocolAny
        };
    }

    internal static string NormalizeAddressValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "*";
        }

        string[] entries = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSingleAddressValue)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries.Length == 0 ? "*" : string.Join(",", entries);
    }

    internal static string NormalizePortValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static string NormalizeIcmpTypesAndCodes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string[] entries = value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries.Length == 0 ? string.Empty : string.Join(";", entries);
    }

    internal static string NormalizeInterfaceTypes(string? value, string defaultValue = "All")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return string.Join(
            ",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static string NormalizeInterfaceAliases(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static int BuildProfileMask(FirewallRuleModel rule)
    {
        int profiles = 0;
        if (rule.ProfileDomain)
        {
            profiles |= NetFwProfile2Domain;
        }

        if (rule.ProfilePrivate)
        {
            profiles |= NetFwProfile2Private;
        }

        if (rule.ProfilePublic)
        {
            profiles |= NetFwProfile2Public;
        }

        return profiles == 0 ? NetFwProfile2All : profiles;
    }

    internal static int BuildProfileMask(ConnectionSecurityRuleModel rule)
    {
        int profiles = 0;
        if (rule.ProfileDomain)
        {
            profiles |= NetFwProfile2Domain;
        }

        if (rule.ProfilePrivate)
        {
            profiles |= NetFwProfile2Private;
        }

        if (rule.ProfilePublic)
        {
            profiles |= NetFwProfile2Public;
        }

        return profiles == 0 ? NetFwProfile2All : profiles;
    }

    internal static int NormalizeProfileMask(int mask)
    {
        if (mask == 0 || mask == NetFwProfile2All)
        {
            return NetFwProfile2All;
        }

        int normalized = 0;
        if ((mask & NetFwProfile2Domain) != 0)
        {
            normalized |= NetFwProfile2Domain;
        }

        if ((mask & NetFwProfile2Private) != 0)
        {
            normalized |= NetFwProfile2Private;
        }

        if ((mask & NetFwProfile2Public) != 0)
        {
            normalized |= NetFwProfile2Public;
        }

        return normalized == 0 ? NetFwProfile2All : normalized;
    }

    internal static void ApplyProfileMask(FirewallRuleModel rule, int mask)
    {
        rule.ProfilesMask = NormalizeProfileMask(mask);
        rule.Profile = BuildProfileDisplay(rule.ProfilesMask);
    }

    internal static void ApplyProfileMask(ConnectionSecurityRuleModel rule, int mask)
    {
        int normalized = NormalizeProfileMask(mask);
        rule.ProfilesMask = normalized;
        rule.ProfileDomain = normalized == NetFwProfile2All || (normalized & NetFwProfile2Domain) != 0;
        rule.ProfilePrivate = normalized == NetFwProfile2All || (normalized & NetFwProfile2Private) != 0;
        rule.ProfilePublic = normalized == NetFwProfile2All || (normalized & NetFwProfile2Public) != 0;
        rule.ProfileDisplay = BuildProfileDisplay(normalized);
    }

    internal static string BuildProfileDisplay(int mask)
    {
        if (mask == NetFwProfile2All)
        {
            return "All";
        }

        List<string> profiles = [];
        if ((mask & NetFwProfile2Domain) != 0)
        {
            profiles.Add("Domain");
        }

        if ((mask & NetFwProfile2Private) != 0)
        {
            profiles.Add("Private");
        }

        if ((mask & NetFwProfile2Public) != 0)
        {
            profiles.Add("Public");
        }

        return profiles.Count == 0 ? "All" : string.Join(", ", profiles);
    }

    internal static string BuildRuleDescription(FirewallRuleModel rule)
    {
        string line1 = $"Profile: {rule.Profile} | Local Port: {rule.LocalPortDisplay} | Remote Port: {rule.RemotePortDisplay}";
        string line2 = $"Protocol: {rule.Protocol} | Action: {rule.Action} | Program: {rule.ProgramDisplay}";
        return $"{line1}{Environment.NewLine}{line2}";
    }

    internal static string ResolveIndirectString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '@')
        {
            return value ?? string.Empty;
        }

        Span<char> buffer = stackalloc char[1024];
        int result = Win32PInvoke.SHLoadIndirectString(value, buffer);
        if (result < 0)
        {
            return value;
        }

        int terminatorIndex = buffer.IndexOf('\0');
        ReadOnlySpan<char> resolved = terminatorIndex >= 0 ? buffer[..terminatorIndex] : buffer;
        return resolved.ToString();
    }

    internal static string[] ParseCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string JoinCsv(IEnumerable<string>? values, string defaultValue = "")
    {
        if (values is null)
        {
            return defaultValue;
        }

        string[] normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? defaultValue : string.Join(", ", normalized);
    }

    internal static string ReadInterfaceAliases(object? interfacesValue)
    {
        if (interfacesValue is null)
        {
            return string.Empty;
        }

        if (interfacesValue is string singleValue)
        {
            return NormalizeInterfaceAliases(singleValue);
        }

        if (interfacesValue is IEnumerable<object> enumerable)
        {
            return JoinCsv(enumerable.Select(item => item?.ToString() ?? string.Empty));
        }

        return interfacesValue.ToString() ?? string.Empty;
    }

    internal static object? BuildInterfaceAliasesVariant(string? aliases)
    {
        string[] parsed = ParseCsv(aliases);
        // The COM INetFwRule::Interfaces property expects null to mean "all interfaces".
        // An empty SAFEARRAY is not valid and may throw ArgumentException.
        return parsed.Length == 0 ? null : parsed;
    }

    internal static string NormalizeComStringValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    internal static string NormalizeComSddlValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        return string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "NotConfigured", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;
    }

    internal static string NormalizeLocalAppPackageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        if (string.Equals(normalized, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (string.Equals(normalized, ApplicationPackagesOnlyWildcard, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationPackagesOnlyWildcard;
        }

        if (string.Equals(normalized, "AllApplicationPackages", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ALL APPLICATION PACKAGES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "APPLICATION PACKAGE AUTHORITY\\ALL APPLICATION PACKAGES", StringComparison.OrdinalIgnoreCase))
        {
            return AllApplicationPackagesSid;
        }

        if (TryResolveWellKnownCapabilitySid(normalized, out string? capabilitySid))
        {
            return capabilitySid!;
        }

        return normalized;
    }

    internal static bool IsOwnerSidLikeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        return normalized.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase);
    }

}
