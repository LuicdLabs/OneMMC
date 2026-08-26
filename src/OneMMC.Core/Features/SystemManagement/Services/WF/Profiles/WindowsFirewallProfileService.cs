using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Infrastructure.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

public class WindowsFirewallProfileService
{
    private const ushort GpoBoolFalse = 0;
    private const ushort GpoBoolTrue = 1;
    private const ushort GpoBoolNotConfigured = 2;
    private const uint TrafficExemptionIcmp = 0x2;
    private const uint TrafficExemptionNotConfigured = uint.MaxValue;

    private const ushort AuthMethodAnonymous = 65001;
    private const ushort AuthMethodMachineKerberos = 65002;
    private const ushort AuthMethodMachineNtlm = 65003;
    private const ushort AuthMethodUserKerberos = 65004;
    private const ushort AuthMethodUserNtlm = 65005;
    private const ushort AuthMethodMachineCertificate = 65006;
    private const ushort AuthMethodUserCertificate = 65007;
    private const ushort AuthMethodMachineHealthCertificate = 65008;
    private const ushort AuthMethodPreSharedKey = 2;

    private static readonly IReadOnlyList<MainModeProposalDefinition> DefaultMainModeProposals =
    [
        new(65001, 3, 2),
        new(6, 3, 2)
    ];

    private static readonly IReadOnlyList<MainModeProposalDefinition> AdvancedMainModeProposals =
    [
        new(65003, 65001, 14),
        new(65002, 65001, 14),
        new(65001, 3, 2)
    ];

    private static readonly IReadOnlyList<QuickModeProposalDefinition> AdvancedQuickModeProposals =
    [
        new(2, null, 65001, 65003, 60, 100000),
        new(1, 65001, null, null, 60, 100000),
        new(2, null, 3, 65001, 60, 100000)
    ];

    private static readonly ushort[] MachineKerberosAuthMethods = [AuthMethodMachineKerberos];
    private static readonly ushort[] MachineKerberosAndNtlmAuthMethods = [AuthMethodMachineKerberos, AuthMethodMachineNtlm];
    private static readonly ushort[] UserKerberosAuthMethods = [AuthMethodUserKerberos];
    private static readonly ushort[] UserKerberosNtlmAnonymousAuthMethods = [AuthMethodUserKerberos, AuthMethodUserNtlm, AuthMethodAnonymous];

    private readonly ILogger<WindowsFirewallProfileService> _logger;

    public WindowsFirewallProfileService()
        : this(NullLogger<WindowsFirewallProfileService>.Instance)
    {
    }

    public WindowsFirewallProfileService(ILogger<WindowsFirewallProfileService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<FirewallProfileModel> GetProfiles()
        => Enum.GetValues<FirewallProfileType>().Select(GetProfile).ToList();

    public FirewallProfileModel GetProfile(FirewallProfileType profileType)
    {
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        try
        {
            int profileMask = (int)profileType;

            var model = new FirewallProfileModel
            {
                ProfileType = profileType,
                DisplayName = profileType.ToString(),
                IsActive = (policy.get_CurrentProfileTypes() & profileMask) != 0,
                IsEnabled = FirewallCom.ToBool(policy.get_FirewallEnabled(profileMask)),
                DefaultInboundAction = policy.get_DefaultInboundAction(profileMask) == WindowsFirewallSupport.NetFwActionAllow
                    ? FirewallDefaultAction.Allow
                    : FirewallDefaultAction.Block,
                DefaultOutboundAction = policy.get_DefaultOutboundAction(profileMask) == WindowsFirewallSupport.NetFwActionAllow
                    ? FirewallDefaultAction.Allow
                    : FirewallDefaultAction.Block,
                BlockAllInboundTraffic = FirewallCom.ToBool(policy.get_BlockAllInboundTraffic(profileMask)),
                NotificationsDisabled = FirewallCom.ToBool(policy.get_NotificationsDisabled(profileMask)),
                UnicastResponsesToMulticastBroadcastDisabled = FirewallCom.ToBool(policy.get_UnicastResponsesToMulticastBroadcastDisabled(profileMask)),
                PolicyModifyState = (FirewallPolicyModifyState)policy.get_LocalPolicyModifyState(),
                LoggingSettings = ReadLoggingSettings(profileType)
            };

            foreach (NetworkConnectionItem connection in BuildProtectedNetworkConnections(policy, profileMask))
            {
                model.ProtectedNetworkConnections.Add(connection);
            }

            return model;
        }
        finally
        {
            FirewallCom.Release(policy);
        }
    }

    public void UpdateProfile(FirewallProfileModel profile)
    {
        INetFwPolicy2 policy = WindowsFirewallSupport.CreatePolicy2();
        try
        {
            int profileMask = (int)profile.ProfileType;

            policy.set_FirewallEnabled(profileMask, FirewallCom.ToVariantBool(profile.IsEnabled));
            policy.set_DefaultInboundAction(profileMask, profile.DefaultInboundAction == FirewallDefaultAction.Allow
                    ? WindowsFirewallSupport.NetFwActionAllow
                    : WindowsFirewallSupport.NetFwActionBlock);
            policy.set_DefaultOutboundAction(profileMask, profile.DefaultOutboundAction == FirewallDefaultAction.Allow
                    ? WindowsFirewallSupport.NetFwActionAllow
                    : WindowsFirewallSupport.NetFwActionBlock);
            policy.set_BlockAllInboundTraffic(profileMask, FirewallCom.ToVariantBool(profile.BlockAllInboundTraffic));
            policy.set_NotificationsDisabled(profileMask, FirewallCom.ToVariantBool(profile.NotificationsDisabled));
            policy.set_UnicastResponsesToMulticastBroadcastDisabled(profileMask, FirewallCom.ToVariantBool(profile.UnicastResponsesToMulticastBroadcastDisabled));

            UpdateProtectedNetworkConnections(profile);
            WriteLoggingSettings(profile.ProfileType, profile.LoggingSettings);
            _logger.LogInformation("Updated firewall profile {ProfileType}.", profile.ProfileType);
        }
        finally
        {
            FirewallCom.Release(policy);
        }
    }

    public IpsecDefaultsModel GetIpsecDefaults()
    {
        using FirewallSettingSnapshot snapshot = GetFirewallSettingSnapshot();

        return new IpsecDefaultsModel
        {
            KeyExchangeMode = ResolveKeyExchangeMode(snapshot.MainModeProposals),
            DataProtectionMode = snapshot.DefaultQuickModeSet is null ? "Default" : "Advanced",
            AuthenticationMethodMode = ResolveAuthenticationMethodMode(snapshot),
            MainModeCryptoSet = snapshot.DefaultMainModeSet?.GetValue("CreationClassName")?.ToString() ?? string.Empty,
            QuickModeCryptoSet = snapshot.DefaultQuickModeSet?.GetValue("CreationClassName")?.ToString() ?? string.Empty,
            Phase1AuthSet = snapshot.DefaultPhase1AuthSet?.GetValue("CreationClassName")?.ToString() ?? string.Empty,
            Phase2AuthSet = snapshot.DefaultPhase2AuthSet?.GetValue("CreationClassName")?.ToString() ?? string.Empty,
            AdvancedMainModeSecurityMethods = ProposalConverter.ReadMainModeSecurityMethods(snapshot.MainModeProposals).ToList(),
            AdvancedIntegrityAlgorithms = ProposalConverter.ReadIntegrityAlgorithms(snapshot.DefaultQuickModeSet).ToList(),
            AdvancedIntegrityEncryptionAlgorithms = ProposalConverter.ReadIntegrityEncryptionAlgorithms(snapshot.DefaultQuickModeSet).ToList(),
            AdvancedFirstAuthMethods = AuthProposalManager.ReadAuthMethodResults(snapshot.DefaultPhase1AuthSet).ToList(),
            AdvancedSecondAuthMethods = AuthProposalManager.ReadAuthMethodResults(snapshot.DefaultPhase2AuthSet).ToList(),
            IsAdvancedFirstAuthOptional = false,
            IsAdvancedSecondAuthOptional = false,
            MainModeKeyLifetimeMinutes = (int)ValueConverter.ConvertToUInt32(snapshot.DefaultMainModeSet?.GetValue("MaxLifetimeMinutes"), 480),
            MainModeKeyLifetimeSessions = (int)ValueConverter.ConvertToUInt32(snapshot.DefaultMainModeSet?.GetValue("MaxLifetimeSessions"), 0),
            MainModeForceDiffieHellman = ValueConverter.ConvertToBoolean(snapshot.DefaultMainModeSet?.GetValue("ForceDiffieHellman"), false),
            NatTraversalMode = ResolveNatTraversalMode(snapshot.AllowIpsecThroughNat),
            IcmpExemptionEnabled = (snapshot.Exemptions & TrafficExemptionIcmp) != 0
        };
    }

    public void UpdateIpsecDefaults(IpsecDefaultsModel defaults)
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject setting = CimSetManager.GetFirewallSettingInstance(session);

        setting.SetProperty("AllowIPsecThroughNAT", ResolveNatTraversalValue(defaults.NatTraversalMode), WbemType.UInt16);
        setting.SetProperty("Exemptions", UpdateExemptionsMask(
            ValueConverter.ConvertToUInt32(setting.GetValue("Exemptions"), 0),
            defaults.IcmpExemptionEnabled), WbemType.UInt32);

        string normalizedAuthenticationMode = NormalizeAuthenticationMethodMode(defaults.AuthenticationMethodMode);
        ApplyMainModeDefaults(session, defaults);
        ApplyQuickModeDefaults(session, defaults);
        ApplyAuthenticationDefaults(session, setting, defaults, normalizedAuthenticationMode);
        session.ModifyInstance(setting);

        IpsecDefaultsModel actual = GetIpsecDefaults();
        VerifyIpsecDefaultsApplied(defaults, actual, normalizedAuthenticationMode);

        _logger.LogInformation(
            "Updated IPsec defaults. KeyExchangeMode={KeyExchangeMode}, DataProtectionMode={DataProtectionMode}, AuthenticationMethodMode={AuthenticationMethodMode}, NatTraversalMode={NatTraversalMode}, IcmpExemptionEnabled={IcmpExemptionEnabled}.",
            defaults.KeyExchangeMode,
            defaults.DataProtectionMode,
            normalizedAuthenticationMode,
            defaults.NatTraversalMode,
            defaults.IcmpExemptionEnabled);
    }

    public IpsecDefaultsModel UpdateIcmpExemption(bool enabled)
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject setting = CimSetManager.GetFirewallSettingInstance(session);

        setting.SetProperty("Exemptions", UpdateExemptionsMask(
            ValueConverter.ConvertToUInt32(setting.GetValue("Exemptions"), 0),
            enabled), WbemType.UInt32);
        session.ModifyInstance(setting);

        IpsecDefaultsModel actual = GetIpsecDefaults();
        if (actual.IcmpExemptionEnabled != enabled)
        {
            _logger.LogWarning(
                "Windows Firewall did not report the requested IPsec ICMP exemption state. Requested={Requested}, Actual={Actual}.",
                enabled,
                actual.IcmpExemptionEnabled);
        }

        return actual;
    }

    public FirewallTunnelAuthorizationSettings GetTunnelAuthorizationSettings()
    {
        using FirewallSettingSnapshot snapshot = GetFirewallSettingSnapshot();

        bool hasTransportAuthorizations = snapshot.HasTransportAuthorizations;
        string machineList = hasTransportAuthorizations
            ? snapshot.RemoteMachineTransportAuthorizationList
            : snapshot.RemoteMachineTunnelAuthorizationList;
        string userList = hasTransportAuthorizations
            ? snapshot.RemoteUserTransportAuthorizationList
            : snapshot.RemoteUserTunnelAuthorizationList;

        var settings = new FirewallTunnelAuthorizationSettings
        {
            Mode = hasTransportAuthorizations
                ? TunnelAuthorizationMode.Custom
                : snapshot.HasTunnelAuthorizations
                    ? TunnelAuthorizationMode.Advanced
                    : TunnelAuthorizationMode.None
        };

        foreach (TunnelAuthorizationItem item in WindowsFirewallSupport.ParseAuthorizationSddl(machineList, allowEntries: true))
        {
            settings.AllowedComputers.Add(item);
        }

        foreach (TunnelAuthorizationItem item in WindowsFirewallSupport.ParseAuthorizationSddl(machineList, allowEntries: false))
        {
            settings.DeniedComputers.Add(item);
        }

        foreach (TunnelAuthorizationItem item in WindowsFirewallSupport.ParseAuthorizationSddl(userList, allowEntries: true))
        {
            settings.AllowedUsers.Add(item);
        }

        foreach (TunnelAuthorizationItem item in WindowsFirewallSupport.ParseAuthorizationSddl(userList, allowEntries: false))
        {
            settings.DeniedUsers.Add(item);
        }

        return settings;
    }

    public void UpdateTunnelAuthorizationSettings(FirewallTunnelAuthorizationSettings settings)
    {
        string machineAuthorization = settings.Mode == TunnelAuthorizationMode.None
            ? "NotConfigured"
            : WindowsFirewallSupport.BuildAuthorizationSddl(settings.AllowedComputers, settings.DeniedComputers);
        string userAuthorization = settings.Mode == TunnelAuthorizationMode.None
            ? "NotConfigured"
            : WindowsFirewallSupport.BuildAuthorizationSddl(settings.AllowedUsers, settings.DeniedUsers);

        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject firewallSetting = CimSetManager.GetFirewallSettingInstance(session);

        firewallSetting.SetProperty("RemoteMachineTunnelAuthorizationList",
            settings.Mode == TunnelAuthorizationMode.Advanced ? machineAuthorization : "NotConfigured", WbemType.String);
        firewallSetting.SetProperty("RemoteUserTunnelAuthorizationList",
            settings.Mode == TunnelAuthorizationMode.Advanced ? userAuthorization : "NotConfigured", WbemType.String);
        firewallSetting.SetProperty("RemoteMachineTransportAuthorizationList",
            settings.Mode == TunnelAuthorizationMode.Custom ? machineAuthorization : "NotConfigured", WbemType.String);
        firewallSetting.SetProperty("RemoteUserTransportAuthorizationList",
            settings.Mode == TunnelAuthorizationMode.Custom ? userAuthorization : "NotConfigured", WbemType.String);

        session.ModifyInstance(firewallSetting);
        _logger.LogInformation("Updated tunnel authorization settings. Mode={Mode}.", settings.Mode);
    }

    private FirewallLoggingSettings ReadLoggingSettings(FirewallProfileType profileType)
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject profile = CimSetManager.GetFirewallProfileInstance(session, profileType);

        return new FirewallLoggingSettings
        {
            FileName = profile.GetValue("LogFileName")?.ToString() ?? "%systemroot%\\system32\\logfiles\\firewall\\pfirewall.log",
            MaxFileSizeKilobytes = (int)Math.Max(1, ValueConverter.ConvertToUInt64(profile.GetValue("LogMaxSizeKilobytes"), 4096)),
            LogDroppedPackets = ValueConverter.ReadGpoBool(profile.GetValue("LogBlocked")),
            LogSuccessfulConnections = ValueConverter.ReadGpoBool(profile.GetValue("LogAllowed"))
        };
    }

    private static void WriteLoggingSettings(FirewallProfileType profileType, FirewallLoggingSettings settings)
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject profile = CimSetManager.GetFirewallProfileInstance(session, profileType);

        profile.SetProperty("LogFileName", string.IsNullOrWhiteSpace(settings.FileName)
            ? "%systemroot%\\system32\\logfiles\\firewall\\pfirewall.log"
            : settings.FileName.Trim(), WbemType.String);
        profile.SetProperty("LogMaxSizeKilobytes", (ulong)Math.Max(1, settings.MaxFileSizeKilobytes), WbemType.UInt64);
        profile.SetProperty("LogAllowed", ValueConverter.ToGpoBool(settings.LogSuccessfulConnections), WbemType.UInt16);
        profile.SetProperty("LogBlocked", ValueConverter.ToGpoBool(settings.LogDroppedPackets), WbemType.UInt16);
        session.ModifyInstance(profile);
    }

    private FirewallSettingSnapshot GetFirewallSettingSnapshot()
    {
        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject firewallSetting = CimSetManager.GetFirewallSettingInstance(session);
        WbemObject? defaultMainModeSet = CimSetManager.GetDefaultMainModeSet(session);

        return new FirewallSettingSnapshot(
            ValueConverter.ConvertToUInt16(firewallSetting.GetValue("AllowIPsecThroughNAT"), 0),
            ValueConverter.ConvertToUInt32(firewallSetting.GetValue("Exemptions"), 0),
            firewallSetting.GetValue("RemoteMachineTransportAuthorizationList")?.ToString() ?? "NotConfigured",
            firewallSetting.GetValue("RemoteMachineTunnelAuthorizationList")?.ToString() ?? "NotConfigured",
            firewallSetting.GetValue("RemoteUserTransportAuthorizationList")?.ToString() ?? "NotConfigured",
            firewallSetting.GetValue("RemoteUserTunnelAuthorizationList")?.ToString() ?? "NotConfigured",
            ValueConverter.ConvertToUInt16(firewallSetting.GetValue("RequireFullAuthSupport"), GpoBoolNotConfigured),
            ProposalConverter.ReadMainModeProposals(defaultMainModeSet),
            defaultMainModeSet,
            CimSetManager.FindSetByCreationClass(session, "MSFT_NetIKEQMCryptoSet", "MSFT|FW|QMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}"),
            CimSetManager.FindSetByCreationClass(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}"),
            CimSetManager.FindSetByCreationClass(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}"));
    }

    private static string NormalizeAuthenticationMethodMode(string? mode)
    {
        if (string.Equals(mode, "Computer and user", StringComparison.OrdinalIgnoreCase))
        {
            return "Computer and user";
        }

        if (string.Equals(mode, "Computer", StringComparison.OrdinalIgnoreCase))
        {
            return "Computer";
        }

        if (string.Equals(mode, "User", StringComparison.OrdinalIgnoreCase))
        {
            return "User";
        }

        if (string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            return "Advanced";
        }

        return "Default";
    }

    private static string ResolveKeyExchangeMode(IReadOnlyList<MainModeProposalDefinition> proposals)
    {
        if (proposals.Count == 0)
        {
            return "Default";
        }

        return ProposalConverter.AreMainModeProposalsEqual(proposals, DefaultMainModeProposals)
            ? "Default"
            : "Advanced";
    }

    private string ResolveAuthenticationMethodMode(FirewallSettingSnapshot snapshot)
    {
        ushort[] phase1Methods = ProposalConverter.ReadAuthMethods(snapshot.DefaultPhase1AuthSet);
        ushort[] phase2Methods = ProposalConverter.ReadAuthMethods(snapshot.DefaultPhase2AuthSet);

        if (snapshot.RequireFullAuthSupport == GpoBoolNotConfigured &&
            phase1Methods.Length == 0 &&
            phase2Methods.Length == 0)
        {
            return "Default";
        }

        if (snapshot.RequireFullAuthSupport == GpoBoolFalse &&
            phase1Methods.Length == 0 &&
            phase2Methods.Length == 0)
        {
            return "Computer";
        }

        if (phase1Methods.SequenceEqual(MachineKerberosAuthMethods) &&
            phase2Methods.SequenceEqual(UserKerberosAuthMethods))
        {
            return "Computer and user";
        }

        if (phase1Methods.Length == 0 &&
            phase2Methods.SequenceEqual(UserKerberosAuthMethods))
        {
            return "User";
        }

        return phase1Methods.Length == 0 && phase2Methods.Length == 0
            ? "Default"
            : "Advanced";
    }

    private static FirewallNatTraversalMode ResolveNatTraversalMode(ushort value)
        => value switch
        {
            2 => FirewallNatTraversalMode.Both,
            1 => FirewallNatTraversalMode.Server,
            _ => FirewallNatTraversalMode.None
        };

    private static ushort ResolveNatTraversalValue(FirewallNatTraversalMode mode)
        => mode switch
        {
            FirewallNatTraversalMode.Both => 2,
            FirewallNatTraversalMode.Server => 1,
            _ => 0
        };

    private static uint UpdateExemptionsMask(uint currentMask, bool icmpEnabled)
    {
        if (currentMask == TrafficExemptionNotConfigured)
        {
            currentMask = 0;
        }

        return icmpEnabled
            ? currentMask | TrafficExemptionIcmp
            : currentMask & ~TrafficExemptionIcmp;
    }

    private void ApplyMainModeDefaults(WbemServices session, IpsecDefaultsModel defaults)
    {
        if (string.Equals(defaults.KeyExchangeMode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<MainModeProposalDefinition> proposals = ProposalConverter.BuildMainModeProposalDefinitions(defaults.AdvancedMainModeSecurityMethods);
            if (proposals.Count == 0)
            {
                proposals = AdvancedMainModeProposals;
            }

            WbemObject[] mainModeProposals = CimSetManager.BuildMainModeProposals(session, proposals);
            try
            {
                CimSetManager.UpsertMainModeSet(session, mainModeProposals, defaults);
            }
            finally
            {
                foreach (WbemObject proposal in mainModeProposals)
                {
                    proposal.Dispose();
                }
            }

            return;
        }

        CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEMMCryptoSet", "MSFT|FW|MMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}");
    }

    private void ApplyQuickModeDefaults(WbemServices session, IpsecDefaultsModel defaults)
    {
        if (string.Equals(defaults.DataProtectionMode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<QuickModeProposalDefinition> proposals = ProposalConverter.BuildQuickModeProposalDefinitions(
                defaults.AdvancedIntegrityAlgorithms,
                defaults.AdvancedIntegrityEncryptionAlgorithms);

            if (proposals.Count == 0)
            {
                proposals = AdvancedQuickModeProposals;
            }

            WbemObject[] quickModeProposals = CimSetManager.BuildQuickModeProposals(session, proposals);
            try
            {
                CimSetManager.UpsertSet(
                    session,
                    "MSFT_NetIKEQMCryptoSet",
                    "MSFT|FW|QMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}",
                    quickModeProposals);
            }
            finally
            {
                foreach (WbemObject proposal in quickModeProposals)
                {
                    proposal.Dispose();
                }
            }

            return;
        }

        CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEQMCryptoSet", "MSFT|FW|QMCryptoSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}");
    }

    private void ApplyAuthenticationDefaults(
        WbemServices session,
        WbemObject firewallSetting,
        IpsecDefaultsModel defaults,
        string mode)
    {
        switch (mode)
        {
            case "Computer":
                firewallSetting.SetProperty("RequireFullAuthSupport", GpoBoolFalse, WbemType.UInt16);
                CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}");
                CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}");
                break;

            case "Computer and user":
                firewallSetting.SetProperty("RequireFullAuthSupport", GpoBoolTrue, WbemType.UInt16);
                CimSetManager.UpsertAuthSet(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}", MachineKerberosAuthMethods);
                CimSetManager.UpsertAuthSet(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}", UserKerberosAuthMethods);
                break;

            case "User":
                firewallSetting.SetProperty("RequireFullAuthSupport", GpoBoolTrue, WbemType.UInt16);
                CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}");
                CimSetManager.UpsertAuthSet(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}", UserKerberosAuthMethods);
                break;

            case "Advanced":
                firewallSetting.SetProperty("RequireFullAuthSupport", GpoBoolTrue, WbemType.UInt16);

                WbemObject[] phase1AdvancedProposals = AuthProposalManager.BuildAuthProposals(session, defaults.AdvancedFirstAuthMethods).ToArray();
                WbemObject[] phase2AdvancedProposals = AuthProposalManager.BuildAuthProposals(session, defaults.AdvancedSecondAuthMethods).ToArray();

                try
                {
                    if (phase1AdvancedProposals.Length == 0 && phase2AdvancedProposals.Length == 0)
                    {
                        CimSetManager.UpsertAuthSet(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}", MachineKerberosAndNtlmAuthMethods);
                        CimSetManager.UpsertAuthSet(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}", UserKerberosNtlmAnonymousAuthMethods);
                        break;
                    }

                    if (phase1AdvancedProposals.Length == 0)
                    {
                        CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}");
                    }
                    else
                    {
                        CimSetManager.UpsertSet(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}", phase1AdvancedProposals);
                    }

                    if (phase2AdvancedProposals.Length == 0)
                    {
                        CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}");
                    }
                    else
                    {
                        CimSetManager.UpsertSet(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}", phase2AdvancedProposals);
                    }
                }
                finally
                {
                    foreach (WbemObject proposal in phase1AdvancedProposals)
                    {
                        proposal.Dispose();
                    }

                    foreach (WbemObject proposal in phase2AdvancedProposals)
                    {
                        proposal.Dispose();
                    }
                }

                break;

            default:
                firewallSetting.SetProperty("RequireFullAuthSupport", GpoBoolNotConfigured, WbemType.UInt16);
                CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP1AuthSet", "MSFT|FW|P1AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}");
                CimSetManager.DeleteSetIfExists(session, "MSFT_NetIKEP2AuthSet", "MSFT|FW|P2AuthSet|{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}");
                break;
        }
    }

    private void VerifyIpsecDefaultsApplied(
        IpsecDefaultsModel requested,
        IpsecDefaultsModel actual,
        string requestedAuthenticationMode)
    {
        string requestedKeyExchangeMode = string.Equals(requested.KeyExchangeMode, "Advanced", StringComparison.OrdinalIgnoreCase)
            ? "Advanced"
            : "Default";
        if (!string.Equals(actual.KeyExchangeMode, requestedKeyExchangeMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Windows Firewall reported Key exchange (Main Mode) as '{actual.KeyExchangeMode}' after applying '{requestedKeyExchangeMode}'.");
        }

        string requestedDataProtectionMode = string.Equals(requested.DataProtectionMode, "Advanced", StringComparison.OrdinalIgnoreCase)
            ? "Advanced"
            : "Default";
        if (!string.Equals(actual.DataProtectionMode, requestedDataProtectionMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Windows Firewall reported Data protection (Quick Mode) as '{actual.DataProtectionMode}' after applying '{requestedDataProtectionMode}'.");
        }

        if (!string.Equals(requestedAuthenticationMode, "Advanced", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actual.AuthenticationMethodMode, requestedAuthenticationMode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Windows Firewall reported Authentication method as {ActualMode} after applying {RequestedMode}.",
                actual.AuthenticationMethodMode,
                requestedAuthenticationMode);
        }

        if (actual.NatTraversalMode != requested.NatTraversalMode)
        {
            _logger.LogWarning(
                "Windows Firewall reported NAT traversal as {ActualMode} after applying {RequestedMode}.",
                actual.NatTraversalMode,
                requested.NatTraversalMode);
        }

        if (actual.IcmpExemptionEnabled != requested.IcmpExemptionEnabled)
        {
            _logger.LogWarning(
                "Windows Firewall reported IPsec ICMP exemption as {ActualValue} after applying {RequestedValue}.",
                actual.IcmpExemptionEnabled,
                requested.IcmpExemptionEnabled);
        }
    }

    private static string[] ReadExcludedNetworkConnections(INetFwPolicy2 policy, int profileMask)
    {
        // ExcludedInterfaces is a VARIANT holding a SAFEARRAY of interface aliases (or a single BSTR).
        policy.get_ExcludedInterfaces(profileMask, out Variant excluded);
        try
        {
            List<string> list = excluded.ToStringList();
            if (list.Count > 0)
            {
                return list.ToArray();
            }

            string? single = excluded.ToInvariantString();
            return string.IsNullOrWhiteSpace(single) ? [] : WindowsFirewallSupport.ParseCsv(single);
        }
        finally
        {
            excluded.Clear();
        }
    }

    private IReadOnlyList<NetworkConnectionItem> BuildProtectedNetworkConnections(INetFwPolicy2 policy, int profileMask)
    {
        HashSet<string> excludedAliases = ReadExcludedNetworkConnections(policy, profileMask)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetVisibleNetworkConnectionAliases()
            .Select(alias => new NetworkConnectionItem
            {
                Name = alias,
                IsSelected = !excludedAliases.Contains(alias)
            })
            .ToList();
    }

    private IReadOnlyList<string> GetVisibleNetworkConnectionAliases()
    {
        try
        {
            using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
            List<string> aliases = [];
            foreach (WbemObject instance in session.EnumerateInstances("MSFT_NetAdapter"))
            {
                using (instance)
                {
                    string? name = instance.GetValue("Name")?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        aliases.Add(name.Trim());
                    }
                }
            }

            return aliases
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate visible network adapters for protected network connections.");
            return [];
        }
    }

    private void UpdateProtectedNetworkConnections(FirewallProfileModel profile)
    {
        string[] excludedAliases = profile.ProtectedNetworkConnections
            .Where(connection => !connection.IsSelected)
            .Select(connection => connection.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using WbemServices session = WbemServices.Connect(WindowsFirewallSupport.StandardCimNamespace);
        using WbemObject cimProfile = CimSetManager.GetFirewallProfileInstance(session, profile.ProfileType);

        try
        {
            cimProfile.SetProperty("DisabledInterfaceAliases", excludedAliases, WbemType.StringArray);
            session.ModifyInstance(cimProfile);
        }
        catch (COMException ex)
        {
            string[] knownAliases = GetVisibleNetworkConnectionAliases()
                .ToArray();

            string[] filteredAliases = excludedAliases
                .Where(alias => knownAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (filteredAliases.Length == excludedAliases.Length)
            {
                throw;
            }

            _logger.LogWarning(
                ex,
                "Falling back to filtered excluded interface aliases for {ProfileType}. Requested={RequestedCount}, Applied={AppliedCount}.",
                profile.ProfileType,
                excludedAliases.Length,
                filteredAliases.Length);

            cimProfile.SetProperty("DisabledInterfaceAliases", filteredAliases, WbemType.StringArray);
            session.ModifyInstance(cimProfile);
        }
    }
}
