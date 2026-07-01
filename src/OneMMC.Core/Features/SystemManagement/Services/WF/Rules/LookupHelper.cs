using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

internal static class LookupHelper
{
    internal static dynamic? FindRule(dynamic rules, string? primaryName, string? secondaryName)
    {
        HashSet<string> candidateNames = new(StringComparer.OrdinalIgnoreCase);
        AddCandidateName(candidateNames, primaryName);
        AddCandidateName(candidateNames, secondaryName);

        foreach (string candidateName in candidateNames)
        {
            dynamic? match = TryGetRuleByExactName(rules, candidateName);
            if (match is not null)
            {
                return match;
            }
        }

        foreach (dynamic item in rules)
        {
            try
            {
                string ruleName = item.Name ?? string.Empty;
                if (candidateNames.Contains(ruleName))
                {
                    return item;
                }

                string resolvedRuleName = WindowsFirewallSupport.ResolveIndirectString(ruleName);
                if (candidateNames.Contains(resolvedRuleName))
                {
                    return item;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    internal static bool ShouldReplaceRule(INetFwRule3 existingRule, FirewallRuleModel updatedRule)
    {
        string existingName = existingRule.Name ?? string.Empty;
        if (!string.Equals(existingName, updatedRule.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsClearingStringProperty(existingRule, "ApplicationName", updatedRule.Program)) return true;
        if (IsClearingStringProperty(existingRule, "serviceName", updatedRule.ServiceName)) return true;
        if (IsClearingStringProperty(existingRule, "Grouping", updatedRule.Grouping)) return true;
        if (IsClearingStringProperty(existingRule, "LocalAppPackageId", WindowsFirewallSupport.NormalizeLocalAppPackageId(updatedRule.LocalAppPackageId))) return true;
        if (IsClearingStringProperty(existingRule, "LocalUserOwner", updatedRule.LocalUserOwner)) return true;
        if (IsClearingStringProperty(existingRule, "LocalUserAuthorizedList", WindowsFirewallSupport.NormalizeComSddlValue(updatedRule.LocalUserAuthorizedList))) return true;
        if (IsClearingStringProperty(existingRule, "RemoteMachineAuthorizedList", WindowsFirewallSupport.NormalizeComSddlValue(updatedRule.RemoteMachineAuthorizedList))) return true;
        if (IsClearingStringProperty(existingRule, "RemoteUserAuthorizedList", WindowsFirewallSupport.NormalizeComSddlValue(updatedRule.RemoteUserAuthorizedList))) return true;

        return false;
    }

    internal static void ReplaceRule(INetFwRules rules, INetFwRule3 existingRule, FirewallRuleModel updatedRule)
    {
        string existingName = existingRule.Name ?? string.Empty;
        INetFwRule3 replacementRule = WindowsFirewallSupport.CreateRule();
        ComApplier.ApplyRuleToComObject(updatedRule, replacementRule);

        if (!string.IsNullOrWhiteSpace(existingName))
        {
            rules.Remove(existingName);
        }

        rules.Add(replacementRule);
    }

    private static void AddCandidateName(ISet<string> names, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        names.Add(name);
        names.Add(WindowsFirewallSupport.ResolveIndirectString(name));
    }

    private static dynamic? TryGetRuleByExactName(dynamic rules, string name)
    {
        try
        {
            return rules.Item(name);
        }
        catch (System.IO.FileNotFoundException ex) when ((uint)ex.HResult == 0x80070002)
        {
            return null;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002)
        {
            return null;
        }
    }

    internal static bool IsClearingStringProperty(INetFwRule3 existingRule, string propertyName, string? newValue)
    {
        if (!string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        try
        {
            string existingValue = propertyName switch
            {
                "ApplicationName" => existingRule.ApplicationName,
                "serviceName" => existingRule.serviceName,
                "Grouping" => existingRule.Grouping,
                "LocalAppPackageId" => existingRule.LocalAppPackageId,
                "LocalUserOwner" => existingRule.LocalUserOwner,
                "LocalUserAuthorizedList" => existingRule.LocalUserAuthorizedList,
                "RemoteMachineAuthorizedList" => existingRule.RemoteMachineAuthorizedList,
                "RemoteUserAuthorizedList" => existingRule.RemoteUserAuthorizedList,
                _ => string.Empty
            } ?? string.Empty;

            return !string.IsNullOrWhiteSpace(existingValue);
        }
        catch
        {
            return false;
        }
    }
}
