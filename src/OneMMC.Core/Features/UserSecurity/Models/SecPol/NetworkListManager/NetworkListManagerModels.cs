using System.Diagnostics.CodeAnalysis;

namespace OneMMC.Core.Features.UserSecurity.Models.SecPol.NetworkListManager;

/// <summary>
/// Identifies the node shape used by Network List Manager Policies.
/// </summary>
public enum NetworkListPolicyNodeKind
{
    /// <summary>
    /// A live identified network backed by a signature key.
    /// </summary>
    IdentifiedNetwork,

    /// <summary>
    /// The synthetic "Unidentified Networks" policy node.
    /// </summary>
    UnidentifiedNetworks,

    /// <summary>
    /// The synthetic "Identifying Networks" policy node.
    /// </summary>
    IdentifyingNetworks,

    /// <summary>
    /// The synthetic "All Networks" policy node.
    /// </summary>
    AllNetworks
}

/// <summary>
/// Tri-state permission mode used by Network List Manager policies.
/// </summary>
public enum NetworkListPermissionMode
{
    /// <summary>
    /// The policy value is absent.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// The user can change the setting.
    /// </summary>
    Allow,

    /// <summary>
    /// The user cannot change the setting.
    /// </summary>
    Deny
}

/// <summary>
/// Location type mode used by Network List Manager policies.
/// </summary>
public enum NetworkListLocationMode
{
    /// <summary>
    /// The policy value is absent.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// Private network location.
    /// </summary>
    Private,

    /// <summary>
    /// Public network location.
    /// </summary>
    Public
}

/// <summary>
/// Holds the four serialized icon payload strings used by Network List Manager.
/// </summary>
public sealed class NetworkListIconPayload
{
    /// <summary>
    /// Gets or sets the serialized 16x16 icon payload.
    /// </summary>
    public string Icon16Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 24x24 icon payload.
    /// </summary>
    public string Icon24Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 32x32 icon payload.
    /// </summary>
    public string Icon32Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized 48x48 icon payload.
    /// </summary>
    public string Icon48Hex { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether all four icon payload strings are present.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Icon16Hex), nameof(Icon24Hex), nameof(Icon32Hex), nameof(Icon48Hex))]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Icon16Hex)
        && !string.IsNullOrWhiteSpace(Icon24Hex)
        && !string.IsNullOrWhiteSpace(Icon32Hex)
        && !string.IsNullOrWhiteSpace(Icon48Hex);
}

/// <summary>
/// Represents the editable policy state for a single node.
/// </summary>
public sealed class NetworkListPolicyState
{
    /// <summary>
    /// Gets or sets whether a custom network name is configured.
    /// </summary>
    public bool HasCustomName { get; set; }

    /// <summary>
    /// Gets or sets the configured network name.
    /// </summary>
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configured icon payload.
    /// </summary>
    public NetworkListIconPayload? IconPayload { get; set; }

    /// <summary>
    /// Gets or sets the permission for the network name.
    /// </summary>
    public NetworkListPermissionMode NamePermission { get; set; }

    /// <summary>
    /// Gets or sets the permission for the icon.
    /// </summary>
    public NetworkListPermissionMode IconPermission { get; set; }

    /// <summary>
    /// Gets or sets the configured location type.
    /// </summary>
    public NetworkListLocationMode LocationType { get; set; }

    /// <summary>
    /// Gets or sets the permission for the location type.
    /// </summary>
    public NetworkListPermissionMode LocationPermission { get; set; }
}

/// <summary>
/// Represents a single node displayed on the Network List Manager page.
/// </summary>
public sealed class NetworkListPolicyNode
{
    /// <summary>
    /// Gets or sets the display name shown in the expander header.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the descriptive text shown under the header.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy signature identifier.
    /// </summary>
    public string SignatureId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the node kind.
    /// </summary>
    public NetworkListPolicyNodeKind Kind { get; set; }

    /// <summary>
    /// Gets or sets whether this identified network is domain-authenticated.
    /// </summary>
    /// <remarks>
    /// NLA always assigns the Domain location type to such a network, so its location type and location
    /// user permissions are not configurable; secpol.msc omits the Network Location tab for them.
    /// Always <see langword="false"/> for the synthetic nodes.
    /// </remarks>
    public bool IsDomainAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the current editable state.
    /// </summary>
    public NetworkListPolicyState State { get; set; } = new();
}
