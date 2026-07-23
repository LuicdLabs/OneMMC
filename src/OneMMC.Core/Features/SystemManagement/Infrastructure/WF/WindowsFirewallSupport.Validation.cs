using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF;

public static partial class WindowsFirewallSupport
{
    internal static void ValidateRule(FirewallRuleModel rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("Firewall rule name is required.", nameof(rule));
        }

        if (rule.Direction == FirewallRuleDirection.ConnectionSecurity)
        {
            throw new ArgumentException("Connection security rules must be handled by ConnectionSecurityService.", nameof(rule));
        }

        rule.ProtocolNumber = ResolveProtocolNumber(rule.Protocol, rule.ProtocolNumber);
        rule.EdgeTraversalOptions = rule.EdgeTraversal switch
        {
            FirewallEdgeTraversal.Allow => 1,
            FirewallEdgeTraversal.DeferToUser => 2,
            FirewallEdgeTraversal.DeferToApp => 3,
            _ => 0
        };

        if (rule.ProtocolNumber == NetFwIpProtocolAny)
        {
            rule.LocalPort = string.Empty;
            rule.RemotePort = string.Empty;
            rule.IcmpTypesAndCodes = string.Empty;
        }
        else if (rule.Protocol is not FirewallRuleProtocol.TCP and not FirewallRuleProtocol.UDP)
        {
            rule.LocalPort = string.Empty;
            rule.RemotePort = string.Empty;
        }

        rule.LocalPort = NormalizePortValue(rule.LocalPort);
        rule.RemotePort = NormalizePortValue(rule.RemotePort);
        rule.LocalAddress = NormalizeAddressValue(rule.LocalAddress);
        rule.RemoteAddress = NormalizeAddressValue(rule.RemoteAddress);
        rule.InterfaceTypes = NormalizeInterfaceTypes(rule.InterfaceTypes);
        rule.Interfaces = NormalizeInterfaceAliases(rule.Interfaces);
        rule.IcmpTypesAndCodes = rule.ProtocolNumber is 1 or 58
            ? NormalizeIcmpTypesAndCodes(rule.IcmpTypesAndCodes)
            : string.Empty;
        rule.ProfilesMask = NormalizeProfileMask(rule.ProfilesMask == 0 ? BuildProfileMask(rule) : rule.ProfilesMask);
        ApplyProfileMask(rule, rule.ProfilesMask);
    }

    internal static void ValidateConnectionSecurityRule(ConnectionSecurityRuleModel rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("Connection security rule name is required.", nameof(rule));
        }

        if (rule.OutboundSecurity == ConnectionSecurityRequirement.None &&
            rule.InboundSecurity == ConnectionSecurityRequirement.Request)
        {
            throw new ArgumentException("OutboundSecurity=None with InboundSecurity=Request is not a valid Windows Firewall connection security rule combination.", nameof(rule));
        }

        if (rule.InboundSecurity == ConnectionSecurityRequirement.None &&
            rule.OutboundSecurity != ConnectionSecurityRequirement.None)
        {
            throw new ArgumentException("InboundSecurity=None cannot be combined with outbound authentication requirements.", nameof(rule));
        }

        if (rule.Mode != ConnectionSecurityMode.Tunnel &&
            rule.OutboundSecurity == ConnectionSecurityRequirement.None &&
            rule.InboundSecurity == ConnectionSecurityRequirement.Require)
        {
            throw new ArgumentException("Require inbound and clear outbound authentication mode requires tunnel mode.", nameof(rule));
        }

        int mask = rule.ProfilesMask == 0 ? BuildProfileMask(rule) : rule.ProfilesMask;
        ApplyProfileMask(rule, mask);
        rule.Protocol = string.IsNullOrWhiteSpace(rule.Protocol) ? "Any" : rule.Protocol.Trim();
        rule.LocalPort = string.IsNullOrWhiteSpace(rule.LocalPort) ? "Any" : NormalizePortValue(rule.LocalPort);
        rule.RemotePort = string.IsNullOrWhiteSpace(rule.RemotePort) ? "Any" : NormalizePortValue(rule.RemotePort);
        rule.Endpoint1Expression = string.IsNullOrWhiteSpace(rule.Endpoint1Expression) ? "Any" : rule.Endpoint1Expression.Trim();
        rule.Endpoint2Expression = string.IsNullOrWhiteSpace(rule.Endpoint2Expression) ? "Any" : rule.Endpoint2Expression.Trim();
        rule.InterfaceTypes = NormalizeInterfaceTypes(rule.InterfaceTypes, "Any");

        bool hasSpecificInterfaceType = ParseCsv(rule.InterfaceTypes)
            .Any(value => !string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(value, "All", StringComparison.OrdinalIgnoreCase));
        bool hasSpecificEndpoints = !string.Equals(rule.Endpoint1Expression, "Any", StringComparison.OrdinalIgnoreCase) ||
                                    !string.Equals(rule.Endpoint2Expression, "Any", StringComparison.OrdinalIgnoreCase);

        if (hasSpecificInterfaceType && hasSpecificEndpoints)
        {
            throw new ArgumentException(
                "Windows Firewall connection security rules do not allow specific endpoint addresses together with a specific interface type condition.",
                nameof(rule));
        }

        ValidateCertificateAuthMethods(rule);

        if (rule.Mode == ConnectionSecurityMode.Tunnel)
        {
            ValidateTunnelEndpointFamilies(rule);
            ValidateTunnelKeyModule(rule);
        }
    }

    private static void ValidateCertificateAuthMethods(ConnectionSecurityRuleModel rule)
    {
        foreach (var method in rule.FirstAuthMethods.Concat(rule.SecondAuthMethods))
        {
            if (IsCertificateAuthKind(method.Result.Kind ?? string.Empty) &&
                !CertificateAuthorityNameSupport.IsValidTrustedCaName(method.Result.CaDistinguishedName))
            {
                throw new ArgumentException(
                    "Certificate authentication methods require a valid X.500 certification authority name without the '|' character.",
                    nameof(rule));
            }
        }
    }

    private static bool IsCertificateAuthKind(string kind)
        => kind is "ComputerCertificate" or "UserCertificate" or "ComputerHealthCertificate";

    private static void ValidateTunnelEndpointFamilies(ConnectionSecurityRuleModel rule)
    {
        bool localHasIpv4 = HasTunnelEndpointFamily(rule.LocalTunnelEndpoint, AddressFamily.InterNetwork);
        bool localHasIpv6 = HasTunnelEndpointFamily(rule.LocalTunnelEndpoint, AddressFamily.InterNetworkV6);
        bool remoteHasIpv4 = HasTunnelEndpointFamily(rule.RemoteTunnelEndpoint, AddressFamily.InterNetwork);
        bool remoteHasIpv6 = HasTunnelEndpointFamily(rule.RemoteTunnelEndpoint, AddressFamily.InterNetworkV6);
        bool localDynamic = !localHasIpv4 && !localHasIpv6;
        bool remoteDynamic = !remoteHasIpv4 && !remoteHasIpv6;

        if (!localDynamic &&
            !remoteDynamic &&
            (localHasIpv4 != remoteHasIpv4 || localHasIpv6 != remoteHasIpv6))
        {
            throw new ArgumentException(
                "Local and remote tunnel endpoint addresses must use matching IP address families.",
                nameof(rule));
        }
    }

    private static bool HasTunnelEndpointFamily(string endpointExpression, AddressFamily addressFamily)
    {
        foreach (string value in ParseCsv(endpointExpression))
        {
            if (string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "*", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string addressPart = value.Split(new[] { '/', '-' }, 2, StringSplitOptions.TrimEntries)[0];
            if (IPAddress.TryParse(addressPart, out IPAddress? address))
            {
                if (address.AddressFamily == addressFamily)
                {
                    return true;
                }

                continue;
            }

            if (addressFamily == AddressFamily.InterNetworkV6 && addressPart.Contains(':'))
            {
                return true;
            }

            if (addressFamily == AddressFamily.InterNetwork && addressPart.Contains('.'))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateTunnelKeyModule(ConnectionSecurityRuleModel rule)
    {
        if (!string.Equals(rule.KeyModule, "IKEv2", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(rule.TunnelType, "ClientToGateway", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rule.TunnelType, "PointToSite", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("IKEv2 keying is only valid for client-to-gateway tunnel rules.", nameof(rule));
        }

        if (rule.SecondAuthMethods.Count > 0)
        {
            throw new ArgumentException("IKEv2 tunnel rules support first authentication only.", nameof(rule));
        }

        if (rule.FirstAuthMethods.Any(method => IsIkeV2UnsupportedAuthKind(method.Result.Kind)))
        {
            throw new ArgumentException("IKEv2 tunnel rules do not support Kerberos, NTLM, or preshared key authentication.", nameof(rule));
        }
    }

    private static bool IsIkeV2UnsupportedAuthKind(string kind)
        => kind is "ComputerKerberos" or "ComputerNtlm" or "UserKerberos" or "UserNtlm" or "PresharedKey";
}
