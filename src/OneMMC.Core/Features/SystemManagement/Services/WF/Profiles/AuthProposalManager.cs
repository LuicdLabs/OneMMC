using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using Microsoft.Management.Infrastructure;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

internal static class AuthProposalManager
{
    private const ushort AuthMethodAnonymous = 65001;
    private const ushort AuthMethodMachineKerberos = 65002;
    private const ushort AuthMethodMachineNtlm = 65003;
    private const ushort AuthMethodUserKerberos = 65004;
    private const ushort AuthMethodUserNtlm = 65005;
    private const ushort AuthMethodMachineCertificate = 65006;
    private const ushort AuthMethodUserCertificate = 65007;
    private const ushort AuthMethodMachineHealthCertificate = 65008;
    private const ushort AuthMethodPreSharedKey = 2;

    internal static IEnumerable<CimInstance> BuildAuthProposals(IEnumerable<ushort> methods)
    {
        foreach (ushort method in methods.Distinct())
        {
            yield return CreateBasicAuthProposal(method);
        }
    }

    internal static IEnumerable<CimInstance> BuildAuthProposals(IEnumerable<AuthMethodDialogResult>? methods)
    {
        if (methods is null)
        {
            yield break;
        }

        foreach (AuthMethodDialogResult method in methods)
        {
            if (TryBuildAuthProposal(method, out CimInstance? proposal) && proposal is not null)
            {
                yield return proposal;
            }
        }
    }

    private static bool TryBuildAuthProposal(AuthMethodDialogResult method, out CimInstance? proposal)
    {
        proposal = null;
        string kind = method.Kind ?? string.Empty;

        switch (kind)
        {
            case "ComputerKerberos":
                proposal = CreateKerberosAuthProposal(AuthMethodMachineKerberos);
                return true;

            case "ComputerNtlm":
                proposal = CreateBasicAuthProposal(AuthMethodMachineNtlm);
                return true;

            case "UserKerberos":
                proposal = CreateKerberosAuthProposal(AuthMethodUserKerberos);
                return true;

            case "UserNtlm":
                proposal = CreateBasicAuthProposal(AuthMethodUserNtlm);
                return true;

            case "Anonymous":
                proposal = CreateBasicAuthProposal(AuthMethodAnonymous);
                return true;

            case "PresharedKey":
                proposal = CreatePresharedKeyAuthProposal(method.PresharedKey);
                return true;

            case "ComputerCertificate":
                proposal = CreateCertificateAuthProposal(
                    method,
                    method.HealthCertificateOnly ? AuthMethodMachineHealthCertificate : AuthMethodMachineCertificate);
                return true;

            case "UserCertificate":
                proposal = CreateCertificateAuthProposal(method, AuthMethodUserCertificate);
                return true;

            case "ComputerHealthCertificate":
                proposal = CreateCertificateAuthProposal(method, AuthMethodMachineHealthCertificate);
                return true;

            case "Phase1AuthSet":
            case "Phase2AuthSet":
                return false;

            default:
                if (string.Equals(method.Method, "Anonymous", StringComparison.OrdinalIgnoreCase))
                {
                    proposal = CreateBasicAuthProposal(AuthMethodAnonymous);
                    return true;
                }

                return false;
        }
    }

    private static CimInstance CreateBasicAuthProposal(ushort authenticationMethod)
    {
        var instance = new CimInstance("MSFT_NetIKEBasicAuthProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("AuthenticationMethod", authenticationMethod, CimType.UInt16, CimFlags.None));
        return instance;
    }

    private static CimInstance CreateKerberosAuthProposal(ushort authenticationMethod)
    {
        var instance = new CimInstance("MSFT_NetIKEKerbAuthProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("AuthenticationMethod", authenticationMethod, CimType.UInt16, CimFlags.None));
        return instance;
    }

    private static CimInstance CreatePresharedKeyAuthProposal(string? key)
    {
        var instance = new CimInstance("MSFT_NetIKEPSKAuthProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("AuthenticationMethod", AuthMethodPreSharedKey, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("PreSharedKey", key ?? string.Empty, CimType.String, CimFlags.None));
        return instance;
    }

    private static CimInstance CreateCertificateAuthProposal(AuthMethodDialogResult method, ushort authenticationMethod)
    {
        var instance = new CimInstance("MSFT_NetIKECertAuthProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("AuthenticationMethod", authenticationMethod, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("TrustedCA", method.CaDistinguishedName ?? string.Empty, CimType.String, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("TrustedCAType", AuthMethodValueMapper.ResolveTrustedCaTypeCode(method.CertificateStoreType), CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("SigningAlgorithm", AuthMethodValueMapper.ResolveSigningAlgorithmCode(method.SigningAlgorithm), CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("MapToAccount", method.CertificateMappingEnabled, CimType.Boolean, CimFlags.None));

        AdvancedCertCriteriaResult? advanced = method.AdvancedCriteria;
        if (advanced is not null)
        {
            if (advanced.RequiredEkus.Count > 0)
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("EKUs", advanced.RequiredEkus.ToArray(), CimType.StringArray, CimFlags.None));
            }

            if (!string.IsNullOrWhiteSpace(advanced.CertificateName))
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("CertName", advanced.CertificateName.Trim(), CimType.String, CimFlags.None));
                instance.CimInstanceProperties.Add(CimProperty.Create("CertNameType", ResolveCertNameType(advanced.NameTypeTag), CimType.UInt16, CimFlags.None));
            }

            if (!string.IsNullOrWhiteSpace(advanced.Thumbprint))
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("Thumbprint", advanced.Thumbprint.Trim(), CimType.String, CimFlags.None));
            }

            if (advanced.FollowRenewal)
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("FollowRenewal", true, CimType.Boolean, CimFlags.None));
            }

            if (string.Equals(advanced.RestrictUsage, "Selection only", StringComparison.OrdinalIgnoreCase))
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("SelectionCriteria", true, CimType.Boolean, CimFlags.None));
            }
            else if (string.Equals(advanced.RestrictUsage, "Validation only", StringComparison.OrdinalIgnoreCase))
            {
                instance.CimInstanceProperties.Add(CimProperty.Create("ValidationCriteria", true, CimType.Boolean, CimFlags.None));
            }
        }

        return instance;
    }

    internal static IReadOnlyList<AuthMethodDialogResult> ReadAuthMethodResults(CimInstance? set)
    {
        if (set?.CimInstanceProperties["Proposals"]?.Value is not CimInstance[] proposals || proposals.Length == 0)
        {
            return [];
        }

        List<AuthMethodDialogResult> methods = [];
        foreach (CimInstance proposal in proposals)
        {
            ushort authMethod = ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["AuthenticationMethod"]?.Value, 0);
            string className = proposal.CimSystemProperties.ClassName ?? string.Empty;

            switch (className)
            {
                case "MSFT_NetIKEKerbAuthProposal":
                    methods.Add(authMethod == AuthMethodUserKerberos
                        ? new AuthMethodDialogResult { Kind = "UserKerberos", Method = GetString("WF_AuthMethod_UserKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") }
                        : new AuthMethodDialogResult { Kind = "ComputerKerberos", Method = GetString("WF_AuthMethod_ComputerKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") });
                    break;

                case "MSFT_NetIKEPSKAuthProposal":
                    string key = proposal.CimInstanceProperties["PreSharedKey"]?.Value?.ToString() ?? string.Empty;
                    methods.Add(new AuthMethodDialogResult
                    {
                        Kind = "PresharedKey",
                        Method = GetString("WF_AuthMethod_PresharedKey"),
                        Details = string.IsNullOrWhiteSpace(key) ? GetString("WF_AuthDetails_KeyNotSet") : GetString("WF_AuthDetails_KeyConfigured"),
                        PresharedKey = key
                    });
                    break;

                case "MSFT_NetIKECertAuthProposal":
                    methods.Add(ReadCertificateAuthMethodResult(proposal, authMethod));
                    break;

                default:
                    methods.Add(authMethod switch
                    {
                        AuthMethodMachineNtlm => new AuthMethodDialogResult { Kind = "ComputerNtlm", Method = GetString("WF_AuthMethod_ComputerNtlm"), Details = GetString("WF_AuthDetails_NtlmAuthentication") },
                        AuthMethodUserNtlm => new AuthMethodDialogResult { Kind = "UserNtlm", Method = GetString("WF_AuthMethod_UserNtlm"), Details = GetString("WF_AuthDetails_NtlmAuthentication") },
                        AuthMethodAnonymous => new AuthMethodDialogResult { Kind = "Anonymous", Method = GetString("WF_AuthMethod_Anonymous"), Details = GetString("WF_AuthDetails_AnonymousAuthentication") },
                        _ => new AuthMethodDialogResult
                        {
                            Kind = "Unsupported",
                            Method = GetString("WF_AuthMethod_UnsupportedProposal"),
                            Details = FormatString("WF_AuthDetails_UnsupportedProposalFormat", className, authMethod)
                        }
                    });
                    break;
            }
        }

        return methods;
    }

    private static AuthMethodDialogResult ReadCertificateAuthMethodResult(CimInstance proposal, ushort authMethod)
    {
        string kind = authMethod switch
        {
            AuthMethodUserCertificate => "UserCertificate",
            AuthMethodMachineHealthCertificate => "ComputerHealthCertificate",
            _ => "ComputerCertificate"
        };

        string method = kind switch
        {
            "UserCertificate" => GetString("WF_AuthMethod_UserCertificateFromCA"),
            "ComputerHealthCertificate" => GetString("WF_AuthMethod_ComputerHealthCertificateFromCA"),
            _ => GetString("WF_AuthMethod_ComputerCertificateFromCA")
        };

        string signing = AuthMethodValueMapper.GetSigningAlgorithmTag(ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["SigningAlgorithm"]?.Value, 1));
        string storeType = AuthMethodValueMapper.GetCertificateStoreTypeTag(ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["TrustedCAType"]?.Value, 1));
        string ca = proposal.CimInstanceProperties["TrustedCA"]?.Value?.ToString() ?? string.Empty;
        bool healthCertificateOnly = authMethod == AuthMethodMachineHealthCertificate;
        bool certificateMappingEnabled = Convert.ToBoolean(proposal.CimInstanceProperties["MapToAccount"]?.Value ?? false, CultureInfo.InvariantCulture);
        AdvancedCertCriteriaResult? advancedCriteria = ReadAdvancedCertCriteria(proposal);

        return new AuthMethodDialogResult
        {
            Kind = kind,
            Method = method,
            Details = AuthMethodValueMapper.BuildCertificateDetails(
                ca,
                signing,
                storeType,
                healthCertificateOnly && kind == "ComputerCertificate",
                certificateMappingEnabled,
                advancedCriteria),
            SigningAlgorithm = signing,
            CertificateStoreType = storeType,
            CaDistinguishedName = ca,
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
        bool followRenewal = Convert.ToBoolean(proposal.CimInstanceProperties["FollowRenewal"]?.Value ?? false, CultureInfo.InvariantCulture);
        bool selectionCriteria = Convert.ToBoolean(proposal.CimInstanceProperties["SelectionCriteria"]?.Value ?? false, CultureInfo.InvariantCulture);
        bool validationCriteria = Convert.ToBoolean(proposal.CimInstanceProperties["ValidationCriteria"]?.Value ?? false, CultureInfo.InvariantCulture);

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
            NameTypeTag = ResolveCertNameTypeTag(ValueConverter.ConvertToUInt16(proposal.CimInstanceProperties["CertNameType"]?.Value, 0)),
            CertificateName = certName,
            Thumbprint = thumbprint,
            FollowRenewal = followRenewal
        };
    }

    private static ushort ResolveCertNameType(string? nameTypeTag)
    {
        return nameTypeTag switch
        {
            "SANDNS" => 1,
            "SANPrincipal" => 2,
            "SANEmail" => 3,
            "SubjectCN" => 4,
            "SubjectOU" => 5,
            "SubjectO" => 6,
            "SubjectDC" => 7,
            _ => 0
        };
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
}
