using System.Collections.Generic;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

internal static class CimHelper
{
    internal static WbemObject? GetFirewallRuleInstance(WbemServices session, params string?[] ruleNames)
    {
        HashSet<string> candidateNames = BuildCandidateNames(ruleNames);
        if (candidateNames.Count == 0)
        {
            return null;
        }

        foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetFirewallRule"))
        {
            if (MatchesRuleName(instance, candidateNames))
            {
                return instance;
            }

            instance.Dispose();
        }

        return null;
    }

    internal static WbemObject? GetSecurityFilterInstance(WbemServices session, WbemObject ruleInstance)
    {
        foreach (WbemObject filter in session.EnumerateAssociatedInstances(
                ruleInstance,
                "MSFT_NetFirewallRuleFilterBySecurity",
                "MSFT_NetNetworkLayerSecurityFilter"))
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

    private static bool MatchesRuleName(WbemObject instance, IReadOnlySet<string> candidateNames)
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

    private static string GetPropertyValue(WbemObject instance, string propertyName)
        => instance.GetValue(propertyName)?.ToString() ?? string.Empty;

    internal static bool ReadBool(WbemObject instance, string propertyName)
    {
        object? value = instance.GetValue(propertyName);
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
