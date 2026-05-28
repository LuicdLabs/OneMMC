using System;
using ManagementTools.Core.Features.SystemManagement.Interop.WF;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

internal static class RuleApplier
{
    internal static void ApplyRuleState(CimSession session, CimInstance instance, ConnectionSecurityRuleModel rule)
    {
        string ruleIdentity = AuthManager.ResolveRuleIdentity(instance, rule);

        instance.CimInstanceProperties["Description"].Value = string.IsNullOrWhiteSpace(rule.Description)
            ? null
            : rule.Description.Trim();
        instance.CimInstanceProperties["Profiles"].Value = ValueHelper.ResolveProfilesMask(rule);
        instance.CimInstanceProperties["InboundSecurity"].Value = (ushort)rule.InboundSecurity;
        instance.CimInstanceProperties["OutboundSecurity"].Value = (ushort)rule.OutboundSecurity;
        instance.CimInstanceProperties["Mode"].Value = ValueHelper.ResolveModeValue(rule.Mode);
        instance.CimInstanceProperties["MainModeCryptoSet"].Value = ValueHelper.ResolveMainModeCryptoSetId(rule.MainModeCryptoSet);
        instance.CimInstanceProperties["Phase1AuthSet"].Value = AuthManager.ResolvePhase1AuthSetId(session, rule, ruleIdentity);
        instance.CimInstanceProperties["Phase2AuthSet"].Value = AuthManager.ResolvePhase2AuthSetId(session, rule, ruleIdentity);
        instance.CimInstanceProperties["QuickModeCryptoSet"].Value = ValueHelper.ResolveQuickModeCryptoSetId(rule.QuickModeCryptoSet);
        instance.CimInstanceProperties["KeyModule"].Value = ValueHelper.ResolveKeyModuleValue(rule.KeyModule);
        instance.CimInstanceProperties["AllowSetKey"].Value = rule.AllowSetKey;
        instance.CimInstanceProperties["AllowWatchKey"].Value = rule.AllowWatchKey;
        instance.CimInstanceProperties["BypassTunnelIfEncrypted"].Value = rule.BypassTunnelIfEncrypted;
        instance.CimInstanceProperties["RequireAuthorization"].Value = rule.RequireAuthorization;
        instance.CimInstanceProperties["Machines"].Value = ValueHelper.NormalizeOptionalString(rule.Machines);
        instance.CimInstanceProperties["Users"].Value = ValueHelper.NormalizeOptionalString(rule.Users);

        if (rule.Mode == ConnectionSecurityMode.Tunnel)
        {
            instance.CimInstanceProperties["LocalTunnelEndpoint"].Value = ValueHelper.BuildAddressArray(rule.LocalTunnelEndpoint);
            instance.CimInstanceProperties["RemoteTunnelEndpoint"].Value = ValueHelper.BuildAddressArray(rule.RemoteTunnelEndpoint);
            instance.CimInstanceProperties["RemoteTunnelEndpointDNSName"].Value = ValueHelper.NormalizeOptionalString(rule.RemoteTunnelEndpointDnsName);
            ValueHelper.SetPropertyValueIfPresent(instance, "TunnelType", ValueHelper.ResolveTunnelTypeValue(rule.TunnelType, rule.KeyModule));
        }
        else
        {
            instance.CimInstanceProperties["LocalTunnelEndpoint"].Value = null;
            instance.CimInstanceProperties["RemoteTunnelEndpoint"].Value = null;
            instance.CimInstanceProperties["RemoteTunnelEndpointDNSName"].Value = null;
            ValueHelper.SetPropertyValueIfPresent(instance, "TunnelType", null);
        }

        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, instance);
    }

    internal static void ApplyAddressFilter(CimSession session, CimInstance ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using CimInstance filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByAddress",
            "MSFT_NetAddressFilter",
            "The connection security rule address filter could not be found.");

        filter.CimInstanceProperties["LocalAddress"].Value = ValueHelper.BuildAddressArray(rule.Endpoint1Expression);
        filter.CimInstanceProperties["RemoteAddress"].Value = ValueHelper.BuildAddressArray(rule.Endpoint2Expression);
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, filter);
    }

    internal static void ApplyProtocolFilter(CimSession session, CimInstance ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using CimInstance filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByProtocolPort",
            "MSFT_NetProtocolPortFilter",
            "The connection security rule protocol/port filter could not be found.");

        filter.CimInstanceProperties["Protocol"].Value = ValueHelper.ResolveProtocolValue(rule.Protocol);
        filter.CimInstanceProperties["LocalPort"].Value = ValueHelper.SupportsPorts(rule.Protocol)
            ? ValueHelper.BuildPortArray(rule.LocalPort)
            : null;
        filter.CimInstanceProperties["RemotePort"].Value = ValueHelper.SupportsPorts(rule.Protocol)
            ? ValueHelper.BuildPortArray(rule.RemotePort)
            : null;
        filter.CimInstanceProperties["IcmpType"].Value = null;
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, filter);
    }

    internal static void ApplyInterfaceTypeFilter(CimSession session, CimInstance ruleInstance, ConnectionSecurityRuleModel rule)
    {
        using CimInstance filter = GetRequiredAssociatedInstance(
            session,
            ruleInstance,
            "MSFT_NetConSecRuleFilterByInterfaceType",
            "MSFT_NetInterfaceTypeFilter",
            "The connection security rule interface type filter could not be found.");

        filter.CimInstanceProperties["InterfaceType"].Value = ValueHelper.ResolveInterfaceTypeValue(rule.InterfaceTypes);
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, filter);
    }

    private static CimInstance GetRequiredAssociatedInstance(
        CimSession session,
        CimInstance ruleInstance,
        string associationClassName,
        string resultClassName,
        string errorMessage)
    {
        foreach (CimInstance filter in session.EnumerateAssociatedInstances(
                     WindowsFirewallSupport.StandardCimNamespace,
                     ruleInstance,
                     associationClassName,
                     resultClassName,
                     null,
                     null))
        {
            return filter;
        }

        throw new InvalidOperationException(errorMessage);
    }
}
