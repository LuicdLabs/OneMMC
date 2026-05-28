using System;
using System.Globalization;
using System.Linq;
using ManagementTools.Core.Localization;
using ManagementTools.Core.Features.SystemManagement.Interop.WF;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

internal static class RuleMapper
{
    private const string DefaultPhase1AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}";
    private const string DefaultPhase2AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}";
    private const ushort AuthMethodAnonymous = 65001;
    private const ushort AuthMethodMachineKerberos = 65002;
    private const ushort AuthMethodMachineNtlm = 65003;
    private const ushort AuthMethodUserKerberos = 65004;
    private const ushort AuthMethodUserNtlm = 65005;
    private const ushort AuthMethodMachineCertificate = 65006;
    private const ushort AuthMethodUserCertificate = 65007;
    private const ushort AuthMethodMachineHealthCertificate = 65008;

    internal static ConnectionSecurityRuleModel MapRule(CimSession session, CimInstance instance)
    {
        string policyRuleName = instance.CimInstanceProperties["PolicyRuleName"]?.Value?.ToString() ?? string.Empty;
        string displayName = instance.CimInstanceProperties["DisplayName"]?.Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = policyRuleName;
        }
        if (string.IsNullOrWhiteSpace(policyRuleName))
        {
            policyRuleName = displayName;
        }
        displayName ??= string.Empty;
        policyRuleName ??= string.Empty;

        var model = new ConnectionSecurityRuleModel
        {
            Name = displayName,
            OriginalName = policyRuleName,
            Description = instance.CimInstanceProperties["Description"]?.Value?.ToString() ?? string.Empty,
            Enabled = ValueHelper.ReadEnabled(instance),
            InboundSecurity = ValueHelper.ReadRequirement(instance, "InboundSecurity"),
            OutboundSecurity = ValueHelper.ReadRequirement(instance, "OutboundSecurity"),
            Mode = ValueHelper.ReadMode(instance),
            MainModeCryptoSet = instance.CimInstanceProperties["MainModeCryptoSet"]?.Value?.ToString() ?? string.Empty,
            QuickModeCryptoSet = instance.CimInstanceProperties["QuickModeCryptoSet"]?.Value?.ToString() ?? string.Empty,
            Phase1AuthSet = instance.CimInstanceProperties["Phase1AuthSet"]?.Value?.ToString() ?? string.Empty,
            Phase2AuthSet = instance.CimInstanceProperties["Phase2AuthSet"]?.Value?.ToString() ?? string.Empty,
            KeyModule = ValueHelper.ResolveKeyModule(instance.CimInstanceProperties["KeyModule"]?.Value),
            AllowSetKey = ValueHelper.ReadBool(instance, "AllowSetKey"),
            AllowWatchKey = ValueHelper.ReadBool(instance, "AllowWatchKey"),
            BypassTunnelIfEncrypted = ValueHelper.ReadBool(instance, "BypassTunnelIfEncrypted"),
            RequireAuthorization = ValueHelper.ReadBool(instance, "RequireAuthorization"),
            RemoteTunnelEndpointDnsName = instance.CimInstanceProperties["RemoteTunnelEndpointDNSName"]?.Value?.ToString() ?? string.Empty,
            Machines = instance.CimInstanceProperties["Machines"]?.Value?.ToString() ?? string.Empty,
            Users = instance.CimInstanceProperties["Users"]?.Value?.ToString() ?? string.Empty,
            RuleGroup = instance.CimInstanceProperties["RuleGroup"]?.Value?.ToString() ?? string.Empty,
            DisplayGroup = instance.CimInstanceProperties["DisplayGroup"]?.Value?.ToString() ?? string.Empty,
            PolicyStoreSource = instance.CimInstanceProperties["PolicyStoreSource"]?.Value?.ToString() ?? string.Empty
        };

        WindowsFirewallSupport.ApplyProfileMask(model, ValueHelper.ReadInt32(instance, "Profiles", WindowsFirewallSupport.NetFwProfile2All));
        model.TunnelType = ValueHelper.ResolveTunnelType(ValueHelper.ReadPropertyValue(instance, "TunnelType"));
        model.LocalTunnelEndpoint = ValueHelper.JoinArray(instance.CimInstanceProperties["LocalTunnelEndpoint"]?.Value);
        model.RemoteTunnelEndpoint = ValueHelper.JoinArray(instance.CimInstanceProperties["RemoteTunnelEndpoint"]?.Value);
        model.Kind = ResolveKind(model);

        ApplyAddressFilters(session, instance, model);
        ApplyProtocolFilters(session, instance, model);
        ApplyInterfaceTypeFilters(session, instance, model);
        PopulateAuthMethods(session, model);
        model.Summary = $"{model.Endpoint1Expression} -> {model.Endpoint2Expression} | {model.InboundSecurity}/{model.OutboundSecurity}";
        return model;
    }

    private static void ApplyAddressFilters(CimSession session, CimInstance instance, ConnectionSecurityRuleModel model)
    {
        foreach (CimInstance filter in session.EnumerateAssociatedInstances(
                     WindowsFirewallSupport.StandardCimNamespace,
                     instance,
                     "MSFT_NetConSecRuleFilterByAddress",
                     "MSFT_NetAddressFilter",
                     null,
                     null))
        {
            using (filter)
            {
                model.Endpoint1Expression = ValueHelper.JoinArray(filter.CimInstanceProperties["LocalAddress"]?.Value, "Any");
                model.Endpoint2Expression = ValueHelper.JoinArray(filter.CimInstanceProperties["RemoteAddress"]?.Value, "Any");
                return;
            }
        }
    }

    private static void ApplyProtocolFilters(CimSession session, CimInstance instance, ConnectionSecurityRuleModel model)
    {
        foreach (CimInstance filter in session.EnumerateAssociatedInstances(
                     WindowsFirewallSupport.StandardCimNamespace,
                     instance,
                     "MSFT_NetConSecRuleFilterByProtocolPort",
                     "MSFT_NetProtocolPortFilter",
                     null,
                     null))
        {
            using (filter)
            {
                model.Protocol = filter.CimInstanceProperties["Protocol"]?.Value?.ToString() ?? "Any";
                model.LocalPort = ValueHelper.JoinArray(filter.CimInstanceProperties["LocalPort"]?.Value, "Any");
                model.RemotePort = ValueHelper.JoinArray(filter.CimInstanceProperties["RemotePort"]?.Value, "Any");
                return;
            }
        }
    }

    private static void ApplyInterfaceTypeFilters(CimSession session, CimInstance instance, ConnectionSecurityRuleModel model)
    {
        foreach (CimInstance filter in session.EnumerateAssociatedInstances(
                     WindowsFirewallSupport.StandardCimNamespace,
                     instance,
                     "MSFT_NetConSecRuleFilterByInterfaceType",
                     "MSFT_NetInterfaceTypeFilter",
                     null,
                     null))
        {
            using (filter)
            {
                model.InterfaceTypes = ValueHelper.ResolveInterfaceType(filter.CimInstanceProperties["InterfaceType"]?.Value);
                return;
            }
        }
    }

    private static void PopulateAuthMethods(CimSession session, ConnectionSecurityRuleModel model)
    {
        PopulateAuthMethodsFromSet(session, model, phase1: true);
        PopulateAuthMethodsFromSet(session, model, phase1: false);
    }

    private static void PopulateAuthMethodsFromSet(CimSession session, ConnectionSecurityRuleModel model, bool phase1)
    {
        string setId = phase1 ? model.Phase1AuthSet : model.Phase2AuthSet;
        if (string.IsNullOrWhiteSpace(setId))
        {
            return;
        }

        using CimInstance? authSet = FindAuthSetById(session, phase1, setId);
        if (authSet?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return;
        }

        foreach (CimInstance proposal in proposals)
        {
            if (!TryReadAuthMethodFromProposal(proposal, out AuthMethodDialogResult? result) || result is null)
            {
                continue;
            }

            AddAuthMethod(model, phase1, result);
        }

    }

    private static void AddAuthMethod(ConnectionSecurityRuleModel model, bool phase1, AuthMethodDialogResult result)
    {
        AuthMethodListItem item = new()
        {
            Method = string.IsNullOrWhiteSpace(result.Method) ? result.Kind : result.Method,
            Details = result.Details,
            Result = result
        };

        if (phase1)
        {
            model.FirstAuthMethods.Add(item);
        }
        else
        {
            model.SecondAuthMethods.Add(item);
        }
    }

    private static CimInstance? FindAuthSetById(CimSession session, bool phase1, string setId)
    {
        string normalizedSetId = setId.Trim();
        if (string.Equals(normalizedSetId, "Default", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSetId = phase1 ? DefaultPhase1AuthSetId : DefaultPhase2AuthSetId;
        }

        string className = phase1 ? "MSFT_NetIKEP1AuthSet" : "MSFT_NetIKEP2AuthSet";
        foreach (CimInstance set in session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, className))
        {
            string creationClassName = set.CimInstanceProperties["CreationClassName"]?.Value?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(creationClassName))
            {
                continue;
            }

            string extractedId = ExtractAuthSetId(creationClassName);
            if (string.Equals(creationClassName, normalizedSetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extractedId, normalizedSetId, StringComparison.OrdinalIgnoreCase))
            {
                return set;
            }

            set.Dispose();
        }

        return null;
    }

    private static string ExtractAuthSetId(string creationClassName)
    {
        int braceIndex = creationClassName.IndexOf('{', StringComparison.Ordinal);
        return braceIndex >= 0 ? creationClassName[braceIndex..] : creationClassName;
    }

    private static bool TryReadAuthMethodFromProposal(CimInstance proposal, out AuthMethodDialogResult? result)
    {
        result = null;

        string className = proposal.CimSystemProperties.ClassName ?? string.Empty;
        ushort authMethod = ReadUInt16(proposal.CimInstanceProperties["AuthenticationMethod"]?.Value, 0);

        switch (className)
        {
            case "MSFT_NetIKEKerbAuthProposal":
                result = authMethod == AuthMethodUserKerberos
                    ? new AuthMethodDialogResult { Kind = "UserKerberos", Method = GetString("WF_AuthMethod_UserKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") }
                    : new AuthMethodDialogResult { Kind = "ComputerKerberos", Method = GetString("WF_AuthMethod_ComputerKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") };
                return true;

            case "MSFT_NetIKEPSKAuthProposal":
                string preSharedKey = proposal.CimInstanceProperties["PreSharedKey"]?.Value?.ToString() ?? string.Empty;
                result = new AuthMethodDialogResult
                {
                    Kind = "PresharedKey",
                    Method = GetString("WF_AuthMethod_PresharedKey"),
                    Details = string.IsNullOrWhiteSpace(preSharedKey) ? GetString("WF_AuthDetails_KeyNotSet") : GetString("WF_AuthDetails_KeyConfigured"),
                    PresharedKey = preSharedKey
                };
                return true;

            case "MSFT_NetIKECertAuthProposal":
                result = ReadCertificateAuthMethodResult(proposal, authMethod);
                return true;

            case "MSFT_NetIKEBasicAuthProposal":
                result = authMethod switch
                {
                    AuthMethodMachineNtlm => new AuthMethodDialogResult { Kind = "ComputerNtlm", Method = GetString("WF_AuthMethod_ComputerNtlm"), Details = GetString("WF_AuthDetails_NtlmAuthentication") },
                    AuthMethodUserNtlm => new AuthMethodDialogResult { Kind = "UserNtlm", Method = GetString("WF_AuthMethod_UserNtlm"), Details = GetString("WF_AuthDetails_NtlmAuthentication") },
                    AuthMethodAnonymous => new AuthMethodDialogResult { Kind = "Anonymous", Method = GetString("WF_AuthMethod_Anonymous"), Details = GetString("WF_AuthDetails_AnonymousAuthentication") },
                    _ => null
                };
                return result is not null;

            default:
                return false;
        }
    }

    private static AuthMethodDialogResult ReadCertificateAuthMethodResult(CimInstance proposal, ushort authMethod)
    {
        string kind = authMethod switch
        {
            AuthMethodUserCertificate => "UserCertificate",
            AuthMethodMachineHealthCertificate => "ComputerHealthCertificate",
            _ => "ComputerCertificate"
        };

        string methodName = kind switch
        {
            "UserCertificate" => GetString("WF_AuthMethod_UserCertificateFromCA"),
            "ComputerHealthCertificate" => GetString("WF_AuthMethod_ComputerHealthCertificateFromCA"),
            _ => GetString("WF_AuthMethod_ComputerCertificateFromCA")
        };

        string signingAlgorithm = AuthMethodValueMapper.GetSigningAlgorithmTag(ReadUInt16(proposal.CimInstanceProperties["SigningAlgorithm"]?.Value, 1));
        string storeType = AuthMethodValueMapper.GetCertificateStoreTypeTag(ReadUInt16(proposal.CimInstanceProperties["TrustedCAType"]?.Value, 1));
        string caName = proposal.CimInstanceProperties["TrustedCA"]?.Value?.ToString() ?? string.Empty;
        bool healthCertificateOnly = authMethod == AuthMethodMachineHealthCertificate;
        bool certificateMappingEnabled = ReadBoolValue(proposal.CimInstanceProperties["MapToAccount"]?.Value);
        AdvancedCertCriteriaResult? advancedCriteria = ReadAdvancedCertCriteria(proposal);

        return new AuthMethodDialogResult
        {
            Kind = kind,
            Method = methodName,
            Details = AuthMethodValueMapper.BuildCertificateDetails(
                caName,
                signingAlgorithm,
                storeType,
                healthCertificateOnly && kind == "ComputerCertificate",
                certificateMappingEnabled,
                advancedCriteria),
            SigningAlgorithm = signingAlgorithm,
            CertificateStoreType = storeType,
            CaDistinguishedName = caName,
            HealthCertificateOnly = healthCertificateOnly,
            CertificateMappingEnabled = certificateMappingEnabled,
            AdvancedCriteria = advancedCriteria
        };
    }

    private static AdvancedCertCriteriaResult? ReadAdvancedCertCriteria(CimInstance proposal)
    {
        string certName = proposal.CimInstanceProperties["CertName"]?.Value?.ToString() ?? string.Empty;
        string thumbprint = proposal.CimInstanceProperties["Thumbprint"]?.Value?.ToString() ?? string.Empty;
        string[] requiredEkus = proposal.CimInstanceProperties["EKUs"]?.Value as string[] ?? [];
        bool followRenewal = ReadBoolValue(proposal.CimInstanceProperties["FollowRenewal"]?.Value);
        bool selectionCriteria = ReadBoolValue(proposal.CimInstanceProperties["SelectionCriteria"]?.Value);
        bool validationCriteria = ReadBoolValue(proposal.CimInstanceProperties["ValidationCriteria"]?.Value);

        if (string.IsNullOrWhiteSpace(certName) &&
            string.IsNullOrWhiteSpace(thumbprint) &&
            requiredEkus.Length == 0 &&
            !followRenewal &&
            !selectionCriteria &&
            !validationCriteria)
        {
            return null;
        }

        string restrictUsage = selectionCriteria
            ? "Selection only"
            : validationCriteria
                ? "Validation only"
                : "No restriction";

        return new AdvancedCertCriteriaResult
        {
            RestrictUsage = restrictUsage,
            RequiredEkus = requiredEkus,
            NameTypeTag = ResolveCertNameTypeTag(ReadUInt16(proposal.CimInstanceProperties["CertNameType"]?.Value, 0)),
            CertificateName = certName,
            Thumbprint = thumbprint,
            FollowRenewal = followRenewal
        };
    }

    private static ushort ReadUInt16(object? value, ushort defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static bool ReadBoolValue(object? value)
    {
        if (value is null)
        {
            return false;
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }
    }

    private static string GetString(string key)
        => LocalizationProvider.Current.GetString(ResourceFileNames.WF, key);

    private static string FormatString(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, GetString(key), args);

    private static string ResolveCertNameTypeTag(ushort value)
    {
        return value switch
        {
            1 => "SANDNS",
            2 => "SANPrincipal",
            3 => "SANEmail",
            4 => "SubjectCN",
            5 => "SubjectOU",
            6 => "SubjectO",
            7 => "SubjectDC",
            _ => "None"
        };
    }

    private static ConnectionSecurityRuleKind ResolveKind(ConnectionSecurityRuleModel model)
    {
        if (model.Mode == ConnectionSecurityMode.Tunnel)
        {
            return ConnectionSecurityRuleKind.Tunnel;
        }

        if (model.InboundSecurity == ConnectionSecurityRequirement.None &&
            model.OutboundSecurity == ConnectionSecurityRequirement.None)
        {
            return ConnectionSecurityRuleKind.AuthenticationExemption;
        }

        if (!ValueHelper.IsAnyOrEmpty(model.Protocol) ||
            !ValueHelper.IsAnyOrEmpty(model.LocalPort) ||
            !ValueHelper.IsAnyOrEmpty(model.RemotePort) ||
            !ValueHelper.IsAnyOrEmpty(model.InterfaceTypes))
        {
            return ConnectionSecurityRuleKind.Custom;
        }

        if (!string.Equals(model.Endpoint1Expression, "Any", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(model.Endpoint2Expression, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionSecurityRuleKind.ServerToServer;
        }

        return ConnectionSecurityRuleKind.Isolation;
    }
}
