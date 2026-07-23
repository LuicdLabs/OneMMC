using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

internal static class AuthManager
{
    private const string DefaultMainModeCryptoSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}";
    private const string DefaultQuickModeCryptoSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}";
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
    private const ushort AuthMethodPreSharedKey = 2;
    private const string ManagedAuthSetDisplayPrefix = "OneMMC Connection Security";

    internal static string ResolvePhase1AuthSetId(WbemServices session, ConnectionSecurityRuleModel rule, string ruleIdentity)
        => ResolvePhaseAuthSetId(session, rule, ruleIdentity, phase1: true, rule.Phase1AuthSet, rule.FirstAuthMethods, DefaultPhase1AuthSetId);

    internal static string ResolvePhase2AuthSetId(WbemServices session, ConnectionSecurityRuleModel rule, string ruleIdentity)
        => ResolvePhaseAuthSetId(session, rule, ruleIdentity, phase1: false, rule.Phase2AuthSet, rule.SecondAuthMethods, DefaultPhase2AuthSetId);

    internal static string ResolveRuleIdentity(WbemObject instance, ConnectionSecurityRuleModel rule)
    {
        string policyRuleName = instance.GetValue("PolicyRuleName")?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(policyRuleName))
        {
            return policyRuleName;
        }

        if (!string.IsNullOrWhiteSpace(rule.OriginalName))
        {
            return rule.OriginalName;
        }

        if (!string.IsNullOrWhiteSpace(rule.Name))
        {
            return rule.Name;
        }

        return instance.GetValue("ElementName")?.ToString() ?? Guid.NewGuid().ToString("N");
    }

    internal static void DeleteManagedAuthSets(WbemServices session, string ruleIdentity)
    {
        if (string.IsNullOrWhiteSpace(ruleIdentity))
        {
            return;
        }

        DeleteManagedAuthSetIfExists(session, phase1: true, ruleIdentity);
        DeleteManagedAuthSetIfExists(session, phase1: false, ruleIdentity);
    }

    internal static WbemObject? FindSetByCreationClass(WbemServices session, string className, string creationClassName)
    {
        foreach (WbemObject instance in session.EnumerateInstances(className))
        {
            if (string.Equals(
                    instance.GetValue("CreationClassName")?.ToString(),
                    creationClassName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return instance;
            }

            instance.Dispose();
        }

        return null;
    }

    private static string ResolvePhaseAuthSetId(
        WbemServices session,
        ConnectionSecurityRuleModel rule,
        string ruleIdentity,
        bool phase1,
        string configuredSetId,
        IEnumerable<AuthMethodListItem> methods,
        string defaultSetId)
    {
        string normalizedConfiguredSetId = string.IsNullOrWhiteSpace(configuredSetId)
            ? defaultSetId
            : configuredSetId.Trim();

        List<AuthMethodDialogResult> authMethods = methods
            .Where(method => method.Result is not null)
            .Select(method => method.Result)
            .ToList();

        if (authMethods.Count == 0)
        {
            DeleteManagedAuthSetIfExists(session, phase1, ruleIdentity);
            return normalizedConfiguredSetId;
        }

        string? referencedSetId = authMethods
            .Select(method => ResolveReferencedAuthSetId(method, phase1))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        WbemObject[] proposals = BuildAuthProposals(session, authMethods).ToArray();
        if (proposals.Length == 0)
        {
            DeleteManagedAuthSetIfExists(session, phase1, ruleIdentity);
            return !string.IsNullOrWhiteSpace(referencedSetId)
                ? referencedSetId
                : normalizedConfiguredSetId;
        }

        string setId = BuildManagedAuthSetId(ruleIdentity, phase1);
        string creationClassName = BuildManagedAuthSetCreationClassName(phase1, setId);
        try
        {
            UpsertManagedAuthSet(session, phase1, setId, creationClassName, rule.Name, proposals);
        }
        finally
        {
            foreach (WbemObject proposal in proposals)
            {
                proposal.Dispose();
            }
        }

        return setId;
    }

    private static string ResolveReferencedAuthSetId(AuthMethodDialogResult method, bool phase1)
    {
        string expectedKind = phase1 ? "Phase1AuthSet" : "Phase2AuthSet";
        if (!string.Equals(method.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(method.Details))
        {
            return method.Details.Trim();
        }

        if (!string.IsNullOrWhiteSpace(method.Method))
        {
            string candidate = method.Method.Trim();
            if (candidate.StartsWith("{", StringComparison.Ordinal) ||
                candidate.StartsWith("MSFT|", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<WbemObject> BuildAuthProposals(WbemServices session, IEnumerable<AuthMethodDialogResult> methods)
    {
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

            default:
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
        if (!CertificateAuthorityNameSupport.IsValidTrustedCaName(method.CaDistinguishedName))
        {
            throw new ArgumentException(
                "Certificate authentication proposals require a valid X.500 certification authority name without the '|' character.",
                nameof(method));
        }

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

    private static string BuildManagedAuthSetId(string ruleIdentity, bool phase1)
    {
        string prefix = phase1 ? "P1" : "P2";
        Guid guid = CreateDeterministicGuid($"OneMMC.ConnectionSecurity.{prefix}.{ruleIdentity.Trim().ToUpperInvariant()}");
        return guid.ToString("B");
    }

    private static string BuildManagedAuthSetCreationClassName(bool phase1, string setId)
        => $"MSFT|FW|{(phase1 ? "P1" : "P2")}AuthSet|{setId}";

    private static Guid CreateDeterministicGuid(string input)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private static void UpsertManagedAuthSet(
        WbemServices session,
        bool phase1,
        string setId,
        string creationClassName,
        string ruleName,
        WbemObject[] proposals)
    {
        string className = phase1 ? "MSFT_NetIKEP1AuthSet" : "MSFT_NetIKEP2AuthSet";
        string displayName = $"{ManagedAuthSetDisplayPrefix} - {ruleName} - {(phase1 ? "Phase 1" : "Phase 2")}";

        using WbemObject? existing = FindSetByCreationClass(session, className, creationClassName);
        if (existing is null)
        {
            using WbemObject instance = session.SpawnInstance(className);
            instance.SetProperty("CreationClassName", creationClassName, WbemType.String);
            instance.SetProperty("PolicyActionName", string.Empty, WbemType.String);
            instance.SetProperty("PolicyRuleCreationClassName", string.Empty, WbemType.String);
            instance.SetProperty("PolicyRuleName", string.Empty, WbemType.String);
            instance.SetProperty("SystemCreationClassName", string.Empty, WbemType.String);
            instance.SetProperty("SystemName", string.Empty, WbemType.String);
            instance.SetProperty("DisplayName", displayName, WbemType.String);
            instance.SetProperty("PolicyStoreSource", "PersistentStore", WbemType.String);
            instance.SetProperty("PolicyStoreSourceType", (ushort)1, WbemType.UInt16);
            instance.SetProperty("Proposals", proposals, WbemType.InstanceArray);
            session.CreateInstance(instance);
            return;
        }

        existing.SetProperty("DisplayName", displayName);
        existing.SetProperty("Proposals", proposals);
        session.ModifyInstance(existing);
    }

    private static void DeleteManagedAuthSetIfExists(WbemServices session, bool phase1, string ruleIdentity)
    {
        string setId = BuildManagedAuthSetId(ruleIdentity, phase1);
        string creationClassName = BuildManagedAuthSetCreationClassName(phase1, setId);
        string className = phase1 ? "MSFT_NetIKEP1AuthSet" : "MSFT_NetIKEP2AuthSet";

        using WbemObject? existing = FindSetByCreationClass(session, className, creationClassName);
        if (existing is null)
        {
            return;
        }

        string displayName = existing.GetValue("DisplayName")?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(displayName) &&
            !displayName.StartsWith(ManagedAuthSetDisplayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        session.DeleteInstance(existing);
    }
}
