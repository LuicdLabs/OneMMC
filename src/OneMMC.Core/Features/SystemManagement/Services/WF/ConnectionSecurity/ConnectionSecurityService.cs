using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

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
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        List<ConnectionSecurityRuleModel> rules = [];

        foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetConSecRule"))
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

        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        AddRuleInternal(session, rule);
        rule.OriginalName = rule.Name;
        _logger.LogInformation("Added connection security rule {RuleName}.", rule.Name);
    }

    public void UpdateRule(ConnectionSecurityRuleModel rule)
    {
        WindowsFirewallSupport.ValidateConnectionSecurityRule(rule);

        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        string lookupName = string.IsNullOrWhiteSpace(rule.OriginalName)
            ? rule.Name
            : rule.OriginalName;

        using WbemObject existing = GetRuleInstance(session, lookupName)
            ?? throw new InvalidOperationException($"Connection security rule '{lookupName}' was not found.");

        if (!string.Equals(lookupName, rule.Name, StringComparison.OrdinalIgnoreCase))
        {
            string previousRuleIdentity = AuthManager.ResolveRuleIdentity(existing, rule);
            AddRuleInternal(session, rule);
            DeleteRuleInternal(session, existing);
            AuthManager.DeleteManagedAuthSets(session, previousRuleIdentity);
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
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject existing = GetRuleInstance(session, name)
            ?? throw new InvalidOperationException($"Connection security rule '{name}' was not found.");

        string policyRuleName = existing.GetValue("PolicyRuleName")?.ToString() ?? string.Empty;
        DeleteRuleInternal(session, existing);
        AuthManager.DeleteManagedAuthSets(session, name);
        if (!string.IsNullOrWhiteSpace(policyRuleName))
        {
            AuthManager.DeleteManagedAuthSets(session, policyRuleName);
        }
        _logger.LogInformation("Deleted connection security rule {RuleName}.", name);
    }

    public void SetRuleEnabled(string name, bool enabled)
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject existing = GetRuleInstance(session, name)
            ?? throw new InvalidOperationException($"Connection security rule '{name}' was not found.");

        SetRuleEnabledInternal(session, existing, enabled);
        _logger.LogInformation("Set connection security rule {RuleName} enabled={Enabled}.", name, enabled);
    }

    private static void AddRuleInternal(WbemServices session, ConnectionSecurityRuleModel rule)
    {
        using WbemObject skeleton = CreateRuleSkeleton(session, rule);
        session.CreateInstance(skeleton);

        using WbemObject created = GetRuleInstance(session, rule.Name)
            ?? throw new InvalidOperationException($"Connection security rule '{rule.Name}' was created but could not be queried.");
        string ruleIdentity = AuthManager.ResolveRuleIdentity(created, rule);

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
            AuthManager.DeleteManagedAuthSets(session, ruleIdentity);
            throw;
        }
    }

    private static WbemObject CreateRuleSkeleton(WbemServices session, ConnectionSecurityRuleModel rule)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetConSecRule");
        instance.SetProperty("ElementName", rule.Name.Trim(), WbemType.String);
        instance.SetProperty("Description", (rule.Description ?? string.Empty).Trim(), WbemType.String);
        instance.SetProperty("Profiles", ValueHelper.ResolveProfilesMask(rule), WbemType.UInt16);
        instance.SetProperty("InboundSecurity", (ushort)rule.InboundSecurity, WbemType.UInt16);
        instance.SetProperty("OutboundSecurity", (ushort)rule.OutboundSecurity, WbemType.UInt16);
        instance.SetProperty("Mode", ValueHelper.ResolveModeValue(rule.Mode), WbemType.UInt16);
        return instance;
    }

    private static WbemObject? GetRuleInstance(WbemServices session, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetConSecRule"))
        {
            if (MatchesRuleName(instance, name))
            {
                return instance;
            }

            instance.Dispose();
        }

        return null;
    }

    private static bool MatchesRuleName(WbemObject instance, string name)
    {
        string displayName = instance.GetValue("DisplayName")?.ToString() ?? string.Empty;
        string elementName = instance.GetValue("ElementName")?.ToString() ?? string.Empty;
        string policyRuleName = instance.GetValue("PolicyRuleName")?.ToString() ?? string.Empty;

        return string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(elementName, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(policyRuleName, name, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteRuleInternal(WbemServices session, WbemObject instance)
    {
        session.DeleteInstance(instance);
    }

    private static void SetRuleEnabledInternal(WbemServices session, WbemObject instance, bool enabled)
    {
        string methodName = enabled ? "Enable" : "Disable";
        session.ExecMethod(instance, methodName);
    }

}

