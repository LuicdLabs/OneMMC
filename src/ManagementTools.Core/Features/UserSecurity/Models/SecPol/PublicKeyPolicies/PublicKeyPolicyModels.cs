namespace ManagementTools.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;

/// <summary>
/// Public Key Policies nodes shown by the Local Security Policy snap-in.
/// </summary>
public enum PublicKeyPolicyNodeKind
{
    /// <summary>Encrypting File System recovery agent policies.</summary>
    EncryptingFileSystem,

    /// <summary>Data Protection recovery agent policies.</summary>
    DataProtection,

    /// <summary>BitLocker Drive Encryption recovery agent policies.</summary>
    BitLockerDriveEncryption,

    /// <summary>Certificate enrollment policy settings.</summary>
    CertificateEnrollmentPolicy,

    /// <summary>Certificate path validation settings.</summary>
    CertificatePathValidation,

    /// <summary>Certificate auto-enrollment settings.</summary>
    CertificateAutoEnrollment
}

/// <summary>
/// Row categories shown in the Public Key Policies details pane.
/// </summary>
public enum PublicKeyPolicyRowKind
{
    /// <summary>A certificate row.</summary>
    Certificate,

    /// <summary>A policy setting row.</summary>
    Setting,

    /// <summary>An informational capability row.</summary>
    Information
}

/// <summary>
/// Configuration states supported by the certificate auto-enrollment policy.
/// </summary>
public enum CertificateAutoEnrollmentPolicyState
{
    /// <summary>The local policy setting is not configured.</summary>
    NotConfigured,

    /// <summary>Certificate auto-enrollment is enabled.</summary>
    Enabled,

    /// <summary>Certificate auto-enrollment is disabled.</summary>
    Disabled
}

/// <summary>
/// Configuration states supported by the certificate enrollment policy server list.
/// </summary>
public enum CertificateEnrollmentPolicyState
{
    /// <summary>The local policy setting is not configured.</summary>
    NotConfigured,

    /// <summary>The policy server list is enabled.</summary>
    Enabled
}

/// <summary>
/// Authentication modes supported by certificate enrollment policy servers.
/// </summary>
public enum CertificateEnrollmentPolicyAuthType
{
    /// <summary>Anonymous authentication.</summary>
    Anonymous = 1,

    /// <summary>Kerberos authentication.</summary>
    Kerberos = 2,

    /// <summary>User name and password authentication.</summary>
    UserName = 4,

    /// <summary>Client certificate authentication.</summary>
    ClientCertificate = 8
}

/// <summary>
/// File encryption states supported by the Encrypting File System policy.
/// </summary>
public enum EfsFileEncryptionState
{
    /// <summary>The local policy setting is not defined.</summary>
    NotDefined,

    /// <summary>EFS file encryption is allowed.</summary>
    Allow,

    /// <summary>EFS file encryption is not allowed.</summary>
    DontAllow
}

/// <summary>
/// Elliptic Curve Cryptography modes supported by the Encrypting File System policy.
/// </summary>
public enum EfsEllipticCurveMode
{
    /// <summary>Both ECC and RSA keys are allowed.</summary>
    Allow,

    /// <summary>ECC keys are required.</summary>
    Require,

    /// <summary>ECC keys are not allowed.</summary>
    DontAllow
}

/// <summary>
/// Trusted publisher management modes used by Public Key Policies.
/// </summary>
public enum PublicKeyTrustedPublisherScope
{
    /// <summary>End users can manage trusted publishers.</summary>
    EndUsers = 0,

    /// <summary>Local computer administrators can manage trusted publishers.</summary>
    LocalAdministrators = 1,

    /// <summary>Enterprise administrators can manage trusted publishers.</summary>
    EnterpriseAdministrators = 2
}

/// <summary>
/// Editable certificate path validation settings.
/// </summary>
public sealed class CertificatePathValidationSettings
{
    /// <summary>Gets or sets a value indicating whether the Stores tab is locally defined.</summary>
    public bool StoresDefined { get; set; }

    /// <summary>Gets or sets a value indicating whether current-user trusted roots are allowed.</summary>
    public bool AllowUserTrustedRoots { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether peer trust certificates are allowed.</summary>
    public bool AllowPeerTrustCertificates { get; set; } = true;

    /// <summary>Gets the allowed peer trust enhanced key usage OIDs.</summary>
    public List<string> PeerTrustPurposeOids { get; } = [];

    /// <summary>Gets or sets a value indicating whether only enterprise roots should be trusted.</summary>
    public bool OnlyTrustEnterpriseRoots { get; set; }

    /// <summary>Gets or sets a value indicating whether UPN name constraints are required for NTAuth chain policy.</summary>
    public bool RequireUpnNameConstraints { get; set; }

    /// <summary>Gets or sets a value indicating whether the Trusted Publishers tab is locally defined.</summary>
    public bool TrustedPublishersDefined { get; set; }

    /// <summary>Gets or sets who can select trusted publishers.</summary>
    public PublicKeyTrustedPublisherScope TrustedPublisherScope { get; set; } = PublicKeyTrustedPublisherScope.EndUsers;

    /// <summary>Gets or sets a value indicating whether publisher certificate revocation is checked.</summary>
    public bool CheckPublisherRevocation { get; set; }

    /// <summary>Gets or sets a value indicating whether timestamp certificate revocation is checked.</summary>
    public bool CheckTimestampRevocation { get; set; }

    /// <summary>Gets or sets a value indicating whether the Network Retrieval tab is locally defined.</summary>
    public bool NetworkRetrievalDefined { get; set; }

    /// <summary>Gets or sets a value indicating whether root certificates are automatically updated.</summary>
    public bool AutomaticallyUpdateRootCertificates { get; set; } = true;

    /// <summary>Gets or sets the default URL retrieval timeout in seconds.</summary>
    public int UrlRetrievalTimeoutSeconds { get; set; } = 15;

    /// <summary>Gets or sets the cumulative path validation retrieval timeout in seconds.</summary>
    public int PathValidationRetrievalTimeoutSeconds { get; set; } = 20;

    /// <summary>Gets or sets a value indicating whether AIA retrieval is allowed during path validation.</summary>
    public bool AllowAiaRetrieval { get; set; } = true;

    /// <summary>Gets or sets the cross-certificate download interval in hours.</summary>
    public int CrossCertificateDownloadIntervalHours { get; set; } = 168;

    /// <summary>Gets or sets a value indicating whether the Revocation tab is locally defined.</summary>
    public bool RevocationDefined { get; set; }

    /// <summary>Gets or sets a value indicating whether CRL responses should be preferred before cached OCSP responses.</summary>
    public bool PreferCrlBeforeOcsp { get; set; }

    /// <summary>Gets or sets the cached OCSP response threshold used before CRLs are preferred.</summary>
    public int CachedOcspResponseThreshold { get; set; } = 50;

    /// <summary>Gets or sets a value indicating whether CRL and OCSP validity can be extended.</summary>
    public bool ExtendRevocationFreshnessLifetime { get; set; }

    /// <summary>Gets or sets the revocation validity extension period in hours.</summary>
    public int RevocationFreshnessExtensionHours { get; set; } = 12;
}

/// <summary>
/// Editable certificate auto-enrollment policy settings.
/// </summary>
public sealed class CertificateAutoEnrollmentSettings
{
    /// <summary>The configured auto-enrollment policy state.</summary>
    public CertificateAutoEnrollmentPolicyState State { get; set; }

    /// <summary>Whether expired, pending, and revoked certificates are managed automatically.</summary>
    public bool EnableMyStoreManagement { get; set; }

    /// <summary>Whether certificates that use certificate templates are updated automatically.</summary>
    public bool EnableTemplateCheck { get; set; }
}

/// <summary>
/// Editable certificate enrollment policy server-list settings.
/// </summary>
public sealed class CertificateEnrollmentPolicySettings
{
    /// <summary>Gets or sets the configured enrollment policy state.</summary>
    public CertificateEnrollmentPolicyState State { get; set; }

    /// <summary>Gets the locally configured policy servers.</summary>
    public List<CertificateEnrollmentPolicyServer> Servers { get; } = [];
}

/// <summary>
/// A certificate enrollment policy server entry.
/// </summary>
public sealed class CertificateEnrollmentPolicyServer
{
    /// <summary>Gets or sets the registry subkey identifier for the policy server.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the policy server URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the policy server policy identifier.</summary>
    public string PolicyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the policy server friendly name.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the policy server flags.</summary>
    public uint Flags { get; set; }

    /// <summary>Gets or sets the policy server authentication mode.</summary>
    public CertificateEnrollmentPolicyAuthType AuthType { get; set; } = CertificateEnrollmentPolicyAuthType.Kerberos;

    /// <summary>Gets or sets the policy server selection cost.</summary>
    public uint Cost { get; set; }

    /// <summary>Gets or sets localized authentication text for display.</summary>
    public string AuthenticationText { get; set; } = string.Empty;

    /// <summary>Gets a display name for the server.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Url : FriendlyName;

    /// <summary>Gets the display text for the policy server selection cost.</summary>
    public string CostDisplay => Cost.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// Editable Encrypting File System policy settings.
/// </summary>
public sealed class EfsPolicySettings
{
    /// <summary>Gets or sets the configured EFS file encryption state.</summary>
    public EfsFileEncryptionState FileEncryptionState { get; set; }

    /// <summary>Gets or sets the configured ECC key policy.</summary>
    public EfsEllipticCurveMode EllipticCurveMode { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's Documents folder should be encrypted.</summary>
    public bool EncryptDocumentsFolder { get; set; }

    /// <summary>Gets or sets a value indicating whether EFS requires smart-card backed keys.</summary>
    public bool RequireSmartCard { get; set; }

    /// <summary>Gets or sets a value indicating whether EFS creates a caching-capable key from smart-card private keys.</summary>
    public bool CreateCachingCapableUserKey { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether EFS key backup notifications are shown.</summary>
    public bool DisplayKeyBackupNotifications { get; set; }

    /// <summary>Gets or sets the EFS certificate template common name.</summary>
    public string TemplateName { get; set; } = "EFS";

    /// <summary>Gets or sets a value indicating whether self-signed EFS certificates are allowed.</summary>
    public bool AllowSelfSignedCertificates { get; set; } = true;

    /// <summary>Gets or sets the RSA self-signed certificate key length.</summary>
    public int RsaKeyLength { get; set; } = 2048;

    /// <summary>Gets or sets the ECC self-signed certificate key length.</summary>
    public int EccKeyLength { get; set; } = 256;

    /// <summary>Gets or sets a value indicating whether EFS secrets are cleared after the cache timeout.</summary>
    public bool ClearCacheOnTimeout { get; set; } = true;

    /// <summary>Gets or sets the EFS cache timeout in minutes.</summary>
    public int CacheTimeoutMinutes { get; set; } = 480;

    /// <summary>Gets or sets a value indicating whether EFS secrets are cleared when the user locks the session.</summary>
    public bool ClearCacheOnLock { get; set; }
}

/// <summary>
/// A Public Key Policies navigation node.
/// </summary>
public sealed class PublicKeyPolicyNode
{
    /// <summary>The node kind.</summary>
    public PublicKeyPolicyNodeKind Kind { get; init; }

    /// <summary>The localized display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The localized description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Rows shown when the node is selected.</summary>
    public IReadOnlyList<PublicKeyPolicyRow> Rows { get; init; } = [];

    /// <summary>Read-only properties shown for this policy node.</summary>
    public IReadOnlyList<PublicKeyPolicyDetailItem> Details { get; init; } = [];

    /// <summary>Editable auto-enrollment settings, when this is the auto-enrollment node.</summary>
    public CertificateAutoEnrollmentSettings? AutoEnrollmentSettings { get; init; }

    /// <summary>Editable certificate path validation settings, when this is the path validation node.</summary>
    public CertificatePathValidationSettings? PathValidationSettings { get; init; }

    /// <summary>Editable certificate enrollment policy settings, when this is the enrollment policy node.</summary>
    public CertificateEnrollmentPolicySettings? EnrollmentPolicySettings { get; init; }

    /// <summary>Editable Encrypting File System settings, when this is the EFS node.</summary>
    public EfsPolicySettings? EfsSettings { get; init; }

    /// <summary>Gets a value indicating whether this node exposes read-only properties.</summary>
    public bool CanViewProperties => Details.Count > 0
        || AutoEnrollmentSettings is not null
        || PathValidationSettings is not null
        || EnrollmentPolicySettings is not null
        || EfsSettings is not null;

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
/// A row shown in the Public Key Policies details pane.
/// </summary>
public sealed class PublicKeyPolicyRow
{
    /// <summary>The row kind.</summary>
    public PublicKeyPolicyRowKind Kind { get; init; } = PublicKeyPolicyRowKind.Setting;

    /// <summary>The primary row name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The secondary row value.</summary>
    public string Setting { get; init; } = string.Empty;

    /// <summary>Certificate subject, when the row represents a certificate.</summary>
    public string IssuedTo { get; init; } = string.Empty;

    /// <summary>Certificate issuer, when the row represents a certificate.</summary>
    public string IssuedBy { get; init; } = string.Empty;

    /// <summary>Certificate expiration text.</summary>
    public string ExpirationDate { get; init; } = string.Empty;

    /// <summary>Certificate intended purposes text.</summary>
    public string IntendedPurposes { get; init; } = string.Empty;

    /// <summary>Certificate friendly name.</summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>Certificate status text.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Certificate template text.</summary>
    public string CertificateTemplate { get; init; } = string.Empty;

    /// <summary>Details shown in the certificate properties dialog.</summary>
    public IReadOnlyList<PublicKeyPolicyDetailItem> Details { get; init; } = [];

    /// <summary>Raw certificate data used by the native Windows certificate viewer.</summary>
    public byte[]? CertificateRawData { get; init; }

    /// <summary>Certificate thumbprint, when available.</summary>
    public string Thumbprint { get; init; } = string.Empty;

    /// <summary>Optional raw source path or diagnostic value.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Certificate list primary text, with an informational-row fallback.</summary>
    public string CertificateIssuedToDisplay => string.IsNullOrWhiteSpace(IssuedTo) ? Name : IssuedTo;

    /// <summary>Certificate list issuer text, with an informational-row fallback.</summary>
    public string CertificateIssuedByDisplay => string.IsNullOrWhiteSpace(IssuedBy) ? Setting : IssuedBy;

    /// <summary>Primary list text for the current row kind.</summary>
    public string PrimaryDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? CertificateIssuedToDisplay : Name;

    /// <summary>Secondary list text for the current row kind.</summary>
    public string SecondaryDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? CertificateIssuedByDisplay : Setting;

    /// <summary>Expiration list text for certificate rows.</summary>
    public string ExpirationDateDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? ExpirationDate : string.Empty;

    /// <summary>Intended purposes list text for certificate rows.</summary>
    public string IntendedPurposesDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? IntendedPurposes : string.Empty;

    /// <summary>Friendly name list text for certificate rows.</summary>
    public string FriendlyNameDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? FriendlyName : string.Empty;

    /// <summary>Status list text for certificate rows.</summary>
    public string StatusDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? Status : string.Empty;

    /// <summary>Certificate template list text for certificate rows.</summary>
    public string CertificateTemplateDisplay => Kind == PublicKeyPolicyRowKind.Certificate ? CertificateTemplate : string.Empty;

    /// <summary>Gets a value indicating whether this row has certificate details to show.</summary>
    public bool CanViewDetails => Kind == PublicKeyPolicyRowKind.Certificate
        && (CertificateRawData is not null || Details.Count > 0);
}

/// <summary>
/// A name/value detail shown for a Public Key Policies certificate.
/// </summary>
public sealed class PublicKeyPolicyDetailItem
{
    /// <summary>The localized detail name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The detail value.</summary>
    public string Value { get; init; } = string.Empty;
}
