using CommunityToolkit.Mvvm.ComponentModel;
using ManagementTools.Core.Features.UserSecurity.Models.NetworkListManager;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.UserSecurity.ViewModels.NetworkListManager;

/// <summary>
/// Observable wrapper used by the Network List Manager page.
/// </summary>
public sealed partial class NetworkListPolicyNodeViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkListPolicyNodeViewModel"/> class.
    /// </summary>
    /// <param name="node">The source node.</param>
    public NetworkListPolicyNodeViewModel(NetworkListPolicyNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        DisplayName = node.DisplayName;
        Description = node.Description;
        SignatureId = node.SignatureId;
        Kind = node.Kind;
        HasCustomName = node.State.HasCustomName;
        NetworkName = node.State.NetworkName;
        IconPayload = node.State.IconPayload;
        NamePermissionIndex = ToPermissionIndex(node.State.NamePermission);
        IconPermissionIndex = ToPermissionIndex(node.State.IconPermission);
        LocationTypeIndex = ToLocationIndex(node.State.LocationType);
        LocationPermissionIndex = ToPermissionIndex(node.State.LocationPermission);
    }

    /// <summary>
    /// Gets the header text.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the header description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the backing signature identifier.
    /// </summary>
    public string SignatureId { get; }

    /// <summary>
    /// Gets the node kind.
    /// </summary>
    public NetworkListPolicyNodeKind Kind { get; }

    /// <summary>
    /// Gets whether a custom network name is currently configured.
    /// </summary>
    public bool HasCustomName { get; }

    /// <summary>
    /// Gets the configured network name.
    /// </summary>
    public string NetworkName { get; }

    /// <summary>
    /// Gets the configured icon payload.
    /// </summary>
    public NetworkListIconPayload? IconPayload { get; }

    /// <summary>
    /// Gets whether a custom icon is configured.
    /// </summary>
    public bool HasCustomIcon => IconPayload?.IsConfigured == true;

    /// <summary>
    /// Gets the glyph used by the expander header.
    /// </summary>
    public string HeaderGlyph => Kind switch
    {
        NetworkListPolicyNodeKind.IdentifiedNetwork => "\uE968",
        NetworkListPolicyNodeKind.UnidentifiedNetworks => "\uE897",
        NetworkListPolicyNodeKind.IdentifyingNetworks => "\uE823",
        NetworkListPolicyNodeKind.AllNetworks => "\uE930",
        _ => "\uE968"
    };

    /// <summary>
    /// Gets whether this is an identified network node.
    /// </summary>
    public bool IsIdentifiedNetwork => Kind == NetworkListPolicyNodeKind.IdentifiedNetwork;

    /// <summary>
    /// Gets whether this is the unidentified networks node.
    /// </summary>
    public bool IsUnidentifiedNetworks => Kind == NetworkListPolicyNodeKind.UnidentifiedNetworks;

    /// <summary>
    /// Gets whether this is the identifying networks node.
    /// </summary>
    public bool IsIdentifyingNetworks => Kind == NetworkListPolicyNodeKind.IdentifyingNetworks;

    /// <summary>
    /// Gets whether this is the all networks node.
    /// </summary>
    public bool IsAllNetworks => Kind == NetworkListPolicyNodeKind.AllNetworks;

    /// <summary>
    /// Gets whether the node exposes a location type combo box.
    /// </summary>
    public bool ShowsLocationType => Kind != NetworkListPolicyNodeKind.AllNetworks;

    /// <summary>
    /// Gets whether the node exposes a location permission combo box.
    /// </summary>
    public bool ShowsLocationPermission =>
        Kind == NetworkListPolicyNodeKind.IdentifiedNetwork
        || Kind == NetworkListPolicyNodeKind.UnidentifiedNetworks;

    /// <summary>
    /// Gets the display text for the current network name state.
    /// </summary>
    public string NetworkNameSummary => HasCustomName
        ? NetworkName
        : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.StateNotConfigured);

    /// <summary>
    /// Gets the display text for the current icon state.
    /// </summary>
    public string NetworkIconSummary => HasCustomIcon
        ? LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.IconConfigured)
        : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.StateNotConfigured);

    /// <summary>
    /// Gets or sets the selected index for the name permission combo box.
    /// </summary>
    [ObservableProperty]
    public partial int NamePermissionIndex { get; set; }

    /// <summary>
    /// Gets or sets the selected index for the icon permission combo box.
    /// </summary>
    [ObservableProperty]
    public partial int IconPermissionIndex { get; set; }

    /// <summary>
    /// Gets or sets the selected index for the location type combo box.
    /// </summary>
    [ObservableProperty]
    public partial int LocationTypeIndex { get; set; }

    /// <summary>
    /// Gets or sets the selected index for the location permission combo box.
    /// </summary>
    [ObservableProperty]
    public partial int LocationPermissionIndex { get; set; }

    /// <summary>
    /// Gets whether this node matches the supplied filter text.
    /// </summary>
    /// <param name="filterText">The current filter text.</param>
    /// <returns><see langword="true"/> when the node should remain visible.</returns>
    public bool MatchesFilter(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || NetworkName.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private static int ToPermissionIndex(NetworkListPermissionMode mode) => mode switch
    {
        NetworkListPermissionMode.Allow => 1,
        NetworkListPermissionMode.Deny => 2,
        _ => 0
    };

    private static int ToLocationIndex(NetworkListLocationMode mode) => mode switch
    {
        NetworkListLocationMode.Private => 1,
        NetworkListLocationMode.Public => 2,
        _ => 0
    };
}
