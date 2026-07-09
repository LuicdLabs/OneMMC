using System;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

internal static class CimSetManager
{
    private const string DefaultMainModeCryptoSetCreationClass = "MSFT|FW|MMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}";
    private const string DefaultQuickModeCryptoSetCreationClass = "MSFT|FW|QMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}";
    private const string DefaultPhase1AuthSetCreationClass = "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}";
    private const string DefaultPhase2AuthSetCreationClass = "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}";

    internal static void UpsertAuthSet(
        WbemServices session,
        string setClassName,
        string creationClassName,
        System.Collections.Generic.IEnumerable<ushort> authenticationMethods)
    {
        WbemObject[] proposals = AuthProposalManager.BuildAuthProposals(session, authenticationMethods).ToArray();
        if (proposals.Length == 0)
        {
            DeleteSetIfExists(session, setClassName, creationClassName);
            return;
        }

        try
        {
            UpsertSet(session, setClassName, creationClassName, proposals);
        }
        finally
        {
            foreach (WbemObject proposal in proposals)
            {
                proposal.Dispose();
            }
        }
    }

    internal static void UpsertMainModeSet(
        WbemServices session,
        WbemObject[] proposals,
        IpsecDefaultsModel defaults)
    {
        if (proposals.Length == 0)
        {
            throw new ArgumentException("At least one proposal is required.", nameof(proposals));
        }

        using WbemObject? existing = GetDefaultMainModeSet(session);
        if (existing is null)
        {
            using WbemObject instance = BuildMainModeSetInstance(session, proposals, defaults);
            session.CreateInstance(instance);
            return;
        }

        existing.SetProperty("Proposals", proposals);
        ApplyMainModeOptions(existing, defaults);
        session.ModifyInstance(existing);
    }

    internal static void UpsertSet(
        WbemServices session,
        string setClassName,
        string creationClassName,
        WbemObject[] proposals)
    {
        if (proposals.Length == 0)
        {
            throw new ArgumentException("At least one proposal is required.", nameof(proposals));
        }

        using WbemObject? existing = FindSetByCreationClass(session, setClassName, creationClassName);
        if (existing is null)
        {
            using WbemObject instance = BuildPolicySetInstance(session, setClassName, creationClassName, proposals);
            session.CreateInstance(instance);
            return;
        }

        existing.SetProperty("Proposals", proposals);
        session.ModifyInstance(existing);
    }

    internal static void DeleteSetIfExists(WbemServices session, string setClassName, string creationClassName)
    {
        using WbemObject? existing = FindSetByCreationClass(session, setClassName, creationClassName);
        if (existing is not null)
        {
            session.DeleteInstance(existing);
        }
    }

    internal static WbemObject? GetDefaultMainModeSet(WbemServices session)
        => FindSetByCreationClass(session, "MSFT_NetIKEMMCryptoSet", DefaultMainModeCryptoSetCreationClass);

    internal static WbemObject? FindSetByCreationClass(WbemServices session, string setClassName, string creationClassName)
    {
        foreach (WbemObject instance in session.EnumerateInstances(setClassName))
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

    internal static WbemObject GetFirewallSettingInstance(WbemServices session)
    {
        WbemObject? fallback = null;
        foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetSecuritySettingData"))
        {
            if (string.Equals(
                    instance.GetValue("InstanceID")?.ToString(),
                    "MSFT|GlobalIPSecSettingData",
                    StringComparison.OrdinalIgnoreCase))
            {
                fallback?.Dispose();
                return instance;
            }

            fallback ??= instance;
            if (!ReferenceEquals(fallback, instance))
            {
                instance.Dispose();
            }
        }

        return fallback ?? throw new InvalidOperationException("The firewall IPsec settings instance could not be found.");
    }

    internal static WbemObject GetFirewallProfileInstance(WbemServices session, FirewallProfileType profileType)
    {
        string profileName = profileType.ToString();
        foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetFirewallProfile"))
        {
            if (string.Equals(
                    instance.GetValue("Name")?.ToString(),
                    profileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return instance;
            }

            instance.Dispose();
        }

        throw new InvalidOperationException($"The firewall profile '{profileName}' could not be found.");
    }

    internal static WbemObject[] BuildMainModeProposals(WbemServices session, System.Collections.Generic.IEnumerable<MainModeProposalDefinition> proposals)
        => proposals.Select(proposal => BuildMainModeProposal(session, proposal)).ToArray();

    internal static WbemObject[] BuildQuickModeProposals(WbemServices session, System.Collections.Generic.IEnumerable<QuickModeProposalDefinition> proposals)
        => proposals.Select(proposal => BuildQuickModeProposal(session, proposal)).ToArray();

    internal static void ApplyMainModeOptions(WbemObject instance, IpsecDefaultsModel defaults)
    {
        instance.SetProperty("MaxLifetimeMinutes", (uint)Math.Max(0, defaults.MainModeKeyLifetimeMinutes), WbemType.UInt32);
        instance.SetProperty("MaxLifetimeSessions", (uint)Math.Max(0, defaults.MainModeKeyLifetimeSessions), WbemType.UInt32);
        instance.SetProperty("ForceDiffieHellman", defaults.MainModeForceDiffieHellman, WbemType.Boolean);
    }

    private static WbemObject BuildPolicySetInstance(WbemServices session, string setClassName, string creationClassName, WbemObject[] proposals)
    {
        WbemObject instance = session.SpawnInstance(setClassName);
        instance.SetProperty("CreationClassName", creationClassName, WbemType.String);
        instance.SetProperty("PolicyActionName", string.Empty, WbemType.String);
        instance.SetProperty("PolicyRuleCreationClassName", string.Empty, WbemType.String);
        instance.SetProperty("PolicyRuleName", string.Empty, WbemType.String);
        instance.SetProperty("SystemCreationClassName", string.Empty, WbemType.String);
        instance.SetProperty("SystemName", string.Empty, WbemType.String);
        instance.SetProperty("PolicyStoreSource", "PersistentStore", WbemType.String);
        instance.SetProperty("PolicyStoreSourceType", (ushort)1, WbemType.UInt16);
        instance.SetProperty("Proposals", proposals, WbemType.InstanceArray);
        return instance;
    }

    private static WbemObject BuildMainModeSetInstance(WbemServices session, WbemObject[] proposals, IpsecDefaultsModel defaults)
    {
        WbemObject instance = BuildPolicySetInstance(
            session,
            "MSFT_NetIKEMMCryptoSet",
            DefaultMainModeCryptoSetCreationClass,
            proposals);
        ApplyMainModeOptions(instance, defaults);
        return instance;
    }

    private static WbemObject BuildMainModeProposal(WbemServices session, MainModeProposalDefinition proposal)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKEMMCryptoProposal");
        instance.SetProperty("CipherAlgorithm", proposal.CipherAlgorithm, WbemType.UInt16);
        instance.SetProperty("HashAlgorithm", proposal.HashAlgorithm, WbemType.UInt16);
        instance.SetProperty("GroupId", proposal.GroupId, WbemType.UInt16);
        return instance;
    }

    private static WbemObject BuildQuickModeProposal(WbemServices session, QuickModeProposalDefinition proposal)
    {
        WbemObject instance = session.SpawnInstance("MSFT_NetIKEQMCryptoProposal");
        instance.SetProperty("Encapsulation", proposal.Encapsulation, WbemType.UInt16);
        instance.SetProperty("HashAlgorithmAH", proposal.HashAlgorithmAh ?? 0, WbemType.UInt16);
        instance.SetProperty("HashAlgorithmESP", proposal.HashAlgorithmEsp ?? 0, WbemType.UInt16);
        instance.SetProperty("CipherAlgorithm", proposal.CipherAlgorithm ?? 0, WbemType.UInt16);
        instance.SetProperty("MaxLifetimeMinutes", proposal.MaxLifetimeMinutes, WbemType.UInt32);
        instance.SetProperty("MaxLifetimeKilobytes", proposal.MaxLifetimeKilobytes, WbemType.UInt64);
        return instance;
    }
}
