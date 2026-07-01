using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.PublicKeyPolicies;

/// <summary>
/// Reads machine-scoped Public Key Policies that have documented local policy storage.
/// </summary>
public sealed class PublicKeyPolicyService
{
    private const string SystemCertificatesPolicyRoot = @"SOFTWARE\Policies\Microsoft\SystemCertificates";
    private const string ChainEngineConfigPath = SystemCertificatesPolicyRoot + @"\ChainEngine\Config";
    private const string AuthRootPath = SystemCertificatesPolicyRoot + @"\AuthRoot";
    private const string ProtectedRootsPath = SystemCertificatesPolicyRoot + @"\Root\ProtectedRoots";
    private const string TrustedPublisherSaferPath = SystemCertificatesPolicyRoot + @"\TrustedPublisher\Safer";
    private const string AutoEnrollmentPolicyPath = @"SOFTWARE\Policies\Microsoft\Cryptography\AutoEnrollment";
    private const string EnrollmentPolicyServersPath = @"SOFTWARE\Policies\Microsoft\Cryptography\PolicyServers";
    private const string EfsPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\EFS";
    private const string EnrollmentPolicyServersFlagsValueName = "Flags";
    private const string EnrollmentPolicyServerUrlValueName = "URL";
    private const string EnrollmentPolicyServerPolicyIdValueName = "PolicyID";
    private const string EnrollmentPolicyServerFriendlyNameValueName = "FriendlyName";
    private const string EnrollmentPolicyServerAuthFlagsValueName = "AuthFlags";
    private const string EnrollmentPolicyServerCostValueName = "Cost";
    private const string BlobValueName = "Blob";
    private const string ChainEngineOptionsValueName = "Options";
    private const string ChainUrlRetrievalTimeoutMillisecondsValueName = "ChainUrlRetrievalTimeoutMilliseconds";
    private const string ChainRevAccumulativeUrlRetrievalTimeoutMillisecondsValueName = "ChainRevAccumulativeUrlRetrievalTimeoutMilliseconds";
    private const string CrossCertDownloadIntervalHoursValueName = "CrossCertDownloadIntervalHours";
    private const string CryptnetCachedOcspSwitchToCrlCountValueName = "CryptnetCachedOcspSwitchToCrlCount";
    private const string CrlValidityExtensionPeriodValueName = "CRLValidityExtensionPeriod";
    private const string DisableRootAutoUpdateValueName = "DisableRootAutoUpdate";
    private const string EnableDisallowedCertAutoUpdateValueName = "EnableDisallowedCertAutoUpdate";
    private const string ProtectedRootsFlagsValueName = "Flags";
    private const string PeerUsagesValueName = "PeerUsages";
    private const string AuthenticodeFlagsValueName = "AuthenticodeFlags";
    private const string AutoEnrollmentPolicyValueName = "AEPolicy";
    private const string OfflineExpirationPercentValueName = "OfflineExpirationPercent";
    private const string StoreNameValueName = "StoreName";
    private const string EnableBalloonNotificationsValueName = "EnableBalloonNotifications";
    private const string EfsConfigurationValueName = "EfsConfiguration";
    private const string EfsOptionsValueName = "EfsOptions";
    private const string EfsCacheTimeoutValueName = "CacheTimeout";
    private const string EfsTemplateNameValueName = "TemplateName";
    private const string EfsRsaKeyLengthValueName = "RSAKeyLength";
    private const string EfsSuiteBAlgorithmValueName = "SuiteBAlgorithm";
    private const string EfsDefaultTemplateName = "EFS";
    private const string EfsSuiteBAlgorithmP256 = "ECDH_P256";
    private const string EfsSuiteBAlgorithmP384 = "ECDH_P384";
    private const string EfsSuiteBAlgorithmP521 = "ECDH_P521";
    private const int EfsDefaultRsaKeyLength = 2048;
    private const int EfsDefaultEccKeyLength = 256;
    private const int EfsDefaultCacheTimeoutMinutes = 480;
    private const int EfsMinimumCacheTimeoutMinutes = 5;
    private const int EfsMaximumCacheTimeoutMinutes = 10080;
    private const int DisableAiaRetrievalOptionsValue = 2;
    private const uint EnrollmentPolicyServerAutoEnrollmentFlag = 0x00000010;
    private const uint EnrollmentPolicyServerDefaultCost = 0x7FFFFFFD;
    private const uint ProtectedRootsDisableCurrentUserFlag = 0x00000001;
    private const uint ProtectedRootsOnlyEnterpriseRootsFlag = 0x00000008;
    private const uint ProtectedRootsRequireUpnNameConstraintsFlag = 0x00000010;
    private const uint ProtectedRootsDisablePeerTrustFlag = 0x00010000;
    private const uint TrustedPublisherRevocationFlag = 0x00000100;
    private const uint TrustedPublisherTimestampRevocationFlag = 0x00000200;
    private const uint TrustedPublisherScopeMask = 0x00000003;
    private const uint DisableAiaRetrievalOptionsFlag = 0x00000002;
    private const uint AutoEnrollmentDisabledFlag = 0x00008000;
    private const uint AutoEnrollmentTemplateCheckFlag = 0x00000001;
    private const uint AutoEnrollmentStoreManagementFlags = 0x00000006;
    private const uint AutoEnrollmentKnownFlags =
        AutoEnrollmentDisabledFlag
        | AutoEnrollmentTemplateCheckFlag
        | AutoEnrollmentStoreManagementFlags;
    private const uint EfsEncryptDocumentsFolderFlag = 0x00000001;
    private const uint EfsCreateSmartCardUserKeyFlag = 0x00000002;
    private const uint EfsAllowSelfSignedCertificatesFlag = 0x00000004;
    private const uint EfsClearCacheOnTimeoutFlag = 0x00000010;
    private const uint EfsClearCacheOnLockFlag = 0x00000020;
    private const uint EfsRequireSmartCardFlag = 0x00000100;
    private const uint EfsKeyBackupNotificationFlag = 0x00000400;
    private const uint EfsDisallowEccKeysFlag = 0x00001000;
    private const uint EfsRequireEccKeysFlag = 0x00002000;
    private const uint EfsDefaultOptions =
        EfsCreateSmartCardUserKeyFlag
        | EfsAllowSelfSignedCertificatesFlag
        | EfsClearCacheOnTimeoutFlag;
    private const uint EfsEditableOptionsMask =
        EfsEncryptDocumentsFolderFlag
        | EfsCreateSmartCardUserKeyFlag
        | EfsAllowSelfSignedCertificatesFlag
        | EfsClearCacheOnTimeoutFlag
        | EfsClearCacheOnLockFlag
        | EfsRequireSmartCardFlag
        | EfsKeyBackupNotificationFlag
        | EfsDisallowEccKeysFlag
        | EfsRequireEccKeysFlag;
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string SecureEmailOid = "1.3.6.1.5.5.7.3.4";
    private const string EncryptingFileSystemOid = "1.3.6.1.4.1.311.10.3.4";
    private const string EfsRecoveryOid = "1.3.6.1.4.1.311.10.3.4.1";

    private readonly ILogger<PublicKeyPolicyService> _logger;
    private readonly LocalPolicyFileStore _localPolicyFileStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicKeyPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="localPolicyFileStore">The shared local policy file store.</param>
    public PublicKeyPolicyService(
        ILogger<PublicKeyPolicyService> logger,
        LocalPolicyFileStore localPolicyFileStore)
    {
        _logger = logger;
        _localPolicyFileStore = localPolicyFileStore;
    }

    /// <summary>
    /// Loads all Public Key Policies nodes.
    /// </summary>
    /// <returns>The loaded node collection.</returns>
    public IReadOnlyList<PublicKeyPolicyNode> LoadNodes()
    {
        CertificateEnrollmentPolicySettings enrollmentPolicySettings = LoadEnrollmentPolicySettings();
        CertificatePathValidationSettings pathValidationSettings = LoadCertificatePathValidationSettings();
        CertificateAutoEnrollmentSettings autoEnrollmentSettings = LoadAutoEnrollmentSettings();
        EfsPolicySettings efsSettings = LoadEfsPolicySettings();

        return
        [
            LoadRecoveryAgentNode(
                PublicKeyPolicyNodeKind.EncryptingFileSystem,
                PublicKeyPolicyKeys.NodeEncryptingFileSystem,
                PublicKeyPolicyKeys.NodeEncryptingFileSystemDescription,
                "EFS",
                efsSettings),
            LoadRecoveryAgentNode(
                PublicKeyPolicyNodeKind.DataProtection,
                PublicKeyPolicyKeys.NodeDataProtection,
                PublicKeyPolicyKeys.NodeDataProtectionDescription,
                "DPNGRA"),
            LoadRecoveryAgentNode(
                PublicKeyPolicyNodeKind.BitLockerDriveEncryption,
                PublicKeyPolicyKeys.NodeBitLockerDriveEncryption,
                PublicKeyPolicyKeys.NodeBitLockerDriveEncryptionDescription,
                "FVE"),
            new PublicKeyPolicyNode
            {
                Kind = PublicKeyPolicyNodeKind.CertificateEnrollmentPolicy,
                DisplayName = GetString(PublicKeyPolicyKeys.NodeCertificateEnrollmentPolicy),
                Description = GetString(PublicKeyPolicyKeys.NodeCertificateEnrollmentPolicyDescription),
                Rows = LoadEnrollmentPolicyRows(enrollmentPolicySettings),
                Details = LoadEnrollmentPolicyDetails(enrollmentPolicySettings),
                EnrollmentPolicySettings = enrollmentPolicySettings
            },
            new PublicKeyPolicyNode
            {
                Kind = PublicKeyPolicyNodeKind.CertificatePathValidation,
                DisplayName = GetString(PublicKeyPolicyKeys.NodeCertificatePathValidation),
                Description = GetString(PublicKeyPolicyKeys.NodeCertificatePathValidationDescription),
                Rows = LoadCertificatePathValidationRows(pathValidationSettings),
                Details = LoadCertificatePathValidationDetails(pathValidationSettings),
                PathValidationSettings = pathValidationSettings
            },
            new PublicKeyPolicyNode
            {
                Kind = PublicKeyPolicyNodeKind.CertificateAutoEnrollment,
                DisplayName = GetString(PublicKeyPolicyKeys.NodeCertificateAutoEnrollment),
                Description = GetString(PublicKeyPolicyKeys.NodeCertificateAutoEnrollmentDescription),
                Rows = LoadAutoEnrollmentRows(autoEnrollmentSettings),
                Details = LoadAutoEnrollmentDetails(autoEnrollmentSettings),
                AutoEnrollmentSettings = autoEnrollmentSettings
            }
        ];
    }

    /// <summary>
    /// Saves the local machine certificate auto-enrollment policy.
    /// </summary>
    /// <param name="settings">The auto-enrollment settings to persist.</param>
    public void SaveAutoEnrollment(CertificateAutoEnrollmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureMachinePolicyIsWritable();
        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        uint existingPolicy = ReadDword(snapshot, AutoEnrollmentPolicyPath, AutoEnrollmentPolicyValueName) ?? 0U;
        if (settings.State == CertificateAutoEnrollmentPolicyState.NotConfigured)
        {
            snapshot.DeleteValue(AutoEnrollmentPolicyPath, AutoEnrollmentPolicyValueName);
        }
        else
        {
            snapshot.ForgetValue(AutoEnrollmentPolicyPath, AutoEnrollmentPolicyValueName);
            uint policy = existingPolicy & ~AutoEnrollmentKnownFlags;
            policy |= settings.State == CertificateAutoEnrollmentPolicyState.Disabled
                ? AutoEnrollmentDisabledFlag
                : 0U;

            if (settings.State == CertificateAutoEnrollmentPolicyState.Enabled)
            {
                if (settings.EnableMyStoreManagement)
                {
                    policy |= AutoEnrollmentStoreManagementFlags;
                }

                if (settings.EnableTemplateCheck)
                {
                    policy |= AutoEnrollmentTemplateCheckFlag;
                }
            }

            snapshot.SetValue(AutoEnrollmentPolicyPath, AutoEnrollmentPolicyValueName, policy, RegistryValueKind.DWord);
        }

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation("Certificate auto-enrollment policy saved: {SaveResult}", result);
    }

    /// <summary>
    /// Saves local machine certificate enrollment policy server-list settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    public void SaveCertificateEnrollmentPolicy(CertificateEnrollmentPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureMachinePolicyIsWritable();
        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        if (settings.State == CertificateEnrollmentPolicyState.NotConfigured)
        {
            ClearEnrollmentPolicyServerTree(snapshot);
        }
        else
        {
            ClearEnrollmentPolicyServerTree(snapshot);
            snapshot.SetValue(
                EnrollmentPolicyServersPath,
                EnrollmentPolicyServersFlagsValueName,
                0U,
                RegistryValueKind.DWord);

            foreach (CertificateEnrollmentPolicyServer server in settings.Servers)
            {
                SaveEnrollmentPolicyServer(snapshot, server);
            }
        }

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation("Certificate enrollment policy server-list setting saved: {SaveResult}", result);
    }

    /// <summary>
    /// Saves local machine certificate path validation policy settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    public void SaveCertificatePathValidation(CertificatePathValidationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureMachinePolicyIsWritable();
        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        SavePathValidationStores(snapshot, settings);
        SavePathValidationTrustedPublishers(snapshot, settings);
        SavePathValidationNetworkRetrieval(snapshot, settings);
        SavePathValidationRevocation(snapshot, settings);

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation("Certificate path validation policy saved: {SaveResult}", result);
    }

    /// <summary>
    /// Saves local machine Encrypting File System policy settings.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    public void SaveEfsPolicy(EfsPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureMachinePolicyIsWritable();
        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        if (settings.FileEncryptionState == EfsFileEncryptionState.NotDefined)
        {
            snapshot.DeleteValue(EfsPolicyPath, EfsConfigurationValueName);
            snapshot.DeleteValue(EfsPolicyPath, EfsOptionsValueName);
            snapshot.DeleteValue(EfsPolicyPath, EfsCacheTimeoutValueName);
            snapshot.DeleteValue(EfsPolicyPath, EfsTemplateNameValueName);
            snapshot.DeleteValue(EfsPolicyPath, EfsRsaKeyLengthValueName);
            snapshot.DeleteValue(EfsPolicyPath, EfsSuiteBAlgorithmValueName);
        }
        else
        {
            snapshot.ForgetValue(EfsPolicyPath, EfsConfigurationValueName);
            snapshot.SetValue(
                EfsPolicyPath,
                EfsConfigurationValueName,
                settings.FileEncryptionState == EfsFileEncryptionState.DontAllow ? 1U : 0U,
                RegistryValueKind.DWord);

            uint existingOptions = ReadDword(snapshot, EfsPolicyPath, EfsOptionsValueName) ?? EfsDefaultOptions;
            uint options = existingOptions & ~EfsEditableOptionsMask;
            options |= CreateEfsOptions(settings);
            snapshot.SetValue(EfsPolicyPath, EfsOptionsValueName, options, RegistryValueKind.DWord);
            snapshot.SetValue(
                EfsPolicyPath,
                EfsCacheTimeoutValueName,
                (uint)Math.Clamp(
                    settings.CacheTimeoutMinutes,
                    EfsMinimumCacheTimeoutMinutes,
                    EfsMaximumCacheTimeoutMinutes),
                RegistryValueKind.DWord);
            snapshot.SetValue(
                EfsPolicyPath,
                EfsTemplateNameValueName,
                string.IsNullOrWhiteSpace(settings.TemplateName)
                    ? EfsDefaultTemplateName
                    : settings.TemplateName.Trim(),
                RegistryValueKind.String);
            snapshot.SetValue(
                EfsPolicyPath,
                EfsRsaKeyLengthValueName,
                (uint)NormalizeRsaKeyLength(settings.RsaKeyLength),
                RegistryValueKind.DWord);
            snapshot.SetValue(
                EfsPolicyPath,
                EfsSuiteBAlgorithmValueName,
                MapEccKeyLengthToSuiteBAlgorithm(settings.EccKeyLength),
                RegistryValueKind.String);
        }

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation("Encrypting File System policy saved: {SaveResult}", result);
    }

    /// <summary>
    /// Adds a recovery agent certificate to the selected Public Key Policies certificate store.
    /// </summary>
    /// <param name="nodeKind">The selected recovery-agent node kind.</param>
    /// <param name="certificatePath">The certificate file path.</param>
    public void AddRecoveryAgentCertificate(PublicKeyPolicyNodeKind nodeKind, string certificatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);

        EnsureMachinePolicyIsWritable();
        byte[] certificateBytes = File.ReadAllBytes(certificatePath);
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
        string thumbprint = NormalizeThumbprint(certificate.Thumbprint);
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new CryptographicException(GetString(PublicKeyPolicyKeys.CertificateBlobUnavailable));
        }

        string storeName = GetRecoveryAgentStoreName(nodeKind);
        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        ValidateRecoveryAgentCertificate(nodeKind, storeName, certificate, thumbprint, snapshot);

        string certificateKey = GetRecoveryAgentCertificateKey(storeName, thumbprint);
        snapshot.SetValue(certificateKey, BlobValueName, certificate.RawData.ToArray(), RegistryValueKind.Binary);

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation(
            "Recovery agent certificate saved to {StoreName}: {Thumbprint}. {SaveResult}",
            storeName,
            thumbprint,
            result);
    }

    /// <summary>
    /// Deletes a recovery agent certificate from the selected Public Key Policies certificate store.
    /// </summary>
    /// <param name="row">The selected certificate row.</param>
    public void DeleteRecoveryAgentCertificate(PublicKeyPolicyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        EnsureMachinePolicyIsWritable();
        string storeName = row.Source;
        string thumbprint = NormalizeThumbprint(row.Thumbprint);
        if (!IsRecoveryAgentStoreName(storeName) || string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException(GetString(PublicKeyPolicyKeys.CertificateBlobUnavailable));
        }

        PolFile snapshot = _localPolicyFileStore.LoadSnapshot(isUser: false);
        ClearTree(snapshot, GetRecoveryAgentCertificateKey(storeName, thumbprint));

        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation(
            "Recovery agent certificate deleted from {StoreName}: {Thumbprint}. {SaveResult}",
            storeName,
            thumbprint,
            result);
    }

    private PublicKeyPolicyNode LoadRecoveryAgentNode(
        PublicKeyPolicyNodeKind kind,
        string nameKey,
        string descriptionKey,
        string storeName,
        EfsPolicySettings? efsSettings = null)
    {
        List<PublicKeyPolicyRow> rows = LoadGroupPolicyCertificates(storeName);
        if (rows.Count == 0)
        {
            rows.Add(new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Information,
                Name = GetString(PublicKeyPolicyKeys.NoRecoveryAgents),
                Setting = string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(PublicKeyPolicyKeys.GroupPolicyStorePathFormat),
                    $@"HKLM\{SystemCertificatesPolicyRoot}\{storeName}\Certificates")
            });
        }

        return new PublicKeyPolicyNode
        {
            Kind = kind,
            DisplayName = GetString(nameKey),
            Description = GetString(descriptionKey),
            Rows = rows,
            Details = efsSettings is null ? [] : LoadEfsPolicyDetails(efsSettings),
            EfsSettings = efsSettings
        };
    }

    private List<PublicKeyPolicyRow> LoadGroupPolicyCertificates(string storeName)
    {
        List<PublicKeyPolicyRow> rows = [];
        string certificatesPath = $@"{SystemCertificatesPolicyRoot}\{storeName}\Certificates";

        try
        {
            using RegistryKey? certificatesKey = Registry.LocalMachine.OpenSubKey(certificatesPath);
            if (certificatesKey is null)
            {
                return rows;
            }

            foreach (string thumbprint in certificatesKey.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                using RegistryKey? certificateKey = certificatesKey.OpenSubKey(thumbprint);
                byte[]? blob = certificateKey?.GetValue(BlobValueName) as byte[];
                rows.Add(CreateCertificateRow(storeName, thumbprint, blob));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Group Policy certificate store {StoreName}.", storeName);
            rows.Add(new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Information,
                Name = GetString(PublicKeyPolicyKeys.StoreReadFailed),
                Setting = ex.Message
            });
        }

        return rows;
    }

    private PublicKeyPolicyRow CreateCertificateRow(string storeName, string thumbprint, byte[]? blob)
    {
        if (blob is not null)
        {
            try
            {
                using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(blob);
                string issuedTo = GetPreferredName(certificate, forIssuer: false);
                string issuedBy = GetPreferredName(certificate, forIssuer: true);
                string friendlyName = string.IsNullOrWhiteSpace(certificate.FriendlyName)
                    ? string.Empty
                    : certificate.FriendlyName;
                string status = FormatStatus(certificate);
                string intendedPurposes = FormatEnhancedKeyUsage(certificate);
                string certificateTemplate = GetCertificateTemplateText(certificate);

                return new PublicKeyPolicyRow
                {
                    Kind = PublicKeyPolicyRowKind.Certificate,
                    Name = string.IsNullOrWhiteSpace(friendlyName) ? issuedTo : friendlyName,
                    Setting = status,
                    IssuedTo = issuedTo,
                    IssuedBy = issuedBy,
                    ExpirationDate = certificate.NotAfter.ToString("d", CultureInfo.CurrentCulture),
                    IntendedPurposes = intendedPurposes,
                    FriendlyName = friendlyName,
                    Status = status,
                    CertificateTemplate = certificateTemplate,
                    Details = BuildCertificateDetails(
                        certificate,
                        storeName,
                        thumbprint,
                        status,
                        intendedPurposes,
                        certificateTemplate),
                    CertificateRawData = certificate.RawData.ToArray(),
                    Thumbprint = certificate.Thumbprint ?? thumbprint,
                    Source = storeName
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to decode certificate blob from store {StoreName}, thumbprint {Thumbprint}.", storeName, thumbprint);
            }
        }

        return new PublicKeyPolicyRow
        {
            Kind = PublicKeyPolicyRowKind.Certificate,
            Name = thumbprint,
            Setting = GetString(PublicKeyPolicyKeys.CertificateBlobUnavailable),
            IssuedTo = thumbprint,
            Status = GetString(PublicKeyPolicyKeys.CertificateBlobUnavailable),
            Thumbprint = thumbprint,
            Source = storeName
        };
    }

    private IReadOnlyList<PublicKeyPolicyRow> LoadEnrollmentPolicyRows(CertificateEnrollmentPolicySettings settings)
    {
        List<PublicKeyPolicyRow> rows = [];

        foreach (CertificateEnrollmentPolicyServer server in settings.Servers
            .OrderBy(static server => server.Cost)
            .ThenBy(static server => server.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            string authenticationText = string.IsNullOrWhiteSpace(server.AuthenticationText)
                ? FormatEnrollmentPolicyAuthType(server.AuthType)
                : server.AuthenticationText;
            rows.Add(new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = server.DisplayName,
                Setting = string.IsNullOrWhiteSpace(server.Url)
                    ? authenticationText
                    : $"{authenticationText}: {server.Url}",
                Source = server.Id
            });
        }

        if (rows.Count == 0)
        {
            rows.Add(new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Information,
                Name = settings.State == CertificateEnrollmentPolicyState.Enabled
                    ? GetString(PublicKeyPolicyKeys.EnrollmentPolicyNoServers)
                    : GetString(PublicKeyPolicyKeys.NotConfigured),
                Setting = settings.State == CertificateEnrollmentPolicyState.Enabled
                    ? $@"HKLM\{EnrollmentPolicyServersPath}"
                    : GetString(PublicKeyPolicyKeys.EnrollmentPolicyCmdletBacked)
            });
        }

        return rows;
    }

    private IReadOnlyList<PublicKeyPolicyDetailItem> LoadEnrollmentPolicyDetails(CertificateEnrollmentPolicySettings settings)
    {
        List<PublicKeyPolicyDetailItem> details =
        [
            CreateDetail(PublicKeyPolicyKeys.EnrollmentPolicyConfigurationModel, FormatEnrollmentPolicyState(settings.State)),
            CreateDetail(PublicKeyPolicyKeys.EnrollmentPolicyServerCount, settings.Servers.Count.ToString(CultureInfo.CurrentCulture)),
            CreateDetail(PublicKeyPolicyKeys.EnrollmentPolicyPolicySource, $@"HKLM\{EnrollmentPolicyServersPath}")
        ];

        foreach (CertificateEnrollmentPolicyServer server in settings.Servers
            .OrderBy(static server => server.Cost)
            .ThenBy(static server => server.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            string authenticationText = string.IsNullOrWhiteSpace(server.AuthenticationText)
                ? FormatEnrollmentPolicyAuthType(server.AuthType)
                : server.AuthenticationText;
            details.Add(CreateLiteralDetail(
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(PublicKeyPolicyKeys.EnrollmentPolicyServerDetailFormat),
                    server.DisplayName),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(PublicKeyPolicyKeys.EnrollmentPolicyServerDetailValueFormat),
                    server.Url,
                    authenticationText,
                    server.Cost,
                    server.Flags)));
        }

        foreach (PublicKeyPolicyDetailItem rawValue in LoadRegistryValueDetails(EnrollmentPolicyServersPath))
        {
            details.Add(rawValue);
        }

        return details;
    }

    private IReadOnlyList<PublicKeyPolicyRow> LoadCertificatePathValidationRows(CertificatePathValidationSettings settings)
    {
        uint? disableRootAutoUpdate = ReadDword(AuthRootPath, DisableRootAutoUpdateValueName);
        uint? enableDisallowedAutoUpdate = ReadDword(AuthRootPath, EnableDisallowedCertAutoUpdateValueName);

        return
        [
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationStoresDefined),
                Setting = FormatDefined(settings.StoresDefined)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationTrustedPublishersDefined),
                Setting = FormatDefined(settings.TrustedPublishersDefined)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationNetworkRetrievalDefined),
                Setting = FormatDefined(settings.NetworkRetrievalDefined)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationRevocationDefined),
                Setting = FormatDefined(settings.RevocationDefined)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationAiaRetrieval),
                Setting = settings.NetworkRetrievalDefined && !settings.AllowAiaRetrieval
                    ? GetString(SecPolKeys.ValueDisabled)
                    : FormatOption(settings.NetworkRetrievalDefined, settings.AllowAiaRetrieval)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationRootAutoUpdate),
                Setting = settings.NetworkRetrievalDefined
                    ? FormatOption(settings.NetworkRetrievalDefined, settings.AutomaticallyUpdateRootCertificates)
                    : FormatDisabledWhenOne(disableRootAutoUpdate)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.PathValidationDisallowedAutoUpdate),
                Setting = enableDisallowedAutoUpdate == 0U
                    ? GetString(SecPolKeys.ValueDisabled)
                    : GetString(SecPolKeys.ValueEnabled)
            },
            CreateStoreCountRow(PublicKeyPolicyKeys.PathValidationTrustedRoots, "Root"),
            CreateStoreCountRow(PublicKeyPolicyKeys.PathValidationTrustedPublishers, "TrustedPublisher"),
            CreateStoreCountRow(PublicKeyPolicyKeys.PathValidationIntermediateCas, "CA")
        ];
    }

    private static IReadOnlyList<PublicKeyPolicyRow> LoadAutoEnrollmentRows(CertificateAutoEnrollmentSettings settings)
    {
        return
        [
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.AutoEnrollmentConfigurationModel),
                Setting = FormatAutoEnrollmentState(settings.State)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.AutoEnrollmentRenewExpired),
                Setting = FormatAutoEnrollmentOption(settings.State, settings.EnableMyStoreManagement)
            },
            new PublicKeyPolicyRow
            {
                Kind = PublicKeyPolicyRowKind.Setting,
                Name = GetString(PublicKeyPolicyKeys.AutoEnrollmentUpdateTemplates),
                Setting = FormatAutoEnrollmentOption(settings.State, settings.EnableTemplateCheck)
            }
        ];
    }

    private IReadOnlyList<PublicKeyPolicyDetailItem> LoadCertificatePathValidationDetails(CertificatePathValidationSettings settings)
    {
        uint? chainOptions = ReadDword(ChainEngineConfigPath, ChainEngineOptionsValueName);
        uint? disableRootAutoUpdate = ReadDword(AuthRootPath, DisableRootAutoUpdateValueName);
        uint? enableDisallowedAutoUpdate = ReadDword(AuthRootPath, EnableDisallowedCertAutoUpdateValueName);

        List<PublicKeyPolicyDetailItem> details =
        [
            CreateDetail(PublicKeyPolicyKeys.PathValidationStoresDefined, FormatDefined(settings.StoresDefined)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationAllowUserTrustedRoots, FormatOption(settings.StoresDefined, settings.AllowUserTrustedRoots)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationAllowPeerTrust, FormatOption(settings.StoresDefined, settings.AllowPeerTrustCertificates)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationPeerTrustPurposes, FormatPeerTrustPurposes(settings.PeerTrustPurposeOids)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationRootStoresMode, FormatRootStoresMode(settings.OnlyTrustEnterpriseRoots)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationUpnConstraints, FormatOption(settings.StoresDefined, settings.RequireUpnNameConstraints)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedPublishersDefined, FormatDefined(settings.TrustedPublishersDefined)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedPublisherManagement, FormatTrustedPublisherScope(settings.TrustedPublisherScope)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedPublisherRevocation, FormatOption(settings.TrustedPublishersDefined, settings.CheckPublisherRevocation)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedTimestampRevocation, FormatOption(settings.TrustedPublishersDefined, settings.CheckTimestampRevocation)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationNetworkRetrievalDefined, FormatDefined(settings.NetworkRetrievalDefined)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationAiaRetrieval, FormatAiaRetrieval(chainOptions)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationRootAutoUpdate, FormatDisabledWhenOne(disableRootAutoUpdate)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationDisallowedAutoUpdate, FormatEnabledWhenOne(enableDisallowedAutoUpdate)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationUrlRetrievalTimeout, FormatSeconds(settings.NetworkRetrievalDefined, settings.UrlRetrievalTimeoutSeconds)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationPathRetrievalTimeout, FormatSeconds(settings.NetworkRetrievalDefined, settings.PathValidationRetrievalTimeoutSeconds)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationCrossCertInterval, FormatHours(settings.NetworkRetrievalDefined, settings.CrossCertificateDownloadIntervalHours)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationRevocationDefined, FormatDefined(settings.RevocationDefined)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationPreferCrlBeforeOcsp, FormatOption(settings.RevocationDefined, settings.PreferCrlBeforeOcsp)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationCachedOcspThreshold, FormatNullableNumber(settings.RevocationDefined && settings.PreferCrlBeforeOcsp, settings.CachedOcspResponseThreshold)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationExtendRevocationLifetime, FormatOption(settings.RevocationDefined, settings.ExtendRevocationFreshnessLifetime)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationRevocationExtensionHours, FormatHours(settings.RevocationDefined && settings.ExtendRevocationFreshnessLifetime, settings.RevocationFreshnessExtensionHours)),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedRoots, FormatCertificateCount("Root")),
            CreateDetail(PublicKeyPolicyKeys.PathValidationTrustedPublishers, FormatCertificateCount("TrustedPublisher")),
            CreateDetail(PublicKeyPolicyKeys.PathValidationIntermediateCas, FormatCertificateCount("CA")),
            CreateDetail(PublicKeyPolicyKeys.PathValidationChainEnginePolicySource, $@"HKLM\{ChainEngineConfigPath}"),
            CreateDetail(PublicKeyPolicyKeys.PathValidationAuthRootPolicySource, $@"HKLM\{AuthRootPath}")
        ];

        foreach (PublicKeyPolicyDetailItem rawValue in LoadRegistryValueDetails(ChainEngineConfigPath))
        {
            details.Add(rawValue);
        }

        foreach (PublicKeyPolicyDetailItem rawValue in LoadRegistryValueDetails(AuthRootPath))
        {
            details.Add(rawValue);
        }

        return details;
    }

    private IReadOnlyList<PublicKeyPolicyDetailItem> LoadAutoEnrollmentDetails(CertificateAutoEnrollmentSettings settings)
    {
        object? storeName = ReadRegistryValue(AutoEnrollmentPolicyPath, StoreNameValueName);
        uint? expirationPercentage = ReadDword(AutoEnrollmentPolicyPath, OfflineExpirationPercentValueName);
        uint? balloonNotifications = ReadDword(AutoEnrollmentPolicyPath, EnableBalloonNotificationsValueName);

        List<PublicKeyPolicyDetailItem> details =
        [
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentConfigurationModel, FormatAutoEnrollmentState(settings.State)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentRenewExpired, FormatAutoEnrollmentOption(settings.State, settings.EnableMyStoreManagement)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentUpdateTemplates, FormatAutoEnrollmentOption(settings.State, settings.EnableTemplateCheck)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentExpirationNotifications, FormatNullableDword(expirationPercentage)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentBalloonNotifications, FormatNullableBoolean(balloonNotifications)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentStoreNames, FormatRegistryValue(storeName)),
            CreateDetail(PublicKeyPolicyKeys.AutoEnrollmentPolicySource, $@"HKLM\{AutoEnrollmentPolicyPath}")
        ];

        foreach (PublicKeyPolicyDetailItem rawValue in LoadRegistryValueDetails(AutoEnrollmentPolicyPath))
        {
            details.Add(rawValue);
        }

        return details;
    }

    private CertificateEnrollmentPolicySettings LoadEnrollmentPolicySettings()
    {
        PolFile snapshot = LoadLocalPolicySnapshotForRead();
        List<CertificateEnrollmentPolicyServer> servers = LoadEnrollmentPolicyServers(snapshot);
        bool hasPolicyServersRoot = snapshot.ContainsValue(EnrollmentPolicyServersPath, EnrollmentPolicyServersFlagsValueName)
            || servers.Count > 0;

        CertificateEnrollmentPolicySettings settings = new()
        {
            State = hasPolicyServersRoot
                ? CertificateEnrollmentPolicyState.Enabled
                : CertificateEnrollmentPolicyState.NotConfigured
        };

        settings.Servers.AddRange(servers);
        return settings;
    }

    private static List<CertificateEnrollmentPolicyServer> LoadEnrollmentPolicyServers(PolFile snapshot)
    {
        Dictionary<string, CertificateEnrollmentPolicyServer> servers = new(StringComparer.OrdinalIgnoreCase);

        using (RegistryKey? policyServersKey = Registry.LocalMachine.OpenSubKey(EnrollmentPolicyServersPath))
        {
            if (policyServersKey is not null)
            {
                foreach (string serverId in policyServersKey.GetSubKeyNames())
                {
                    using RegistryKey? serverKey = policyServersKey.OpenSubKey(serverId);
                    CertificateEnrollmentPolicyServer? server = serverKey is null
                        ? null
                        : LoadEnrollmentPolicyServerFromRegistry(serverId, serverKey);
                    if (server is not null)
                    {
                        servers[serverId] = server;
                    }
                }
            }
        }

        foreach (string serverId in snapshot.GetKeyNames(EnrollmentPolicyServersPath))
        {
            string serverKeyPath = GetEnrollmentPolicyServerKey(serverId);
            servers.TryGetValue(serverId, out CertificateEnrollmentPolicyServer? existingServer);
            CertificateEnrollmentPolicyServer? mergedServer = MergeEnrollmentPolicyServer(existingServer, snapshot, serverId, serverKeyPath);
            if (mergedServer is null)
            {
                servers.Remove(serverId);
                continue;
            }

            servers[serverId] = mergedServer;
        }

        return servers.Values
            .Where(static server => IsMeaningfulEnrollmentPolicyServer(server))
            .OrderBy(static server => server.Cost)
            .ThenBy(static server => server.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static CertificateEnrollmentPolicyServer? LoadEnrollmentPolicyServerFromRegistry(string serverId, RegistryKey serverKey)
    {
        CertificateEnrollmentPolicyServer server = new()
        {
            Id = serverId,
            Url = ReadRegistryString(serverKey, EnrollmentPolicyServerUrlValueName) ?? string.Empty,
            PolicyId = ReadRegistryString(serverKey, EnrollmentPolicyServerPolicyIdValueName) ?? string.Empty,
            FriendlyName = ReadRegistryString(serverKey, EnrollmentPolicyServerFriendlyNameValueName) ?? string.Empty,
            Flags = ReadRegistryDword(serverKey, EnrollmentPolicyServersFlagsValueName) ?? 0U,
            AuthType = NormalizeEnrollmentPolicyAuthType(ReadRegistryDword(serverKey, EnrollmentPolicyServerAuthFlagsValueName)),
            Cost = ReadRegistryDword(serverKey, EnrollmentPolicyServerCostValueName) ?? 0U
        };
        server.AuthenticationText = FormatEnrollmentPolicyAuthType(server.AuthType);
        return IsMeaningfulEnrollmentPolicyServer(server) ? server : null;
    }

    private static CertificateEnrollmentPolicyServer? MergeEnrollmentPolicyServer(
        CertificateEnrollmentPolicyServer? existingServer,
        PolFile snapshot,
        string serverId,
        string serverKeyPath)
    {
        CertificateEnrollmentPolicyServer server = existingServer is null
            ? new CertificateEnrollmentPolicyServer { Id = serverId }
            : CloneEnrollmentPolicyServer(existingServer);

        ApplySnapshotString(snapshot, serverKeyPath, EnrollmentPolicyServerUrlValueName, value => server.Url = value);
        ApplySnapshotString(snapshot, serverKeyPath, EnrollmentPolicyServerPolicyIdValueName, value => server.PolicyId = value);
        ApplySnapshotString(snapshot, serverKeyPath, EnrollmentPolicyServerFriendlyNameValueName, value => server.FriendlyName = value);
        ApplySnapshotDword(snapshot, serverKeyPath, EnrollmentPolicyServersFlagsValueName, value => server.Flags = value);
        ApplySnapshotDword(snapshot, serverKeyPath, EnrollmentPolicyServerAuthFlagsValueName, value => server.AuthType = NormalizeEnrollmentPolicyAuthType(value));
        ApplySnapshotDword(snapshot, serverKeyPath, EnrollmentPolicyServerCostValueName, value => server.Cost = value);

        server.AuthenticationText = FormatEnrollmentPolicyAuthType(server.AuthType);
        return IsMeaningfulEnrollmentPolicyServer(server) ? server : null;
    }

    private static CertificateEnrollmentPolicyServer CloneEnrollmentPolicyServer(CertificateEnrollmentPolicyServer server)
    {
        return new CertificateEnrollmentPolicyServer
        {
            Id = server.Id,
            Url = server.Url,
            PolicyId = server.PolicyId,
            FriendlyName = server.FriendlyName,
            Flags = server.Flags,
            AuthType = server.AuthType,
            Cost = server.Cost,
            AuthenticationText = server.AuthenticationText
        };
    }

    private static void ApplySnapshotString(
        PolFile snapshot,
        string keyPath,
        string valueName,
        Action<string> apply)
    {
        if (snapshot.WillDeleteValue(keyPath, valueName))
        {
            apply(string.Empty);
            return;
        }

        if (snapshot.ContainsValue(keyPath, valueName))
        {
            apply(snapshot.GetValue(keyPath, valueName)?.ToString() ?? string.Empty);
        }
    }

    private static void ApplySnapshotDword(
        PolFile snapshot,
        string keyPath,
        string valueName,
        Action<uint> apply)
    {
        if (snapshot.WillDeleteValue(keyPath, valueName))
        {
            apply(0U);
            return;
        }

        if (ReadDword(snapshot, keyPath, valueName) is uint value)
        {
            apply(value);
        }
    }

    private static bool IsMeaningfulEnrollmentPolicyServer(CertificateEnrollmentPolicyServer server)
    {
        return !string.IsNullOrWhiteSpace(server.Url)
            || !string.IsNullOrWhiteSpace(server.FriendlyName)
            || !string.IsNullOrWhiteSpace(server.PolicyId);
    }

    private static void SaveEnrollmentPolicyServer(PolFile snapshot, CertificateEnrollmentPolicyServer server)
    {
        if (string.IsNullOrWhiteSpace(server.Url))
        {
            return;
        }

        string serverId = NormalizeEnrollmentPolicyServerId(server);
        string serverKeyPath = GetEnrollmentPolicyServerKey(serverId);
        string policyId = string.IsNullOrWhiteSpace(server.PolicyId)
            ? $"{{{Guid.NewGuid():D}}}".ToUpperInvariant()
            : server.PolicyId.Trim();
        string friendlyName = string.IsNullOrWhiteSpace(server.FriendlyName)
            ? server.Url.Trim()
            : server.FriendlyName.Trim();
        uint cost = server.Cost == 0U ? EnrollmentPolicyServerDefaultCost : server.Cost;

        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServerUrlValueName, server.Url.Trim(), RegistryValueKind.String);
        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServerPolicyIdValueName, policyId, RegistryValueKind.String);
        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServerFriendlyNameValueName, friendlyName, RegistryValueKind.String);
        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServersFlagsValueName, server.Flags, RegistryValueKind.DWord);
        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServerAuthFlagsValueName, (uint)NormalizeEnrollmentPolicyAuthType((uint)server.AuthType), RegistryValueKind.DWord);
        snapshot.SetValue(serverKeyPath, EnrollmentPolicyServerCostValueName, cost, RegistryValueKind.DWord);
    }

    private static void ClearEnrollmentPolicyServerTree(PolFile snapshot)
    {
        ClearTree(snapshot, EnrollmentPolicyServersPath);

        using RegistryKey? policyServersKey = Registry.LocalMachine.OpenSubKey(EnrollmentPolicyServersPath);
        if (policyServersKey is null)
        {
            return;
        }

        ClearRegistryKeyValuesInSnapshot(snapshot, EnrollmentPolicyServersPath, policyServersKey);
    }

    private static void ClearRegistryKeyValuesInSnapshot(PolFile snapshot, string keyPath, RegistryKey key)
    {
        foreach (string valueName in key.GetValueNames())
        {
            snapshot.DeleteValue(keyPath, valueName);
        }

        foreach (string subKeyName in key.GetSubKeyNames())
        {
            using RegistryKey? subKey = key.OpenSubKey(subKeyName);
            if (subKey is not null)
            {
                ClearRegistryKeyValuesInSnapshot(snapshot, $@"{keyPath}\{subKeyName}", subKey);
            }
        }
    }

    private static string GetEnrollmentPolicyServerKey(string serverId)
    {
        return $@"{EnrollmentPolicyServersPath}\{serverId}";
    }

    private static string NormalizeEnrollmentPolicyServerId(CertificateEnrollmentPolicyServer server)
    {
        string serverId = server.Id.Trim();
        if (!string.IsNullOrWhiteSpace(serverId)
            && !serverId.Contains('\\', StringComparison.Ordinal)
            && !serverId.Contains('/', StringComparison.Ordinal))
        {
            return serverId;
        }

        string seed = string.IsNullOrWhiteSpace(server.Url) ? Guid.NewGuid().ToString("N") : server.Url.Trim();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    private static CertificateEnrollmentPolicyAuthType NormalizeEnrollmentPolicyAuthType(uint? authType)
    {
        return authType switch
        {
            (uint)CertificateEnrollmentPolicyAuthType.Anonymous => CertificateEnrollmentPolicyAuthType.Anonymous,
            (uint)CertificateEnrollmentPolicyAuthType.UserName => CertificateEnrollmentPolicyAuthType.UserName,
            (uint)CertificateEnrollmentPolicyAuthType.ClientCertificate => CertificateEnrollmentPolicyAuthType.ClientCertificate,
            _ => CertificateEnrollmentPolicyAuthType.Kerberos
        };
    }

    private IReadOnlyList<PublicKeyPolicyDetailItem> LoadEfsPolicyDetails(EfsPolicySettings settings)
    {
        List<PublicKeyPolicyDetailItem> details =
        [
            CreateDetail(PublicKeyPolicyKeys.EfsFileEncryption, FormatEfsFileEncryptionState(settings.FileEncryptionState)),
            CreateDetail(PublicKeyPolicyKeys.EfsEllipticCurveCryptography, FormatEfsEllipticCurveMode(settings.EllipticCurveMode)),
            CreateDetail(PublicKeyPolicyKeys.EfsEncryptDocumentsFolder, FormatEfsOption(settings.FileEncryptionState, settings.EncryptDocumentsFolder)),
            CreateDetail(PublicKeyPolicyKeys.EfsRequireSmartCard, FormatEfsOption(settings.FileEncryptionState, settings.RequireSmartCard)),
            CreateDetail(PublicKeyPolicyKeys.EfsCreateSmartCardUserKey, FormatEfsOption(settings.FileEncryptionState, settings.CreateCachingCapableUserKey)),
            CreateDetail(PublicKeyPolicyKeys.EfsKeyBackupNotifications, FormatEfsOption(settings.FileEncryptionState, settings.DisplayKeyBackupNotifications)),
            CreateDetail(PublicKeyPolicyKeys.EfsTemplateName, FormatEfsText(settings.FileEncryptionState, settings.TemplateName)),
            CreateDetail(PublicKeyPolicyKeys.EfsAllowSelfSigned, FormatEfsOption(settings.FileEncryptionState, settings.AllowSelfSignedCertificates)),
            CreateDetail(PublicKeyPolicyKeys.EfsRsaKeySize, FormatEfsNumber(settings.FileEncryptionState, settings.RsaKeyLength)),
            CreateDetail(PublicKeyPolicyKeys.EfsEccKeySize, FormatEfsNumber(settings.FileEncryptionState, settings.EccKeyLength)),
            CreateDetail(PublicKeyPolicyKeys.EfsClearCacheOnTimeout, FormatEfsOption(settings.FileEncryptionState, settings.ClearCacheOnTimeout)),
            CreateDetail(PublicKeyPolicyKeys.EfsCacheTimeout, FormatEfsNumber(settings.FileEncryptionState, settings.CacheTimeoutMinutes)),
            CreateDetail(PublicKeyPolicyKeys.EfsClearCacheOnLock, FormatEfsOption(settings.FileEncryptionState, settings.ClearCacheOnLock)),
            CreateDetail(PublicKeyPolicyKeys.EfsPolicySource, $@"HKLM\{EfsPolicyPath}")
        ];

        foreach (PublicKeyPolicyDetailItem rawValue in LoadRegistryValueDetails(EfsPolicyPath))
        {
            details.Add(rawValue);
        }

        return details;
    }

    private CertificateAutoEnrollmentSettings LoadAutoEnrollmentSettings()
    {
        PolFile snapshot = LoadLocalPolicySnapshotForRead();
        uint? policy = ReadDword(snapshot, AutoEnrollmentPolicyPath, AutoEnrollmentPolicyValueName);

        return new CertificateAutoEnrollmentSettings
        {
            State = policy switch
            {
                null => CertificateAutoEnrollmentPolicyState.NotConfigured,
                uint value when (value & AutoEnrollmentDisabledFlag) == AutoEnrollmentDisabledFlag
                    => CertificateAutoEnrollmentPolicyState.Disabled,
                _ => CertificateAutoEnrollmentPolicyState.Enabled
            },
            EnableMyStoreManagement = policy is not null
                && (policy.Value & AutoEnrollmentStoreManagementFlags) == AutoEnrollmentStoreManagementFlags,
            EnableTemplateCheck = policy is not null
                && (policy.Value & AutoEnrollmentTemplateCheckFlag) == AutoEnrollmentTemplateCheckFlag
        };
    }

    private EfsPolicySettings LoadEfsPolicySettings()
    {
        PolFile snapshot = LoadLocalPolicySnapshotForRead();
        uint? configuration = ReadDword(snapshot, EfsPolicyPath, EfsConfigurationValueName);
        uint options = ReadDword(snapshot, EfsPolicyPath, EfsOptionsValueName) ?? EfsDefaultOptions;

        return new EfsPolicySettings
        {
            FileEncryptionState = configuration switch
            {
                null => EfsFileEncryptionState.NotDefined,
                1U => EfsFileEncryptionState.DontAllow,
                _ => EfsFileEncryptionState.Allow
            },
            EllipticCurveMode = GetEfsEllipticCurveMode(options),
            EncryptDocumentsFolder = (options & EfsEncryptDocumentsFolderFlag) == EfsEncryptDocumentsFolderFlag,
            RequireSmartCard = (options & EfsRequireSmartCardFlag) == EfsRequireSmartCardFlag,
            CreateCachingCapableUserKey = (options & EfsCreateSmartCardUserKeyFlag) == EfsCreateSmartCardUserKeyFlag,
            DisplayKeyBackupNotifications = (options & EfsKeyBackupNotificationFlag) == EfsKeyBackupNotificationFlag,
            TemplateName = ReadString(snapshot, EfsPolicyPath, EfsTemplateNameValueName) ?? EfsDefaultTemplateName,
            AllowSelfSignedCertificates = (options & EfsAllowSelfSignedCertificatesFlag) == EfsAllowSelfSignedCertificatesFlag,
            RsaKeyLength = NormalizeRsaKeyLength(
                ToBoundedInt(
                    ReadDword(snapshot, EfsPolicyPath, EfsRsaKeyLengthValueName),
                    EfsDefaultRsaKeyLength,
                    minimum: 1024,
                    maximum: 16384)),
            EccKeyLength = NormalizeEccKeyLength(ReadString(snapshot, EfsPolicyPath, EfsSuiteBAlgorithmValueName)),
            ClearCacheOnTimeout = (options & EfsClearCacheOnTimeoutFlag) == EfsClearCacheOnTimeoutFlag,
            CacheTimeoutMinutes = ToBoundedInt(
                ReadDword(snapshot, EfsPolicyPath, EfsCacheTimeoutValueName),
                EfsDefaultCacheTimeoutMinutes,
                minimum: EfsMinimumCacheTimeoutMinutes,
                maximum: EfsMaximumCacheTimeoutMinutes),
            ClearCacheOnLock = (options & EfsClearCacheOnLockFlag) == EfsClearCacheOnLockFlag
        };
    }

    private CertificatePathValidationSettings LoadCertificatePathValidationSettings()
    {
        PolFile snapshot = LoadLocalPolicySnapshotForRead();
        uint protectedRootFlags = ReadDword(snapshot, ProtectedRootsPath, ProtectedRootsFlagsValueName) ?? 0U;
        uint trustedPublisherFlags = ReadDword(snapshot, TrustedPublisherSaferPath, AuthenticodeFlagsValueName) ?? 0U;
        uint chainOptions = ReadDword(snapshot, ChainEngineConfigPath, ChainEngineOptionsValueName) ?? 0U;
        uint? disableRootAutoUpdate = ReadDword(snapshot, AuthRootPath, DisableRootAutoUpdateValueName);
        uint? urlRetrievalTimeoutMilliseconds = ReadDword(snapshot, ChainEngineConfigPath, ChainUrlRetrievalTimeoutMillisecondsValueName);
        uint? pathRetrievalTimeoutMilliseconds = ReadDword(snapshot, ChainEngineConfigPath, ChainRevAccumulativeUrlRetrievalTimeoutMillisecondsValueName);
        uint? crossCertIntervalHours = ReadDword(snapshot, ChainEngineConfigPath, CrossCertDownloadIntervalHoursValueName);
        uint? cachedOcspThreshold = ReadDword(snapshot, ChainEngineConfigPath, CryptnetCachedOcspSwitchToCrlCountValueName);
        uint? crlValidityExtensionHours = ReadDword(snapshot, ChainEngineConfigPath, CrlValidityExtensionPeriodValueName);

        var settings = new CertificatePathValidationSettings
        {
            StoresDefined = snapshot.ContainsValue(ProtectedRootsPath, ProtectedRootsFlagsValueName)
                || snapshot.ContainsValue(ProtectedRootsPath, PeerUsagesValueName),
            AllowUserTrustedRoots = (protectedRootFlags & ProtectedRootsDisableCurrentUserFlag) != ProtectedRootsDisableCurrentUserFlag,
            AllowPeerTrustCertificates = (protectedRootFlags & ProtectedRootsDisablePeerTrustFlag) != ProtectedRootsDisablePeerTrustFlag,
            OnlyTrustEnterpriseRoots = (protectedRootFlags & ProtectedRootsOnlyEnterpriseRootsFlag) == ProtectedRootsOnlyEnterpriseRootsFlag,
            RequireUpnNameConstraints = (protectedRootFlags & ProtectedRootsRequireUpnNameConstraintsFlag) == ProtectedRootsRequireUpnNameConstraintsFlag,
            TrustedPublishersDefined = snapshot.ContainsValue(TrustedPublisherSaferPath, AuthenticodeFlagsValueName),
            TrustedPublisherScope = (PublicKeyTrustedPublisherScope)(trustedPublisherFlags & TrustedPublisherScopeMask),
            CheckPublisherRevocation = (trustedPublisherFlags & TrustedPublisherRevocationFlag) == TrustedPublisherRevocationFlag,
            CheckTimestampRevocation = (trustedPublisherFlags & TrustedPublisherTimestampRevocationFlag) == TrustedPublisherTimestampRevocationFlag,
            NetworkRetrievalDefined = ContainsAnyValue(
                snapshot,
                AuthRootPath,
                DisableRootAutoUpdateValueName)
                || ContainsAnyValue(
                    snapshot,
                    ChainEngineConfigPath,
                    ChainUrlRetrievalTimeoutMillisecondsValueName,
                    ChainRevAccumulativeUrlRetrievalTimeoutMillisecondsValueName,
                    ChainEngineOptionsValueName,
                    CrossCertDownloadIntervalHoursValueName),
            AutomaticallyUpdateRootCertificates = disableRootAutoUpdate != 1U,
            UrlRetrievalTimeoutSeconds = MillisecondsToSeconds(urlRetrievalTimeoutMilliseconds, defaultSeconds: 15),
            PathValidationRetrievalTimeoutSeconds = MillisecondsToSeconds(pathRetrievalTimeoutMilliseconds, defaultSeconds: 20),
            AllowAiaRetrieval = (chainOptions & DisableAiaRetrievalOptionsFlag) != DisableAiaRetrievalOptionsFlag,
            CrossCertificateDownloadIntervalHours = ToBoundedInt(crossCertIntervalHours, defaultValue: 168, minimum: 0, maximum: 9999),
            RevocationDefined = ContainsAnyValue(
                snapshot,
                ChainEngineConfigPath,
                CryptnetCachedOcspSwitchToCrlCountValueName,
                CrlValidityExtensionPeriodValueName),
            PreferCrlBeforeOcsp = cachedOcspThreshold is not null && cachedOcspThreshold.Value > 0U,
            CachedOcspResponseThreshold = ToBoundedInt(cachedOcspThreshold, defaultValue: 50, minimum: 0, maximum: 9999),
            ExtendRevocationFreshnessLifetime = crlValidityExtensionHours is not null && crlValidityExtensionHours.Value > 0U,
            RevocationFreshnessExtensionHours = ToBoundedInt(crlValidityExtensionHours, defaultValue: 12, minimum: 0, maximum: 9999)
        };

        foreach (string oid in ReadMultiString(snapshot, ProtectedRootsPath, PeerUsagesValueName))
        {
            settings.PeerTrustPurposeOids.Add(oid);
        }

        if (settings.PeerTrustPurposeOids.Count == 0)
        {
            settings.PeerTrustPurposeOids.Add(ClientAuthenticationOid);
            settings.PeerTrustPurposeOids.Add(SecureEmailOid);
            settings.PeerTrustPurposeOids.Add(EncryptingFileSystemOid);
        }

        return settings;
    }

    private static void SavePathValidationStores(PolFile snapshot, CertificatePathValidationSettings settings)
    {
        if (!settings.StoresDefined)
        {
            snapshot.DeleteValue(ProtectedRootsPath, ProtectedRootsFlagsValueName);
            snapshot.DeleteValue(ProtectedRootsPath, PeerUsagesValueName);
            return;
        }

        uint existingFlags = ReadDword(snapshot, ProtectedRootsPath, ProtectedRootsFlagsValueName) ?? 0U;
        uint flags = existingFlags
            & ~(ProtectedRootsDisableCurrentUserFlag
                | ProtectedRootsOnlyEnterpriseRootsFlag
                | ProtectedRootsRequireUpnNameConstraintsFlag
                | ProtectedRootsDisablePeerTrustFlag);

        if (!settings.AllowUserTrustedRoots)
        {
            flags |= ProtectedRootsDisableCurrentUserFlag;
        }

        if (!settings.AllowPeerTrustCertificates)
        {
            flags |= ProtectedRootsDisablePeerTrustFlag;
        }

        if (settings.OnlyTrustEnterpriseRoots)
        {
            flags |= ProtectedRootsOnlyEnterpriseRootsFlag;
        }

        if (settings.RequireUpnNameConstraints)
        {
            flags |= ProtectedRootsRequireUpnNameConstraintsFlag;
        }

        snapshot.SetValue(ProtectedRootsPath, ProtectedRootsFlagsValueName, flags, RegistryValueKind.DWord);

        string[] peerUsages = settings.PeerTrustPurposeOids
            .Where(static oid => !string.IsNullOrWhiteSpace(oid))
            .Select(static oid => oid.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (peerUsages.Length == 0)
        {
            snapshot.DeleteValue(ProtectedRootsPath, PeerUsagesValueName);
        }
        else
        {
            snapshot.SetValue(ProtectedRootsPath, PeerUsagesValueName, peerUsages, RegistryValueKind.MultiString);
        }
    }

    private static void SavePathValidationTrustedPublishers(PolFile snapshot, CertificatePathValidationSettings settings)
    {
        if (!settings.TrustedPublishersDefined)
        {
            snapshot.DeleteValue(TrustedPublisherSaferPath, AuthenticodeFlagsValueName);
            return;
        }

        uint flags = (uint)settings.TrustedPublisherScope & TrustedPublisherScopeMask;
        if (settings.CheckPublisherRevocation)
        {
            flags |= TrustedPublisherRevocationFlag;
        }

        if (settings.CheckTimestampRevocation)
        {
            flags |= TrustedPublisherTimestampRevocationFlag;
        }

        snapshot.SetValue(TrustedPublisherSaferPath, AuthenticodeFlagsValueName, flags, RegistryValueKind.DWord);
    }

    private static void SavePathValidationNetworkRetrieval(PolFile snapshot, CertificatePathValidationSettings settings)
    {
        uint existingOptions = ReadDword(snapshot, ChainEngineConfigPath, ChainEngineOptionsValueName) ?? 0U;
        if (!settings.NetworkRetrievalDefined)
        {
            snapshot.DeleteValue(AuthRootPath, DisableRootAutoUpdateValueName);
            snapshot.DeleteValue(ChainEngineConfigPath, ChainUrlRetrievalTimeoutMillisecondsValueName);
            snapshot.DeleteValue(ChainEngineConfigPath, ChainRevAccumulativeUrlRetrievalTimeoutMillisecondsValueName);
            snapshot.DeleteValue(ChainEngineConfigPath, CrossCertDownloadIntervalHoursValueName);
            SaveChainOptionsWithoutAiaOverride(snapshot, existingOptions);
            return;
        }

        snapshot.SetValue(
            AuthRootPath,
            DisableRootAutoUpdateValueName,
            settings.AutomaticallyUpdateRootCertificates ? 0U : 1U,
            RegistryValueKind.DWord);
        snapshot.SetValue(
            ChainEngineConfigPath,
            ChainUrlRetrievalTimeoutMillisecondsValueName,
            (uint)(Clamp(settings.UrlRetrievalTimeoutSeconds) * 1000),
            RegistryValueKind.DWord);
        snapshot.SetValue(
            ChainEngineConfigPath,
            ChainRevAccumulativeUrlRetrievalTimeoutMillisecondsValueName,
            (uint)(Clamp(settings.PathValidationRetrievalTimeoutSeconds) * 1000),
            RegistryValueKind.DWord);
        snapshot.SetValue(
            ChainEngineConfigPath,
            CrossCertDownloadIntervalHoursValueName,
            (uint)Clamp(settings.CrossCertificateDownloadIntervalHours),
            RegistryValueKind.DWord);

        uint options = settings.AllowAiaRetrieval
            ? existingOptions & ~DisableAiaRetrievalOptionsFlag
            : existingOptions | DisableAiaRetrievalOptionsFlag;
        snapshot.SetValue(ChainEngineConfigPath, ChainEngineOptionsValueName, options, RegistryValueKind.DWord);
    }

    private static void SavePathValidationRevocation(PolFile snapshot, CertificatePathValidationSettings settings)
    {
        if (!settings.RevocationDefined)
        {
            snapshot.DeleteValue(ChainEngineConfigPath, CryptnetCachedOcspSwitchToCrlCountValueName);
            snapshot.DeleteValue(ChainEngineConfigPath, CrlValidityExtensionPeriodValueName);
            return;
        }

        if (settings.PreferCrlBeforeOcsp)
        {
            snapshot.SetValue(
                ChainEngineConfigPath,
                CryptnetCachedOcspSwitchToCrlCountValueName,
                (uint)Clamp(settings.CachedOcspResponseThreshold),
                RegistryValueKind.DWord);
        }
        else
        {
            snapshot.DeleteValue(ChainEngineConfigPath, CryptnetCachedOcspSwitchToCrlCountValueName);
        }

        if (settings.ExtendRevocationFreshnessLifetime)
        {
            snapshot.SetValue(
                ChainEngineConfigPath,
                CrlValidityExtensionPeriodValueName,
                (uint)Clamp(settings.RevocationFreshnessExtensionHours),
                RegistryValueKind.DWord);
        }
        else
        {
            snapshot.DeleteValue(ChainEngineConfigPath, CrlValidityExtensionPeriodValueName);
        }
    }

    private static void SaveChainOptionsWithoutAiaOverride(PolFile snapshot, uint existingOptions)
    {
        uint options = existingOptions & ~DisableAiaRetrievalOptionsFlag;
        if (options == 0U)
        {
            snapshot.DeleteValue(ChainEngineConfigPath, ChainEngineOptionsValueName);
        }
        else
        {
            snapshot.SetValue(ChainEngineConfigPath, ChainEngineOptionsValueName, options, RegistryValueKind.DWord);
        }
    }

    private static uint CreateEfsOptions(EfsPolicySettings settings)
    {
        uint options = 0U;

        if (settings.EncryptDocumentsFolder)
        {
            options |= EfsEncryptDocumentsFolderFlag;
        }

        if (settings.CreateCachingCapableUserKey)
        {
            options |= EfsCreateSmartCardUserKeyFlag;
        }

        if (settings.AllowSelfSignedCertificates)
        {
            options |= EfsAllowSelfSignedCertificatesFlag;
        }

        if (settings.ClearCacheOnTimeout)
        {
            options |= EfsClearCacheOnTimeoutFlag;
        }

        if (settings.ClearCacheOnLock)
        {
            options |= EfsClearCacheOnLockFlag;
        }

        if (settings.RequireSmartCard)
        {
            options |= EfsRequireSmartCardFlag;
        }

        if (settings.DisplayKeyBackupNotifications)
        {
            options |= EfsKeyBackupNotificationFlag;
        }

        options |= settings.EllipticCurveMode switch
        {
            EfsEllipticCurveMode.Require => EfsRequireEccKeysFlag,
            EfsEllipticCurveMode.DontAllow => EfsDisallowEccKeysFlag,
            _ => 0U
        };

        return options;
    }

    private static EfsEllipticCurveMode GetEfsEllipticCurveMode(uint options)
    {
        if ((options & EfsRequireEccKeysFlag) == EfsRequireEccKeysFlag)
        {
            return EfsEllipticCurveMode.Require;
        }

        if ((options & EfsDisallowEccKeysFlag) == EfsDisallowEccKeysFlag)
        {
            return EfsEllipticCurveMode.DontAllow;
        }

        return EfsEllipticCurveMode.Allow;
    }

    private PolFile LoadLocalPolicySnapshotForRead()
    {
        try
        {
            return _localPolicyFileStore.LoadSnapshot(isUser: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load local machine Registry.pol for Public Key Policies.");
            return new PolFile();
        }
    }

    private PublicKeyPolicyRow CreateStoreCountRow(string nameKey, string storeName)
    {
        int count = CountStoreCertificates(storeName);
        return new PublicKeyPolicyRow
        {
            Kind = PublicKeyPolicyRowKind.Setting,
            Name = GetString(nameKey),
            Setting = string.Format(
                CultureInfo.CurrentCulture,
                GetString(PublicKeyPolicyKeys.CertificateCountFormat),
                count),
            Source = $@"HKLM\{SystemCertificatesPolicyRoot}\{storeName}\Certificates"
        };
    }

    private static int CountStoreCertificates(string storeName)
    {
        using RegistryKey? certificatesKey = Registry.LocalMachine.OpenSubKey($@"{SystemCertificatesPolicyRoot}\{storeName}\Certificates");
        return certificatesKey?.GetSubKeyNames().Length ?? 0;
    }

    private static int CountEnrollmentPolicyServers()
    {
        using RegistryKey? policyServersKey = Registry.LocalMachine.OpenSubKey(EnrollmentPolicyServersPath);
        return policyServersKey?.GetSubKeyNames().Length ?? 0;
    }

    private static uint? ReadDword(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
        object? value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static uint? ReadRegistryDword(RegistryKey key, string valueName)
    {
        object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadRegistryString(RegistryKey key, string valueName)
    {
        object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            string text => text,
            _ => null
        };
    }

    private static uint? ReadDword(PolFile snapshot, string keyPath, string valueName)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadString(PolFile snapshot, string keyPath, string valueName)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => null
        };
    }

    private static bool ContainsAnyValue(PolFile snapshot, string keyPath, params string[] valueNames)
    {
        return valueNames.Any(valueName => snapshot.ContainsValue(keyPath, valueName));
    }

    private static IReadOnlyList<string> ReadMultiString(PolFile snapshot, string keyPath, string valueName)
    {
        return snapshot.GetValue(keyPath, valueName) switch
        {
            string[] strings => strings
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray(),
            string value when !string.IsNullOrWhiteSpace(value) => [value],
            _ => []
        };
    }

    private static int MillisecondsToSeconds(uint? milliseconds, int defaultSeconds)
    {
        if (milliseconds is null || milliseconds.Value == 0U)
        {
            return defaultSeconds;
        }

        return ToBoundedInt(milliseconds.Value / 1000U, defaultSeconds, minimum: 0, maximum: 9999);
    }

    private static int ToBoundedInt(uint? value, int defaultValue, int minimum, int maximum)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return ToBoundedInt(value.Value, defaultValue, minimum, maximum);
    }

    private static int ToBoundedInt(uint value, int defaultValue, int minimum, int maximum)
    {
        if (value > (uint)maximum)
        {
            return defaultValue;
        }

        return Math.Clamp((int)value, minimum, maximum);
    }

    private static int Clamp(int value)
    {
        return Math.Clamp(value, 0, 9999);
    }

    private static object? ReadRegistryValue(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private static IReadOnlyList<PublicKeyPolicyDetailItem> LoadRegistryValueDetails(string keyPath)
    {
        List<PublicKeyPolicyDetailItem> details = [];

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
        if (key is null)
        {
            return details;
        }

        foreach (string valueName in key.GetValueNames().Order(StringComparer.CurrentCultureIgnoreCase))
        {
            object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            RegistryValueKind kind = key.GetValueKind(valueName);
            details.Add(CreateLiteralDetail(
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(PublicKeyPolicyKeys.RegistryRawValueFormat),
                    valueName),
                $"{kind}: {FormatRegistryValue(value)}"));
        }

        return details;
    }

    private static string FormatAiaRetrieval(uint? value)
    {
        return value switch
        {
            null => GetString(PublicKeyPolicyKeys.NotConfigured),
            DisableAiaRetrievalOptionsValue => GetString(SecPolKeys.ValueDisabled),
            0U => GetString(SecPolKeys.ValueEnabled),
            _ => value.Value.ToString(CultureInfo.CurrentCulture)
        };
    }

    private static string FormatDefined(bool isDefined)
    {
        return isDefined
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(PublicKeyPolicyKeys.NotConfigured);
    }

    private static string FormatOption(bool isDefined, bool isEnabled)
    {
        if (!isDefined)
        {
            return GetString(PublicKeyPolicyKeys.NotConfigured);
        }

        return isEnabled
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(SecPolKeys.ValueDisabled);
    }

    private static string FormatRootStoresMode(bool onlyEnterpriseRoots)
    {
        return onlyEnterpriseRoots
            ? GetString(PublicKeyPolicyKeys.PathValidationOnlyEnterpriseRoots)
            : GetString(PublicKeyPolicyKeys.PathValidationThirdPartyAndEnterpriseRoots);
    }

    private static string FormatTrustedPublisherScope(PublicKeyTrustedPublisherScope scope)
    {
        return scope switch
        {
            PublicKeyTrustedPublisherScope.LocalAdministrators => GetString(PublicKeyPolicyKeys.PathValidationPublisherScopeLocalAdministrators),
            PublicKeyTrustedPublisherScope.EnterpriseAdministrators => GetString(PublicKeyPolicyKeys.PathValidationPublisherScopeEnterpriseAdministrators),
            _ => GetString(PublicKeyPolicyKeys.PathValidationPublisherScopeEndUsers)
        };
    }

    private static string FormatPeerTrustPurposes(IEnumerable<string> oids)
    {
        string[] values = oids
            .Where(static oid => !string.IsNullOrWhiteSpace(oid))
            .Select(static oid => oid.Trim())
            .ToArray();
        return values.Length == 0
            ? GetString(PublicKeyPolicyKeys.NotConfigured)
            : string.Join(", ", values);
    }

    private static string FormatSeconds(bool isDefined, int seconds)
    {
        return FormatNullableNumber(isDefined, seconds);
    }

    private static string FormatHours(bool isDefined, int hours)
    {
        return FormatNullableNumber(isDefined, hours);
    }

    private static string FormatNullableNumber(bool isDefined, int value)
    {
        return isDefined
            ? value.ToString(CultureInfo.CurrentCulture)
            : GetString(PublicKeyPolicyKeys.NotConfigured);
    }

    private static string FormatDisabledWhenOne(uint? value)
    {
        return value switch
        {
            null => GetString(PublicKeyPolicyKeys.NotConfigured),
            1U => GetString(SecPolKeys.ValueDisabled),
            0U => GetString(SecPolKeys.ValueEnabled),
            _ => value.Value.ToString(CultureInfo.CurrentCulture)
        };
    }

    private static string FormatEnabledWhenOne(uint? value)
    {
        return value switch
        {
            null => GetString(PublicKeyPolicyKeys.NotConfigured),
            0U => GetString(SecPolKeys.ValueDisabled),
            1U => GetString(SecPolKeys.ValueEnabled),
            _ => value.Value.ToString(CultureInfo.CurrentCulture)
        };
    }

    private static string FormatCertificateCount(string storeName)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            GetString(PublicKeyPolicyKeys.CertificateCountFormat),
            CountStoreCertificates(storeName));
    }

    private static string FormatAutoEnrollmentState(CertificateAutoEnrollmentPolicyState state)
    {
        return state switch
        {
            CertificateAutoEnrollmentPolicyState.Enabled => GetString(SecPolKeys.ValueEnabled),
            CertificateAutoEnrollmentPolicyState.Disabled => GetString(SecPolKeys.ValueDisabled),
            _ => GetString(PublicKeyPolicyKeys.NotConfigured)
        };
    }

    private static string FormatEnrollmentPolicyState(CertificateEnrollmentPolicyState state)
    {
        return state == CertificateEnrollmentPolicyState.Enabled
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(PublicKeyPolicyKeys.NotConfigured);
    }

    private static string FormatEnrollmentPolicyAuthType(CertificateEnrollmentPolicyAuthType authType)
    {
        return authType switch
        {
            CertificateEnrollmentPolicyAuthType.Anonymous => GetString(PublicKeyPolicyKeys.EnrollmentPolicyAuthAnonymous),
            CertificateEnrollmentPolicyAuthType.UserName => GetString(PublicKeyPolicyKeys.EnrollmentPolicyAuthUserName),
            CertificateEnrollmentPolicyAuthType.ClientCertificate => GetString(PublicKeyPolicyKeys.EnrollmentPolicyAuthClientCertificate),
            _ => GetString(PublicKeyPolicyKeys.EnrollmentPolicyAuthKerberos)
        };
    }

    private static string FormatAutoEnrollmentOption(CertificateAutoEnrollmentPolicyState state, bool enabled)
    {
        if (state == CertificateAutoEnrollmentPolicyState.NotConfigured)
        {
            return GetString(PublicKeyPolicyKeys.NotConfigured);
        }

        if (state == CertificateAutoEnrollmentPolicyState.Disabled)
        {
            return GetString(SecPolKeys.ValueDisabled);
        }

        return enabled
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(SecPolKeys.ValueDisabled);
    }

    private static string FormatEfsFileEncryptionState(EfsFileEncryptionState state)
    {
        return state switch
        {
            EfsFileEncryptionState.Allow => GetString(PublicKeyPolicyKeys.EfsFileEncryptionAllow),
            EfsFileEncryptionState.DontAllow => GetString(PublicKeyPolicyKeys.EfsFileEncryptionDontAllow),
            _ => GetString(SecPolKeys.ValueNotDefined)
        };
    }

    private static string FormatEfsEllipticCurveMode(EfsEllipticCurveMode mode)
    {
        return mode switch
        {
            EfsEllipticCurveMode.Require => GetString(PublicKeyPolicyKeys.EfsEccRequire),
            EfsEllipticCurveMode.DontAllow => GetString(PublicKeyPolicyKeys.EfsEccDontAllow),
            _ => GetString(PublicKeyPolicyKeys.EfsEccAllow)
        };
    }

    private static string FormatEfsOption(EfsFileEncryptionState state, bool enabled)
    {
        return state == EfsFileEncryptionState.NotDefined
            ? GetString(SecPolKeys.ValueNotDefined)
            : enabled
                ? GetString(SecPolKeys.ValueEnabled)
                : GetString(SecPolKeys.ValueDisabled);
    }

    private static string FormatEfsText(EfsFileEncryptionState state, string value)
    {
        return state == EfsFileEncryptionState.NotDefined
            ? GetString(SecPolKeys.ValueNotDefined)
            : value;
    }

    private static string FormatEfsNumber(EfsFileEncryptionState state, int value)
    {
        return state == EfsFileEncryptionState.NotDefined
            ? GetString(SecPolKeys.ValueNotDefined)
            : value.ToString(CultureInfo.CurrentCulture);
    }

    private static string FormatNullableBoolean(uint? value)
    {
        if (value is null)
        {
            return GetString(PublicKeyPolicyKeys.NotConfigured);
        }

        return value.Value == 0U
            ? GetString(SecPolKeys.ValueDisabled)
            : GetString(SecPolKeys.ValueEnabled);
    }

    private static string FormatNullableDword(uint? value)
    {
        return value is null
            ? GetString(PublicKeyPolicyKeys.NotConfigured)
            : value.Value.ToString(CultureInfo.CurrentCulture);
    }

    private static string FormatRegistryValue(object? value)
    {
        return value switch
        {
            null => GetString(PublicKeyPolicyKeys.NotConfigured),
            string[] strings => string.Join(", ", strings),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static int NormalizeRsaKeyLength(int keyLength)
    {
        return keyLength switch
        {
            1024 or 2048 or 4096 or 8192 or 16384 => keyLength,
            _ => EfsDefaultRsaKeyLength
        };
    }

    private static int NormalizeEccKeyLength(string? suiteBAlgorithm)
    {
        return suiteBAlgorithm switch
        {
            EfsSuiteBAlgorithmP384 => 384,
            EfsSuiteBAlgorithmP521 => 521,
            _ => EfsDefaultEccKeyLength
        };
    }

    private static string MapEccKeyLengthToSuiteBAlgorithm(int keyLength)
    {
        return keyLength switch
        {
            384 => EfsSuiteBAlgorithmP384,
            521 => EfsSuiteBAlgorithmP521,
            _ => EfsSuiteBAlgorithmP256
        };
    }

    private static string GetPreferredName(X509Certificate2 certificate, bool forIssuer)
    {
        string simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer);
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            return simpleName;
        }

        string distinguishedName = forIssuer ? certificate.Issuer : certificate.Subject;
        return string.IsNullOrWhiteSpace(distinguishedName)
            ? certificate.Thumbprint ?? string.Empty
            : distinguishedName;
    }

    private static string FormatEnhancedKeyUsage(X509Certificate2 certificate)
    {
        X509EnhancedKeyUsageExtension? eku = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        if (eku is null || eku.EnhancedKeyUsages.Count == 0)
        {
            return GetString(PublicKeyPolicyKeys.IntendedPurposesAll);
        }

        return string.Join(
            ", ",
            eku.EnhancedKeyUsages
                .Cast<Oid>()
                .Select(static oid => string.IsNullOrWhiteSpace(oid.FriendlyName) ? oid.Value : oid.FriendlyName));
    }

    private static string FormatStatus(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        return now < certificate.NotBefore || now > certificate.NotAfter
            ? GetString(PublicKeyPolicyKeys.CertificateExpiredOrNotYetValid)
            : GetString(PublicKeyPolicyKeys.CertificateValid);
    }

    private static string GetCertificateTemplateText(X509Certificate2 certificate)
    {
        const string certificateTemplateOid = "1.3.6.1.4.1.311.21.7";
        const string certificateTemplateNameOid = "1.3.6.1.4.1.311.20.2";

        X509Extension? templateExtension = certificate.Extensions
            .Cast<X509Extension>()
            .FirstOrDefault(extension =>
                string.Equals(extension.Oid?.Value, certificateTemplateOid, StringComparison.Ordinal)
                || string.Equals(extension.Oid?.Value, certificateTemplateNameOid, StringComparison.Ordinal));

        return templateExtension?.Format(multiLine: false) ?? string.Empty;
    }

    private static IReadOnlyList<PublicKeyPolicyDetailItem> BuildCertificateDetails(
        X509Certificate2 certificate,
        string storeName,
        string fallbackThumbprint,
        string status,
        string intendedPurposes,
        string certificateTemplate)
    {
        List<PublicKeyPolicyDetailItem> details =
        [
            CreateDetail(PublicKeyPolicyKeys.DetailSubject, certificate.Subject),
            CreateDetail(PublicKeyPolicyKeys.DetailIssuer, certificate.Issuer),
            CreateDetail(PublicKeyPolicyKeys.DetailSerialNumber, certificate.SerialNumber),
            CreateDetail(PublicKeyPolicyKeys.DetailThumbprint, certificate.Thumbprint ?? fallbackThumbprint),
            CreateDetail(PublicKeyPolicyKeys.DetailValidFrom, certificate.NotBefore.ToString("G", CultureInfo.CurrentCulture)),
            CreateDetail(PublicKeyPolicyKeys.DetailValidTo, certificate.NotAfter.ToString("G", CultureInfo.CurrentCulture)),
            CreateDetail(PublicKeyPolicyKeys.DetailIntendedPurposes, intendedPurposes),
            CreateDetail(PublicKeyPolicyKeys.DetailFriendlyName, certificate.FriendlyName),
            CreateDetail(PublicKeyPolicyKeys.DetailStatus, status),
            CreateDetail(PublicKeyPolicyKeys.DetailCertificateTemplate, certificateTemplate),
            CreateDetail(PublicKeyPolicyKeys.DetailSignatureAlgorithm, FormatOid(certificate.SignatureAlgorithm)),
            CreateDetail(PublicKeyPolicyKeys.DetailPublicKeyAlgorithm, FormatOid(certificate.PublicKey.Oid)),
            CreateDetail(PublicKeyPolicyKeys.DetailStore, storeName)
        ];

        int? keySize = TryGetPublicKeySize(certificate);
        if (keySize is not null)
        {
            details.Insert(
                details.Count - 1,
                CreateDetail(
                    PublicKeyPolicyKeys.DetailPublicKeySize,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        GetString(PublicKeyPolicyKeys.DetailPublicKeySizeFormat),
                        keySize.Value)));
        }

        return details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static PublicKeyPolicyDetailItem CreateDetail(string nameKey, string value)
    {
        return new PublicKeyPolicyDetailItem
        {
            Name = GetString(nameKey),
            Value = value
        };
    }

    private static PublicKeyPolicyDetailItem CreateLiteralDetail(string name, string value)
    {
        return new PublicKeyPolicyDetailItem
        {
            Name = name,
            Value = value
        };
    }

    private static string FormatOid(Oid? oid)
    {
        if (oid is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(oid.FriendlyName)
            ? oid.Value ?? string.Empty
            : $"{oid.FriendlyName} ({oid.Value})";
    }

    private static int? TryGetPublicKeySize(X509Certificate2 certificate)
    {
        try
        {
            using RSA? rsa = certificate.GetRSAPublicKey();
            if (rsa is not null)
            {
                return rsa.KeySize;
            }

            using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa is not null)
            {
                return ecdsa.KeySize;
            }

            using DSA? dsa = certificate.GetDSAPublicKey();
            return dsa?.KeySize;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static void ValidateRecoveryAgentCertificate(
        PublicKeyPolicyNodeKind nodeKind,
        string storeName,
        X509Certificate2 certificate,
        string thumbprint,
        PolFile snapshot)
    {
        if (nodeKind is PublicKeyPolicyNodeKind.EncryptingFileSystem or PublicKeyPolicyNodeKind.DataProtection
            && !HasEnhancedKeyUsage(certificate, EfsRecoveryOid))
        {
            string key = nodeKind == PublicKeyPolicyNodeKind.EncryptingFileSystem
                ? PublicKeyPolicyKeys.RecoveryAgentCertificateNotSuitableEfs
                : PublicKeyPolicyKeys.RecoveryAgentCertificateNotSuitableDataRecovery;
            throw new InvalidOperationException(GetString(key));
        }

        if (nodeKind == PublicKeyPolicyNodeKind.BitLockerDriveEncryption
            && !HasAllowedBitLockerKeyUsage(certificate))
        {
            throw new InvalidOperationException(GetString(PublicKeyPolicyKeys.RecoveryAgentCertificateNotSuitableDataRecovery));
        }

        if (!RequiresSingleRecoveryAgentCertificate(nodeKind))
        {
            return;
        }

        HashSet<string> existingThumbprints = GetRecoveryAgentCertificateThumbprints(snapshot, storeName);
        if (existingThumbprints.Any(existingThumbprint =>
            !string.Equals(existingThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(GetString(PublicKeyPolicyKeys.RecoveryAgentSingleCertificateLimit));
        }
    }

    private static bool HasEnhancedKeyUsage(X509Certificate2 certificate, string oidValue)
    {
        X509EnhancedKeyUsageExtension? eku = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();
        return eku is not null
            && eku.EnhancedKeyUsages
                .Cast<Oid>()
                .Any(oid => string.Equals(oid.Value, oidValue, StringComparison.Ordinal));
    }

    private static bool HasAllowedBitLockerKeyUsage(X509Certificate2 certificate)
    {
        X509KeyUsageExtension? keyUsage = certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();
        if (keyUsage is null)
        {
            return true;
        }

        const X509KeyUsageFlags allowedFlags =
            X509KeyUsageFlags.DataEncipherment
            | X509KeyUsageFlags.KeyAgreement
            | X509KeyUsageFlags.KeyEncipherment;
        return (keyUsage.KeyUsages & allowedFlags) != 0;
    }

    private static bool RequiresSingleRecoveryAgentCertificate(PublicKeyPolicyNodeKind nodeKind)
    {
        return nodeKind is PublicKeyPolicyNodeKind.DataProtection or PublicKeyPolicyNodeKind.BitLockerDriveEncryption;
    }

    private static HashSet<string> GetRecoveryAgentCertificateThumbprints(PolFile snapshot, string storeName)
    {
        var thumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string certificatesPath = $@"{SystemCertificatesPolicyRoot}\{storeName}\Certificates";

        using RegistryKey? certificatesKey = Registry.LocalMachine.OpenSubKey(certificatesPath);
        if (certificatesKey is not null)
        {
            foreach (string subKeyName in certificatesKey.GetSubKeyNames())
            {
                string thumbprint = NormalizeThumbprint(subKeyName);
                if (!string.IsNullOrWhiteSpace(thumbprint))
                {
                    thumbprints.Add(thumbprint);
                }
            }
        }

        foreach (string subKeyName in snapshot.GetKeyNames(certificatesPath))
        {
            string certificateKey = $@"{certificatesPath}\{subKeyName}";
            if (!snapshot.ContainsValue(certificateKey, BlobValueName))
            {
                continue;
            }

            string thumbprint = NormalizeThumbprint(subKeyName);
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                thumbprints.Add(thumbprint);
            }
        }

        return thumbprints;
    }

    private static string GetRecoveryAgentStoreName(PublicKeyPolicyNodeKind nodeKind)
    {
        return nodeKind switch
        {
            PublicKeyPolicyNodeKind.EncryptingFileSystem => "EFS",
            PublicKeyPolicyNodeKind.DataProtection => "DPNGRA",
            PublicKeyPolicyNodeKind.BitLockerDriveEncryption => "FVE",
            _ => throw new InvalidOperationException(GetString(PublicKeyPolicyKeys.NoRecoveryAgents))
        };
    }

    private static bool IsRecoveryAgentStoreName(string storeName)
    {
        return string.Equals(storeName, "EFS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(storeName, "DPNGRA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(storeName, "FVE", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRecoveryAgentCertificateKey(string storeName, string thumbprint)
    {
        return $@"{SystemCertificatesPolicyRoot}\{storeName}\Certificates\{thumbprint}";
    }

    private static string NormalizeThumbprint(string? thumbprint)
    {
        return string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private static void ClearTree(PolFile snapshot, string keyPath)
    {
        foreach (string subKeyName in snapshot.GetKeyNames(keyPath).ToArray())
        {
            ClearTree(snapshot, $@"{keyPath}\{subKeyName}");
        }

        snapshot.ClearKey(keyPath);
    }

    private void EnsureMachinePolicyIsWritable()
    {
        if (_localPolicyFileStore.IsWritable(isUser: false, out string? error))
        {
            return;
        }

        throw new UnauthorizedAccessException(error ?? GetString(PolicyKeys.AccessDenied_Machine, ResourceFileNames.Policy));
    }

    private static string GetString(string key, string resourceFileName = ResourceFileNames.SecPol)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
