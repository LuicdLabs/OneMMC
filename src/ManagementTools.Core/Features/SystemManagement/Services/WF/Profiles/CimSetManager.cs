using System;
using System.Linq;
using ManagementTools.Core.Features.SystemManagement.Interop.WF;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;

internal static class CimSetManager
{
    private const string DefaultMainModeCryptoSetCreationClass = "MSFT|FW|MMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}";
    private const string DefaultQuickModeCryptoSetCreationClass = "MSFT|FW|QMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}";
    private const string DefaultPhase1AuthSetCreationClass = "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}";
    private const string DefaultPhase2AuthSetCreationClass = "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}";

    internal static void UpsertAuthSet(
        CimSession session,
        string setClassName,
        string creationClassName,
        System.Collections.Generic.IEnumerable<ushort> authenticationMethods)
    {
        CimInstance[] proposals = AuthProposalManager.BuildAuthProposals(authenticationMethods).ToArray();
        if (proposals.Length == 0)
        {
            DeleteSetIfExists(session, setClassName, creationClassName);
            return;
        }

        UpsertSet(session, setClassName, creationClassName, proposals);
    }

    internal static CimInstance UpsertMainModeSet(
        CimSession session,
        CimInstance[] proposals,
        IpsecDefaultsModel defaults)
    {
        if (proposals.Length == 0)
        {
            throw new ArgumentException("At least one proposal is required.", nameof(proposals));
        }

        CimInstance? existing = GetDefaultMainModeSet(session);
        if (existing is null)
        {
            CimInstance created = session.CreateInstance(
                WindowsFirewallSupport.StandardCimNamespace,
                BuildMainModeSetInstance(proposals, defaults));

            return created;
        }

        existing.CimInstanceProperties["Proposals"].Value = proposals;
        ApplyMainModeOptions(existing, defaults);
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, existing);
        return existing;
    }

    internal static CimInstance UpsertSet(
        CimSession session,
        string setClassName,
        string creationClassName,
        CimInstance[] proposals)
    {
        if (proposals.Length == 0)
        {
            throw new ArgumentException("At least one proposal is required.", nameof(proposals));
        }

        CimInstance? existing = FindSetByCreationClass(session, setClassName, creationClassName);
        if (existing is null)
        {
            CimInstance created = session.CreateInstance(
                WindowsFirewallSupport.StandardCimNamespace,
                BuildPolicySetInstance(setClassName, creationClassName, proposals));

            return created;
        }

        existing.CimInstanceProperties["Proposals"].Value = proposals;
        session.ModifyInstance(WindowsFirewallSupport.StandardCimNamespace, existing);
        return existing;
    }

    internal static void DeleteSetIfExists(CimSession session, string setClassName, string creationClassName)
    {
        CimInstance? existing = FindSetByCreationClass(session, setClassName, creationClassName);
        if (existing is not null)
        {
            session.DeleteInstance(WindowsFirewallSupport.StandardCimNamespace, existing);
        }
    }

    internal static CimInstance? GetDefaultMainModeSet(CimSession session)
        => FindSetByCreationClass(session, "MSFT_NetIKEMMCryptoSet", DefaultMainModeCryptoSetCreationClass);

    internal static CimInstance? FindSetByCreationClass(CimSession session, string setClassName, string creationClassName)
    {
        return session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, setClassName)
            .FirstOrDefault(instance =>
                string.Equals(
                    instance.CimInstanceProperties["CreationClassName"]?.Value?.ToString(),
                    creationClassName,
                    StringComparison.OrdinalIgnoreCase));
    }

    internal static CimInstance GetFirewallSettingInstance(CimSession session)
    {
        CimInstance? instance = session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetSecuritySettingData")
            .FirstOrDefault(item =>
                string.Equals(
                    item.CimInstanceProperties["InstanceID"]?.Value?.ToString(),
                    "MSFT|GlobalIPSecSettingData",
                    StringComparison.OrdinalIgnoreCase))
            ?? session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetSecuritySettingData")
                .FirstOrDefault();

        return instance ?? throw new InvalidOperationException("The firewall IPsec settings instance could not be found.");
    }

    internal static CimInstance GetFirewallProfileInstance(CimSession session, FirewallProfileType profileType)
    {
        string profileName = profileType.ToString();
        CimInstance? instance = session.EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetFirewallProfile")
            .FirstOrDefault(item =>
                string.Equals(
                    item.CimInstanceProperties["Name"]?.Value?.ToString(),
                    profileName,
                    StringComparison.OrdinalIgnoreCase));

        return instance ?? throw new InvalidOperationException($"The firewall profile '{profileName}' could not be found.");
    }

    internal static CimInstance[] BuildMainModeProposals(System.Collections.Generic.IEnumerable<MainModeProposalDefinition> proposals)
        => proposals.Select(BuildMainModeProposal).ToArray();

    internal static CimInstance[] BuildQuickModeProposals(System.Collections.Generic.IEnumerable<QuickModeProposalDefinition> proposals)
        => proposals.Select(BuildQuickModeProposal).ToArray();

    internal static void ApplyMainModeOptions(CimInstance instance, IpsecDefaultsModel defaults)
    {
        SetOrAddProperty(instance, "MaxLifetimeMinutes", (uint)Math.Max(0, defaults.MainModeKeyLifetimeMinutes), CimType.UInt32);
        SetOrAddProperty(instance, "MaxLifetimeSessions", (uint)Math.Max(0, defaults.MainModeKeyLifetimeSessions), CimType.UInt32);
        SetOrAddProperty(instance, "ForceDiffieHellman", defaults.MainModeForceDiffieHellman, CimType.Boolean);
    }

    private static CimInstance BuildPolicySetInstance(string setClassName, string creationClassName, CimInstance[] proposals)
    {
        var instance = new CimInstance(setClassName, WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("CreationClassName", creationClassName, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("PolicyActionName", string.Empty, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("PolicyRuleCreationClassName", string.Empty, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("PolicyRuleName", string.Empty, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("SystemCreationClassName", string.Empty, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("SystemName", string.Empty, CimType.String, CimFlags.Key));
        instance.CimInstanceProperties.Add(CimProperty.Create("PolicyStoreSource", "PersistentStore", CimType.String, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("PolicyStoreSourceType", (ushort)1, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("Proposals", proposals, CimType.InstanceArray, CimFlags.None));
        return instance;
    }

    private static CimInstance BuildMainModeSetInstance(CimInstance[] proposals, IpsecDefaultsModel defaults)
    {
        CimInstance instance = BuildPolicySetInstance(
            "MSFT_NetIKEMMCryptoSet",
            DefaultMainModeCryptoSetCreationClass,
            proposals);
        ApplyMainModeOptions(instance, defaults);
        return instance;
    }

    private static void SetOrAddProperty(CimInstance instance, string propertyName, object value, CimType cimType)
    {
        if (instance.CimInstanceProperties[propertyName] is CimProperty property)
        {
            property.Value = value;
            return;
        }

        instance.CimInstanceProperties.Add(CimProperty.Create(propertyName, value, cimType, CimFlags.None));
    }

    private static CimInstance BuildMainModeProposal(MainModeProposalDefinition proposal)
    {
        var instance = new CimInstance("MSFT_NetIKEMMCryptoProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("CipherAlgorithm", proposal.CipherAlgorithm, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("HashAlgorithm", proposal.HashAlgorithm, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("GroupId", proposal.GroupId, CimType.UInt16, CimFlags.None));
        return instance;
    }

    private static CimInstance BuildQuickModeProposal(QuickModeProposalDefinition proposal)
    {
        var instance = new CimInstance("MSFT_NetIKEQMCryptoProposal", WindowsFirewallSupport.StandardCimNamespace);
        instance.CimInstanceProperties.Add(CimProperty.Create("Encapsulation", proposal.Encapsulation, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("HashAlgorithmAH", proposal.HashAlgorithmAh ?? 0, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("HashAlgorithmESP", proposal.HashAlgorithmEsp ?? 0, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("CipherAlgorithm", proposal.CipherAlgorithm ?? 0, CimType.UInt16, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("MaxLifetimeMinutes", proposal.MaxLifetimeMinutes, CimType.UInt32, CimFlags.None));
        instance.CimInstanceProperties.Add(CimProperty.Create("MaxLifetimeKilobytes", proposal.MaxLifetimeKilobytes, CimType.UInt64, CimFlags.None));
        return instance;
    }
}
