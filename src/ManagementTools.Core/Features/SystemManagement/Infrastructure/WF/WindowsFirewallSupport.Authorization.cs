using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;

namespace ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;

public static partial class WindowsFirewallSupport
{
    public static IReadOnlyList<TunnelAuthorizationItem> ParseAuthorizationSddl(string? value, bool allowEntries)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "NotConfigured", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        RawSecurityDescriptor descriptor;
        try
        {
            descriptor = new RawSecurityDescriptor(value);
        }
        catch
        {
            return [];
        }

        if (descriptor.DiscretionaryAcl is null)
        {
            return [];
        }

        List<TunnelAuthorizationItem> items = [];
        foreach (GenericAce ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not CommonAce commonAce || commonAce.SecurityIdentifier is null)
            {
                continue;
            }

            bool isAllow = commonAce.AceQualifier == AceQualifier.AccessAllowed;
            bool isDeny = commonAce.AceQualifier == AceQualifier.AccessDenied;
            if ((allowEntries && !isAllow) || (!allowEntries && !isDeny))
            {
                continue;
            }

            SecurityIdentifier sid = commonAce.SecurityIdentifier;
            items.Add(new TunnelAuthorizationItem
            {
                Sid = sid.Value,
                Name = ResolveAccountName(sid)
            });
        }

        return items;
    }

    public static string BuildAuthorizationSddl(
        IEnumerable<TunnelAuthorizationItem>? allowedItems,
        IEnumerable<TunnelAuthorizationItem>? deniedItems)
    {
        List<string> aces = [];
        aces.AddRange(BuildAuthorizationAces(deniedItems, "D"));
        aces.AddRange(BuildAuthorizationAces(allowedItems, "A"));

        if (aces.Count == 0)
        {
            return "NotConfigured";
        }

        string rawSddl = $"O:LSD:{string.Concat(aces)}";
        
        try
        {
            var sd = new System.Security.AccessControl.RawSecurityDescriptor(rawSddl);
            return sd.GetSddlForm(System.Security.AccessControl.AccessControlSections.All);
        }
        catch
        {
            return rawSddl;
        }
    }



    private static IEnumerable<string> BuildAuthorizationAces(IEnumerable<TunnelAuthorizationItem>? items, string aceType)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (TunnelAuthorizationItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) && string.IsNullOrWhiteSpace(item.Sid))
            {
                continue;
            }

            SecurityIdentifier sid = ResolveSecurityIdentifier(item);
            yield return $"({aceType};;CC;;;{sid.Value})";
        }
    }

    private static SecurityIdentifier ResolveSecurityIdentifier(TunnelAuthorizationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Sid))
        {
            return new SecurityIdentifier(item.Sid);
        }

        return ResolveSecurityIdentifier(item.Name);
    }

    private static SecurityIdentifier ResolveSecurityIdentifier(string accountOrSid)
    {
        if (string.IsNullOrWhiteSpace(accountOrSid))
        {
            throw new ArgumentException("The account name or SID is required.", nameof(accountOrSid));
        }

        string normalizedValue = accountOrSid.Trim();
        if (normalizedValue.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            return new SecurityIdentifier(normalizedValue);
        }

        if (TryResolveWellKnownCapabilitySid(normalizedValue, out string? capabilitySid))
        {
            return new SecurityIdentifier(capabilitySid!);
        }

        var account = new NTAccount(normalizedValue);
        return (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
    }

    private static string ResolveAccountName(SecurityIdentifier sid)
    {
        if (WellKnownCapabilityNameBySid.TryGetValue(sid.Value, out string? capabilityName))
        {
            return capabilityName;
        }

        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            return sid.Value;
        }
    }

}
