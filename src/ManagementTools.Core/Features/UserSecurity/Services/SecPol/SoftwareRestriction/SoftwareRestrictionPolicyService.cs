using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using ManagementTools.Core.Infrastructure.PolicyStorage;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

/// <summary>
/// Reads and writes machine-scoped Software Restriction Policies from local policy storage.
/// </summary>
public sealed class SoftwareRestrictionPolicyService
{
    private const string CodeIdentifiersRoot = @"Software\Policies\Microsoft\Windows\Safer\CodeIdentifiers";
    private const string DisallowedLevelRoot = CodeIdentifiersRoot + @"\0";
    private const string BasicUserLevelRoot = CodeIdentifiersRoot + @"\131072";
    private const string UnrestrictedLevelRoot = CodeIdentifiersRoot + @"\262144";
    private const string DisallowedPathsRoot = DisallowedLevelRoot + @"\Paths";
    private const string BasicUserPathsRoot = BasicUserLevelRoot + @"\Paths";
    private const string UnrestrictedPathsRoot = UnrestrictedLevelRoot + @"\Paths";
    private const string DisallowedHashesRoot = DisallowedLevelRoot + @"\Hashes";
    private const string BasicUserHashesRoot = BasicUserLevelRoot + @"\Hashes";
    private const string UnrestrictedHashesRoot = UnrestrictedLevelRoot + @"\Hashes";
    private const string DisallowedCertificatesRoot = DisallowedLevelRoot + @"\Certificates";
    private const string BasicUserCertificatesRoot = BasicUserLevelRoot + @"\Certificates";
    private const string UnrestrictedCertificatesRoot = UnrestrictedLevelRoot + @"\Certificates";
    private const string DisallowedUrlZonesRoot = DisallowedLevelRoot + @"\UrlZones";
    private const string BasicUserUrlZonesRoot = BasicUserLevelRoot + @"\UrlZones";
    private const string UnrestrictedUrlZonesRoot = UnrestrictedLevelRoot + @"\UrlZones";
    private const string TrustedPublisherRoot = @"Software\Policies\Microsoft\SystemCertificates\TrustedPublisher\Safer";
    private const string DefaultLevelValueName = "DefaultLevel";
    private const string TransparentEnabledValueName = "TransparentEnabled";
    private const string PolicyScopeValueName = "PolicyScope";
    private const string AuthenticodeEnabledValueName = "AuthenticodeEnabled";
    private const string ExecutableTypesValueName = "ExecutableTypes";
    private const string AuthenticodeFlagsValueName = "AuthenticodeFlags";
    private const string ItemDataValueName = "ItemData";
    private const string BlobValueName = "Blob";
    private const string DescriptionValueName = "Description";
    private const string SaferFlagsValueName = "SaferFlags";
    private const string LastModifiedValueName = "LastModified";
    private const string HashAlgValueName = "HashAlg";
    private const string ItemSizeValueName = "ItemSize";
    private const string FriendlyNameValueName = "FriendlyName";
    private const uint TransparentEnabledSkipDlls = 1;
    private const uint TransparentEnabledAllFiles = 2;
    private const uint CertificatePublisherRevocationFlag = 0x100;
    private const uint CertificateTimestampRevocationFlag = 0x200;
    private const uint CertificatePublisherScopeMask = 0x3;
    private const uint CalgSha1 = 0x8004;

    private static readonly string[] DefaultDesignatedFileTypes =
    [
        "ADE",
        "ADP",
        "BAS",
        "BAT",
        "CHM",
        "CMD",
        "COM",
        "CPL",
        "CRT",
        "EXE",
        "HLP",
        "HTA",
        "INF",
        "INS",
        "ISP",
        "LNK",
        "MDB",
        "MDE",
        "MSC",
        "MSI",
        "MSP",
        "MST",
        "OCX",
        "PCD",
        "PIF",
        "REG",
        "SCR",
        "SHS",
        "URL",
        "VB",
        "WSC",
        "WSF",
        "WSH"
    ];

    private readonly ILogger<SoftwareRestrictionPolicyService> _logger;
    private readonly LocalPolicyFileStore _localPolicyFileStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="localPolicyFileStore">The shared local policy file store.</param>
    public SoftwareRestrictionPolicyService(
        ILogger<SoftwareRestrictionPolicyService> logger,
        LocalPolicyFileStore localPolicyFileStore)
    {
        _logger = logger;
        _localPolicyFileStore = localPolicyFileStore;
    }

    /// <summary>
    /// Loads the current Software Restriction Policies state.
    /// </summary>
    /// <returns>The current policy state.</returns>
    public SoftwareRestrictionPolicyState LoadPolicy()
    {
        PolFile snapshot = LoadPolicySnapshot();
        List<SoftwareRestrictionRule> rules = LoadRules(snapshot);

        return new SoftwareRestrictionPolicyState
        {
            IsConfigured = IsSoftwareRestrictionPolicyConfigured(snapshot, rules),
            Enforcement = LoadEnforcement(snapshot),
            TrustedPublishers = LoadTrustedPublishers(snapshot),
            DesignatedFileTypes = LoadDesignatedFileTypes(snapshot),
            Rules = rules
        };
    }

    /// <summary>
    /// Creates the default Software Restriction Policies registry values.
    /// </summary>
    public void CreateDefaultPolicy()
    {
        SavePolicy(snapshot =>
        {
            snapshot.SetValue(CodeIdentifiersRoot, DefaultLevelValueName, (uint)SoftwareRestrictionSecurityLevel.Unrestricted, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, TransparentEnabledValueName, TransparentEnabledSkipDlls, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, PolicyScopeValueName, (uint)SoftwareRestrictionUserScope.AllUsers, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, AuthenticodeEnabledValueName, 0U, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, ExecutableTypesValueName, DefaultDesignatedFileTypes, RegistryValueKind.MultiString);
            snapshot.SetValue(TrustedPublisherRoot, AuthenticodeFlagsValueName, 0U, RegistryValueKind.DWord);
            snapshot.ForgetKeyClearance(CodeIdentifiersRoot);
            snapshot.ForgetKeyClearance(DisallowedLevelRoot);
            snapshot.ForgetKeyClearance(BasicUserLevelRoot);
            snapshot.ForgetKeyClearance(UnrestrictedLevelRoot);
            snapshot.ForgetKeyClearance(TrustedPublisherRoot);
        });
    }

    /// <summary>
    /// Deletes Software Restriction Policies values from local policy storage.
    /// </summary>
    public void DeletePolicy()
    {
        SavePolicy(snapshot =>
        {
            ClearTree(snapshot, CodeIdentifiersRoot);
            ClearTree(snapshot, TrustedPublisherRoot);
        });
    }

    /// <summary>
    /// Saves the enforcement settings.
    /// </summary>
    /// <param name="settings">The enforcement settings to persist.</param>
    public void SaveEnforcement(SoftwareRestrictionEnforcementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);
            snapshot.SetValue(CodeIdentifiersRoot, DefaultLevelValueName, (uint)settings.DefaultSecurityLevel, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, PolicyScopeValueName, (uint)settings.UserScope, RegistryValueKind.DWord);
            snapshot.SetValue(
                CodeIdentifiersRoot,
                TransparentEnabledValueName,
                settings.FileScope == SoftwareRestrictionFileScope.AllSoftwareFiles
                    ? TransparentEnabledAllFiles
                    : TransparentEnabledSkipDlls,
                RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, AuthenticodeEnabledValueName, settings.CertificateRulesEnabled ? 1U : 0U, RegistryValueKind.DWord);
        });
    }

    /// <summary>
    /// Saves designated executable file extensions.
    /// </summary>
    /// <param name="fileTypes">The file extensions without leading periods.</param>
    public void SaveDesignatedFileTypes(IEnumerable<string> fileTypes)
    {
        ArgumentNullException.ThrowIfNull(fileTypes);

        string[] normalized = fileTypes
            .Select(NormalizeFileType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);
            snapshot.SetValue(CodeIdentifiersRoot, ExecutableTypesValueName, normalized, RegistryValueKind.MultiString);
        });
    }

    /// <summary>
    /// Saves trusted publisher validation settings.
    /// </summary>
    /// <param name="settings">The trusted publisher settings to persist.</param>
    public void SaveTrustedPublishers(SoftwareRestrictionTrustedPublisherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        uint flags = (uint)settings.PublisherScope & CertificatePublisherScopeMask;
        if (settings.CheckPublisherRevocation)
        {
            flags |= CertificatePublisherRevocationFlag;
        }

        if (settings.CheckTimestampRevocation)
        {
            flags |= CertificateTimestampRevocationFlag;
        }

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);
            if (!settings.IsDefined)
            {
                snapshot.DeleteValue(TrustedPublisherRoot, AuthenticodeFlagsValueName);
                return;
            }

            snapshot.SetValue(TrustedPublisherRoot, AuthenticodeFlagsValueName, flags, RegistryValueKind.DWord);
        });
    }

    /// <summary>
    /// Creates or updates an additional rule.
    /// </summary>
    /// <param name="rule">The rule to persist.</param>
    public void UpsertRule(SoftwareRestrictionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Kind == SoftwareRestrictionRuleKind.Unknown)
        {
            throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
        }

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);
            string originalRuleKey = rule.StoragePath;
            string ruleKey = GetRuleKeyPath(rule);
            bool preservesExistingRulePayload = rule.Kind is SoftwareRestrictionRuleKind.Hash or SoftwareRestrictionRuleKind.Certificate
                && !string.IsNullOrWhiteSpace(originalRuleKey)
                && !File.Exists(ExpandRuleFilePath(rule.Value));
            bool movesExistingRule = !string.IsNullOrWhiteSpace(originalRuleKey)
                && !originalRuleKey.Equals(ruleKey, StringComparison.OrdinalIgnoreCase);

            if (preservesExistingRulePayload && movesExistingRule)
            {
                ClearRuleValues(snapshot, ruleKey);
            }

            if (preservesExistingRulePayload && !CopyExistingRulePayload(snapshot, originalRuleKey, ruleKey))
            {
                throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
            }

            if (movesExistingRule)
            {
                ClearTree(snapshot, originalRuleKey);
            }

            if (!preservesExistingRulePayload)
            {
                ClearRuleValues(snapshot, ruleKey);
            }

            switch (rule.Kind)
            {
                case SoftwareRestrictionRuleKind.Path:
                    SavePathRule(snapshot, ruleKey, rule);
                    break;
                case SoftwareRestrictionRuleKind.NetworkZone:
                    SaveNetworkZoneRule(snapshot, ruleKey, rule);
                    break;
                case SoftwareRestrictionRuleKind.Hash:
                    if (preservesExistingRulePayload)
                    {
                        SaveCommonRuleValues(snapshot, ruleKey, rule);
                    }
                    else
                    {
                        SaveHashRule(snapshot, ruleKey, rule);
                    }

                    break;
                case SoftwareRestrictionRuleKind.Certificate:
                    if (preservesExistingRulePayload)
                    {
                        SaveCommonRuleValues(snapshot, ruleKey, rule);
                    }
                    else
                    {
                        SaveCertificateRule(snapshot, ruleKey, rule);
                    }

                    break;
                default:
                    throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
            }
        });
    }

    /// <summary>
    /// Deletes an additional rule.
    /// </summary>
    /// <param name="rule">The rule to delete.</param>
    public void DeleteRule(SoftwareRestrictionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.StoragePath))
        {
            return;
        }

        SavePolicy(snapshot => ClearTree(snapshot, rule.StoragePath));
    }

    private SoftwareRestrictionEnforcementSettings LoadEnforcement(PolFile snapshot)
    {
        return new SoftwareRestrictionEnforcementSettings
        {
            DefaultSecurityLevel = ReadSecurityLevel(snapshot, CodeIdentifiersRoot, DefaultLevelValueName, SoftwareRestrictionSecurityLevel.Unrestricted),
            UserScope = ReadEnumValue(snapshot, CodeIdentifiersRoot, PolicyScopeValueName, SoftwareRestrictionUserScope.AllUsers),
            FileScope = ReadFileScope(snapshot),
            CertificateRulesEnabled = ReadDword(snapshot, CodeIdentifiersRoot, AuthenticodeEnabledValueName) == 1U
        };
    }

    private SoftwareRestrictionTrustedPublisherSettings LoadTrustedPublishers(PolFile snapshot)
    {
        uint flags = ReadDword(snapshot, TrustedPublisherRoot, AuthenticodeFlagsValueName);

        return new SoftwareRestrictionTrustedPublisherSettings
        {
            IsDefined = snapshot.ContainsValue(TrustedPublisherRoot, AuthenticodeFlagsValueName),
            PublisherScope = (SoftwareRestrictionPublisherScope)(flags & CertificatePublisherScopeMask),
            CheckPublisherRevocation = (flags & CertificatePublisherRevocationFlag) == CertificatePublisherRevocationFlag,
            CheckTimestampRevocation = (flags & CertificateTimestampRevocationFlag) == CertificateTimestampRevocationFlag
        };
    }

    private static List<string> LoadDesignatedFileTypes(PolFile snapshot)
    {
        object? value = snapshot.GetValue(CodeIdentifiersRoot, ExecutableTypesValueName);
        IEnumerable<string> fileTypes = value switch
        {
            string[] array => array,
            IEnumerable<string> enumerable => enumerable,
            string text => text.Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => DefaultDesignatedFileTypes
        };

        return fileTypes
            .Select(NormalizeFileType)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<SoftwareRestrictionRule> LoadRules(PolFile snapshot)
    {
        List<SoftwareRestrictionRule> rules = [];
        LoadRulesFromRoot(snapshot, DisallowedPathsRoot, SoftwareRestrictionRuleKind.Path, SoftwareRestrictionSecurityLevel.Disallowed, rules);
        LoadRulesFromRoot(snapshot, BasicUserPathsRoot, SoftwareRestrictionRuleKind.Path, SoftwareRestrictionSecurityLevel.BasicUser, rules);
        LoadRulesFromRoot(snapshot, UnrestrictedPathsRoot, SoftwareRestrictionRuleKind.Path, SoftwareRestrictionSecurityLevel.Unrestricted, rules);
        LoadRulesFromRoot(snapshot, DisallowedUrlZonesRoot, SoftwareRestrictionRuleKind.NetworkZone, SoftwareRestrictionSecurityLevel.Disallowed, rules);
        LoadRulesFromRoot(snapshot, BasicUserUrlZonesRoot, SoftwareRestrictionRuleKind.NetworkZone, SoftwareRestrictionSecurityLevel.BasicUser, rules);
        LoadRulesFromRoot(snapshot, UnrestrictedUrlZonesRoot, SoftwareRestrictionRuleKind.NetworkZone, SoftwareRestrictionSecurityLevel.Unrestricted, rules);
        LoadRulesFromRoot(snapshot, DisallowedHashesRoot, SoftwareRestrictionRuleKind.Hash, SoftwareRestrictionSecurityLevel.Disallowed, rules);
        LoadRulesFromRoot(snapshot, BasicUserHashesRoot, SoftwareRestrictionRuleKind.Hash, SoftwareRestrictionSecurityLevel.BasicUser, rules);
        LoadRulesFromRoot(snapshot, UnrestrictedHashesRoot, SoftwareRestrictionRuleKind.Hash, SoftwareRestrictionSecurityLevel.Unrestricted, rules);
        LoadRulesFromRoot(snapshot, DisallowedCertificatesRoot, SoftwareRestrictionRuleKind.Certificate, SoftwareRestrictionSecurityLevel.Disallowed, rules);
        LoadRulesFromRoot(snapshot, BasicUserCertificatesRoot, SoftwareRestrictionRuleKind.Certificate, SoftwareRestrictionSecurityLevel.BasicUser, rules);
        LoadRulesFromRoot(snapshot, UnrestrictedCertificatesRoot, SoftwareRestrictionRuleKind.Certificate, SoftwareRestrictionSecurityLevel.Unrestricted, rules);

        return rules
            .OrderBy(static rule => rule.Kind)
            .ThenBy(static rule => rule.Value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void LoadRulesFromRoot(
        PolFile snapshot,
        string rootPath,
        SoftwareRestrictionRuleKind kind,
        SoftwareRestrictionSecurityLevel level,
        ICollection<SoftwareRestrictionRule> rules)
    {
        foreach (string ruleKeyName in snapshot.GetKeyNames(rootPath))
        {
            string ruleKeyPath = $@"{rootPath}\{ruleKeyName}";
            object? itemData = snapshot.GetValue(ruleKeyPath, ItemDataValueName);
            object? blob = snapshot.GetValue(ruleKeyPath, BlobValueName);
            object? rulePayload = itemData ?? blob;

            SoftwareRestrictionRule rule = new()
            {
                Id = ParseGuid(ruleKeyName),
                Kind = kind,
                SecurityLevel = level,
                Description = ReadString(snapshot, ruleKeyPath, DescriptionValueName),
                StoragePath = ruleKeyPath,
                LastModified = ReadFileTime(snapshot, ruleKeyPath, LastModifiedValueName)
            };

            ApplyRulePayload(snapshot, rule, rulePayload);
            rules.Add(rule);
        }
    }

    private static void SavePathRule(PolFile snapshot, string ruleKey, SoftwareRestrictionRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Value);

        snapshot.SetValue(ruleKey, ItemDataValueName, rule.Value.Trim(), RegistryValueKind.ExpandString);
        SaveCommonRuleValues(snapshot, ruleKey, rule);
    }

    private static void SaveNetworkZoneRule(PolFile snapshot, string ruleKey, SoftwareRestrictionRule rule)
    {
        uint zoneValue = ParseNetworkZone(rule.Value);
        snapshot.SetValue(ruleKey, ItemDataValueName, zoneValue, RegistryValueKind.DWord);
        SaveCommonRuleValues(snapshot, ruleKey, rule);
    }

    private static void SaveHashRule(PolFile snapshot, string ruleKey, SoftwareRestrictionRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Value);

        string filePath = ExpandRuleFilePath(rule.Value);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(rule.Value);
        }

        byte[] hash;
        ulong fileSize;
        using (FileStream stream = File.OpenRead(filePath))
        {
            fileSize = (ulong)stream.Length;
            hash = SHA1.HashData(stream);
        }

        snapshot.SetValue(ruleKey, ItemDataValueName, hash, RegistryValueKind.Binary);
        snapshot.SetValue(ruleKey, HashAlgValueName, CalgSha1, RegistryValueKind.DWord);
        snapshot.SetValue(ruleKey, ItemSizeValueName, fileSize, RegistryValueKind.QWord);
        snapshot.SetValue(ruleKey, FriendlyNameValueName, Path.GetFileName(filePath), RegistryValueKind.String);
        SaveCommonRuleValues(snapshot, ruleKey, rule);
    }

    private static void SaveCertificateRule(PolFile snapshot, string ruleKey, SoftwareRestrictionRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Value);

        string filePath = ExpandRuleFilePath(rule.Value);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(rule.Value);
        }

        byte[] certificateBytes = LoadCertificatePayloadFromFile(filePath);
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);

        snapshot.SetValue(ruleKey, ItemDataValueName, certificateBytes, RegistryValueKind.Binary);
        snapshot.SetValue(ruleKey, FriendlyNameValueName, GetPreferredName(certificate, forIssuer: false), RegistryValueKind.String);
        SaveCommonRuleValues(snapshot, ruleKey, rule);
    }

    private static byte[] LoadCertificatePayloadFromFile(string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(fileBytes);
            return certificate.Export(X509ContentType.Cert);
        }
        catch (CryptographicException)
        {
            try
            {
                // X509CertificateLoader has no signed PE extraction API; keep this scoped call for MMC-compatible signed-file browsing.
#pragma warning disable SYSLIB0057
                using X509Certificate signerCertificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                return signerCertificate.Export(X509ContentType.Cert);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.CertificateRuleInvalid), ex);
            }
        }
    }

    private static void SaveCommonRuleValues(PolFile snapshot, string ruleKey, SoftwareRestrictionRule rule)
    {
        snapshot.SetValue(ruleKey, DescriptionValueName, rule.Description ?? string.Empty, RegistryValueKind.String);
        snapshot.SetValue(ruleKey, SaferFlagsValueName, 0U, RegistryValueKind.DWord);
        snapshot.SetValue(ruleKey, LastModifiedValueName, (ulong)DateTimeOffset.UtcNow.ToFileTime(), RegistryValueKind.QWord);
    }

    private void ApplyRulePayload(PolFile snapshot, SoftwareRestrictionRule rule, object? payload)
    {
        if (rule.Kind == SoftwareRestrictionRuleKind.Certificate
            && TryApplyCertificateRule(snapshot, rule, payload))
        {
            return;
        }

        rule.Value = FormatRuleValue(rule.Kind, payload);
        if (rule.Kind == SoftwareRestrictionRuleKind.Certificate)
        {
            rule.Details = BuildRawCertificateRuleDetails(snapshot, rule, payload);
        }
    }

    private bool TryApplyCertificateRule(PolFile snapshot, SoftwareRestrictionRule rule, object? payload)
    {
        byte[]? certificateBytes = TryGetBinaryPayload(payload);
        if (certificateBytes is null || certificateBytes.Length == 0)
        {
            return false;
        }

        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            string thumbprint = certificate.Thumbprint ?? Convert.ToHexString(certificateBytes);
            string subject = GetPreferredName(certificate, forIssuer: false);
            string issuer = GetPreferredName(certificate, forIssuer: true);
            string status = FormatCertificateStatus(certificate);
            string intendedPurposes = FormatEnhancedKeyUsage(certificate);

            rule.Value = thumbprint;
            rule.CertificateSubject = subject;
            rule.CertificateIssuer = issuer;
            rule.CertificateSerialNumber = certificate.SerialNumber;
            rule.CertificateThumbprint = thumbprint;
            rule.Details = BuildCertificateRuleDetails(snapshot, rule, certificate, status, intendedPurposes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SoftwareRestrictionPolicyService] Unable to decode SRP certificate rule {RulePath}.", rule.StoragePath);
            return false;
        }
    }

    private static string FormatRuleValue(SoftwareRestrictionRuleKind kind, object? itemData)
    {
        return itemData switch
        {
            null => string.Empty,
            string text => text,
            uint number when kind == SoftwareRestrictionRuleKind.NetworkZone => number.ToString(CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => itemData.ToString() ?? string.Empty
        };
    }

    private static IReadOnlyList<SoftwareRestrictionDetailItem> BuildCertificateRuleDetails(
        PolFile snapshot,
        SoftwareRestrictionRule rule,
        X509Certificate2 certificate,
        string status,
        string intendedPurposes)
    {
        List<SoftwareRestrictionDetailItem> details =
        [
            CreateDetail(SoftwareRestrictionKeys.DetailRuleKind, rule.KindDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailSecurityLevel, rule.SecurityLevelDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailCertificateRulesEnabled, FormatBoolean(ReadDword(snapshot, CodeIdentifiersRoot, AuthenticodeEnabledValueName) == 1U)),
            CreateDetail(SoftwareRestrictionKeys.DetailSubject, certificate.Subject),
            CreateDetail(SoftwareRestrictionKeys.DetailIssuer, certificate.Issuer),
            CreateDetail(SoftwareRestrictionKeys.DetailSerialNumber, certificate.SerialNumber),
            CreateDetail(SoftwareRestrictionKeys.DetailThumbprint, certificate.Thumbprint ?? rule.CertificateThumbprint),
            CreateDetail(SoftwareRestrictionKeys.DetailValidFrom, certificate.NotBefore.ToString("G", CultureInfo.CurrentCulture)),
            CreateDetail(SoftwareRestrictionKeys.DetailValidTo, certificate.NotAfter.ToString("G", CultureInfo.CurrentCulture)),
            CreateDetail(SoftwareRestrictionKeys.DetailIntendedPurposes, intendedPurposes),
            CreateDetail(SoftwareRestrictionKeys.DetailFriendlyName, certificate.FriendlyName),
            CreateDetail(SoftwareRestrictionKeys.DetailStatus, status),
            CreateDetail(SoftwareRestrictionKeys.DetailStoragePath, rule.StoragePath)
        ];

        if (rule.LastModified is not null)
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailLastModified, rule.LastModified.Value.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailDescription, rule.Description));
        }

        return details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static IReadOnlyList<SoftwareRestrictionDetailItem> BuildRawCertificateRuleDetails(
        PolFile snapshot,
        SoftwareRestrictionRule rule,
        object? payload)
    {
        List<SoftwareRestrictionDetailItem> details =
        [
            CreateDetail(SoftwareRestrictionKeys.DetailRuleKind, rule.KindDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailSecurityLevel, rule.SecurityLevelDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailCertificateRulesEnabled, FormatBoolean(ReadDword(snapshot, CodeIdentifiersRoot, AuthenticodeEnabledValueName) == 1U)),
            CreateDetail(SoftwareRestrictionKeys.DetailRawData, FormatRuleValue(rule.Kind, payload)),
            CreateDetail(SoftwareRestrictionKeys.DetailStoragePath, rule.StoragePath)
        ];

        if (rule.LastModified is not null)
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailLastModified, rule.LastModified.Value.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailDescription, rule.Description));
        }

        return details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static SoftwareRestrictionDetailItem CreateDetail(string nameKey, string value)
    {
        return new SoftwareRestrictionDetailItem
        {
            Name = GetString(nameKey),
            Value = value
        };
    }

    private static byte[]? TryGetBinaryPayload(object? payload)
    {
        if (payload is byte[] bytes)
        {
            return bytes;
        }

        if (payload is string text)
        {
            string normalized = string.Concat(text.Where(static character => !char.IsWhiteSpace(character)));
            if (normalized.Length % 2 == 0)
            {
                try
                {
                    return Convert.FromHexString(normalized);
                }
                catch (FormatException)
                {
                    return null;
                }
            }
        }

        return null;
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
            return GetString(SoftwareRestrictionKeys.IntendedPurposesAll);
        }

        return string.Join(
            ", ",
            eku.EnhancedKeyUsages
                .Cast<Oid>()
                .Select(static oid => string.IsNullOrWhiteSpace(oid.FriendlyName) ? oid.Value : oid.FriendlyName));
    }

    private static string FormatCertificateStatus(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        return now < certificate.NotBefore || now > certificate.NotAfter
            ? GetString(SoftwareRestrictionKeys.CertificateExpiredOrNotYetValid)
            : GetString(SoftwareRestrictionKeys.CertificateValid);
    }

    private static string FormatBoolean(bool value)
    {
        return value
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(SecPolKeys.ValueDisabled);
    }

    private static string GetRuleKeyPath(SoftwareRestrictionRule rule)
    {
        string root = (rule.SecurityLevel, rule.Kind) switch
        {
            (SoftwareRestrictionSecurityLevel.Disallowed, SoftwareRestrictionRuleKind.Path) => DisallowedPathsRoot,
            (SoftwareRestrictionSecurityLevel.BasicUser, SoftwareRestrictionRuleKind.Path) => BasicUserPathsRoot,
            (SoftwareRestrictionSecurityLevel.Unrestricted, SoftwareRestrictionRuleKind.Path) => UnrestrictedPathsRoot,
            (SoftwareRestrictionSecurityLevel.Disallowed, SoftwareRestrictionRuleKind.NetworkZone) => DisallowedUrlZonesRoot,
            (SoftwareRestrictionSecurityLevel.BasicUser, SoftwareRestrictionRuleKind.NetworkZone) => BasicUserUrlZonesRoot,
            (SoftwareRestrictionSecurityLevel.Unrestricted, SoftwareRestrictionRuleKind.NetworkZone) => UnrestrictedUrlZonesRoot,
            (SoftwareRestrictionSecurityLevel.Disallowed, SoftwareRestrictionRuleKind.Hash) => DisallowedHashesRoot,
            (SoftwareRestrictionSecurityLevel.BasicUser, SoftwareRestrictionRuleKind.Hash) => BasicUserHashesRoot,
            (SoftwareRestrictionSecurityLevel.Unrestricted, SoftwareRestrictionRuleKind.Hash) => UnrestrictedHashesRoot,
            (SoftwareRestrictionSecurityLevel.Disallowed, SoftwareRestrictionRuleKind.Certificate) => DisallowedCertificatesRoot,
            (SoftwareRestrictionSecurityLevel.BasicUser, SoftwareRestrictionRuleKind.Certificate) => BasicUserCertificatesRoot,
            (SoftwareRestrictionSecurityLevel.Unrestricted, SoftwareRestrictionRuleKind.Certificate) => UnrestrictedCertificatesRoot,
            _ => DisallowedPathsRoot
        };

        Guid ruleId = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        return $@"{root}\{{{ruleId:D}}}";
    }

    private static void ClearRuleValues(PolFile snapshot, string ruleKey)
    {
        foreach (string valueName in new[]
        {
            ItemDataValueName,
            BlobValueName,
            DescriptionValueName,
            SaferFlagsValueName,
            LastModifiedValueName,
            HashAlgValueName,
            ItemSizeValueName,
            FriendlyNameValueName
        })
        {
            snapshot.ForgetValue(ruleKey, valueName);
        }
    }

    private static bool CopyExistingRulePayload(PolFile snapshot, string sourceRuleKey, string destinationRuleKey)
    {
        bool copied = false;
        foreach (string valueName in new[]
        {
            ItemDataValueName,
            BlobValueName,
            HashAlgValueName,
            ItemSizeValueName,
            FriendlyNameValueName
        })
        {
            object? value = snapshot.GetValue(sourceRuleKey, valueName);
            if (value is null)
            {
                continue;
            }

            RegistryValueKind valueKind = snapshot.GetValueKind(sourceRuleKey, valueName);
            if (valueKind == RegistryValueKind.Unknown)
            {
                continue;
            }

            snapshot.SetValue(destinationRuleKey, valueName, value, valueKind);
            copied = true;
        }

        return copied;
    }

    private void SavePolicy(Action<PolFile> mutateSnapshot)
    {
        ArgumentNullException.ThrowIfNull(mutateSnapshot);

        PolFile snapshot = LoadPolicySnapshot();
        mutateSnapshot(snapshot);
        EnsureMachinePolicyIsWritable();
        string result = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);
        _logger.LogInformation("[SoftwareRestrictionPolicyService] Software Restriction Policies saved: {SaveResult}", result);
    }

    private PolFile LoadPolicySnapshot()
    {
        PolFile snapshot = LoadWritablePolicySnapshot();
        CopyLiveRegistryTreeWhenMissing(snapshot, CodeIdentifiersRoot);
        CopyLiveRegistryTreeWhenMissing(snapshot, TrustedPublisherRoot);
        return snapshot;
    }

    private PolFile LoadWritablePolicySnapshot()
    {
        try
        {
            return _localPolicyFileStore.LoadSnapshot(isUser: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SoftwareRestrictionPolicyService] Failed to load machine Registry.pol. Starting from an empty snapshot.");
            return new PolFile();
        }
    }

    private static void CopyLiveRegistryTreeWhenMissing(PolFile snapshot, string rootPath)
    {
        if (snapshot.GetValueNames(rootPath).Count > 0 || snapshot.GetKeyNames(rootPath).Count > 0)
        {
            return;
        }

        using RegistryKey? sourceKey = Registry.LocalMachine.OpenSubKey(rootPath);
        if (sourceKey is not null)
        {
            CopyRegistryTreeToPolFile(sourceKey, rootPath, snapshot);
        }
    }

    private static void CopyRegistryTreeToPolFile(RegistryKey sourceKey, string relativePath, PolFile destination)
    {
        foreach (string valueName in sourceKey.GetValueNames())
        {
            object? rawValue = sourceKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawValue is not null)
            {
                destination.SetValue(relativePath, valueName, rawValue, sourceKey.GetValueKind(valueName));
            }
        }

        foreach (string subKeyName in sourceKey.GetSubKeyNames())
        {
            using RegistryKey? subKey = sourceKey.OpenSubKey(subKeyName, writable: false);
            if (subKey is not null)
            {
                CopyRegistryTreeToPolFile(subKey, $@"{relativePath}\{subKeyName}", destination);
            }
        }
    }

    private static void EnsurePolicyRoot(PolFile snapshot)
    {
        if (!snapshot.ContainsValue(CodeIdentifiersRoot, DefaultLevelValueName))
        {
            snapshot.SetValue(CodeIdentifiersRoot, DefaultLevelValueName, (uint)SoftwareRestrictionSecurityLevel.Unrestricted, RegistryValueKind.DWord);
        }

        if (!snapshot.ContainsValue(CodeIdentifiersRoot, TransparentEnabledValueName))
        {
            snapshot.SetValue(CodeIdentifiersRoot, TransparentEnabledValueName, TransparentEnabledSkipDlls, RegistryValueKind.DWord);
        }

        if (!snapshot.ContainsValue(CodeIdentifiersRoot, PolicyScopeValueName))
        {
            snapshot.SetValue(CodeIdentifiersRoot, PolicyScopeValueName, (uint)SoftwareRestrictionUserScope.AllUsers, RegistryValueKind.DWord);
        }

        if (!snapshot.ContainsValue(CodeIdentifiersRoot, ExecutableTypesValueName))
        {
            snapshot.SetValue(CodeIdentifiersRoot, ExecutableTypesValueName, DefaultDesignatedFileTypes, RegistryValueKind.MultiString);
        }
    }

    private static bool IsSoftwareRestrictionPolicyConfigured(PolFile snapshot, IReadOnlyCollection<SoftwareRestrictionRule> rules)
    {
        return snapshot.ContainsValue(CodeIdentifiersRoot, DefaultLevelValueName)
            || snapshot.ContainsValue(CodeIdentifiersRoot, TransparentEnabledValueName)
            || snapshot.ContainsValue(CodeIdentifiersRoot, PolicyScopeValueName)
            || snapshot.ContainsValue(CodeIdentifiersRoot, ExecutableTypesValueName)
            || rules.Count > 0;
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

    private static SoftwareRestrictionFileScope ReadFileScope(PolFile snapshot)
    {
        return ReadDword(snapshot, CodeIdentifiersRoot, TransparentEnabledValueName) == TransparentEnabledAllFiles
            ? SoftwareRestrictionFileScope.AllSoftwareFiles
            : SoftwareRestrictionFileScope.ExecutableFilesOnly;
    }

    private static SoftwareRestrictionSecurityLevel ReadSecurityLevel(
        PolFile snapshot,
        string keyPath,
        string valueName,
        SoftwareRestrictionSecurityLevel fallback)
    {
        uint value = ReadDword(snapshot, keyPath, valueName, (uint)fallback);
        return value switch
        {
            (uint)SoftwareRestrictionSecurityLevel.Disallowed => SoftwareRestrictionSecurityLevel.Disallowed,
            (uint)SoftwareRestrictionSecurityLevel.BasicUser => SoftwareRestrictionSecurityLevel.BasicUser,
            (uint)SoftwareRestrictionSecurityLevel.Unrestricted => SoftwareRestrictionSecurityLevel.Unrestricted,
            _ => fallback
        };
    }

    private static TEnum ReadEnumValue<TEnum>(PolFile snapshot, string keyPath, string valueName, TEnum fallback)
        where TEnum : struct, Enum
    {
        uint value = ReadDword(snapshot, keyPath, valueName, Convert.ToUInt32(fallback, CultureInfo.InvariantCulture));
        object typedValue = Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(TEnum)), CultureInfo.InvariantCulture);
        return Enum.IsDefined(typeof(TEnum), typedValue)
            ? (TEnum)Enum.ToObject(typeof(TEnum), typedValue)
            : fallback;
    }

    private static uint ReadDword(PolFile snapshot, string keyPath, string valueName, uint fallback = 0)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        if (value is null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string ReadString(PolFile snapshot, string keyPath, string valueName)
    {
        return snapshot.GetValue(keyPath, valueName) as string ?? string.Empty;
    }

    private static DateTimeOffset? ReadFileTime(PolFile snapshot, string keyPath, string valueName)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        if (value is null)
        {
            return null;
        }

        try
        {
            long fileTime = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return DateTimeOffset.FromFileTime(fileTime);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Guid ParseGuid(string value)
    {
        return Guid.TryParse(value.Trim('{', '}'), out Guid id) ? id : Guid.NewGuid();
    }

    private static uint ParseNetworkZone(string value)
    {
        if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint zone)
            && zone <= 4)
        {
            return zone;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, GetString(SoftwareRestrictionKeys.NetworkZoneInvalid));
    }

    private static string NormalizeFileType(string fileType)
    {
        return fileType.Trim().TrimStart('.').ToUpperInvariant();
    }

    private static string ExpandRuleFilePath(string value)
    {
        return Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    }

    private static string GetString(string key, string resourceFileName = ResourceFileNames.SecPol)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
