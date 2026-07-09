using System;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

internal static class RuleApplier
{
    internal static void ApplyRuleState(WbemServices session, WbemObject instance, ConnectionSecurityRuleModel rule)
    {
        string ruleIdentity = AuthManager.ResolveRuleIdentity(instance, rule);

        instance.SetProperty("Description", string.IsNullOrWhiteSpace(rule.Description)
            ? null
            : rule.Description.Trim());
        instance.SetProperty("Profiles", ValueHelper.ResolveProfilesMask(rule));
        instance.SetProperty("InboundSecurity", (ushort)rule.InboundSecurity);
        instance.SetProperty("OutboundSecurity", (ushort)rule.OutboundSecurity);
        instance.SetProperty("Mode", ValueHelper.ResolveModeValue(rule.Mode));
        instance.SetProperty("MainModeCryptoSet", ValueHelper.ResolveMainModeCryptoSetId(rule.MainModeCryptoSet));
        instance.SetProperty("Phase1AuthSet", AuthManager.ResolvePhase1AuthSetId(session, rule, ruleIdentity));
        instance.SetProperty("Phase2AuthSet", AuthManager.ResolvePhase2AuthSetId(session, rule, ruleIdentity));
        instance.SetProperty("QuickModeCryptoSet", ValueHelper.ResolveQuickModeCryptoSetId(rule.QuickModeCryptoSet));
        instance.SetProperty("KeyModule", ValueHelper.ResolveKeyModuleValue(rule.KeyModule));
        instance.SetProperty("AllowSetKey", rule.AllowSetKey);
        instance.SetProperty("AllowWatchKey", rule.AllowWatchKey);
        instance.SetProperty("BypassTunnelIfEncrypted", rule.BypassTunnelIfEncrypted);
        instance.SetProperty("RequireAuthorization", rule.RequireAuthorization);
        instance.SetProperty("Machines", ValueHelper.NormalizeOptionalString(rule.Machines));
        instance.SetProperty("Users", ValueHelper.NormalizeOptionalString(rule.Users));

        if (rule.Mode == ConnectionSecurityMode.Tunnel)
        {
            instance.SetProperty("LocalTunnelEndpoint", ValueHelper.BuildAddressArray(rule.LocalTunnelEndpoint));
            instance.SetProperty("RemoteTunnelEndpoint", ValueHelper.BuildAddressArray(rule.RemoteTunnelEndpoint));
            instance.SetProperty("RemoteTunnelEndpointDNSName", ValueHelper.NormalizeOptionalString(rule.RemoteTunnelEndpointDnsName));
            ValueHelper.SetPropertyValueIfPresent(instance, "TunnelType", ValueHelper.ResolveTunnelTypeValue(rule.TunnelType, rule.KeyModule));
        }
        else
        {
            instance.SetProperty("LocalTunnelEndpoint", null);
            instance.SetProperty("RemoteTunnelEndpoint", null);
            instance.SetProperty("RemoteTunnelEndpointDNSName", null);
            ValueHelper.SetPropertyValueIfPresent(instance, "TunnelType", null);
        }

        session.ModifyInstance(instance);
    }

    internal static void ApplyAddressFilter(WbemServices session, WbemObject ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using WbemObject filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByAddress",
            "MSFT_NetAddressFilter",
            "The connection security rule address filter could not be found.");

        filter.SetProperty("LocalAddress", ValueHelper.BuildAddressArray(rule.Endpoint1Expression));
        filter.SetProperty("RemoteAddress", ValueHelper.BuildAddressArray(rule.Endpoint2Expression));
        session.ModifyInstance(filter);
    }

    internal static void ApplyProtocolFilter(WbemServices session, WbemObject ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using WbemObject filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByProtocolPort",
            "MSFT_NetProtocolPortFilter",
            "The connection security rule protocol/port filter could not be found.");

        filter.SetProperty("Protocol", ValueHelper.ResolveProtocolValue(rule.Protocol));
        filter.SetProperty("LocalPort", ValueHelper.SupportsPorts(rule.Protocol)
            ? ValueHelper.BuildPortArray(rule.LocalPort)
            : null);
        filter.SetProperty("RemotePort", ValueHelper.SupportsPorts(rule.Protocol)
            ? ValueHelper.BuildPortArray(rule.RemotePort)
            : null);
        filter.SetProperty("IcmpType", null);
        session.ModifyInstance(filter);
    }

    internal static void ApplyInterfaceTypeFilter(WbemServices session, WbemObject ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using WbemObject filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByInterfaceType",
            "MSFT_NetInterfaceTypeFilter",
            "The connection security rule interface type filter could not be found.");

        filter.SetProperty("InterfaceType", ValueHelper.ResolveInterfaceTypeValue(rule.InterfaceTypes));
        session.ModifyInstance(filter);
    }

    private static WbemObject GetRequiredAssociatedInstance(
        WbemServices session,
        WbemObject ruleInstance,
        string associationClassName,
        string resultClassName,
        string errorMessage)
    {
        foreach (WbemObject filter in session.EnumerateAssociatedInstances(
                     ruleInstance,
                     associationClassName,
                     resultClassName))
        {
            return filter;
        }

        throw new InvalidOperationException(errorMessage);
    }
}
