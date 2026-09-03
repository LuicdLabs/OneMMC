namespace OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;

/// <summary>
/// Row categories shown on the IP Security Policies page.
/// </summary>
public enum IPSecurityPolicyRowKind
{
    /// <summary>A static IP Security Policy.</summary>
    Policy,

    /// <summary>A shared IP filter list.</summary>
    FilterList,

    /// <summary>A shared IP filter action.</summary>
    FilterAction
}

/// <summary>
/// A policy or status row shown in the IP Security Policies details pane.
/// </summary>
public sealed class IPSecurityPolicyRow
{
    /// <summary>The row kind.</summary>
    public IPSecurityPolicyRowKind Kind { get; init; } = IPSecurityPolicyRowKind.Policy;

    /// <summary>The policy or status name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The policy description or status details.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The assignment, enabled, or capability state.</summary>
    public string PolicyAssigned { get; init; } = string.Empty;

    /// <summary>The last modified time text, when available.</summary>
    public string LastModified { get; init; } = string.Empty;

    /// <summary>The source store or provider.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Additional searchable summary text.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Read-only details for this policy or diagnostic row.</summary>
    public IReadOnlyList<IPSecurityPolicyDetailItem> Details { get; init; } = [];

    /// <summary>The underlying policy definition when <see cref="Kind"/> is <see cref="IPSecurityPolicyRowKind.Policy"/>.</summary>
    public IPSecurityPolicyDefinition? Policy { get; init; }

    /// <summary>The underlying filter-list definition when <see cref="Kind"/> is <see cref="IPSecurityPolicyRowKind.FilterList"/>.</summary>
    public IPSecurityFilterListDefinition? FilterList { get; init; }

    /// <summary>The underlying filter-action definition when <see cref="Kind"/> is <see cref="IPSecurityPolicyRowKind.FilterAction"/>.</summary>
    public IPSecurityFilterActionDefinition? FilterAction { get; init; }

    /// <summary>Gets a value indicating whether this row exposes read-only details.</summary>
    public bool CanViewDetails => Details.Count > 0;
}

/// <summary>
/// A name/value detail shown for an IP Security Policies row.
/// </summary>
public sealed class IPSecurityPolicyDetailItem
{
    /// <summary>The localized detail name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The detail value.</summary>
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Loaded IP Security Policies state.
/// </summary>
public sealed class IPSecurityPolicySnapshot
{
    /// <summary>Policy rows shown by the page.</summary>
    public IReadOnlyList<IPSecurityPolicyRow> Policies { get; init; } = [];

    /// <summary>Filter list rows shown by the page.</summary>
    public IReadOnlyList<IPSecurityPolicyRow> FilterLists { get; init; } = [];

    /// <summary>Filter action rows shown by the page.</summary>
    public IReadOnlyList<IPSecurityPolicyRow> FilterActions { get; init; } = [];

    /// <summary>Indicates whether one or more sources could not be read without administrator privileges.</summary>
    public bool RequiresAdministrator { get; init; }
}

/// <summary>
/// A typed snapshot of the legacy static local IPsec policy store.
/// </summary>
public sealed class IPSecurityStaticStoreSnapshot
{
    /// <summary>The policies in the store.</summary>
    public IReadOnlyList<IPSecurityPolicyDefinition> Policies { get; init; } = [];

    /// <summary>The shared filter lists in the store.</summary>
    public IReadOnlyList<IPSecurityFilterListDefinition> FilterLists { get; init; } = [];

    /// <summary>The shared filter actions in the store.</summary>
    public IReadOnlyList<IPSecurityFilterActionDefinition> FilterActions { get; init; } = [];
}

/// <summary>
/// A legacy static IP Security Policy.
/// </summary>
public sealed class IPSecurityPolicyDefinition
{
    /// <summary>The policy name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The policy description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Indicates whether the policy is assigned to the local computer.</summary>
    public bool IsAssigned { get; init; }

    /// <summary>Indicates whether master key perfect forward secrecy is enabled.</summary>
    public bool UseMasterPerfectForwardSecrecy { get; init; }

    /// <summary>The number of quick-mode sessions per main-mode session.</summary>
    public int QuickModeSessionsPerMainMode { get; init; }

    /// <summary>The main-mode lifetime in minutes.</summary>
    public int MainModeLifetimeMinutes { get; init; }

    /// <summary>Indicates whether the legacy default response rule is enabled.</summary>
    public bool IsDefaultResponseRuleActive { get; init; }

    /// <summary>The policy store polling interval in minutes.</summary>
    public int PollingIntervalMinutes { get; init; }

    /// <summary>The main-mode security methods in preference order.</summary>
    public IReadOnlyList<string> MainModeSecurityMethods { get; init; } = [];

    /// <summary>The rules owned by this policy.</summary>
    public IReadOnlyList<IPSecurityRuleDefinition> Rules { get; init; } = [];

    /// <summary>
    /// The time the policy object was last modified, read from the registry store's
    /// <c>whenChanged</c> value. <see langword="null"/> when unavailable (for example, when the
    /// data comes only from the netsh-style export, which carries no last-modified field).
    /// </summary>
    public DateTimeOffset? LastModifiedTime { get; init; }
}

/// <summary>
/// A security rule owned by a legacy static IP Security Policy.
/// </summary>
public sealed class IPSecurityRuleDefinition
{
    /// <summary>The native NFA identifier used for unnamed rules such as default response.</summary>
    public Guid Identifier { get; init; }

    /// <summary>Indicates whether this is the policy's dynamic default response rule.</summary>
    public bool IsDefaultResponseRule { get; init; }

    /// <summary>The rule name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The owning policy name.</summary>
    public string PolicyName { get; init; } = string.Empty;

    /// <summary>The rule description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The referenced filter list name.</summary>
    public string FilterListName { get; init; } = string.Empty;

    /// <summary>The referenced filter action name.</summary>
    public string FilterActionName { get; init; } = string.Empty;

    /// <summary>
    /// The resolved filter action, including unnamed rule-owned negotiation policies.
    /// </summary>
    public IPSecurityFilterActionDefinition? FilterAction { get; init; }

    /// <summary>The optional tunnel endpoint.</summary>
    public string TunnelEndpoint { get; init; } = string.Empty;

    /// <summary>The raw connection type value used by the static policy store.</summary>
    public string ConnectionType { get; init; } = string.Empty;

    /// <summary>Indicates whether the rule is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>The authentication methods in preference order.</summary>
    public IReadOnlyList<IPSecurityAuthenticationMethodDefinition> AuthenticationMethods { get; init; } = [];
}

/// <summary>
/// An authentication method used by an IP Security rule.
/// </summary>
public sealed class IPSecurityAuthenticationMethodDefinition
{
    /// <summary>The authentication method kind.</summary>
    public required IPSecurityAuthenticationMethodKind Kind { get; init; }

    /// <summary>The method-specific detail, such as a CA name or pre-shared key placeholder.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Indicates whether certificate-to-account mapping is requested.</summary>
    public bool EnableCertificateToAccountMapping { get; init; }

    /// <summary>Indicates whether the CA name is excluded from certificate requests.</summary>
    public bool ExcludeCertificateAuthorityName { get; init; }
}

/// <summary>
/// Authentication methods supported by the legacy static IPsec store.
/// </summary>
public enum IPSecurityAuthenticationMethodKind
{
    /// <summary>Active Directory default Kerberos V5 authentication.</summary>
    Kerberos,

    /// <summary>Certificate authority authentication.</summary>
    CertificateAuthority,

    /// <summary>Pre-shared key authentication.</summary>
    PreSharedKey
}

/// <summary>
/// A shared IP filter list.
/// </summary>
public sealed class IPSecurityFilterListDefinition
{
    /// <summary>The filter list name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The filter list description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The filters contained by the list.</summary>
    public IReadOnlyList<IPSecurityFilterDefinition> Filters { get; init; } = [];
}

/// <summary>
/// An IP traffic filter contained by a shared filter list.
/// </summary>
public sealed class IPSecurityFilterDefinition
{
    /// <summary>The owning filter list name.</summary>
    public string FilterListName { get; init; } = string.Empty;

    /// <summary>The filter description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The raw source address expression.</summary>
    public string SourceAddress { get; init; } = string.Empty;

    /// <summary>The raw source mask or prefix.</summary>
    public string SourceMask { get; init; } = string.Empty;

    /// <summary>The raw destination address expression.</summary>
    public string DestinationAddress { get; init; } = string.Empty;

    /// <summary>The raw destination mask or prefix.</summary>
    public string DestinationMask { get; init; } = string.Empty;

    /// <summary>The protocol name or number.</summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>The source port, or zero for any port.</summary>
    public int SourcePort { get; init; }

    /// <summary>The destination port, or zero for any port.</summary>
    public int DestinationPort { get; init; }

    /// <summary>Indicates whether a mirrored filter is generated.</summary>
    public bool IsMirrored { get; init; }
}

/// <summary>
/// A shared IP filter action.
/// </summary>
public sealed class IPSecurityFilterActionDefinition
{
    /// <summary>The filter action name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The filter action description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The action behavior.</summary>
    public IPSecurityFilterActionKind Action { get; init; }

    /// <summary>Indicates whether quick-mode perfect forward secrecy is enabled.</summary>
    public bool UseQuickModePerfectForwardSecrecy { get; init; }

    /// <summary>Indicates whether unsecured inbound communication is accepted before replying with IPsec.</summary>
    public bool AcceptUnsecuredInbound { get; init; }

    /// <summary>Indicates whether communication may fall back to unsecured traffic.</summary>
    public bool AllowUnsecuredFallback { get; init; }

    /// <summary>The quick-mode security methods in preference order.</summary>
    public IReadOnlyList<string> QuickModeSecurityMethods { get; init; } = [];
}

/// <summary>
/// Filter action behaviors supported by the legacy static IPsec store.
/// </summary>
public enum IPSecurityFilterActionKind
{
    /// <summary>Permit matching traffic.</summary>
    Permit,

    /// <summary>Block matching traffic.</summary>
    Block,

    /// <summary>Negotiate IPsec security for matching traffic.</summary>
    Negotiate
}
