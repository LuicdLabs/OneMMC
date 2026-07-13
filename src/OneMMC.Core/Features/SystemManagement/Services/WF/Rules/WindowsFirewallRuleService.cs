using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

public class WindowsFirewallRuleService
{
    private readonly ILogger<WindowsFirewallRuleService> _logger;

    public WindowsFirewallRuleService()
        : this(NullLogger<WindowsFirewallRuleService>.Instance)
    {
    }

    public WindowsFirewallRuleService(ILogger<WindowsFirewallRuleService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<FirewallRuleModel> GetRules(FirewallRuleDirection direction)
    {
        if (direction == FirewallRuleDirection.ConnectionSecurity)
        {
            return [];
        }

        List<FirewallRuleModel> rules = [];
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        int policyModifyState = policy.get_LocalPolicyModifyState();
        policy.get_Rules(out INetFwRules ruleCollection);
        IReadOnlyDictionary<string, uint> compartmentsByRuleName = FirewallBinaryNativeMethods.GetCompartmentIdsByRuleName();
        int targetDirection = direction == FirewallRuleDirection.Inbound
            ? WindowsFirewallSupport.NetFwRuleDirIn
            : WindowsFirewallSupport.NetFwRuleDirOut;

        foreach (INetFwRule3 rule in FirewallCom.EnumerateRules(ruleCollection))
        {
            if (rule.get_Direction() != targetDirection)
            {
                FirewallCom.Release(rule);
                continue;
            }

            string rawRuleName = rule.get_Name() ?? string.Empty;
            string resolvedRuleName = WindowsFirewallSupport.ResolveIndirectString(rawRuleName);
            int secureFlags = rule.get_SecureFlags();
            int ruleAction = rule.get_Action();
            var model = new FirewallRuleModel
            {
                Name = rawRuleName,
                DisplayName = string.Equals(rawRuleName, resolvedRuleName, StringComparison.Ordinal)
                    ? string.Empty
                    : resolvedRuleName,
                OriginalName = rawRuleName,
                Description = WindowsFirewallSupport.ResolveIndirectString(rule.get_Description() ?? string.Empty),
                Grouping = rule.get_Grouping() ?? string.Empty,
                DisplayGrouping = WindowsFirewallSupport.ResolveIndirectString(rule.get_Grouping() ?? string.Empty),
                Enabled = FirewallCom.ToBool(rule.get_Enabled()),
                Direction = direction,
                Action = ruleAction == WindowsFirewallSupport.NetFwActionAllow ? FirewallRuleAction.Allow : FirewallRuleAction.Block,
                ConnectionAction = (ruleAction == WindowsFirewallSupport.NetFwActionBlock)
                    ? FirewallConnectionAction.Block
                    : (secureFlags > 0 ? FirewallConnectionAction.AllowIfSecure : FirewallConnectionAction.Allow),
                Program = rule.get_ApplicationName() ?? string.Empty,
                ServiceName = rule.get_serviceName() ?? string.Empty,
                Services = rule.get_serviceName() ?? string.Empty,
                LocalPort = rule.get_LocalPorts() ?? string.Empty,
                RemotePort = rule.get_RemotePorts() ?? string.Empty,
                LocalAddress = rule.get_LocalAddresses() ?? string.Empty,
                RemoteAddress = rule.get_RemoteAddresses() ?? string.Empty,
                InterfaceTypes = string.IsNullOrWhiteSpace(rule.get_InterfaceTypes()) ? "All" : rule.get_InterfaceTypes(),
                Interfaces = FirewallCom.ReadInterfaces(rule),
                IcmpTypesAndCodes = rule.get_IcmpTypesAndCodes() ?? string.Empty,
                LocalAppPackageId = rule.get_LocalAppPackageId() ?? string.Empty,
                SecureFlags = secureFlags,
                LocalUserAuthorizedList = rule.get_LocalUserAuthorizedList() ?? string.Empty,
                LocalUserOwner = rule.get_LocalUserOwner() ?? string.Empty,
                RemoteMachineAuthorizedList = rule.get_RemoteMachineAuthorizedList() ?? string.Empty,
                RemoteUserAuthorizedList = rule.get_RemoteUserAuthorizedList() ?? string.Empty,
                EdgeTraversalOptions = rule.get_EdgeTraversalOptions(),
                PolicyModifyState = (FirewallPolicyModifyState)policyModifyState
            };

            if (TryGetCompartmentId(compartmentsByRuleName, rawRuleName, resolvedRuleName, out uint compartmentId) &&
                compartmentId > 0)
            {
                model.Compartments = compartmentId.ToString(CultureInfo.InvariantCulture);
            }

            int protocolNumber = rule.get_Protocol();
            model.ProtocolNumber = protocolNumber == WindowsFirewallSupport.NetFwIpProtocolAny ? 256 : protocolNumber;
            model.Protocol = WindowsFirewallSupport.ResolveProtocol(protocolNumber);

            int profiles = rule.get_Profiles();
            WindowsFirewallSupport.ApplyProfileMask(model, profiles);
            model.IsRuleGroupEnabled = string.IsNullOrWhiteSpace(model.Grouping)
                || IsRuleGroupEnabled(model.Grouping, (FirewallRuleProfiles)model.ProfilesMask);
            model.DisplayDescription = WindowsFirewallSupport.BuildRuleDescription(model);
            rules.Add(model);
            FirewallCom.Release(rule);
        }

        return rules
            .OrderBy(rule => rule.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public FirewallRuleModel? GetRule(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return null;
        }

        foreach (FirewallRuleDirection direction in new[] { FirewallRuleDirection.Inbound, FirewallRuleDirection.Outbound })
        {
            FirewallRuleModel? rule = GetRules(direction).FirstOrDefault(item =>
                string.Equals(item.Name, ruleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.OriginalName, ruleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.DisplayName, ruleName, StringComparison.OrdinalIgnoreCase));
            if (rule is not null)
            {
                return rule;
            }
        }

        return null;
    }

    public IReadOnlyList<PredefinedFirewallRuleGroup> GetPredefinedRuleGroups(FirewallRuleDirection direction)
    {
        if (direction == FirewallRuleDirection.ConnectionSecurity)
        {
            return [];
        }

        Dictionary<string, PredefinedFirewallRuleGroup> groups = new(StringComparer.OrdinalIgnoreCase);
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        policy.get_Rules(out INetFwRules ruleCollection);
        int targetDirection = direction == FirewallRuleDirection.Inbound
            ? WindowsFirewallSupport.NetFwRuleDirIn
            : WindowsFirewallSupport.NetFwRuleDirOut;

        foreach (INetFwRule3 rule in FirewallCom.EnumerateRules(ruleCollection))
        {
            try
            {
                if (rule.get_Direction() != targetDirection)
                {
                    continue;
                }

                string grouping = rule.get_Grouping() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(grouping))
                {
                    continue;
                }

                if (!groups.TryGetValue(grouping, out PredefinedFirewallRuleGroup? group))
                {
                    group = new PredefinedFirewallRuleGroup
                    {
                        GroupKey = grouping,
                        DisplayName = WindowsFirewallSupport.ResolveIndirectString(grouping)
                    };
                    groups[grouping] = group;
                }

                string ruleName = rule.get_Name() ?? string.Empty;
                if (group.Rules.Any(item => string.Equals(item.RuleName, ruleName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                group.Rules.Add(new PredefinedFirewallRuleItem
                {
                    RuleName = ruleName,
                    Name = WindowsFirewallSupport.ResolveIndirectString(ruleName),
                    Description = WindowsFirewallSupport.ResolveIndirectString(rule.get_Description() ?? string.Empty),
                    Service = rule.get_serviceName() ?? string.Empty
                });
            }
            finally
            {
                FirewallCom.Release(rule);
            }
        }

        return groups.Values
            .OrderBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool IsRuleGroupEnabled(string groupName, FirewallRuleProfiles profiles = FirewallRuleProfiles.All)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return true;
        }

        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        int profileMask = profiles == FirewallRuleProfiles.None
            ? WindowsFirewallSupport.NetFwProfile2All
            : WindowsFirewallSupport.NormalizeProfileMask((int)profiles);
        return FirewallCom.ToBool(policy.IsRuleGroupEnabled(profileMask, groupName));
    }

    public void SetRuleGroupEnabled(string groupName, bool enabled, FirewallRuleProfiles profiles = FirewallRuleProfiles.All)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        int profileMask = profiles == FirewallRuleProfiles.None
            ? WindowsFirewallSupport.NetFwProfile2All
            : WindowsFirewallSupport.NormalizeProfileMask((int)profiles);
        policy.EnableRuleGroup(profileMask, groupName, FirewallCom.ToVariantBool(enabled));
        _logger.LogInformation("Set rule group {GroupName} enabled={Enabled}.", groupName, enabled);
    }

    public FirewallPolicyModifyState GetLocalPolicyModifyState()
    {
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        return (FirewallPolicyModifyState)policy.get_LocalPolicyModifyState();
    }

    public void RestoreLocalFirewallDefaults()
    {
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        policy.RestoreLocalFirewallDefaults();
        _logger.LogWarning("Restored local Windows Firewall defaults.");
    }

    public void AddRule(FirewallRuleModel rule)
    {
        WindowsFirewallSupport.ValidateRule(rule);
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();

        if (rule.IsPredefined)
        {
            SetRuleGroupEnabled(rule.Grouping, true, (FirewallRuleProfiles)rule.ProfilesMask);
            return;
        }

        INetFwRule3 fwRule = WindowsFirewallSupport.CreateRule();
        ComApplier.ApplyRuleToComObject(rule, fwRule);
        policy.get_Rules(out INetFwRules ruleCollection);
        ruleCollection.Add(fwRule);
        ApplyRuleCompartment(rule);
        _logger.LogInformation("Added Windows Firewall rule {RuleName}.", rule.Name);
    }

    public void SetRuleEnabled(string ruleName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new ArgumentException("Firewall rule name is required.", nameof(ruleName));
        }

        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        policy.get_Rules(out INetFwRules ruleCollection);
        INetFwRule3? existingRule = LookupHelper.FindRule(ruleCollection, ruleName, ruleName);
        if (existingRule is null)
        {
            _logger.LogWarning(
                "Skipped updating Windows Firewall rule enabled state because it could not be found. Name={RuleName}",
                ruleName);
            throw new InvalidOperationException($"Windows Firewall rule '{ruleName}' was not found.");
        }

        existingRule.put_Enabled(FirewallCom.ToVariantBool(enabled));
        INetFwRule3? verifiedRule = LookupHelper.FindRule(ruleCollection, ruleName, ruleName);
        bool verifiedState = verifiedRule is null ? !enabled : FirewallCom.ToBool(verifiedRule.get_Enabled());
        if (verifiedState != enabled)
        {
            throw new InvalidOperationException(
                $"Windows Firewall did not apply rule '{ruleName}' enabled={enabled}.");
        }

        _logger.LogInformation("Set Windows Firewall rule {RuleName} enabled={Enabled}.", ruleName, enabled);
    }

    public void UpdateRule(FirewallRuleModel rule)
    {
        WindowsFirewallSupport.ValidateRule(rule);
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        policy.get_Rules(out INetFwRules ruleCollection);

        if (rule.IsPredefined)
        {
            INetFwRule3? predefinedRule = LookupHelper.FindRule(ruleCollection, rule.OriginalName, rule.Name);
            if (predefinedRule is null)
            {
                _logger.LogWarning(
                    "Skipped updating predefined Windows Firewall rule because it could not be found. Name={RuleName}, OriginalName={OriginalName}",
                    rule.Name,
                    rule.OriginalName);
                return;
            }

            ComApplier.ApplyMutablePredefinedRuleToComObject(rule, predefinedRule);

            _logger.LogInformation("Updated predefined Windows Firewall rule {RuleName}.", rule.Name);
            return;
        }

        INetFwRule3? existingRule = LookupHelper.FindRule(ruleCollection, rule.OriginalName, rule.Name);
        if (existingRule is null)
        {
            _logger.LogWarning(
                "Skipped updating Windows Firewall rule because it could not be found. Name={RuleName}, OriginalName={OriginalName}",
                rule.Name,
                rule.OriginalName);
            return;
        }

        if (LookupHelper.ShouldReplaceRule(existingRule, rule))
        {
            LookupHelper.ReplaceRule(ruleCollection, existingRule, rule);
        }
        else
        {
            ComApplier.ApplyRuleToComObject(rule, existingRule);
        }

        rule.OriginalName = rule.Name;
        ApplyRuleCompartment(rule);
        _logger.LogInformation("Updated Windows Firewall rule {RuleName}.", rule.Name);
    }

    private static bool TryGetCompartmentId(
        IReadOnlyDictionary<string, uint> compartmentsByRuleName,
        string rawRuleName,
        string resolvedRuleName,
        out uint compartmentId)
    {
        return compartmentsByRuleName.TryGetValue(rawRuleName, out compartmentId) ||
               compartmentsByRuleName.TryGetValue(resolvedRuleName, out compartmentId);
    }

    private static void ApplyRuleCompartment(FirewallRuleModel rule)
    {
        if (!FirewallBinaryNativeMethods.TryParseCompartmentId(rule.Compartments, out uint compartmentId))
        {
            throw new ArgumentException("Firewall rule compartment must be a number from 0 through 65535.", nameof(rule));
        }

        if (compartmentId == 0 && !FirewallBinaryNativeMethods.IsAvailable)
        {
            return;
        }

        FirewallBinaryNativeMethods.SetCompartmentId(rule, compartmentId);
    }

    public void DeleteRule(string ruleName)
    {
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        policy.get_Rules(out INetFwRules ruleCollection);
        ruleCollection.Remove(ruleName);
        _logger.LogInformation("Deleted Windows Firewall rule {RuleName}.", ruleName);
    }

    public bool TryGetOverrideBlockRules(string ruleName, out bool overrideBlockRules)
        => TryGetOverrideBlockRules(new[] { ruleName }, out overrideBlockRules);

    public bool TryGetOverrideBlockRules(IEnumerable<string?> ruleNames, out bool overrideBlockRules)
    {
        overrideBlockRules = false;
        string?[] lookupNames = ruleNames.Where(ruleName => !string.IsNullOrWhiteSpace(ruleName)).ToArray();
        if (lookupNames.Length == 0)
        {
            return false;
        }

        try
        {
            using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
            using WbemObject? ruleInstance = CimHelper.GetFirewallRuleInstance(session, lookupNames);
            if (ruleInstance is null)
            {
                return false;
            }

            using WbemObject? filter = CimHelper.GetSecurityFilterInstance(session, ruleInstance);
            if (filter is null)
            {
                return false;
            }

            overrideBlockRules = CimHelper.ReadBool(filter, "OverrideBlockRules");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read OverrideBlockRules for firewall rule {RuleName}.", FormatRuleNames(lookupNames));
            return false;
        }
    }

    public bool SetOverrideBlockRules(string ruleName, bool overrideBlockRules)
        => SetOverrideBlockRules(new[] { ruleName }, overrideBlockRules);

    public bool SetOverrideBlockRules(IEnumerable<string?> ruleNames, bool overrideBlockRules)
    {
        string?[] lookupNames = ruleNames.Where(ruleName => !string.IsNullOrWhiteSpace(ruleName)).ToArray();
        if (lookupNames.Length == 0)
        {
            return false;
        }

        try
        {
            using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
            using WbemObject? ruleInstance = CimHelper.GetFirewallRuleInstance(session, lookupNames);
            if (ruleInstance is null)
            {
                _logger.LogWarning("Firewall rule {RuleName} not found when setting OverrideBlockRules.", FormatRuleNames(lookupNames));
                return false;
            }

            using WbemObject? filter = CimHelper.GetSecurityFilterInstance(session, ruleInstance);
            if (filter is null)
            {
                _logger.LogWarning("Security filter not found for firewall rule {RuleName}.", FormatRuleNames(lookupNames));
                return false;
            }

            if (filter.TrySetProperty("OverrideBlockRules", overrideBlockRules))
            {
                session.ModifyInstance(filter);
                _logger.LogInformation(
                    "Set OverrideBlockRules for firewall rule {RuleName} to {OverrideBlockRules}.",
                    FormatRuleNames(lookupNames),
                    overrideBlockRules);
                return true;
            }
            else
            {
                _logger.LogWarning("OverrideBlockRules property not available for firewall rule {RuleName}.", FormatRuleNames(lookupNames));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set OverrideBlockRules for firewall rule {RuleName}.", FormatRuleNames(lookupNames));
            return false;
        }
    }

    private static string FormatRuleNames(IEnumerable<string?> ruleNames)
        => string.Join(", ", ruleNames.Where(ruleName => !string.IsNullOrWhiteSpace(ruleName)));
}
