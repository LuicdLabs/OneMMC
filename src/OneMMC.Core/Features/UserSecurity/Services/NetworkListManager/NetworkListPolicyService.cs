using System.IO;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Features.UserSecurity.Models.NetworkListManager;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.NetworkListManager;

/// <summary>
/// Reads and writes machine-scoped Network List Manager policies.
/// </summary>
public sealed class NetworkListPolicyService
{
    private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures";
    private const string LiveSignaturesRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures";
    private const string LiveProfilesRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";
    private const string EveryNetworkKey = "EveryNetwork";
    private const string ManagedSignaturesContainer = "Managed";
    private const string UnmanagedSignaturesContainer = "Unmanaged";
    // NLA writes Category=2 (NLM_NETWORK_CATEGORY_DOMAIN_AUTHENTICATED) on a domain network's profile.
    private const uint DomainAuthenticatedCategory = 2U;
    private const string UnidentifiedSignatureMarker = "0F0000F001";
    private const string IdentifyingSignatureMarker = "0F0000F002";
    // These two pseudo-network keys are stable secpol.msc policy targets. Live identified
    // network signatures are still discovered dynamically because they vary by machine.
    private const string CanonicalUnidentifiedSignatureKey = "010103000F0000F0010000000F0000F0C967A3643C3AD745950DA7859209176EF5B87C875FA20DF21951640E807D7C24";
    private const string CanonicalIdentifyingSignatureKey = "010103000F0000F0020000000F0000F0ABA0226144020107D469B778399BF3083A7EBB37586084F5B7A71A633E24B5AF";
    private const string SyntheticSignaturePrefix = "01010300";
    private const string SyntheticSignatureSuffixPrefix = "0000000F0000F0";
    private const string NeutralSyntheticHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly string[] ManagedValueNames =
    [
        "NetworkName",
        "NameReadOnly",
        "Icon16",
        "Icon24",
        "Icon32",
        "Icon48",
        "IconReadOnly",
        "Category",
        "CategoryReadOnly"
    ];

    private readonly ILogger<NetworkListPolicyService> _logger;
    private readonly LocalPolicyFileStore _localPolicyFileStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkListPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="localPolicyFileStore">The shared local policy file store.</param>
    public NetworkListPolicyService(
        ILogger<NetworkListPolicyService> logger,
        LocalPolicyFileStore localPolicyFileStore)
    {
        _logger = logger;
        _localPolicyFileStore = localPolicyFileStore;
    }

    /// <summary>
    /// Loads all nodes displayed on the Network List Manager page.
    /// </summary>
    /// <returns>A sorted collection of policy nodes.</returns>
    public IReadOnlyList<NetworkListPolicyNode> LoadNodes()
    {
        PolFile snapshot = LoadPolicySnapshot();
        List<NetworkListPolicyNode> nodes = [];

        nodes.AddRange(LoadIdentifiedNetworkNodes(snapshot));

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksDescription),
            SignatureId = CanonicalUnidentifiedSignatureKey,
            Kind = NetworkListPolicyNodeKind.UnidentifiedNetworks,
            State = ReadPolicyState(snapshot, CanonicalUnidentifiedSignatureKey)
        });

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksDescription),
            SignatureId = CanonicalIdentifyingSignatureKey,
            Kind = NetworkListPolicyNodeKind.IdentifyingNetworks,
            State = ReadPolicyState(snapshot, CanonicalIdentifyingSignatureKey)
        });

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksDescription),
            SignatureId = EveryNetworkKey,
            Kind = NetworkListPolicyNodeKind.AllNetworks,
            State = ReadPolicyState(snapshot, EveryNetworkKey)
        });

        return nodes;
    }

    /// <summary>
    /// Saves the configured network name.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="hasCustomName"><see langword="true"/> to persist a name; otherwise clear it.</param>
    /// <param name="networkName">The configured network name.</param>
    public void SaveNetworkName(string signatureId, bool hasCustomName, string? networkName)
    {
        SavePolicy(signatureId, (snapshot, keyPath) =>
        {
            if (hasCustomName && !string.IsNullOrWhiteSpace(networkName))
            {
                snapshot.SetValue(keyPath, "NetworkName", networkName.Trim(), RegistryValueKind.String);
            }
            else
            {
                DeleteValueIfPresent(snapshot, keyPath, "NetworkName");
            }
        });
    }

    /// <summary>
    /// Saves the configured icon payload.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="payload">The icon payload, or <see langword="null"/> to clear it.</param>
    public void SaveNetworkIcon(string signatureId, NetworkListIconPayload? payload)
    {
        SavePolicy(signatureId, (snapshot, keyPath) =>
        {
            if (payload?.IsConfigured == true)
            {
                snapshot.SetValue(keyPath, "Icon16", payload.Icon16Hex, RegistryValueKind.String);
                snapshot.SetValue(keyPath, "Icon24", payload.Icon24Hex, RegistryValueKind.String);
                snapshot.SetValue(keyPath, "Icon32", payload.Icon32Hex, RegistryValueKind.String);
                snapshot.SetValue(keyPath, "Icon48", payload.Icon48Hex, RegistryValueKind.String);
            }
            else
            {
                DeleteValueIfPresent(snapshot, keyPath, "Icon16");
                DeleteValueIfPresent(snapshot, keyPath, "Icon24");
                DeleteValueIfPresent(snapshot, keyPath, "Icon32");
                DeleteValueIfPresent(snapshot, keyPath, "Icon48");
            }
        });
    }

    /// <summary>
    /// Saves the name permission policy.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="mode">The permission mode to persist.</param>
    public void SaveNamePermission(string signatureId, NetworkListPermissionMode mode)
    {
        SavePermissionValue(signatureId, "NameReadOnly", mode);
    }

    /// <summary>
    /// Saves the icon permission policy.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="mode">The permission mode to persist.</param>
    public void SaveIconPermission(string signatureId, NetworkListPermissionMode mode)
    {
        SavePermissionValue(signatureId, "IconReadOnly", mode);
    }

    /// <summary>
    /// Saves the location type policy.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="mode">The location type to persist.</param>
    public void SaveLocationType(string signatureId, NetworkListLocationMode mode)
    {
        SavePolicy(signatureId, (snapshot, keyPath) =>
        {
            if (mode == NetworkListLocationMode.NotConfigured)
            {
                DeleteValueIfPresent(snapshot, keyPath, "Category");
            }
            else
            {
                snapshot.SetValue(
                    keyPath,
                    "Category",
                    mode == NetworkListLocationMode.Private ? 1U : 0U,
                    RegistryValueKind.DWord);
            }
        });
    }

    /// <summary>
    /// Saves the location permission policy.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="mode">The permission mode to persist.</param>
    public void SaveLocationPermission(string signatureId, NetworkListPermissionMode mode)
    {
        SavePermissionValue(signatureId, "CategoryReadOnly", mode);
    }

    private IEnumerable<NetworkListPolicyNode> LoadIdentifiedNetworkNodes(PolFile snapshot)
    {
        List<NetworkListPolicyNode> nodes = [];
        HashSet<string> seenSignatures = new(StringComparer.OrdinalIgnoreCase);

        foreach (string containerName in new[] { ManagedSignaturesContainer, UnmanagedSignaturesContainer })
        {
            using RegistryKey? containerKey = Registry.LocalMachine.OpenSubKey($@"{LiveSignaturesRoot}\{containerName}");
            if (containerKey is null)
            {
                continue;
            }

            foreach (string signatureId in containerKey.GetSubKeyNames())
            {
                if (!seenSignatures.Add(signatureId))
                {
                    continue;
                }

                using RegistryKey? liveSignatureKey = containerKey.OpenSubKey(signatureId);
                if (liveSignatureKey is null)
                {
                    continue;
                }

                string displayName = ResolveDisplayName(liveSignatureKey);
                nodes.Add(new NetworkListPolicyNode
                {
                    DisplayName = displayName,
                    Description = string.Empty,
                    SignatureId = signatureId,
                    Kind = NetworkListPolicyNodeKind.IdentifiedNetwork,
                    IsDomainAuthenticated = IsDomainAuthenticatedNetwork(containerName, liveSignatureKey),
                    State = ReadPolicyState(snapshot, signatureId)
                });
            }
        }

        nodes.Sort(static (left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return nodes;
    }

    private static string ResolveDisplayName(RegistryKey liveSignatureKey)
    {
        string? description = liveSignatureKey.GetValue("Description") as string;
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        string? firstNetwork = liveSignatureKey.GetValue("FirstNetwork") as string;
        if (!string.IsNullOrWhiteSpace(firstNetwork))
        {
            return firstNetwork;
        }

        using RegistryKey? profileKey = OpenProfileKey(liveSignatureKey);
        if (profileKey?.GetValue("ProfileName") is string profileName && !string.IsNullOrWhiteSpace(profileName))
        {
            return profileName;
        }

        return liveSignatureKey.Name.Split('\\').LastOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Determines whether an identified network is domain-authenticated, and therefore has no configurable
    /// network location.
    /// </summary>
    /// <remarks>
    /// NLA files a domain network's signature under the "Managed" container and stamps <c>Managed=1</c>
    /// (and, once the location is resolved, <c>Category=2</c>) on the backing profile. Either signal is
    /// enough; the profile is consulted as well because NLA re-files signatures between the two containers
    /// as networks are re-identified.
    /// </remarks>
    /// <param name="containerName">The signature container the network was discovered in.</param>
    /// <param name="liveSignatureKey">The live signature key.</param>
    /// <returns><see langword="true"/> when the network is domain-authenticated.</returns>
    private static bool IsDomainAuthenticatedNetwork(string containerName, RegistryKey liveSignatureKey)
    {
        if (string.Equals(containerName, ManagedSignaturesContainer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using RegistryKey? profileKey = OpenProfileKey(liveSignatureKey);
        if (profileKey is null)
        {
            return false;
        }

        return HasDwordValue(profileKey, "Managed", 1U)
            || HasDwordValue(profileKey, "Category", DomainAuthenticatedCategory);
    }

    private static RegistryKey? OpenProfileKey(RegistryKey liveSignatureKey)
    {
        if (liveSignatureKey.GetValue("ProfileGuid") is not string profileGuid
            || string.IsNullOrWhiteSpace(profileGuid))
        {
            return null;
        }

        return Registry.LocalMachine.OpenSubKey($@"{LiveProfilesRoot}\{profileGuid}");
    }

    private static bool HasDwordValue(RegistryKey key, string valueName, uint expected) =>
        key.GetValue(valueName) is int raw && (uint)raw == expected;

    private NetworkListPolicyState ReadPolicyState(PolFile snapshot, string signatureId)
    {
        string stateSignatureId = ResolveStateSignatureId(snapshot, signatureId);
        string keyPath = GetPolicyKeyPath(stateSignatureId);
        string? networkName = ReadStringValue(snapshot, keyPath, "NetworkName");
        string? icon16 = ReadStringValue(snapshot, keyPath, "Icon16");
        string? icon24 = ReadStringValue(snapshot, keyPath, "Icon24");
        string? icon32 = ReadStringValue(snapshot, keyPath, "Icon32");
        string? icon48 = ReadStringValue(snapshot, keyPath, "Icon48");

        NetworkListIconPayload? iconPayload = null;
        if (!string.IsNullOrWhiteSpace(icon16)
            || !string.IsNullOrWhiteSpace(icon24)
            || !string.IsNullOrWhiteSpace(icon32)
            || !string.IsNullOrWhiteSpace(icon48))
        {
            iconPayload = new NetworkListIconPayload
            {
                Icon16Hex = icon16 ?? string.Empty,
                Icon24Hex = icon24 ?? string.Empty,
                Icon32Hex = icon32 ?? string.Empty,
                Icon48Hex = icon48 ?? string.Empty
            };
        }

        return new NetworkListPolicyState
        {
            HasCustomName = !string.IsNullOrWhiteSpace(networkName),
            NetworkName = networkName ?? string.Empty,
            IconPayload = iconPayload,
            NamePermission = ReadPermissionMode(snapshot, keyPath, "NameReadOnly"),
            IconPermission = ReadPermissionMode(snapshot, keyPath, "IconReadOnly"),
            LocationType = ReadLocationMode(snapshot, keyPath, "Category"),
            LocationPermission = ReadPermissionMode(snapshot, keyPath, "CategoryReadOnly")
        };
    }

    private static string? ReadStringValue(PolFile snapshot, string keyPath, string valueName)
    {
        return snapshot.GetValue(keyPath, valueName) as string;
    }

    private static NetworkListPermissionMode ReadPermissionMode(PolFile snapshot, string keyPath, string valueName)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        if (value is null)
        {
            return NetworkListPermissionMode.NotConfigured;
        }

        return Convert.ToUInt32(value) switch
        {
            0U => NetworkListPermissionMode.Allow,
            1U => NetworkListPermissionMode.Deny,
            _ => NetworkListPermissionMode.NotConfigured
        };
    }

    private static NetworkListLocationMode ReadLocationMode(PolFile snapshot, string keyPath, string valueName)
    {
        object? value = snapshot.GetValue(keyPath, valueName);
        if (value is null)
        {
            return NetworkListLocationMode.NotConfigured;
        }

        return Convert.ToUInt32(value) switch
        {
            1U => NetworkListLocationMode.Private,
            0U => NetworkListLocationMode.Public,
            _ => NetworkListLocationMode.NotConfigured
        };
    }

    private void SavePermissionValue(string signatureId, string valueName, NetworkListPermissionMode mode)
    {
        SavePolicy(signatureId, (snapshot, keyPath) =>
        {
            if (mode == NetworkListPermissionMode.NotConfigured)
            {
                DeleteValueIfPresent(snapshot, keyPath, valueName);
            }
            else
            {
                snapshot.SetValue(
                    keyPath,
                    valueName,
                    mode == NetworkListPermissionMode.Allow ? 0U : 1U,
                    RegistryValueKind.DWord);
            }
        });
    }

    private void SavePolicy(string signatureId, Action<PolFile, string> mutateSnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        ArgumentNullException.ThrowIfNull(mutateSnapshot);

        PolFile snapshot = LoadWritablePolicySnapshot();

        string resolvedSignatureId = ResolveSignatureIdForSave(signatureId);
        string keyPath = GetPolicyKeyPath(resolvedSignatureId);
        mutateSnapshot(snapshot, keyPath);
        CleanupNeutralSyntheticKey(snapshot, resolvedSignatureId);

        EnsureMachinePolicyIsWritable();
        _ = _localPolicyFileStore.SaveSnapshot(isUser: false, snapshot);

        if (ShouldDeleteLiveRegistryKey(snapshot, keyPath))
        {
            DeleteRegistryKeyIfEmpty(keyPath);
        }

        foreach (string neutralKeyPath in GetNeutralSyntheticKeyPaths(resolvedSignatureId))
        {
            if (ShouldDeleteLiveRegistryKey(snapshot, neutralKeyPath))
            {
                DeleteRegistryKeyIfEmpty(neutralKeyPath);
            }
        }
    }

    private static bool ShouldDeleteLiveRegistryKey(PolFile snapshot, string keyPath)
    {
        return snapshot.GetValueNames(keyPath).Count == 0;
    }

    private static void DeleteRegistryKeyIfEmpty(string keyPath)
    {
        string? parentPath = Path.GetDirectoryName(keyPath)?.Replace('/', '\\');
        string? leafName = Path.GetFileName(keyPath);
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(leafName))
        {
            return;
        }

        using RegistryKey? parentKey = Registry.LocalMachine.OpenSubKey(parentPath, writable: true);
        using RegistryKey? leafKey = parentKey?.OpenSubKey(leafName, writable: false);
        if (parentKey is null || leafKey is null)
        {
            return;
        }

        if (leafKey.GetValueNames().Length == 0 && leafKey.GetSubKeyNames().Length == 0)
        {
            parentKey.DeleteSubKey(leafName, throwOnMissingSubKey: false);
        }
    }

    private static void DeleteValueIfPresent(PolFile snapshot, string keyPath, string valueName)
    {
        if (snapshot.ContainsValue(keyPath, valueName))
        {
            snapshot.DeleteValue(keyPath, valueName);
        }
    }

    private static string GetPolicyKeyPath(string signatureId) => $@"{PolicyRoot}\{signatureId}";

    private static string ResolveSignatureIdForSave(string signatureId)
    {
        if (TryResolveSyntheticSignature(signatureId, out _, out string canonicalSignatureKey))
        {
            return canonicalSignatureKey;
        }

        return signatureId;
    }

    private string ResolveStateSignatureId(PolFile snapshot, string signatureId)
    {
        if (!TryResolveSyntheticSignature(signatureId, out string marker, out string canonicalSignatureKey)
            || !signatureId.Equals(canonicalSignatureKey, StringComparison.OrdinalIgnoreCase)
            || HasManagedValues(snapshot, signatureId))
        {
            return signatureId;
        }

        string neutralSignatureKey = CreateNeutralSyntheticSignatureKey(marker);
        return HasManagedValues(snapshot, neutralSignatureKey)
            ? neutralSignatureKey
            : signatureId;
    }

    private static bool TryResolveSyntheticSignature(string signatureId, out string marker, out string canonicalSignatureKey)
    {
        marker = string.Empty;
        canonicalSignatureKey = string.Empty;

        string neutralUnidentifiedSignatureKey = CreateNeutralSyntheticSignatureKey(UnidentifiedSignatureMarker);
        if (signatureId.Equals(CanonicalUnidentifiedSignatureKey, StringComparison.OrdinalIgnoreCase)
            || signatureId.Equals(neutralUnidentifiedSignatureKey, StringComparison.OrdinalIgnoreCase))
        {
            marker = UnidentifiedSignatureMarker;
            canonicalSignatureKey = CanonicalUnidentifiedSignatureKey;
            return true;
        }

        string neutralIdentifyingSignatureKey = CreateNeutralSyntheticSignatureKey(IdentifyingSignatureMarker);
        if (signatureId.Equals(CanonicalIdentifyingSignatureKey, StringComparison.OrdinalIgnoreCase)
            || signatureId.Equals(neutralIdentifyingSignatureKey, StringComparison.OrdinalIgnoreCase))
        {
            marker = IdentifyingSignatureMarker;
            canonicalSignatureKey = CanonicalIdentifyingSignatureKey;
            return true;
        }

        return false;
    }

    private static string CreateNeutralSyntheticSignatureKey(string marker) =>
        $"{SyntheticSignaturePrefix}{marker}{SyntheticSignatureSuffixPrefix}{NeutralSyntheticHash}";

    private static IEnumerable<string> GetNeutralSyntheticKeyPaths(string signatureId)
    {
        if (!TryResolveSyntheticSignature(signatureId, out string marker, out _))
        {
            yield break;
        }

        yield return GetPolicyKeyPath(CreateNeutralSyntheticSignatureKey(marker));
    }

    private static void CleanupNeutralSyntheticKey(PolFile snapshot, string signatureId)
    {
        foreach (string neutralKeyPath in GetNeutralSyntheticKeyPaths(signatureId))
        {
            foreach (string valueName in ManagedValueNames)
            {
                DeleteValueIfPresent(snapshot, neutralKeyPath, valueName);
            }
        }
    }

    private static bool HasManagedValues(PolFile snapshot, string signatureId)
    {
        string keyPath = GetPolicyKeyPath(signatureId);
        if (ManagedValueNames.Any(valueName => snapshot.ContainsValue(keyPath, valueName)))
        {
            return true;
        }

        using RegistryKey? policyKey = Registry.LocalMachine.OpenSubKey(keyPath);
        return policyKey?.GetValueNames()
            .Any(valueName => ManagedValueNames.Contains(valueName, StringComparer.OrdinalIgnoreCase)) == true;
    }

    private PolFile LoadPolicySnapshot()
    {
        string polFilePath = _localPolicyFileStore.GetPolicyFilePath(isUser: false);
        if (File.Exists(polFilePath))
        {
            try
            {
                return PolFile.Load(polFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[NetworkListPolicyService] Failed to load Registry.pol. Falling back to live registry.");
            }
        }

        var snapshot = new PolFile();
        using RegistryKey? policyRootKey = Registry.LocalMachine.OpenSubKey(PolicyRoot);
        if (policyRootKey is not null)
        {
            CopyRegistryTreeToPolFile(policyRootKey, PolicyRoot, snapshot);
        }

        return snapshot;
    }

    private PolFile LoadWritablePolicySnapshot()
    {
        string polFilePath = _localPolicyFileStore.GetPolicyFilePath(isUser: false);
        if (!File.Exists(polFilePath))
        {
            return new PolFile();
        }

        try
        {
            return PolFile.Load(polFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NetworkListPolicyService] Failed to load writable Registry.pol snapshot. Starting from an empty snapshot.");
            return new PolFile();
        }
    }

    private static void CopyRegistryTreeToPolFile(RegistryKey sourceKey, string relativePath, PolFile destination)
    {
        foreach (string valueName in sourceKey.GetValueNames())
        {
            RegistryValueKind valueKind = sourceKey.GetValueKind(valueName);
            object? rawValue = sourceKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawValue is not null)
            {
                destination.SetValue(relativePath, valueName, rawValue, valueKind);
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

    private void EnsureMachinePolicyIsWritable()
    {
        if (_localPolicyFileStore.IsWritable(isUser: false, out string? error))
        {
            return;
        }

        throw new UnauthorizedAccessException(error ?? LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine));
    }
}
