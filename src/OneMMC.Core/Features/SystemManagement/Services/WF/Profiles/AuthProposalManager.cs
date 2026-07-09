using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;

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

    internal static IEnumerable<WbemObject> BuildAuthProposals(WbemServices session, IEnumerable<ushort> methods)
    {
        foreach (ushort method in methods.Distinct())
        {
            yield return CreateBasicAuthProposal(session, method);
        }
    }

    internal static IEnumerable<WbemObject> BuildAuthProposals(WbemServices session, IEnumerable<AuthMethodDialogResult>? methods)
    {
        if (methods is null)
        {
            yield break;
        }

        foreach (AuthMethodDialogResult method in methods)
        {
            if (TryBuildAuthProposal(session, method, out WbemObject? proposal) && proposal is not null)
            {
                yield return proposal;
            }
        }
    }

    private static bool TryBuildAuthProposal(WbemServices session, AuthMethodDialogResult method, out WbemObject? proposal)
    {
        proposal = null;
        string kind = method.Kind ?? string.Empty;

        switch (kind)
        {
            case "ComputerKerberos":
                proposal = CreateKerberosAuthProposal(session, AuthMethodMachineKerberos);
                return true;

            case "ComputerNtlm":
                proposal = CreateBasicAuthProposal(session, AuthMethodMachineNtlm);
                return true;

            case "UserKerberos":
                proposal = CreateKerberosAuthProposal(session, AuthMethodUserKerberos);
                return true;

            case "UserNtlm":
                proposal = CreateBasicAuthProposal(session, AuthMethodUserNtlm);
                return true;

            case "Anonymous":
                proposal = CreateBasicAuthProposal(session, AuthMethodAnonymous);
                return true;

            case "PresharedKey":
                proposal = CreatePresharedKeyAuthProposal(session, method.PresharedKey);
                return true;

            case "ComputerCertificate":
                proposal = CreateCertificateAuthProposal(
                    session,
                    method,
                    method.HealthCertificateOnly ? AuthMethodMachineHealthCertificate : AuthMethodMachineCertificate);
                return true;

            case "UserCertificate":
                proposal = CreateCertificateAuthProposal(session, method, AuthMethodUserCertificate);
                return true;

            case "ComputerHealthCertificate":
                proposal = CreateCertificateAuthProposal(session, method, AuthMethodMachineHealthCertificate);
                return true;

            case "Phase1AuthSet":
            case "Phase2AuthSet":
                return false;

            default:
                if (string.Equals(method.Method, "Anonymous", StringComparison.OrdinalIgnoreCase))
                {
                    proposal = CreateBasicAuthProposal(session, AuthMethodAnonymous);
                    return true;
                }

                return false;
        }
    }

    private static WbemObject CreateBasicAuthProposal(WbemServices session, ushort authenticationMethod)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKEBasicAuthProposal");
        instance.SetProperty("AuthenticationMethod", authenticationMethod, WbemType.UInt16);
        return instance;
    }

    private static WbemObject CreateKerberosAuthProposal(WbemServices session, ushort authenticationMethod)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKEKerbAuthProposal");
        instance.SetProperty("AuthenticationMethod", authenticationMethod, WbemType.UInt16);
        return instance;
    }

    private static WbemObject CreatePresharedKeyAuthProposal(WbemServices session, string? key)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKEPSKAuthProposal");
        instance.SetProperty("AuthenticationMethod", AuthMethodPreSharedKey, WbemType.UInt16);
        instance.SetProperty("PreSharedKey", key ?? string.Empty, WbemType.String);
        return instance;
    }

    private static WbemObject CreateCertificateAuthProposal(WbemServices session, AuthMethodDialogResult method, ushort authenticationMethod)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKECertAuthProposal");
        instance.SetProperty("AuthenticationMethod", authenticationMethod, WbemType.UInt16);
        instance.SetProperty("TrustedCA", method.CaDistinguishedName ?? string.Empty, WbemType.String);
        instance.SetProperty("TrustedCAType", AuthMethodValueMapper.ResolveTrustedCaTypeCode(method.CertificateStoreType), WbemType.UInt16);
        instance.SetProperty("SigningAlgorithm", AuthMethodValueMapper.ResolveSigningAlgorithmCode(method.SigningAlgorithm), WbemType.UInt16);
        instance.SetProperty("MapToAccount", method.CertificateMappingEnabled, WbemType.Boolean);

        AdvancedCertCriteriaResult? advanced = method.AdvancedCriteria;
        if (advanced is not null)
        {
            if (advanced.RequiredEkus.Count > 0)
            {
                instance.SetProperty("EKUs", advanced.RequiredEkus.ToArray(), WbemType.StringArray);
            }

            if (!string.IsNullOrWhiteSpace(advanced.CertificateName))
            {
                instance.SetProperty("CertName", advanced.CertificateName.Trim(), WbemType.String);
                instance.SetProperty("CertNameType", ResolveCertNameType(advanced.NameTypeTag), WbemType.UInt16);
            }

            if (!string.IsNullOrWhiteSpace(advanced.Thumbprint))
            {
                instance.SetProperty("Thumbprint", advanced.Thumbprint.Trim(), WbemType.String);
            }

            if (advanced.FollowRenewal)
            {
                instance.SetProperty("FollowRenewal", true, WbemType.Boolean);
            }

            if (string.Equals(advanced.RestrictUsage, "Selection only", StringComparison.OrdinalIgnoreCase))
            {
                instance.SetProperty("SelectionCriteria", true, WbemType.Boolean);
            }
            else if (string.Equals(advanced.RestrictUsage, "Validation only", StringComparison.OrdinalIgnoreCase))
            {
                instance.SetProperty("ValidationCriteria", true, WbemType.Boolean);
            }
        }

        return instance;
    }

    internal static IReadOnlyList<AuthMethodDialogResult> ReadAuthMethodResults(WbemObject? set)
    {
        if (set?.GetValue("Proposals") is not WbemObject[] proposals || proposals.Length == 0)
        {
            return [];
        }

        List<AuthMethodDialogResult> methods = [];
        foreach (WbemObject proposal in proposals)
        {
            ushort authMethod = ValueConverter.ConvertToUInt16(proposal.GetValue("AuthenticationMethod"), 0);
            string className = proposal.ClassName ?? string.Empty;

            switch (className)
            {
                case "MSFT_NetIKEKerbAuthProposal":
                    methods.Add(authMethod == AuthMethodUserKerberos
                        ? new AuthMethodDialogResult { Kind = "UserKerberos", Method = GetString("WF_AuthMethod_UserKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") }
                        : new AuthMethodDialogResult { Kind = "ComputerKerberos", Method = GetString("WF_AuthMethod_ComputerKerberos"), Details = GetString("WF_AuthDetails_KerberosAuthentication") });
                    break;

                case "MSFT_NetIKEPSKAuthProposal":
                    string key = proposal.GetValue("PreSharedKey")?.ToString() ?? string.Empty;
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

    private static AuthMethodDialogResult ReadCertificateAuthMethodResult(WbemObject proposal, ushort authMethod)
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

        string signing = AuthMethodValueMapper.GetSigningAlgorithmTag(ValueConverter.ConvertToUInt16(proposal.GetValue("SigningAlgorithm"), 1));
        string storeType = AuthMethodValueMapper.GetCertificateStoreTypeTag(ValueConverter.ConvertToUInt16(proposal.GetValue("TrustedCAType"), 1));
        string ca = proposal.GetValue("TrustedCA")?.ToString() ?? string.Empty;
        bool healthCertificateOnly = authMethod == AuthMethodMachineHealthCertificate;
        bool certificateMappingEnabled = Convert.ToBoolean(proposal.GetValue("MapToAccount") ?? false, CultureInfo.InvariantCulture);
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

    private static AdvancedCertCriteriaResult? ReadAdvancedCertCriteria(WbemObject proposal)
    {
        string certName = proposal.GetValue("CertName")?.ToString() ?? string.Empty;
        string thumbprint = proposal.GetValue("Thumbprint")?.ToString() ?? string.Empty;
        string[] requiredEkus = proposal.GetValue("EKUs") as string[] ?? [];
        bool followRenewal = Convert.ToBoolean(proposal.GetValue("FollowRenewal") ?? false, CultureInfo.InvariantCulture);
        bool selectionCriteria = Convert.ToBoolean(proposal.GetValue("SelectionCriteria") ?? false, CultureInfo.InvariantCulture);
        bool validationCriteria = Convert.ToBoolean(proposal.GetValue("ValidationCriteria") ?? false, CultureInfo.InvariantCulture);

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
            NameTypeTag = ResolveCertNameTypeTag(ValueConverter.ConvertToUInt16(proposal.GetValue("CertNameType"), 0)),
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
