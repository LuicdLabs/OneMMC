using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using Microsoft.Management.Infrastructure;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

internal static class CimHelper
{
    internal static CimInstance? GetFirewallRuleInstance(CimSession session, params string?[] ruleNames)
    {
        HashSet<string> candidateNames = BuildCandidateNames(ruleNames);
        if (candidateNames.Count == 0)
        {
            return null;
        }

        foreach (CimInstance instance in session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetFirewallRule"))
        {
            if (MatchesRuleName(instance, candidateNames))
            {
                return instance;
            }

            instance.Dispose();
        }

        return null;
    }

    internal static CimInstance? GetSecurityFilterInstance(CimSession session, CimInstance ruleInstance)
    {
        foreach (CimInstance filter in session.EnumerateAssociatedInstances(
                WindowsFirewallSupport.StandardCimNamespace,
                ruleInstance,
                "MSFT_NetFirewallRuleFilterBySecurity",
                "MSFT_NetNetworkLayerSecurityFilter",
                null,
                null))
        {
            return filter;
        }

        return null;
    }

    private static HashSet<string> BuildCandidateNames(IEnumerable<string?> ruleNames)
    {
        HashSet<string> candidateNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? ruleName in ruleNames)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
            {
                continue;
            }

            string trimmedRuleName = ruleName.Trim();
            candidateNames.Add(trimmedRuleName);

            string resolvedRuleName = WindowsFirewallSupport.ResolveIndirectString(trimmedRuleName);
            if (!string.IsNullOrWhiteSpace(resolvedRuleName))
            {
                candidateNames.Add(resolvedRuleName.Trim());
            }
        }

        return candidateNames;
    }

    private static bool MatchesRuleName(CimInstance instance, IReadOnlySet<string> candidateNames)
    {
        foreach (string propertyName in new[] { "DisplayName", "ElementName", "PolicyRuleName", "InstanceID", "Name" })
        {
            string propertyValue = GetPropertyValue(instance, propertyName);
            if (string.IsNullOrWhiteSpace(propertyValue))
            {
                continue;
            }

            string trimmedPropertyValue = propertyValue.Trim();
            if (candidateNames.Contains(trimmedPropertyValue))
            {
                return true;
            }

            string resolvedPropertyValue = WindowsFirewallSupport.ResolveIndirectString(trimmedPropertyValue);
            if (!string.IsNullOrWhiteSpace(resolvedPropertyValue) &&
                candidateNames.Contains(resolvedPropertyValue.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetPropertyValue(CimInstance instance, string propertyName)
    {
        CimProperty? property = instance.CimInstanceProperties
            .FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        return property?.Value?.ToString() ?? string.Empty;
    }

    internal static bool ReadBool(CimInstance instance, string propertyName)
    {
        object? value = instance.CimInstanceProperties[propertyName]?.Value;
        return value switch
        {
            bool boolValue => boolValue,
            ushort ushortValue => ushortValue != 0,
            uint uintValue => uintValue != 0,
            int intValue => intValue != 0,
            _ => false
        };
    }
}
