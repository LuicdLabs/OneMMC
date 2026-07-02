using System;
using System.Collections.Generic;
using Microsoft.Management.Infrastructure;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

internal sealed partial record FirewallSettingSnapshot(
    ushort AllowIpsecThroughNat,
    uint Exemptions,
    string RemoteMachineTransportAuthorizationList,
    string RemoteMachineTunnelAuthorizationList,
    string RemoteUserTransportAuthorizationList,
    string RemoteUserTunnelAuthorizationList,
    ushort RequireFullAuthSupport,
    IReadOnlyList<MainModeProposalDefinition> MainModeProposals,
    CimInstance? DefaultMainModeSet,
    CimInstance? DefaultQuickModeSet,
    CimInstance? DefaultPhase1AuthSet,
    CimInstance? DefaultPhase2AuthSet) : IDisposable
{
    public bool HasTransportAuthorizations =>
        IsConfigured(RemoteMachineTransportAuthorizationList) || IsConfigured(RemoteUserTransportAuthorizationList);

    public bool HasTunnelAuthorizations =>
        IsConfigured(RemoteMachineTunnelAuthorizationList) || IsConfigured(RemoteUserTunnelAuthorizationList);

    private static bool IsConfigured(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(value, "NotConfigured", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        DefaultMainModeSet?.Dispose();
        DefaultQuickModeSet?.Dispose();
        DefaultPhase1AuthSet?.Dispose();
        DefaultPhase2AuthSet?.Dispose();
    }
}

internal readonly record struct MainModeProposalDefinition(ushort CipherAlgorithm, ushort HashAlgorithm, ushort GroupId);

internal readonly record struct QuickModeProposalDefinition(
    ushort Encapsulation,
    ushort? HashAlgorithmAh,
    ushort? HashAlgorithmEsp,
    ushort? CipherAlgorithm,
    uint MaxLifetimeMinutes,
    ulong MaxLifetimeKilobytes);
