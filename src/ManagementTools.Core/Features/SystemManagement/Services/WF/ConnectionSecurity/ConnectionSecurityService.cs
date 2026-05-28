using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

public class ConnectionSecurityService
{
    private readonly ILogger<ConnectionSecurityService> _logger;

    public ConnectionSecurityService()
        : this(NullLogger<ConnectionSecurityService>.Instance)
    {
    }

    public ConnectionSecurityService(ILogger<ConnectionSecurityService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ConnectionSecurityRuleModel> GetRules()
    {
        using CimSession session = CimSession.Create(null);
        List<ConnectionSecurityRuleModel> rules = [];

        foreach (CimInstance instance in session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetConSecRule"))
        {
            using (instance)
            {
                rules.Add(RuleMapper.MapRule(session, instance));
            }
        }

        return rules
            .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ConnectionSecurityRuleModel? GetRule(string name)
    {
        return GetRules().FirstOrDefault(rule =>
            string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.OriginalName, name, StringComparison.OrdinalIgnoreCase));
    }

    public void AddRule(ConnectionSecurityRuleModel rule)
    {
        WindowsFirewallSupport.ValidateConnectionSecurityRule(rule);

        using CimSession session = CimSession.Create(null);
        AddRuleInternal(session, rule);
        rule.OriginalName = rule.Name;
        _logger.LogInformation("Added connection security rule {RuleName}.", rule.Name);
    }

    public void UpdateRule(ConnectionSecurityRuleModel rule)
    {
        WindowsFirewallSupport.ValidateConnectionSecurityRule(rule);

        using CimSession session = CimSession.Create(null);
        string lookupName = string.IsNullOrWhiteSpace(rule.OriginalName)
            ? rule.Name
            : rule.OriginalName;

        using CimInstance existing = GetRuleInstance(session, lookupName)
            ?? throw new InvalidOperationException($"Connection security rule '{lookupName}' was not found.");

        if (!string.Equals(lookupName, rule.Name, StringComparison.OrdinalIgnoreCase))
        {
            AddRuleInternal(session, rule);
            AuthManager.DeleteManagedAuthSets(session, lookupName);
            DeleteRuleInternal(session, existing);
        }
        else
        {
            RuleApplier.ApplyRuleState(session, existing, rule);
            RuleApplier.ApplyAddressFilter(session, existing, rule);
            RuleApplier.ApplyProtocolFilter(session, existing, rule);
            RuleApplier.ApplyInterfaceTypeFilter(session, existing, rule);
            SetRuleEnabledInternal(session, existing, rule.Enabled);
        }

        rule.OriginalName = rule.Name;
        _logger.LogInformation("Updated connection security rule {RuleName}.", rule.Name);
    }

    public void DeleteRule(string name)
    {
        using CimSession session = CimSession.Create(null);
        using CimInstance existing = GetRuleInstance(session, name)
            ?? throw new InvalidOperationException($"Connection security rule '{name}' was not found.");

        AuthManager.DeleteManagedAuthSets(session, name);
        string policyRuleName = existing.CimInstanceProperties["PolicyRuleName"]?.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(policyRuleName))
        {
            AuthManager.DeleteManagedAuthSets(session, policyRuleName);
        }

        DeleteRuleInternal(session, existing);
        _logger.LogInformation("Deleted connection security rule {RuleName}.", name);
    }

    public void SetRuleEnabled(string name, bool enabled)
    {
        using CimSession session = CimSession.Create(null);
        using CimInstance existing = GetRuleInstance(session, name)
            ?? throw new InvalidOperationException($"Connection security rule '{name}' was not found.");

        SetRuleEnabledInternal(session, existing, enabled);
        _logger.LogInformation("Set connection security rule {RuleName} enabled={Enabled}.", name, enabled);
    }

    private static void AddRuleInternal(CimSession session, ConnectionSecurityRuleModel rule)
    {
        using CimInstance skeleton = CreateRuleSkeleton(rule);
        using CimInstance _ = session.CreateInstance(WindowsFirewallSupport.StandardCimNamespace, skeleton);

        using CimInstance created = GetRuleInstance(session, rule.Name)
            ?? throw new InvalidOperationException($"Connection security rule '{rule.Name}' was created but could not be queried.");

        try
        {
            RuleApplier.ApplyRuleState(session, created, rule);
            RuleApplier.ApplyAddressFilter(session, created, rule);
            RuleApplier.ApplyProtocolFilter(session, created, rule);
            RuleApplier.ApplyInterfaceTypeFilter(session, created, rule);
            SetRuleEnabledInternal(session, created, rule.Enabled);
        }
        catch
        {
            DeleteRuleInternal(session, created);
            throw;
        }
    }

    private static CimInstance CreateRuleSkeleton(ConnectionSecurityRuleModel rule)
    {
        var instance = new CimInstance("MSFT_NetConSecRule", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("ElementName", rule.Name.Trim(), CimType.String, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("Description", (rule.Description ?? string.Empty).Trim(), CimType.String, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("Profiles", ValueHelper.ResolveProfilesMask(rule), CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("InboundSecurity", (ushort)rule.InboundSecurity, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("OutboundSecurity", (ushort)rule.OutboundSecurity, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("Mode", ValueHelper.ResolveModeValue(rule.Mode), CimType.UInt16, CimFlags.None));
        return instance;
    }

    private static CimInstance? GetRuleInstance(CimSession session, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (CimInstance instance in session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetConSecRule"))
        {
            if (MatchesRuleName(instance, name))
            {
                return instance;
            }

            instance.Dispose();
        }

        return null;
    }

    private static bool MatchesRuleName(CimInstance instance, string name)
    {
        string displayName = instance.CimInstanceProperties["DisplayName"]?.Value?.ToString() ?? string.Empty;
        string elementName = instance.CimInstanceProperties["ElementName"]?.Value?.ToString() ?? string.Empty;
        string policyRuleName = instance.CimInstanceProperties["PolicyRuleName"]?.Value?.ToString() ?? string.Empty;

        return string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(elementName, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(policyRuleName, name, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteRuleInternal(CimSession session, CimInstance instance)
    {
        session.DeleteInstance(WindowsFirewallSupport.StandardCimNamespace, instance);
    }

    private static void SetRuleEnabledInternal(CimSession session, CimInstance instance, bool enabled)
    {
        string methodName = enabled ? "Enable" : "Disable";
        session.InvokeMethod(
            WindowsFirewallSupport.StandardCimNamespace,
            instance,
            methodName,
            new CimMethodParametersCollection());
    }

}

