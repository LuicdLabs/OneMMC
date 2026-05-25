using System;
using System.Runtime.InteropServices;
using ManagementTools.Core.Features.SystemManagement.Interop.WF;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;

internal static class ComApplier
{
    internal static void ApplyRuleToComObject(FirewallRuleModel rule, INetFwRule3 target)
    {
        SetComProperty("Name", rule.Name, () => target.Name = rule.Name);

        string description = WindowsFirewallSupport.NormalizeComStringValue(rule.Description);
        SetComProperty("Description", description, () => target.Description = description);

        SetComProperty("Enabled", rule.Enabled, () => target.Enabled = rule.Enabled);

        int direction = rule.Direction == FirewallRuleDirection.Inbound
            ? WindowsFirewallSupport.NetFwRuleDirIn
            : WindowsFirewallSupport.NetFwRuleDirOut;
        SetComProperty("Direction", direction, () => target.Direction = direction);

        int currentProtocol = 256;
        try
        {
            currentProtocol = target.Protocol;
        }
        catch
        {
        }

        if (currentProtocol != rule.ProtocolNumber)
        {
            if (currentProtocol is 6 or 17)
            {
                SetComProperty("LocalPorts", null, () => target.LocalPorts = null!);
                SetComProperty("RemotePorts", null, () => target.RemotePorts = null!);
            }
            else if (currentProtocol is 1 or 58)
            {
                SetComProperty("IcmpTypesAndCodes", "*", () => target.IcmpTypesAndCodes = "*");
            }
        }

        SetComProperty("Protocol", rule.ProtocolNumber, () => target.Protocol = rule.ProtocolNumber);

        int action = rule.Action == FirewallRuleAction.Block
            ? WindowsFirewallSupport.NetFwActionBlock
            : WindowsFirewallSupport.NetFwActionAllow;
        SetComProperty("Action", action, () => target.Action = action);

        string localAddresses = WindowsFirewallSupport.NormalizeAddressValue(rule.LocalAddress);
        SetComProperty("LocalAddresses", localAddresses, () => target.LocalAddresses = localAddresses);

        string remoteAddresses = WindowsFirewallSupport.NormalizeAddressValue(rule.RemoteAddress);
        SetComProperty("RemoteAddresses", remoteAddresses, () => target.RemoteAddresses = remoteAddresses);

        int profiles = WindowsFirewallSupport.NormalizeProfileMask(rule.ProfilesMask);
        SetComProperty("Profiles", profiles, () => target.Profiles = profiles);

        if (!string.IsNullOrWhiteSpace(rule.Program))
        {
            string program = rule.Program.Trim();
            SetComProperty("ApplicationName", program, () => target.ApplicationName = program);
        }

        if (!string.IsNullOrWhiteSpace(rule.ServiceName))
        {
            string serviceName = rule.ServiceName.Trim();
            SetComProperty("serviceName", serviceName, () => target.serviceName = serviceName);
        }

        if (rule.ProtocolNumber is 6 or 17)
        {
            string localPorts = WindowsFirewallSupport.NormalizePortValue(rule.LocalPort);
            SetComProperty("LocalPorts", localPorts, () => target.LocalPorts = localPorts);

            string remotePorts = WindowsFirewallSupport.NormalizePortValue(rule.RemotePort);
            SetComProperty("RemotePorts", remotePorts, () => target.RemotePorts = remotePorts);
        }

        if (rule.ProtocolNumber is 1 or 58)
        {
            string icmp = string.IsNullOrWhiteSpace(rule.IcmpTypesAndCodes)
                ? "*"
                : WindowsFirewallSupport.NormalizeIcmpTypesAndCodes(rule.IcmpTypesAndCodes);
            SetComProperty("IcmpTypesAndCodes", icmp, () => target.IcmpTypesAndCodes = icmp);
        }

        string interfaceTypes = WindowsFirewallSupport.NormalizeInterfaceTypes(rule.InterfaceTypes);
        SetComProperty("InterfaceTypes", interfaceTypes, () => target.InterfaceTypes = interfaceTypes);

        string normalizedInterfaces = WindowsFirewallSupport.NormalizeInterfaceAliases(rule.Interfaces);
        object? interfacesVariant = WindowsFirewallSupport.BuildInterfaceAliasesVariant(normalizedInterfaces);
        SetComProperty("Interfaces", interfacesVariant, () => target.Interfaces = interfacesVariant!);

        if (!string.IsNullOrWhiteSpace(rule.Grouping))
        {
            string grouping = rule.Grouping.Trim();
            SetComProperty("Grouping", grouping, () => target.Grouping = grouping);
        }

        string normalizedPackageId = WindowsFirewallSupport.NormalizeLocalAppPackageId(rule.LocalAppPackageId);
        if (!string.IsNullOrWhiteSpace(normalizedPackageId))
        {
            SetComProperty("LocalAppPackageId", normalizedPackageId, () => target.LocalAppPackageId = normalizedPackageId);
        }

        int secureFlags = Math.Max(0, rule.SecureFlags);
        SetComProperty("SecureFlags", secureFlags, () => target.SecureFlags = secureFlags);

        ApplySecurityProperties(rule, target);

        int edgeTraversalOptions = Math.Clamp(rule.EdgeTraversalOptions, 0, 3);
        SetComProperty("EdgeTraversalOptions", edgeTraversalOptions, () => target.EdgeTraversalOptions = edgeTraversalOptions);
    }

    internal static void ApplyMutablePredefinedRuleToComObject(FirewallRuleModel rule, INetFwRule3 target)
    {
        SetComProperty("Enabled", rule.Enabled, () => target.Enabled = rule.Enabled);

        int action = rule.Action == FirewallRuleAction.Block
            ? WindowsFirewallSupport.NetFwActionBlock
            : WindowsFirewallSupport.NetFwActionAllow;
        SetComProperty("Action", action, () => target.Action = action);

        string localAddresses = WindowsFirewallSupport.NormalizeAddressValue(rule.LocalAddress);
        SetComProperty("LocalAddresses", localAddresses, () => target.LocalAddresses = localAddresses);

        string remoteAddresses = WindowsFirewallSupport.NormalizeAddressValue(rule.RemoteAddress);
        SetComProperty("RemoteAddresses", remoteAddresses, () => target.RemoteAddresses = remoteAddresses);

        int profiles = WindowsFirewallSupport.NormalizeProfileMask(rule.ProfilesMask);
        SetComProperty("Profiles", profiles, () => target.Profiles = profiles);

        string interfaceTypes = WindowsFirewallSupport.NormalizeInterfaceTypes(rule.InterfaceTypes);
        SetComProperty("InterfaceTypes", interfaceTypes, () => target.InterfaceTypes = interfaceTypes);

        string normalizedInterfaces = WindowsFirewallSupport.NormalizeInterfaceAliases(rule.Interfaces);
        object? interfacesVariant = WindowsFirewallSupport.BuildInterfaceAliasesVariant(normalizedInterfaces);
        SetComProperty("Interfaces", interfacesVariant, () => target.Interfaces = interfacesVariant!);

        int secureFlags = Math.Max(0, rule.SecureFlags);
        SetComProperty("SecureFlags", secureFlags, () => target.SecureFlags = secureFlags);

        ApplySecurityProperties(rule, target);

        int edgeTraversalOptions = Math.Clamp(rule.EdgeTraversalOptions, 0, 3);
        SetComProperty("EdgeTraversalOptions", edgeTraversalOptions, () => target.EdgeTraversalOptions = edgeTraversalOptions);
    }

    private static void ApplySecurityProperties(FirewallRuleModel rule, INetFwRule3 target)
    {
        string localAuth = WindowsFirewallSupport.NormalizeComSddlValue(rule.LocalUserAuthorizedList);
        if (!string.IsNullOrWhiteSpace(localAuth))
        {
            SetComPropertyWithWmiFallback("LocalUserAuthorizedList", localAuth, rule.Name, () => target.LocalUserAuthorizedList = localAuth);
        }
        else if (LookupHelper.IsClearingStringProperty(target, "LocalUserAuthorizedList", localAuth))
        {
            SetComPropertyWithWmiFallback("LocalUserAuthorizedList", null!, rule.Name, () => target.LocalUserAuthorizedList = null!);
        }

        if (!string.IsNullOrWhiteSpace(rule.LocalUserOwner))
        {
            string localOwner = rule.LocalUserOwner.Trim();
            SetComProperty("LocalUserOwner", localOwner, () => target.LocalUserOwner = localOwner);
        }
        else if (LookupHelper.IsClearingStringProperty(target, "LocalUserOwner", rule.LocalUserOwner))
        {
            SetComProperty("LocalUserOwner", null, () => target.LocalUserOwner = null!);
        }

        string remoteMachineAuth = WindowsFirewallSupport.NormalizeComSddlValue(rule.RemoteMachineAuthorizedList);
        if (!string.IsNullOrWhiteSpace(remoteMachineAuth))
        {
            SetComPropertyWithWmiFallback("RemoteMachineAuthorizedList", remoteMachineAuth, rule.Name, () => target.RemoteMachineAuthorizedList = remoteMachineAuth);
        }
        else if (LookupHelper.IsClearingStringProperty(target, "RemoteMachineAuthorizedList", remoteMachineAuth))
        {
            SetComPropertyWithWmiFallback("RemoteMachineAuthorizedList", null!, rule.Name, () => target.RemoteMachineAuthorizedList = null!);
        }

        string remoteUserAuth = WindowsFirewallSupport.NormalizeComSddlValue(rule.RemoteUserAuthorizedList);
        if (!string.IsNullOrWhiteSpace(remoteUserAuth))
        {
            SetComPropertyWithWmiFallback("RemoteUserAuthorizedList", remoteUserAuth, rule.Name, () => target.RemoteUserAuthorizedList = remoteUserAuth);
        }
        else if (LookupHelper.IsClearingStringProperty(target, "RemoteUserAuthorizedList", remoteUserAuth))
        {
            SetComPropertyWithWmiFallback("RemoteUserAuthorizedList", null!, rule.Name, () => target.RemoteUserAuthorizedList = null!);
        }
    }

    /// <summary>
    /// Sets a property on the INetFwRule COM object, capturing exceptions to provide diagnostic context.
    /// </summary>
    private static void SetComProperty(string propertyName, object? value, Action setter)
    {
        try
        {
            setter();
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Failed to set firewall rule property '{propertyName}' to '{value ?? "(null)"}': {ex.Message}",
                ex);
        }
        catch (COMException ex) when ((uint)ex.ErrorCode == 0x80070057)
        {
            throw new ArgumentException(
                $"Failed to set firewall rule property '{propertyName}' to '{value ?? "(null)"}': {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Sets an SDDL property, falling back to WMI if the COM API strictly rejects it (e.g. Capability SIDs).
    /// </summary>
    internal static void SetComPropertyWithWmiFallback(string propertyName, string value, string ruleName, Action setter)
    {
        try
        {
            SetComProperty(propertyName, value, setter);
        }
        catch (ArgumentException)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
            {
                throw;
            }

            try
            {
                using CimSession session = CimSession.Create(null);
                CimInstance? ruleInstance = CimHelper.GetFirewallRuleInstance(session, ruleName);
                if (ruleInstance is not null)
                {
                    CimInstance? filter = CimHelper.GetSecurityFilterInstance(session, ruleInstance);
                    if (filter is not null)
                    {
                        string wmiPropertyName = propertyName switch
                        {
                            "LocalUserAuthorizedList" => "LocalUser",
                            "RemoteUserAuthorizedList" => "RemoteUser",
                            "RemoteMachineAuthorizedList" => "RemoteMachine",
                            _ => propertyName
                        };

                        if (filter.CimInstanceProperties[wmiPropertyName] is CimProperty property)
                        {
                            property.Value = value;
                            session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, filter);
                            return;
                        }
                    }
                }
            }
            catch
            {
            }

            throw;
        }
    }
}
