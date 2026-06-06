using System;
using System.Collections.Generic;
using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;

/// <summary>
/// Security levels supported by Software Restriction Policies.
/// </summary>
public enum SoftwareRestrictionSecurityLevel
{
    /// <summary>Software is blocked unless an allow rule applies.</summary>
    Disallowed = 0,

    /// <summary>Software runs as a normal user without elevated privileges.</summary>
    BasicUser = 0x20000,

    /// <summary>Software is allowed unless a deny rule applies.</summary>
    Unrestricted = 0x40000
}

/// <summary>
/// Policy sections displayed in the Software Restriction Policies page.
/// </summary>
public enum SoftwareRestrictionSectionKind
{
    /// <summary>The security levels node.</summary>
    SecurityLevels,

    /// <summary>The additional rules node.</summary>
    AdditionalRules,

    /// <summary>The enforcement settings node.</summary>
    Enforcement,

    /// <summary>The designated file types node.</summary>
    DesignatedFileTypes,

    /// <summary>The trusted publishers node.</summary>
    TrustedPublishers
}

/// <summary>
/// Supported additional rule kinds for Software Restriction Policies.
/// </summary>
public enum SoftwareRestrictionRuleKind
{
    /// <summary>A path or registry path rule.</summary>
    Path,

    /// <summary>A file hash rule.</summary>
    Hash,

    /// <summary>A signing certificate rule.</summary>
    Certificate,

    /// <summary>An Internet Explorer security zone rule.</summary>
    NetworkZone,

    /// <summary>A rule found in policy storage that is not yet fully decoded.</summary>
    Unknown
}

/// <summary>
/// Scope for policy enforcement.
/// </summary>
public enum SoftwareRestrictionUserScope
{
    /// <summary>Apply to all users.</summary>
    AllUsers = 0,

    /// <summary>Apply to all users except local administrators.</summary>
    AllUsersExceptLocalAdministrators = 1
}

/// <summary>
/// Scope for file enforcement.
/// </summary>
public enum SoftwareRestrictionFileScope
{
    /// <summary>Apply to all software files except libraries.</summary>
    ExecutableFilesOnly = 0,

    /// <summary>Apply to all software files, including DLLs.</summary>
    AllSoftwareFiles = 1
}

/// <summary>
/// Trusted publisher management mode.
/// </summary>
public enum SoftwareRestrictionPublisherScope
{
    /// <summary>End users can manage trusted publishers.</summary>
    EndUsers = 0,

    /// <summary>Local computer administrators can manage trusted publishers.</summary>
    LocalAdministrators = 1,

    /// <summary>Enterprise administrators can manage trusted publishers.</summary>
    EnterpriseAdministrators = 2
}

/// <summary>
/// A localized item shown in the Software Restriction Policies navigation tree.
/// </summary>
public sealed class SoftwareRestrictionSectionItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionSectionItem"/> class.
    /// </summary>
    public SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
    }

    /// <summary>The section kind.</summary>
    public SoftwareRestrictionSectionKind Kind { get; }

    /// <summary>The localized display name.</summary>
    public string DisplayName { get; }

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
/// A row shown in the Software Restriction Policies details pane.
/// </summary>
public sealed class SoftwareRestrictionListItem
{
    /// <summary>The primary row name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The secondary row value.</summary>
    public string Setting { get; init; } = string.Empty;

    /// <summary>The additional rule security level text.</summary>
    public string RuleSecurityLevel { get; init; } = string.Empty;

    /// <summary>The additional rule description text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The additional rule last modified text.</summary>
    public string LastModified { get; init; } = string.Empty;

    /// <summary>The backing security level, when this row represents a security level.</summary>
    public SoftwareRestrictionSecurityLevel? SecurityLevel { get; init; }

    /// <summary>The backing rule, when this row represents an additional rule.</summary>
    public SoftwareRestrictionRule? Rule { get; init; }
}

/// <summary>
/// A complete Software Restriction Policies snapshot.
/// </summary>
public sealed class SoftwareRestrictionPolicyState
{
    /// <summary>Whether the SRP policy registry root exists in local policy storage.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>General enforcement settings.</summary>
    public SoftwareRestrictionEnforcementSettings Enforcement { get; set; } = new();

    /// <summary>Trusted publisher settings.</summary>
    public SoftwareRestrictionTrustedPublisherSettings TrustedPublishers { get; set; } = new();

    /// <summary>Designated executable file extensions.</summary>
    public List<string> DesignatedFileTypes { get; set; } = [];

    /// <summary>Additional rules.</summary>
    public List<SoftwareRestrictionRule> Rules { get; set; } = [];
}

/// <summary>
/// Enforcement settings for SRP.
/// </summary>
public sealed class SoftwareRestrictionEnforcementSettings
{
    /// <summary>The default security level.</summary>
    public SoftwareRestrictionSecurityLevel DefaultSecurityLevel { get; set; } = SoftwareRestrictionSecurityLevel.Unrestricted;

    /// <summary>The user scope affected by SRP.</summary>
    public SoftwareRestrictionUserScope UserScope { get; set; } = SoftwareRestrictionUserScope.AllUsers;

    /// <summary>The file scope affected by SRP.</summary>
    public SoftwareRestrictionFileScope FileScope { get; set; } = SoftwareRestrictionFileScope.ExecutableFilesOnly;

    /// <summary>Whether certificate rules are enabled.</summary>
    public bool CertificateRulesEnabled { get; set; }
}

/// <summary>
/// Trusted publisher settings used by SRP certificate rule handling.
/// </summary>
public sealed class SoftwareRestrictionTrustedPublisherSettings
{
    /// <summary>Whether the trusted publisher policy settings are defined.</summary>
    public bool IsDefined { get; set; } = true;

    /// <summary>Who can select trusted publishers.</summary>
    public SoftwareRestrictionPublisherScope PublisherScope { get; set; } = SoftwareRestrictionPublisherScope.EndUsers;

    /// <summary>Whether publisher certificates must be checked for revocation.</summary>
    public bool CheckPublisherRevocation { get; set; }

    /// <summary>Whether timestamp certificates must be checked for revocation.</summary>
    public bool CheckTimestampRevocation { get; set; }
}

/// <summary>
/// An SRP additional rule.
/// </summary>
public sealed class SoftwareRestrictionRule
{
    /// <summary>The policy storage identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The rule kind.</summary>
    public SoftwareRestrictionRuleKind Kind { get; set; }

    /// <summary>The rule security level.</summary>
    public SoftwareRestrictionSecurityLevel SecurityLevel { get; set; } = SoftwareRestrictionSecurityLevel.Disallowed;

    /// <summary>Rule value, such as path, zone id, hash, or certificate thumbprint.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Decoded certificate subject, when this is a certificate rule.</summary>
    public string CertificateSubject { get; set; } = string.Empty;

    /// <summary>Decoded certificate issuer, when this is a certificate rule.</summary>
    public string CertificateIssuer { get; set; } = string.Empty;

    /// <summary>Decoded certificate serial number, when this is a certificate rule.</summary>
    public string CertificateSerialNumber { get; set; } = string.Empty;

    /// <summary>Decoded certificate thumbprint, when this is a certificate rule.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>Rule description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Raw policy storage path for rules that are not fully decoded yet.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>The last modified time, when stored by the policy editor.</summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>Read-only details for rules with decoded metadata.</summary>
    public IReadOnlyList<SoftwareRestrictionDetailItem> Details { get; set; } = [];

    /// <summary>Gets a value indicating whether this rule exposes read-only details.</summary>
    public bool CanViewDetails => Details.Count > 0;

    /// <summary>Primary display value for the rule list.</summary>
    public string DisplayValue => Kind == SoftwareRestrictionRuleKind.Certificate
        ? FirstNonEmpty(CertificateSubject, CertificateThumbprint, Value, KindDisplayName)
        : FirstNonEmpty(Value, KindDisplayName);

    /// <summary>Localized rule kind text.</summary>
    public string KindDisplayName => Kind switch
    {
        SoftwareRestrictionRuleKind.Path => Localized(SoftwareRestrictionKeys.RuleKindPath),
        SoftwareRestrictionRuleKind.Hash => Localized(SoftwareRestrictionKeys.RuleKindHash),
        SoftwareRestrictionRuleKind.Certificate => Localized(SoftwareRestrictionKeys.RuleKindCertificate),
        SoftwareRestrictionRuleKind.NetworkZone => Localized(SoftwareRestrictionKeys.RuleKindNetworkZone),
        _ => Localized(SoftwareRestrictionKeys.RuleKindUnknown)
    };

    /// <summary>Localized security level text.</summary>
    public string SecurityLevelDisplayName => FormatSecurityLevel(SecurityLevel);

    /// <summary>
    /// Formats a security level for display.
    /// </summary>
    public static string FormatSecurityLevel(SoftwareRestrictionSecurityLevel level)
        => level switch
        {
            SoftwareRestrictionSecurityLevel.BasicUser => Localized(SoftwareRestrictionKeys.SecurityLevelBasicUser),
            SoftwareRestrictionSecurityLevel.Unrestricted => Localized(SoftwareRestrictionKeys.SecurityLevelUnrestricted),
            _ => Localized(SoftwareRestrictionKeys.SecurityLevelDisallowed)
        };

    private static string Localized(string key)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return key;
        }

        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            return key;
        }

        return value;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}

/// <summary>
/// A name/value detail shown for a Software Restriction Policies rule.
/// </summary>
public sealed class SoftwareRestrictionDetailItem
{
    /// <summary>The localized detail name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The detail value.</summary>
    public string Value { get; init; } = string.Empty;
}
