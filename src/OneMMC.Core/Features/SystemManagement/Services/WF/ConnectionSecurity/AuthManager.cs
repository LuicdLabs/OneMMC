using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using Microsoft.Management.Infrastructure;

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

    internal static string ResolvePhase1AuthSetId(CimSession session, ConnectionSecurityRuleModel rule, string ruleIdentity)
        => ResolvePhaseAuthSetId(session, rule, ruleIdentity, phase1: true, rule.Phase1AuthSet, rule.FirstAuthMethods, DefaultPhase1AuthSetId);

    internal static string ResolvePhase2AuthSetId(CimSession session, ConnectionSecurityRuleModel rule, string ruleIdentity)
        => ResolvePhaseAuthSetId(session, rule, ruleIdentity, phase1: false, rule.Phase2AuthSet, rule.SecondAuthMethods, DefaultPhase2AuthSetId);

    internal static string ResolveRuleIdentity(CimInstance instance, ConnectionSecurityRuleModel rule)
    {
        string policyRuleName = instance.CimInstanceProperties["PolicyRuleName"]?.Value?.ToString() ?? string.Empty;
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

        return instance.CimInstanceProperties["ElementName"]?.Value?.ToString() ?? Guid.NewGuid().ToString("N");
    }

    internal static void DeleteManagedAuthSets(CimSession session, string ruleIdentity)
    {
        if (string.IsNullOrWhiteSpace(ruleIdentity))
        {
            return;
        }

        DeleteManagedAuthSetIfExists(session, phase1: true, ruleIdentity);
        DeleteManagedAuthSetIfExists(session, phase1: false, ruleIdentity);
    }

    internal static CimInstance? FindSetByCreationClass(CimSession session, string className, string creationClassName)
    {
        foreach (CimInstance instance in session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, className))
        {
            if (string.Equals(
                    instance.CimInstanceProperties["CreationClassName"]?.Value?.ToString(),
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
        CimSession session,
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

        CimInstance[] proposals = BuildAuthProposals(authMethods).ToArray();
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
            foreach (CimInstance proposal in proposals)
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

    private static IEnumerable<CimInstance> BuildAuthProposals(IEnumerable<AuthMethodDialogResult> methods)
    {
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

            default:
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
        CimSession session,
        bool phase1,
        string setId,
        string creationClassName,
        string ruleName,
        CimInstance[] proposals)
    {
        string className = phase1 ? "MSFT_NetIKEP1AuthSet" : "MSFT_NetIKEP2AuthSet";
        string displayName = $"{ManagedAuthSetDisplayPrefix} - {ruleName} - {(phase1 ? "Phase 1" : "Phase 2")}";

        using CimInstance? existing = FindSetByCreationClass(session, className, creationClassName);
        if (existing is null)
        {
            using var instance = new CimInstance(className, WindowsFirewallSupport.StandardCimNamespace);
            instance.CimInstanceProperties.Add(CimProperty.Create("CreationClassName", creationClassName, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("PolicyActionName", string.Empty, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("PolicyRuleCreationClassName", string.Empty, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("PolicyRuleName", string.Empty, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("SystemCreationClassName", string.Empty, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("SystemName", string.Empty, CimType.String, CimFlags.Key));
            instance.CimInstanceProperties.Add(CimProperty.Create("DisplayName", displayName, CimType.String, CimFlags.None));
            instance.CimInstanceProperties.Add(CimProperty.Create("PolicyStoreSource", "PersistentStore", CimType.String, CimFlags.None));
            instance.CimInstanceProperties.Add(CimProperty.Create("PolicyStoreSourceType", (ushort)1, CimType.UInt16, CimFlags.None));
            instance.CimInstanceProperties.Add(CimProperty.Create("Proposals", proposals, CimType.InstanceArray, CimFlags.None));
            using CimInstance _ = session.CreateInstance(WindowsFirewallSupport.StandardCimNamespace, instance);
            return;
        }

        existing.CimInstanceProperties["DisplayName"].Value = displayName;
        existing.CimInstanceProperties["Proposals"].Value = proposals;
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, existing);
    }

    private static void DeleteManagedAuthSetIfExists(CimSession session, bool phase1, string ruleIdentity)
    {
        string setId = BuildManagedAuthSetId(ruleIdentity, phase1);
        string creationClassName = BuildManagedAuthSetCreationClassName(phase1, setId);
        string className = phase1 ? "MSFT_NetIKEP1AuthSet" : "MSFT_NetIKEP2AuthSet";

        using CimInstance? existing = FindSetByCreationClass(session, className, creationClassName);
        if (existing is null)
        {
            return;
        }

        string displayName = existing.CimInstanceProperties["DisplayName"]?.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(displayName) &&
            !displayName.StartsWith(ManagedAuthSetDisplayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        session.DeleteInstance(WindowsFirewallSupport.StandardCimNamespace, existing);
    }
}
