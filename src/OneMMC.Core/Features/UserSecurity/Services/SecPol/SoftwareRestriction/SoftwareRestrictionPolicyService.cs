using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

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
    private const string DisallowedUrlZonesRoot = DisallowedLevelRoot + @"\UrlZones";
    private const string BasicUserUrlZonesRoot = BasicUserLevelRoot + @"\UrlZones";
    private const string UnrestrictedUrlZonesRoot = UnrestrictedLevelRoot + @"\UrlZones";
    private const string SystemCertificatesRoot = @"Software\Policies\Microsoft\SystemCertificates";
    private const string TrustedPublisherRoot = SystemCertificatesRoot + @"\TrustedPublisher\Safer";
    private const string TrustedPublisherCertificatesRoot = SystemCertificatesRoot + @"\TrustedPublisher\Certificates";
    private const string DisallowedCertificatesStoreRoot = SystemCertificatesRoot + @"\Disallowed\Certificates";
    private const string DefaultLevelValueName = "DefaultLevel";
    private const string TransparentEnabledValueName = "TransparentEnabled";
    private const string PolicyScopeValueName = "PolicyScope";
    private const string AuthenticodeEnabledValueName = "AuthenticodeEnabled";
    private const string ExecutableTypesValueName = "ExecutableTypes";
    private const string LevelsValueName = "Levels";
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

    // Hash-rule algorithm identifiers (Wincrypt ALG_ID). secpol.msc writes an MD5 primary rule plus a SHA-256
    // child key; SAFER matches on these, so OneMMC must write the same. See doc research in the plan file.
    private const uint CalgMd5 = 0x8003;
    private const uint CalgSha256 = 0x800C;

    // Enables every SAFER security level in the editor UI, matching what secpol.msc writes.
    private const uint AllSecurityLevelsMask = 0x00071000;

    // Serialized cert-store element property ids: CERT_CERT_PROP_ID wraps the encoded certificate itself,
    // CERT_DESCRIPTION_PROP_ID carries the rule description text stored alongside it.
    private const uint CertCertPropId = 0x20;
    private const uint CertDescriptionPropId = 13;

    // Default Unrestricted registry-path safeguards that secpol.msc auto-creates when the default level is set
    // to Disallowed, so the operating system can still start. Fixed GUIDs keep this idempotent.
    private static readonly (string RuleId, string Path)[] DisallowedSafeguardPathRules =
    [
        ("{6f2d1a10-3c4b-4e51-9a01-000000000001}", @"%HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRoot%"),
        ("{6f2d1a10-3c4b-4e51-9a01-000000000002}", @"%HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRoot%\*.exe"),
        ("{6f2d1a10-3c4b-4e51-9a01-000000000003}", @"%HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRoot%\System32\*.exe"),
        ("{6f2d1a10-3c4b-4e51-9a01-000000000004}", @"%HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\ProgramFilesDir%"),
    ];

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
            snapshot.SetValue(CodeIdentifiersRoot, LevelsValueName, AllSecurityLevelsMask, RegistryValueKind.DWord);
            snapshot.SetValue(CodeIdentifiersRoot, ExecutableTypesValueName, DefaultDesignatedFileTypes, RegistryValueKind.MultiString);
        });
    }

    /// <summary>
    /// Deletes Software Restriction Policies values from local policy storage.
    /// </summary>
    public void DeletePolicy()
    {
        SavePolicy(snapshot =>
        {
            snapshot.DeleteKeyTree(CodeIdentifiersRoot);
            snapshot.DeleteKeyTree(TrustedPublisherRoot);
            snapshot.DeleteKeyTree(TrustedPublisherCertificatesRoot);
            snapshot.DeleteKeyTree(DisallowedCertificatesStoreRoot);
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

            if (settings.DefaultSecurityLevel == SoftwareRestrictionSecurityLevel.Disallowed)
            {
                EnsureDisallowedSafeguardRules(snapshot);
            }
        });
    }

    /// <summary>
    /// Creates the four Unrestricted registry-path safeguard rules that secpol.msc auto-creates when the
    /// default level is switched to Disallowed, so Windows and Program Files binaries can still run.
    /// </summary>
    /// <param name="snapshot">The policy snapshot to mutate.</param>
    private static void EnsureDisallowedSafeguardRules(PolFile snapshot)
    {
        foreach ((string ruleId, string path) in DisallowedSafeguardPathRules)
        {
            string ruleKey = $@"{UnrestrictedPathsRoot}\{ruleId}";
            if (snapshot.ContainsValue(ruleKey, ItemDataValueName))
            {
                continue;
            }

            snapshot.SetValue(ruleKey, ItemDataValueName, path, RegistryValueKind.ExpandString);
            snapshot.SetValue(ruleKey, SaferFlagsValueName, 0U, RegistryValueKind.DWord);
            snapshot.SetValue(ruleKey, DescriptionValueName, string.Empty, RegistryValueKind.String);
            snapshot.SetValue(ruleKey, LastModifiedValueName, (ulong)DateTimeOffset.UtcNow.ToFileTime(), RegistryValueKind.QWord);
        }
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

        if (rule.Kind == SoftwareRestrictionRuleKind.Certificate)
        {
            UpsertCertificateRule(rule);
            return;
        }

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);
            string originalRuleKey = rule.StoragePath;
            string ruleKey = GetRuleKeyPath(rule);
            bool preservesExistingRulePayload = rule.Kind is SoftwareRestrictionRuleKind.Hash
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
                snapshot.DeleteKeyTree(originalRuleKey);
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

        SavePolicy(snapshot => snapshot.DeleteKeyTree(rule.StoragePath));
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
        LoadCertificateRulesFromStore(snapshot, TrustedPublisherCertificatesRoot, SoftwareRestrictionSecurityLevel.Unrestricted, rules);
        LoadCertificateRulesFromStore(snapshot, DisallowedCertificatesStoreRoot, SoftwareRestrictionSecurityLevel.Disallowed, rules);

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

        byte[] md5Hash;
        byte[] sha256Hash;
        ulong fileSize;
        using (FileStream stream = File.OpenRead(filePath))
        {
            fileSize = (ulong)stream.Length;
            md5Hash = MD5.HashData(stream);
            stream.Position = 0;
            sha256Hash = SHA256.HashData(stream);
        }

        // Primary rule: MD5 (CALG_MD5), 16 bytes — the value SAFER computes and matches at enforcement time.
        snapshot.SetValue(ruleKey, ItemDataValueName, md5Hash, RegistryValueKind.Binary);
        snapshot.SetValue(ruleKey, HashAlgValueName, CalgMd5, RegistryValueKind.DWord);
        snapshot.SetValue(ruleKey, ItemSizeValueName, fileSize, RegistryValueKind.QWord);
        snapshot.SetValue(ruleKey, FriendlyNameValueName, Path.GetFileName(filePath), RegistryValueKind.String);

        // SHA-256 companion in a child key, exactly as modern secpol.msc writes it, so newer Windows builds can
        // match on the stronger hash. The child key holds only ItemData + HashAlg.
        string sha256RuleKey = GetHashSha256KeyPath(ruleKey);
        snapshot.SetValue(sha256RuleKey, ItemDataValueName, sha256Hash, RegistryValueKind.Binary);
        snapshot.SetValue(sha256RuleKey, HashAlgValueName, CalgSha256, RegistryValueKind.DWord);

        SaveCommonRuleValues(snapshot, ruleKey, rule);
    }

    /// <summary>
    /// Creates or updates a certificate rule. Windows does not keep certificate rules under CodeIdentifiers;
    /// they live as certificates in the group-policy TrustedPublisher (Unrestricted) or Disallowed store, so
    /// the rule is persisted as a serialized cert-store <c>Blob</c> keyed by the certificate thumbprint.
    /// </summary>
    /// <param name="rule">The certificate rule to persist.</param>
    private void UpsertCertificateRule(SoftwareRestrictionRule rule)
    {
        if (rule.SecurityLevel == SoftwareRestrictionSecurityLevel.BasicUser)
        {
            throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.CertificateRuleLevelUnsupported));
        }

        SavePolicy(snapshot =>
        {
            EnsurePolicyRoot(snapshot);

            string originalRuleKey = rule.StoragePath;
            string storeRoot = rule.SecurityLevel == SoftwareRestrictionSecurityLevel.Disallowed
                ? DisallowedCertificatesStoreRoot
                : TrustedPublisherCertificatesRoot;

            byte[] certificateBytes = ResolveCertificateRulePayload(snapshot, rule, originalRuleKey);
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            string ruleKey = $@"{storeRoot}\{certificate.Thumbprint}";

            if (!string.IsNullOrWhiteSpace(originalRuleKey)
                && !originalRuleKey.Equals(ruleKey, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.DeleteKeyTree(originalRuleKey);
            }

            snapshot.SetValue(ruleKey, BlobValueName, BuildSerializedCertificateBlob(certificate, rule.Description), RegistryValueKind.Binary);

            // Certificate rules are only evaluated while AuthenticodeEnabled=1; secpol.msc likewise prompts
            // to turn certificate rules on when the first one is created.
            snapshot.SetValue(CodeIdentifiersRoot, AuthenticodeEnabledValueName, 1U, RegistryValueKind.DWord);
        });
    }

    private static byte[] ResolveCertificateRulePayload(PolFile snapshot, SoftwareRestrictionRule rule, string originalRuleKey)
    {
        if (!string.IsNullOrWhiteSpace(rule.Value))
        {
            string filePath = ExpandRuleFilePath(rule.Value);
            if (File.Exists(filePath))
            {
                return LoadCertificatePayloadFromFile(filePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(originalRuleKey))
        {
            if (snapshot.GetValue(originalRuleKey, BlobValueName) is byte[] blob
                && TryParseSerializedCertificateBlob(blob, out byte[]? fromBlob, out _)
                && fromBlob is not null)
            {
                return fromBlob;
            }

            // Rules written by earlier releases stored the bare DER under ItemData in the Safer tree.
            if (snapshot.GetValue(originalRuleKey, ItemDataValueName) is byte[] derBytes && derBytes.Length > 0)
            {
                return derBytes;
            }
        }

        throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.CertificateRuleInvalid));
    }

    private void LoadCertificateRulesFromStore(
        PolFile snapshot,
        string storeRoot,
        SoftwareRestrictionSecurityLevel level,
        ICollection<SoftwareRestrictionRule> rules)
    {
        foreach (string entryKeyName in snapshot.GetKeyNames(storeRoot))
        {
            string ruleKeyPath = $@"{storeRoot}\{entryKeyName}";
            if (snapshot.GetValue(ruleKeyPath, BlobValueName) is not byte[] blob || blob.Length == 0)
            {
                continue;
            }

            TryParseSerializedCertificateBlob(blob, out byte[]? certificateBytes, out string? description);

            SoftwareRestrictionRule rule = new()
            {
                Id = CreateStableCertificateRuleId(entryKeyName),
                Kind = SoftwareRestrictionRuleKind.Certificate,
                SecurityLevel = level,
                Description = description ?? string.Empty,
                StoragePath = ruleKeyPath
            };

            ApplyRulePayload(snapshot, rule, certificateBytes ?? blob);
            rules.Add(rule);
        }
    }

    /// <summary>
    /// Builds the registry cert-store <c>Blob</c> payload: the certificate serialized as a store element
    /// (property records followed by a final CERT_CERT_PROP_ID record holding the DER bytes), with the rule
    /// description inserted as a CERT_DESCRIPTION_PROP_ID record so it round-trips through the store.
    /// </summary>
    private static byte[] BuildSerializedCertificateBlob(X509Certificate2 certificate, string? description)
    {
        byte[] serialized = certificate.Export(X509ContentType.SerializedCert);
        if (string.IsNullOrWhiteSpace(description))
        {
            return serialized;
        }

        int certRecordOffset = FindCertificateRecordOffset(serialized);
        if (certRecordOffset < 0)
        {
            return serialized;
        }

        byte[] descriptionBytes = Encoding.Unicode.GetBytes(description + "\0");
        byte[] blob = new byte[serialized.Length + 12 + descriptionBytes.Length];
        serialized.AsSpan(0, certRecordOffset).CopyTo(blob);
        BitConverter.TryWriteBytes(blob.AsSpan(certRecordOffset), CertDescriptionPropId);
        BitConverter.TryWriteBytes(blob.AsSpan(certRecordOffset + 4), 1U);
        BitConverter.TryWriteBytes(blob.AsSpan(certRecordOffset + 8), (uint)descriptionBytes.Length);
        descriptionBytes.CopyTo(blob.AsSpan(certRecordOffset + 12));
        serialized.AsSpan(certRecordOffset).CopyTo(blob.AsSpan(certRecordOffset + 12 + descriptionBytes.Length));
        return blob;
    }

    /// <summary>
    /// Walks a serialized cert-store element and extracts the encoded certificate (final CERT_CERT_PROP_ID
    /// record) plus the stored description property, if any. Each record is
    /// <c>[propId][encodingType][length][data]</c> with little-endian 32-bit fields.
    /// </summary>
    private static bool TryParseSerializedCertificateBlob(byte[] blob, out byte[]? certificateBytes, out string? description)
    {
        certificateBytes = null;
        description = null;

        int offset = 0;
        while (offset + 12 <= blob.Length)
        {
            uint propId = BitConverter.ToUInt32(blob, offset);
            uint length = BitConverter.ToUInt32(blob, offset + 8);
            offset += 12;
            if (length > (uint)(blob.Length - offset))
            {
                return false;
            }

            if (propId == CertCertPropId)
            {
                certificateBytes = blob[offset..(offset + (int)length)];
                return true;
            }

            if (propId == CertDescriptionPropId && length >= 2)
            {
                description = Encoding.Unicode.GetString(blob, offset, (int)length).TrimEnd('\0');
            }

            offset += (int)length;
        }

        return false;
    }

    private static int FindCertificateRecordOffset(byte[] blob)
    {
        int offset = 0;
        while (offset + 12 <= blob.Length)
        {
            uint propId = BitConverter.ToUInt32(blob, offset);
            if (propId == CertCertPropId)
            {
                return offset;
            }

            uint length = BitConverter.ToUInt32(blob, offset + 8);
            long next = (long)offset + 12 + length;
            if (next > blob.Length)
            {
                return -1;
            }

            offset = (int)next;
        }

        return -1;
    }

    /// <summary>
    /// Derives a stable rule id from a cert-store key name (the certificate thumbprint) so reloads keep the
    /// same identity; store entries have no GUID of their own.
    /// </summary>
    private static Guid CreateStableCertificateRuleId(string storeKeyName)
    {
        string hex = string.Concat(storeKeyName.Where(Uri.IsHexDigit));
        return hex.Length >= 32
            ? new Guid(Convert.FromHexString(hex[..32]))
            : ParseGuid(storeKeyName);
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
            _ => DisallowedPathsRoot
        };

        Guid ruleId = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        return $@"{root}\{{{ruleId:D}}}";
    }

    private static string GetHashSha256KeyPath(string hashRuleKey) => $@"{hashRuleKey}\SHA256";

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

        // Hash rules carry a SHA-256 companion child key; move it together with the primary payload.
        string sourceSha256Key = GetHashSha256KeyPath(sourceRuleKey);
        string destinationSha256Key = GetHashSha256KeyPath(destinationRuleKey);
        foreach (string valueName in new[] { ItemDataValueName, HashAlgValueName })
        {
            object? value = snapshot.GetValue(sourceSha256Key, valueName);
            if (value is null)
            {
                continue;
            }

            RegistryValueKind valueKind = snapshot.GetValueKind(sourceSha256Key, valueName);
            if (valueKind == RegistryValueKind.Unknown)
            {
                continue;
            }

            snapshot.SetValue(destinationSha256Key, valueName, value, valueKind);
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

        // Self-heal: earlier releases persisted **delvals./**del. tombstones under the SRP trees, which the
        // Registry CSE re-applies forever and which read back as phantom rules. Strip them before the
        // snapshot is read or mutated; deletions are now expressed as key absence plus transient
        // **DeleteKeys instructions consumed at save time.
        snapshot.RemoveDeleteMarkers(CodeIdentifiersRoot);
        snapshot.RemoveDeleteMarkers(TrustedPublisherRoot);
        snapshot.RemoveDeleteMarkers(TrustedPublisherCertificatesRoot);
        snapshot.RemoveDeleteMarkers(DisallowedCertificatesStoreRoot);

        CopyLiveRegistryTreeWhenMissing(snapshot, CodeIdentifiersRoot);
        CopyLiveRegistryTreeWhenMissing(snapshot, TrustedPublisherRoot);
        CopyLiveRegistryTreeWhenMissing(snapshot, TrustedPublisherCertificatesRoot);
        CopyLiveRegistryTreeWhenMissing(snapshot, DisallowedCertificatesStoreRoot);
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

        if (!snapshot.ContainsValue(CodeIdentifiersRoot, LevelsValueName))
        {
            snapshot.SetValue(CodeIdentifiersRoot, LevelsValueName, AllSecurityLevelsMask, RegistryValueKind.DWord);
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
