using System.Collections.ObjectModel;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;

namespace ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;

public enum ConnectionSecurityRuleKind
{
    Isolation,
    AuthenticationExemption,
    ServerToServer,
    Tunnel,
    Custom
}

public enum ConnectionSecurityRequirement
{
    None = 0,
    Request = 1,
    Require = 2
}

public enum ConnectionSecurityMode
{
    Transport = 1,
    Tunnel = 2
}

public sealed class ConnectionSecurityRuleModel
{
    public string Name { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public ConnectionSecurityRuleKind Kind { get; set; }

    public bool ProfileDomain { get; set; } = true;

    public bool ProfilePrivate { get; set; } = true;

    public bool ProfilePublic { get; set; } = true;

    public int ProfilesMask { get; set; }

    public string ProfileDisplay { get; set; } = "All";

    public ConnectionSecurityRequirement InboundSecurity { get; set; } = ConnectionSecurityRequirement.Require;

    public ConnectionSecurityRequirement OutboundSecurity { get; set; } = ConnectionSecurityRequirement.Request;

    public ConnectionSecurityMode Mode { get; set; } = ConnectionSecurityMode.Transport;

    public string Endpoint1Expression { get; set; } = "Any";

    public string Endpoint2Expression { get; set; } = "Any";

    public string Protocol { get; set; } = "Any";

    public string LocalPort { get; set; } = "Any";

    public string RemotePort { get; set; } = "Any";

    public string InterfaceTypes { get; set; } = "Any";

    public string Phase1AuthSet { get; set; } = string.Empty;

    public string Phase2AuthSet { get; set; } = string.Empty;

    public string MainModeCryptoSet { get; set; } = string.Empty;

    public string QuickModeCryptoSet { get; set; } = string.Empty;

    public string KeyModule { get; set; } = string.Empty;

    public bool AllowSetKey { get; set; }

    public bool AllowWatchKey { get; set; }

    public bool BypassTunnelIfEncrypted { get; set; }

    public bool RequireAuthorization { get; set; }

    public string LocalTunnelEndpoint { get; set; } = string.Empty;

    public string RemoteTunnelEndpoint { get; set; } = string.Empty;

    public string RemoteTunnelEndpointDnsName { get; set; } = string.Empty;

    public string TunnelType { get; set; } = string.Empty;

    public string Machines { get; set; } = string.Empty;

    public string Users { get; set; } = string.Empty;

    public string RuleGroup { get; set; } = string.Empty;

    public string DisplayGroup { get; set; } = string.Empty;

    public string PolicyStoreSource { get; set; } = string.Empty;

    public ObservableCollection<AuthMethodListItem> FirstAuthMethods { get; } = [];

    public ObservableCollection<AuthMethodListItem> SecondAuthMethods { get; } = [];

    public bool IsFirstAuthOptional { get; set; }

    public bool IsSecondAuthOptional { get; set; }

    public string Summary { get; set; } = string.Empty;
}


