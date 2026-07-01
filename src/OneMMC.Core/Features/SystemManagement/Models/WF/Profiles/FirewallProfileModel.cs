using System.Collections.Generic;
using System.Collections.ObjectModel;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;

namespace OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;

public enum FirewallProfileType
{
    Domain = 1,
    Private = 2,
    Public = 4
}

public enum FirewallDefaultAction
{
    Block = 0,
    Allow = 1
}

public enum TunnelAuthorizationMode
{
    None,
    Advanced,
    Custom
}

public enum FirewallNatTraversalMode
{
    None,
    Server,
    Both
}

public sealed class FirewallProfileModel
{
    public FirewallProfileType ProfileType { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool IsEnabled { get; set; }

    public FirewallDefaultAction DefaultInboundAction { get; set; } = FirewallDefaultAction.Block;

    public FirewallDefaultAction DefaultOutboundAction { get; set; } = FirewallDefaultAction.Allow;

    public bool BlockAllInboundTraffic { get; set; }

    public bool NotificationsDisabled { get; set; }

    public bool UnicastResponsesToMulticastBroadcastDisabled { get; set; }

    public FirewallPolicyModifyState PolicyModifyState { get; set; } = FirewallPolicyModifyState.Ok;

    public ObservableCollection<NetworkConnectionItem> ProtectedNetworkConnections { get; } = [];

    public FirewallLoggingSettings LoggingSettings { get; set; } = new();
}

public sealed class FirewallLoggingSettings
{
    public bool LogDroppedPackets { get; set; }

    public bool LogSuccessfulConnections { get; set; }

    public string FileName { get; set; } = "%systemroot%\\system32\\logfiles\\firewall\\pfirewall.log";

    public int MaxFileSizeKilobytes { get; set; } = 4096;
}

public sealed class IpsecDefaultsModel
{
    public string KeyExchangeMode { get; set; } = "Default";

    public string DataProtectionMode { get; set; } = "Default";

    public string AuthenticationMethodMode { get; set; } = "Default";

    public string MainModeCryptoSet { get; set; } = string.Empty;

    public string QuickModeCryptoSet { get; set; } = string.Empty;

    public string Phase1AuthSet { get; set; } = string.Empty;

    public string Phase2AuthSet { get; set; } = string.Empty;

    public List<SecurityMethodEntry> AdvancedMainModeSecurityMethods { get; set; } = [];

    public List<DataIntegrityAlgorithmEntry> AdvancedIntegrityAlgorithms { get; set; } = [];

    public List<IntegrityEncryptionAlgorithmEntry> AdvancedIntegrityEncryptionAlgorithms { get; set; } = [];

    public List<AuthMethodDialogResult> AdvancedFirstAuthMethods { get; set; } = [];

    public List<AuthMethodDialogResult> AdvancedSecondAuthMethods { get; set; } = [];

    public bool IsAdvancedFirstAuthOptional { get; set; }

    public bool IsAdvancedSecondAuthOptional { get; set; }

    public int MainModeKeyLifetimeMinutes { get; set; } = 480;

    public int MainModeKeyLifetimeSessions { get; set; }

    public bool MainModeForceDiffieHellman { get; set; }

    public bool IcmpExemptionEnabled { get; set; }

    public FirewallNatTraversalMode NatTraversalMode { get; set; } = FirewallNatTraversalMode.None;

    public bool NatTraversalEnabled
    {
        get => NatTraversalMode != FirewallNatTraversalMode.None;
        set => NatTraversalMode = value ? FirewallNatTraversalMode.Both : FirewallNatTraversalMode.None;
    }
}

public sealed class FirewallTunnelAuthorizationSettings
{
    public TunnelAuthorizationMode Mode { get; set; }

    public ObservableCollection<TunnelAuthorizationItem> AllowedComputers { get; } = [];

    public ObservableCollection<TunnelAuthorizationItem> DeniedComputers { get; } = [];

    public ObservableCollection<TunnelAuthorizationItem> AllowedUsers { get; } = [];

    public ObservableCollection<TunnelAuthorizationItem> DeniedUsers { get; } = [];
}


