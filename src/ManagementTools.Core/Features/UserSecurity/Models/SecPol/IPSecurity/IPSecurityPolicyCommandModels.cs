namespace ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;

/// <summary>
/// Options used to create or modify a legacy static IPsec policy.
/// </summary>
public sealed class IPSecurityPolicyCommandOptions
{
    /// <summary>The existing policy name.</summary>
    public required string Name { get; init; }

    /// <summary>The new policy name, or <see langword="null"/> to keep the current name.</summary>
    public string? NewName { get; init; }

    /// <summary>The policy description, or <see langword="null"/> to leave it unspecified.</summary>
    public string? Description { get; init; }

    /// <summary>Whether master key perfect forward secrecy is enabled.</summary>
    public bool? UseMasterPerfectForwardSecrecy { get; init; }

    /// <summary>The number of quick-mode sessions per main-mode session.</summary>
    public int? QuickModeSessionsPerMainMode { get; init; }

    /// <summary>The main-mode lifetime in minutes.</summary>
    public int? MainModeLifetimeMinutes { get; init; }

    /// <summary>Whether the legacy default response rule is active.</summary>
    public bool? IsDefaultResponseRuleActive { get; init; }

    /// <summary>The policy-store polling interval in minutes.</summary>
    public int? PollingIntervalMinutes { get; init; }

    /// <summary>Whether the policy is assigned.</summary>
    public bool? IsAssigned { get; init; }

    /// <summary>The main-mode security methods in preference order.</summary>
    public IReadOnlyList<string>? MainModeSecurityMethods { get; init; }
}

/// <summary>
/// Options used to create or modify a legacy static IPsec filter list.
/// </summary>
public sealed class IPSecurityFilterListCommandOptions
{
    /// <summary>The existing filter-list name.</summary>
    public required string Name { get; init; }

    /// <summary>The new filter-list name, or <see langword="null"/> to keep the current name.</summary>
    public string? NewName { get; init; }

    /// <summary>The filter-list description, or <see langword="null"/> to leave it unspecified.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Options that identify a filter when adding it or deleting its exact match.
/// </summary>
public sealed class IPSecurityFilterCommandOptions
{
    /// <summary>The owning filter-list name.</summary>
    public required string FilterListName { get; init; }

    /// <summary>The source address expression.</summary>
    public required string SourceAddress { get; init; }

    /// <summary>The destination address expression.</summary>
    public required string DestinationAddress { get; init; }

    /// <summary>The filter description, or <see langword="null"/> to leave it unspecified.</summary>
    public string? Description { get; init; }

    /// <summary>The protocol name or number, or <see langword="null"/> to use the netsh default.</summary>
    public string? Protocol { get; init; }

    /// <summary>Whether the filter is mirrored, or <see langword="null"/> to use the netsh default.</summary>
    public bool? IsMirrored { get; init; }

    /// <summary>The source address mask or prefix, or <see langword="null"/> when not specified.</summary>
    public string? SourceMask { get; init; }

    /// <summary>The destination address mask or prefix, or <see langword="null"/> when not specified.</summary>
    public string? DestinationMask { get; init; }

    /// <summary>The source port, or <see langword="null"/> to use the netsh default.</summary>
    public int? SourcePort { get; init; }

    /// <summary>The destination port, or <see langword="null"/> to use the netsh default.</summary>
    public int? DestinationPort { get; init; }
}

/// <summary>
/// Options used to create or modify a legacy static IPsec filter action.
/// </summary>
public sealed class IPSecurityFilterActionCommandOptions
{
    /// <summary>The existing filter-action name.</summary>
    public required string Name { get; init; }

    /// <summary>The new filter-action name, or <see langword="null"/> to keep the current name.</summary>
    public string? NewName { get; init; }

    /// <summary>The filter-action description, or <see langword="null"/> to leave it unspecified.</summary>
    public string? Description { get; init; }

    /// <summary>Whether quick-mode perfect forward secrecy is enabled.</summary>
    public bool? UseQuickModePerfectForwardSecrecy { get; init; }

    /// <summary>Whether unsecured inbound traffic is accepted before an IPsec response.</summary>
    public bool? AcceptUnsecuredInbound { get; init; }

    /// <summary>Whether communication may fall back to unsecured traffic.</summary>
    public bool? AllowUnsecuredFallback { get; init; }

    /// <summary>The filter-action behavior, or <see langword="null"/> to leave it unspecified.</summary>
    public IPSecurityFilterActionKind? Action { get; init; }

    /// <summary>The quick-mode security methods in preference order.</summary>
    public IReadOnlyList<string>? QuickModeSecurityMethods { get; init; }
}

/// <summary>
/// Options used to create or modify a rule in a legacy static IPsec policy.
/// </summary>
public sealed class IPSecurityRuleCommandOptions
{
    /// <summary>The existing rule name.</summary>
    public required string Name { get; init; }

    /// <summary>The owning policy name.</summary>
    public required string PolicyName { get; init; }

    /// <summary>The new rule name, or <see langword="null"/> to keep the current name.</summary>
    public string? NewName { get; init; }

    /// <summary>The rule description, or <see langword="null"/> to leave it unspecified.</summary>
    public string? Description { get; init; }

    /// <summary>The referenced filter-list name, or <see langword="null"/> to leave it unspecified.</summary>
    public string? FilterListName { get; init; }

    /// <summary>The referenced filter-action name, or <see langword="null"/> to leave it unspecified.</summary>
    public string? FilterActionName { get; init; }

    /// <summary>The tunnel endpoint, or <see langword="null"/> for no create-time tunnel or no update.</summary>
    public string? TunnelEndpoint { get; init; }

    /// <summary>The connection type, or <see langword="null"/> to leave it unspecified.</summary>
    public IPSecurityRuleConnectionType? ConnectionType { get; init; }

    /// <summary>Whether the rule is active, or <see langword="null"/> to leave it unspecified.</summary>
    public bool? IsActive { get; init; }

    /// <summary>
    /// The authentication methods in preference order, or <see langword="null"/> to omit authentication tokens.
    /// </summary>
    public IReadOnlyList<IPSecurityRuleAuthenticationCommand>? AuthenticationMethods { get; init; }
}

/// <summary>
/// Connection types supported by legacy static IPsec rules.
/// </summary>
public enum IPSecurityRuleConnectionType
{
    /// <summary>All connections.</summary>
    All,

    /// <summary>Local-area network connections.</summary>
    Lan,

    /// <summary>Dial-up connections.</summary>
    DialUp
}

/// <summary>
/// Base type for an authentication method supplied to a legacy static IPsec rule command.
/// </summary>
public abstract class IPSecurityRuleAuthenticationCommand
{
}

/// <summary>
/// Requests Kerberos authentication for a legacy static IPsec rule.
/// </summary>
public sealed class IPSecurityKerberosAuthenticationCommand : IPSecurityRuleAuthenticationCommand
{
}

/// <summary>
/// Requests certificate-authority authentication for a legacy static IPsec rule.
/// </summary>
public sealed class IPSecurityCertificateAuthenticationCommand : IPSecurityRuleAuthenticationCommand
{
    /// <summary>The root certificate-authority name.</summary>
    public required string CertificateAuthorityName { get; init; }

    /// <summary>Whether certificate-to-account mapping is requested.</summary>
    public bool EnableCertificateToAccountMapping { get; init; }

    /// <summary>Whether the CA name is excluded from certificate requests.</summary>
    public bool ExcludeCertificateAuthorityName { get; init; }
}

/// <summary>
/// Requests pre-shared-key authentication without publicly exposing the key through a property or string formatting.
/// </summary>
public sealed class IPSecurityPreSharedKeyAuthenticationCommand : IPSecurityRuleAuthenticationCommand
{
    private readonly string _preSharedKey;

    /// <summary>
    /// Initializes a new pre-shared-key authentication request.
    /// </summary>
    /// <param name="preSharedKey">The pre-shared key. It is retained only for command token construction.</param>
    public IPSecurityPreSharedKeyAuthenticationCommand(string preSharedKey)
    {
        ArgumentNullException.ThrowIfNull(preSharedKey);
        _preSharedKey = preSharedKey;
    }

    internal string GetPreSharedKey()
    {
        return _preSharedKey;
    }

    /// <summary>
    /// Returns a non-sensitive representation of this authentication request.
    /// </summary>
    /// <returns>The authentication request type name without the key value.</returns>
    public override string ToString()
    {
        return nameof(IPSecurityPreSharedKeyAuthenticationCommand);
    }
}
