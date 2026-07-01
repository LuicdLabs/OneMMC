using System.Collections.Generic;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Rules;

public class WindowsFirewallService
{
    private readonly WindowsFirewallRuleService _firewallRuleService;

    public WindowsFirewallService(WindowsFirewallRuleService firewallRuleService)
    {
        _firewallRuleService = firewallRuleService;
    }

    public IReadOnlyList<FirewallRuleModel> GetRules(FirewallRuleDirection direction)
        => _firewallRuleService.GetRules(direction);

    public IReadOnlyList<PredefinedFirewallRuleGroup> GetPredefinedRuleGroups(FirewallRuleDirection direction)
        => _firewallRuleService.GetPredefinedRuleGroups(direction);

    public void AddRule(FirewallRuleModel rule)
        => _firewallRuleService.AddRule(rule);

    public void SetRuleEnabled(string ruleName, bool enabled)
        => _firewallRuleService.SetRuleEnabled(ruleName, enabled);

    public void UpdateRule(FirewallRuleModel rule)
        => _firewallRuleService.UpdateRule(rule);

    public void DeleteRule(string ruleName)
        => _firewallRuleService.DeleteRule(ruleName);

    public FirewallPolicyModifyState GetLocalPolicyModifyState()
        => _firewallRuleService.GetLocalPolicyModifyState();

    public void RestoreLocalFirewallDefaults()
        => _firewallRuleService.RestoreLocalFirewallDefaults();
}




