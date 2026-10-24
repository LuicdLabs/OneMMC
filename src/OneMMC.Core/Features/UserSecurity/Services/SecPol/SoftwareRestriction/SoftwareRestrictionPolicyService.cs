using System.ComponentModel;
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
using Windows.Win32.Security;
using Windows.Win32.Security.AppLocker;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

/// <summary>
/// Reads and writes machine-scoped Software Restriction Policies the way secpol.msc does: through the
/// local Group Policy Object and the SAFER API (see <see cref="SaferPolicySession"/>).
/// </summary>
/// <remarks>
/// Security levels, enforcement settings and path/hash/zone rules go through
/// <c>Safer{Get,Set}PolicyInformation</c> and <c>Safer{Get,Set}LevelInformation</c>, so SAFER owns the
/// CodeIdentifiers registry layout (including the MD5 + SHA-256 hash rule pair) and new policies are
/// created by <c>SaferiPopulateDefaultsInRegistry</c>. Settings that have no SAFER API — designated file
/// types, trusted publisher flags and certificate rules — are written to the same GPO registry buffer,
/// exactly as secpol.msc's certmgr.dll writes them.
/// </remarks>
public sealed class SoftwareRestrictionPolicyService
{
    private const string CodeIdentifiersRoot = SaferPolicySession.SaferKeyPath + @"\CodeIdentifiers";
    private const string SystemCertificatesRoot = @"Software\Policies\Microsoft\SystemCertificates";
    private const string TrustedPublisherRoot = SystemCertificatesRoot + @"\TrustedPublisher\Safer";
    private const string TrustedPublisherCertificatesRoot = SystemCertificatesRoot + @"\TrustedPublisher\Certificates";
    private const string DisallowedCertificatesStoreRoot = SystemCertificatesRoot + @"\Disallowed\Certificates";
    private const string PathRulesKeyName = "Paths";
    private const string HashRulesKeyName = "Hashes";
    private const string UrlZoneRulesKeyName = "UrlZones";
    private const string DefaultLevelValueName = "DefaultLevel";
    private const string ExecutableTypesValueName = "ExecutableTypes";
    private const string AuthenticodeFlagsValueName = "AuthenticodeFlags";
    private const string ItemDataValueName = "ItemData";
    private const string BlobValueName = "Blob";
    private const string LegacyDeleteMarkerPrefix = "**";
    private const uint TransparentEnabledSkipDlls = 1;
    private const uint TransparentEnabledAllFiles = 2;
    private const uint CertificatePublisherRevocationFlag = 0x100;
    private const uint CertificateTimestampRevocationFlag = 0x200;
    private const uint CertificatePublisherScopeMask = 0x3;

    // Hash-rule algorithm identifiers (Wincrypt ALG_ID). secpol.msc fills SAFER_HASH_IDENTIFICATION2 with
    // an MD5 primary hash and a SHA-256 companion; SAFER persists both.
    private const uint CalgMd5 = 0x8003;
    private const uint CalgSha256 = 0x800C;

    // Serialized cert-store element property ids: CERT_CERT_PROP_ID wraps the encoded certificate itself,
    // CERT_DESCRIPTION_PROP_ID carries the rule description text stored alongside it.
    private const uint CertCertPropId = 0x20;
    private const uint CertDescriptionPropId = 13;

    private static readonly SoftwareRestrictionSecurityLevel[] SecurityLevels =
    [
        SoftwareRestrictionSecurityLevel.Disallowed,
        SoftwareRestrictionSecurityLevel.BasicUser,
        SoftwareRestrictionSecurityLevel.Unrestricted
    ];

    // Shown until a policy exists; matches the list SaferiPopulateDefaultsInRegistry writes.
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
        "WSC"
    ];

    private readonly ILogger<SoftwareRestrictionPolicyService> _logger;
    private readonly LocalGroupPolicyObjectFactory _gpoFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="gpoFactory">Opens the local Group Policy Object.</param>
    public SoftwareRestrictionPolicyService(
        ILogger<SoftwareRestrictionPolicyService> logger,
        LocalGroupPolicyObjectFactory gpoFactory)
    {
        _logger = logger;
        _gpoFactory = gpoFactory;
    }

    /// <summary>
    /// Gets a value indicating whether the last saved change still needs a policy refresh or sign-in to
    /// take effect, because the automatic Group Policy refresh could not be started.
    /// </summary>
    public bool LastSaveNeedsRefresh { get; private set; }

    /// <summary>
    /// Loads the current Software Restriction Policies state.
    /// </summary>
    /// <returns>The current policy state.</returns>
    public SoftwareRestrictionPolicyState LoadPolicy()
    {
        return SaferPolicySession.Run(_gpoFactory, _logger, forEditing: false, LoadPolicy);
    }

    private SoftwareRestrictionPolicyState LoadPolicy(SaferPolicySession session)
    {
        SoftwareRestrictionEnforcementSettings enforcement = LoadEnforcement(session);
        List<SoftwareRestrictionRule> rules = [];
        LoadSaferRules(session, rules);
        LoadCertificateRulesFromStore(session.Root, TrustedPublisherCertificatesRoot, SoftwareRestrictionSecurityLevel.Unrestricted, enforcement.CertificateRulesEnabled, rules);
        LoadCertificateRulesFromStore(session.Root, DisallowedCertificatesStoreRoot, SoftwareRestrictionSecurityLevel.Disallowed, enforcement.CertificateRulesEnabled, rules);

        return new SoftwareRestrictionPolicyState
        {
            IsConfigured = IsPolicyConfigured(session) || rules.Count > 0,
            Enforcement = enforcement,
            TrustedPublishers = LoadTrustedPublishers(session.Root),
            DesignatedFileTypes = LoadDesignatedFileTypes(session.Root),
            Rules = rules
                .OrderBy(static rule => rule.Kind)
                .ThenBy(static rule => rule.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// Creates a new Software Restriction Policy with the Windows defaults.
    /// </summary>
    public void CreateDefaultPolicy()
    {
        EditPolicy(EnsurePolicyRoot);
    }

    /// <summary>
    /// Deletes the Software Restriction Policies from the local Group Policy Object.
    /// </summary>
    public void DeletePolicy()
    {
        EditPolicy(session =>
        {
            session.Root.DeleteSubKeyTree(CodeIdentifiersRoot, throwOnMissingSubKey: false);
            session.Root.DeleteSubKeyTree(TrustedPublisherRoot, throwOnMissingSubKey: false);
            session.Root.DeleteSubKeyTree(TrustedPublisherCertificatesRoot, throwOnMissingSubKey: false);
            session.Root.DeleteSubKeyTree(DisallowedCertificatesStoreRoot, throwOnMissingSubKey: false);
        });
    }

    /// <summary>
    /// Saves the enforcement settings.
    /// </summary>
    /// <param name="settings">The enforcement settings to persist.</param>
    public void SaveEnforcement(SoftwareRestrictionEnforcementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EditPolicy(session =>
        {
            EnsurePolicyRoot(session);
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyDefaultLevel, (uint)settings.DefaultSecurityLevel);
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyScopeFlags, (uint)settings.UserScope);
            SaferNativeMethods.SetPolicyDword(
                SAFER_POLICY_INFO_CLASS.SaferPolicyEnableTransparentEnforcement,
                settings.FileScope == SoftwareRestrictionFileScope.AllSoftwareFiles
                    ? TransparentEnabledAllFiles
                    : TransparentEnabledSkipDlls);
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyAuthenticodeEnabled, settings.CertificateRulesEnabled ? 1U : 0U);
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

        EditPolicy(session =>
        {
            EnsurePolicyRoot(session);

            // No SAFER API covers the designated file types; certmgr writes this REG_MULTI_SZ directly.
            using RegistryKey codeIdentifiers = session.Root.CreateSubKey(CodeIdentifiersRoot, writable: true);
            codeIdentifiers.SetValue(ExecutableTypesValueName, normalized, RegistryValueKind.MultiString);
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

        EditPolicy(session =>
        {
            EnsurePolicyRoot(session);
            if (!settings.IsDefined)
            {
                using RegistryKey? existing = session.Root.OpenSubKey(TrustedPublisherRoot, writable: true);
                existing?.DeleteValue(AuthenticodeFlagsValueName, throwOnMissingValue: false);
                return;
            }

            using RegistryKey trustedPublisher = session.Root.CreateSubKey(TrustedPublisherRoot, writable: true);
            trustedPublisher.SetValue(AuthenticodeFlagsValueName, unchecked((int)flags), RegistryValueKind.DWord);
        });
    }

    /// <summary>
    /// Creates or updates an additional rule.
    /// </summary>
    /// <param name="rule">The rule to persist.</param>
    public void UpsertRule(SoftwareRestrictionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        switch (rule.Kind)
        {
            case SoftwareRestrictionRuleKind.Unknown:
                throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
            case SoftwareRestrictionRuleKind.Certificate:
                UpsertCertificateRule(rule);
                return;
        }

        EditPolicy(session =>
        {
            EnsurePolicyRoot(session);

            SaferIdentification? existing = null;
            SoftwareRestrictionSecurityLevel existingLevel = default;
            if (TryParseRuleStoragePath(rule.StoragePath, out SoftwareRestrictionSecurityLevel storedLevel, out Guid storedId))
            {
                existing = TryReadIdentification(storedLevel, storedId);
                existingLevel = storedLevel;
            }

            Guid ruleId = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
            SaferIdentification identification = BuildIdentification(rule, ruleId, existing);

            // As in certmgr, a rule that changes security level is deleted from its old level first;
            // an edit within the same level simply replaces the identification.
            if (existing is not null
                && (existingLevel != rule.SecurityLevel || existing.Id != ruleId || existing.Type != identification.Type))
            {
                WithLevel(existingLevel, level => SaferNativeMethods.SetIdentification(level, existing, delete: true));
            }

            WithLevel(rule.SecurityLevel, level => SaferNativeMethods.SetIdentification(level, identification));
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

        EditPolicy(session =>
        {
            if (rule.Kind != SoftwareRestrictionRuleKind.Certificate
                && TryParseRuleStoragePath(rule.StoragePath, out SoftwareRestrictionSecurityLevel level, out Guid id)
                && TryReadIdentification(level, id) is { } existing)
            {
                WithLevel(level, handle => SaferNativeMethods.SetIdentification(handle, existing, delete: true));
                return;
            }

            // Certificate rules live in the policy certificate stores, and rules SAFER cannot decode are
            // removed by key.
            session.Root.DeleteSubKeyTree(rule.StoragePath, throwOnMissingSubKey: false);
        });
    }

    private void EditPolicy(Action<SaferPolicySession> mutate)
    {
        SaferPolicySession.Run(_gpoFactory, _logger, forEditing: true, session =>
        {
            RemoveLegacyDeleteMarkers(session);
            mutate(session);
            LastSaveNeedsRefresh = !session.Commit();
            return true;
        });
    }

    /// <summary>
    /// Creates the default policy with <c>SaferiPopulateDefaultsInRegistry</c> when none exists, as
    /// secpol.msc's "New Software Restriction Policies" does. It writes the security levels, enforcement
    /// values, designated file types and the SystemRoot/ProgramFilesDir Unrestricted path rules.
    /// </summary>
    private void EnsurePolicyRoot(SaferPolicySession session)
    {
        if (IsPolicyConfigured(session))
        {
            return;
        }

        bool populated = SaferNativeMethods.PopulateDefaultsInRegistry(session.Root.Handle);
        _logger.LogInformation("[SoftwareRestrictionPolicyService] SaferiPopulateDefaultsInRegistry wrote defaults: {Populated}", populated);

        if (!IsPolicyConfigured(session))
        {
            // A partial tree left by another tool can make SAFER skip its defaults; seed the root values.
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyDefaultLevel, (uint)SoftwareRestrictionSecurityLevel.Unrestricted);
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyEnableTransparentEnforcement, TransparentEnabledSkipDlls);
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyScopeFlags, (uint)SoftwareRestrictionUserScope.AllUsers);
            using RegistryKey codeIdentifiers = session.Root.CreateSubKey(CodeIdentifiersRoot, writable: true);
            if (codeIdentifiers.GetValue(ExecutableTypesValueName) is null)
            {
                codeIdentifiers.SetValue(ExecutableTypesValueName, DefaultDesignatedFileTypes, RegistryValueKind.MultiString);
            }
        }
    }

    private static bool IsPolicyConfigured(SaferPolicySession session)
    {
        using RegistryKey? codeIdentifiers = session.Root.OpenSubKey(CodeIdentifiersRoot, writable: false);
        return codeIdentifiers?.GetValue(DefaultLevelValueName) is not null;
    }

    /// <summary>
    /// Self-heal: earlier releases persisted <c>**delvals.</c>/<c>**del.</c> tombstones under the SRP trees,
    /// which the Registry CSE re-applies forever. Strip them from the GPO buffer before it is saved again.
    /// </summary>
    private void RemoveLegacyDeleteMarkers(SaferPolicySession session)
    {
        if (!session.IsGroupPolicyObject)
        {
            return;
        }

        foreach (string rootPath in new[] { CodeIdentifiersRoot, TrustedPublisherRoot, TrustedPublisherCertificatesRoot, DisallowedCertificatesStoreRoot })
        {
            using RegistryKey? key = session.Root.OpenSubKey(rootPath, writable: true);
            if (key is not null)
            {
                RemoveLegacyDeleteMarkers(key);
            }
        }
    }

    private void RemoveLegacyDeleteMarkers(RegistryKey key)
    {
        foreach (string valueName in key.GetValueNames())
        {
            if (valueName.StartsWith(LegacyDeleteMarkerPrefix, StringComparison.Ordinal))
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
                _logger.LogInformation("[SoftwareRestrictionPolicyService] Removed legacy delete marker {ValueName} under {KeyName}.", valueName, key.Name);
            }
        }

        foreach (string subKeyName in key.GetSubKeyNames())
        {
            using RegistryKey? subKey = key.OpenSubKey(subKeyName, writable: true);
            if (subKey is not null)
            {
                RemoveLegacyDeleteMarkers(subKey);
            }
        }
    }

    private static SoftwareRestrictionEnforcementSettings LoadEnforcement(SaferPolicySession session)
    {
        SoftwareRestrictionEnforcementSettings settings = new();
        if (!session.HasSaferScope)
        {
            return settings;
        }

        settings.DefaultSecurityLevel = SaferNativeMethods.GetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyDefaultLevel) switch
        {
            (uint)SoftwareRestrictionSecurityLevel.Disallowed => SoftwareRestrictionSecurityLevel.Disallowed,
            (uint)SoftwareRestrictionSecurityLevel.BasicUser => SoftwareRestrictionSecurityLevel.BasicUser,
            _ => SoftwareRestrictionSecurityLevel.Unrestricted
        };
        settings.UserScope = SaferNativeMethods.GetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyScopeFlags) == (uint)SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators
            ? SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators
            : SoftwareRestrictionUserScope.AllUsers;
        settings.FileScope = SaferNativeMethods.GetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyEnableTransparentEnforcement) == TransparentEnabledAllFiles
            ? SoftwareRestrictionFileScope.AllSoftwareFiles
            : SoftwareRestrictionFileScope.ExecutableFilesOnly;
        settings.CertificateRulesEnabled = SaferNativeMethods.GetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyAuthenticodeEnabled) == 1U;
        return settings;
    }

    private static SoftwareRestrictionTrustedPublisherSettings LoadTrustedPublishers(RegistryKey root)
    {
        using RegistryKey? trustedPublisher = root.OpenSubKey(TrustedPublisherRoot, writable: false);
        object? value = trustedPublisher?.GetValue(AuthenticodeFlagsValueName);
        uint flags = value is int dword ? unchecked((uint)dword) : 0;

        return new SoftwareRestrictionTrustedPublisherSettings
        {
            IsDefined = value is not null,
            PublisherScope = (SoftwareRestrictionPublisherScope)(flags & CertificatePublisherScopeMask),
            CheckPublisherRevocation = (flags & CertificatePublisherRevocationFlag) == CertificatePublisherRevocationFlag,
            CheckTimestampRevocation = (flags & CertificateTimestampRevocationFlag) == CertificateTimestampRevocationFlag
        };
    }

    private static List<string> LoadDesignatedFileTypes(RegistryKey root)
    {
        using RegistryKey? codeIdentifiers = root.OpenSubKey(CodeIdentifiersRoot, writable: false);
        IEnumerable<string> fileTypes = codeIdentifiers?.GetValue(ExecutableTypesValueName) switch
        {
            string[] array => array,
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

    /// <summary>
    /// Enumerates the path, hash and zone rules of every security level through SAFER, the way certmgr
    /// fills its result pane (SaferObjectAllIdentificationGuids, then SaferObjectSingleIdentification).
    /// </summary>
    private void LoadSaferRules(SaferPolicySession session, ICollection<SoftwareRestrictionRule> rules)
    {
        if (!session.HasSaferScope)
        {
            return;
        }

        foreach (SoftwareRestrictionSecurityLevel securityLevel in SecurityLevels)
        {
            SAFER_LEVEL_HANDLE level;
            try
            {
                level = SaferNativeMethods.OpenLevel((uint)securityLevel);
            }
            catch (Win32Exception ex)
            {
                _logger.LogDebug(ex, "[SoftwareRestrictionPolicyService] SAFER level {Level} is not available.", securityLevel);
                continue;
            }

            try
            {
                foreach (Guid id in SaferNativeMethods.GetIdentificationGuids(level))
                {
                    SaferIdentification? identification = SaferNativeMethods.GetIdentification(level, id);
                    if (identification is not null)
                    {
                        rules.Add(CreateRule(identification, securityLevel));
                    }
                }
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "[SoftwareRestrictionPolicyService] Failed to enumerate SAFER rules for level {Level}.", securityLevel);
            }
            finally
            {
                SaferNativeMethods.CloseLevel(level);
            }
        }
    }

    private static SoftwareRestrictionRule CreateRule(SaferIdentification identification, SoftwareRestrictionSecurityLevel level)
    {
        SoftwareRestrictionRule rule = new()
        {
            Id = identification.Id,
            SecurityLevel = level,
            Description = identification.Description,
            StoragePath = GetRuleStoragePath(level, identification.Type, identification.Id),
            LastModified = identification.LastModified
        };

        switch (identification.Type)
        {
            case SaferIdentificationType.ImageName:
                rule.Kind = SoftwareRestrictionRuleKind.Path;
                rule.Value = identification.ImagePath;
                break;
            case SaferIdentificationType.ImageHash:
                rule.Kind = SoftwareRestrictionRuleKind.Hash;
                rule.Value = Convert.ToHexString(identification.ImageHash);
                break;
            case SaferIdentificationType.UrlZone:
                rule.Kind = SoftwareRestrictionRuleKind.NetworkZone;
                rule.Value = identification.UrlZoneId.ToString(CultureInfo.InvariantCulture);
                break;
            default:
                rule.Kind = SoftwareRestrictionRuleKind.Unknown;
                break;
        }

        return rule;
    }

    private static SaferIdentification BuildIdentification(SoftwareRestrictionRule rule, Guid ruleId, SaferIdentification? existing)
    {
        SaferIdentification identification = new()
        {
            Id = ruleId,
            Description = rule.Description?.Trim() ?? string.Empty,
            SaferFlags = existing?.SaferFlags ?? 0
        };

        switch (rule.Kind)
        {
            case SoftwareRestrictionRuleKind.Path:
                ArgumentException.ThrowIfNullOrWhiteSpace(rule.Value);
                identification.Type = SaferIdentificationType.ImageName;
                identification.ImagePath = rule.Value.Trim();
                break;

            case SoftwareRestrictionRuleKind.NetworkZone:
                identification.Type = SaferIdentificationType.UrlZone;
                identification.UrlZoneId = ParseNetworkZone(rule.Value);
                break;

            case SoftwareRestrictionRuleKind.Hash:
                identification.Type = SaferIdentificationType.ImageHash;
                FillHash(identification, rule, existing);
                break;

            default:
                throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
        }

        return identification;
    }

    /// <summary>
    /// Computes the MD5 primary hash and SHA-256 companion for a newly chosen file, or keeps the stored
    /// hash when an existing rule is edited without re-browsing (only its description or level changes).
    /// </summary>
    private static void FillHash(SaferIdentification identification, SoftwareRestrictionRule rule, SaferIdentification? existing)
    {
        string filePath = string.IsNullOrWhiteSpace(rule.Value) ? string.Empty : ExpandRuleFilePath(rule.Value);
        if (filePath.Length > 0 && File.Exists(filePath))
        {
            using FileStream stream = File.OpenRead(filePath);
            identification.ImageSize = stream.Length;
            identification.ImageHash = MD5.HashData(stream);
            stream.Position = 0;
            identification.Sha256Hash = SHA256.HashData(stream);
            identification.HashAlgorithm = CalgMd5;
            identification.Sha256HashAlgorithm = CalgSha256;
            identification.FriendlyName = Path.GetFileName(filePath);
            return;
        }

        if (existing is not { Type: SaferIdentificationType.ImageHash })
        {
            throw new FileNotFoundException(rule.Value);
        }

        identification.ImageHash = existing.ImageHash;
        identification.HashAlgorithm = existing.HashAlgorithm;
        identification.ImageSize = existing.ImageSize;
        identification.Sha256Hash = existing.Sha256Hash;
        identification.Sha256HashAlgorithm = existing.Sha256HashAlgorithm;
        identification.FriendlyName = existing.FriendlyName;
    }

    private SaferIdentification? TryReadIdentification(SoftwareRestrictionSecurityLevel securityLevel, Guid id)
    {
        try
        {
            SaferIdentification? identification = null;
            WithLevel(securityLevel, level => identification = SaferNativeMethods.GetIdentification(level, id));
            return identification;
        }
        catch (Win32Exception ex)
        {
            _logger.LogDebug(ex, "[SoftwareRestrictionPolicyService] SAFER rule {RuleId} was not found at level {Level}.", id, securityLevel);
            return null;
        }
    }

    private static void WithLevel(SoftwareRestrictionSecurityLevel securityLevel, Action<SAFER_LEVEL_HANDLE> action)
    {
        SAFER_LEVEL_HANDLE level = SaferNativeMethods.OpenLevel((uint)securityLevel);
        try
        {
            action(level);
        }
        finally
        {
            SaferNativeMethods.CloseLevel(level);
        }
    }

    /// <summary>
    /// Formats the CodeIdentifiers key SAFER stores a rule under. It identifies the rule's level and GUID
    /// for later edits and is shown in the rule details.
    /// </summary>
    private static string GetRuleStoragePath(SoftwareRestrictionSecurityLevel level, SaferIdentificationType type, Guid id)
    {
        string kindKeyName = type switch
        {
            SaferIdentificationType.ImageHash => HashRulesKeyName,
            SaferIdentificationType.UrlZone => UrlZoneRulesKeyName,
            _ => PathRulesKeyName
        };

        return $@"{CodeIdentifiersRoot}\{((uint)level).ToString(CultureInfo.InvariantCulture)}\{kindKeyName}\{id:B}";
    }

    private static bool TryParseRuleStoragePath(string storagePath, out SoftwareRestrictionSecurityLevel level, out Guid id)
    {
        level = default;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(storagePath)
            || !storagePath.StartsWith(CodeIdentifiersRoot + @"\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = storagePath[(CodeIdentifiersRoot.Length + 1)..].Split('\\');
        if (segments.Length != 3
            || !uint.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint levelId)
            || !Enum.IsDefined((SoftwareRestrictionSecurityLevel)levelId)
            || !Guid.TryParse(segments[2], out id))
        {
            return false;
        }

        level = (SoftwareRestrictionSecurityLevel)levelId;
        return true;
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

        EditPolicy(session =>
        {
            EnsurePolicyRoot(session);

            string originalRuleKey = rule.StoragePath;
            string storeRoot = rule.SecurityLevel == SoftwareRestrictionSecurityLevel.Disallowed
                ? DisallowedCertificatesStoreRoot
                : TrustedPublisherCertificatesRoot;

            byte[] certificateBytes = ResolveCertificateRulePayload(session.Root, rule, originalRuleKey);
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            string ruleKey = $@"{storeRoot}\{certificate.Thumbprint}";

            if (!string.IsNullOrWhiteSpace(originalRuleKey)
                && !originalRuleKey.Equals(ruleKey, StringComparison.OrdinalIgnoreCase))
            {
                session.Root.DeleteSubKeyTree(originalRuleKey, throwOnMissingSubKey: false);
            }

            using (RegistryKey certificateKey = session.Root.CreateSubKey(ruleKey, writable: true))
            {
                certificateKey.SetValue(BlobValueName, BuildSerializedCertificateBlob(certificate, rule.Description), RegistryValueKind.Binary);
            }

            // Certificate rules are only evaluated while AuthenticodeEnabled=1; secpol.msc likewise prompts
            // to turn certificate rules on when the first one is created.
            SaferNativeMethods.SetPolicyDword(SAFER_POLICY_INFO_CLASS.SaferPolicyAuthenticodeEnabled, 1U);
        });
    }

    private static byte[] ResolveCertificateRulePayload(RegistryKey root, SoftwareRestrictionRule rule, string originalRuleKey)
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
            using RegistryKey? originalKey = root.OpenSubKey(originalRuleKey, writable: false);
            if (originalKey?.GetValue(BlobValueName) is byte[] blob
                && TryParseSerializedCertificateBlob(blob, out byte[]? fromBlob, out _)
                && fromBlob is not null)
            {
                return fromBlob;
            }

            // Rules written by earlier releases stored the bare DER under ItemData in the Safer tree.
            if (originalKey?.GetValue(ItemDataValueName) is byte[] derBytes && derBytes.Length > 0)
            {
                return derBytes;
            }
        }

        throw new InvalidOperationException(GetString(SoftwareRestrictionKeys.CertificateRuleInvalid));
    }

    private void LoadCertificateRulesFromStore(
        RegistryKey root,
        string storeRoot,
        SoftwareRestrictionSecurityLevel level,
        bool certificateRulesEnabled,
        ICollection<SoftwareRestrictionRule> rules)
    {
        using RegistryKey? store = root.OpenSubKey(storeRoot, writable: false);
        if (store is null)
        {
            return;
        }

        foreach (string entryKeyName in store.GetSubKeyNames())
        {
            using RegistryKey? entry = store.OpenSubKey(entryKeyName, writable: false);
            if (entry?.GetValue(BlobValueName) is not byte[] blob || blob.Length == 0)
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
                StoragePath = $@"{storeRoot}\{entryKeyName}"
            };

            ApplyCertificateRulePayload(rule, certificateBytes ?? blob, certificateRulesEnabled);
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

    private void ApplyCertificateRulePayload(SoftwareRestrictionRule rule, byte[] payload, bool certificateRulesEnabled)
    {
        if (TryApplyCertificateRule(rule, payload, certificateRulesEnabled))
        {
            return;
        }

        rule.Value = Convert.ToHexString(payload);
        rule.Details = BuildRawCertificateRuleDetails(rule, certificateRulesEnabled);
    }

    private bool TryApplyCertificateRule(SoftwareRestrictionRule rule, byte[] certificateBytes, bool certificateRulesEnabled)
    {
        if (certificateBytes.Length == 0)
        {
            return false;
        }

        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            string thumbprint = certificate.Thumbprint ?? Convert.ToHexString(certificateBytes);

            rule.Value = thumbprint;
            rule.CertificateSubject = GetPreferredName(certificate, forIssuer: false);
            rule.CertificateIssuer = GetPreferredName(certificate, forIssuer: true);
            rule.CertificateSerialNumber = certificate.SerialNumber;
            rule.CertificateThumbprint = thumbprint;
            rule.Details = BuildCertificateRuleDetails(
                rule,
                certificate,
                FormatCertificateStatus(certificate),
                FormatEnhancedKeyUsage(certificate),
                certificateRulesEnabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SoftwareRestrictionPolicyService] Unable to decode SRP certificate rule {RulePath}.", rule.StoragePath);
            return false;
        }
    }

    private static IReadOnlyList<SoftwareRestrictionDetailItem> BuildCertificateRuleDetails(
        SoftwareRestrictionRule rule,
        X509Certificate2 certificate,
        string status,
        string intendedPurposes,
        bool certificateRulesEnabled)
    {
        List<SoftwareRestrictionDetailItem> details =
        [
            CreateDetail(SoftwareRestrictionKeys.DetailRuleKind, rule.KindDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailSecurityLevel, rule.SecurityLevelDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailCertificateRulesEnabled, FormatBoolean(certificateRulesEnabled)),
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

        AddCommonDetails(rule, details);
        return details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static IReadOnlyList<SoftwareRestrictionDetailItem> BuildRawCertificateRuleDetails(
        SoftwareRestrictionRule rule,
        bool certificateRulesEnabled)
    {
        List<SoftwareRestrictionDetailItem> details =
        [
            CreateDetail(SoftwareRestrictionKeys.DetailRuleKind, rule.KindDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailSecurityLevel, rule.SecurityLevelDisplayName),
            CreateDetail(SoftwareRestrictionKeys.DetailCertificateRulesEnabled, FormatBoolean(certificateRulesEnabled)),
            CreateDetail(SoftwareRestrictionKeys.DetailRawData, rule.Value),
            CreateDetail(SoftwareRestrictionKeys.DetailStoragePath, rule.StoragePath)
        ];

        AddCommonDetails(rule, details);
        return details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static void AddCommonDetails(SoftwareRestrictionRule rule, List<SoftwareRestrictionDetailItem> details)
    {
        if (rule.LastModified is not null)
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailLastModified, rule.LastModified.Value.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            details.Add(CreateDetail(SoftwareRestrictionKeys.DetailDescription, rule.Description));
        }
    }

    private static SoftwareRestrictionDetailItem CreateDetail(string nameKey, string value)
    {
        return new SoftwareRestrictionDetailItem
        {
            Name = GetString(nameKey),
            Value = value
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
