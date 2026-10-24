using System.Globalization;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.NetworkListManager;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.NetworkListManager.Native;
using OneMMC.Core.Infrastructure.Interop;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.NetworkListManager;

/// <summary>
/// Reads and writes machine-scoped Network List Manager policies the way secpol.msc's Network List
/// Manager Policies node (nlmgp.dll) does.
/// </summary>
/// <remarks>
/// <para>
/// Networks are enumerated through the Network List Manager API (<see cref="INetworkListManager"/>,
/// all connected and disconnected networks) and classified by the <c>NA_NetworkClass</c> property, so
/// the "Identifying" and "Unidentified" pseudo-networks map to their fixed policy keys. NLM has no API
/// that maps a network to the signature its policy is keyed by, so — like nlmgp — the network's profile
/// GUID is looked up under the NLA <c>Signatures\Managed</c>/<c>Unmanaged</c> keys.
/// </para>
/// <para>
/// Policies are read from and written to the local Group Policy Object's machine registry buffer and
/// committed with the Registry client-side extension tagged with the Network List Manager snap-in GUID,
/// the pair nlmgp passes to <c>IGPEInformation::PolicyChanged</c>. A signature key whose values are all
/// cleared is removed, as nlmgp does with <c>SHDeleteEmptyKey</c>. Policy keys whose network no longer
/// exists are listed as "New Network" entries so they stay editable, matching secpol.msc.
/// </para>
/// </remarks>
public sealed class NetworkListPolicyService
{
    private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures";
    private const string LiveSignaturesRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures";
    private const string EveryNetworkKey = "EveryNetwork";
    private const string ManagedSignaturesContainer = "Managed";
    private const string UnmanagedSignaturesContainer = "Unmanaged";
    private const string ProfileGuidValueName = "ProfileGuid";
    private const string NetworkClassPropertyName = "NA_NetworkClass";
    private const string UnidentifiedSignatureMarker = "0F0000F001";
    private const string IdentifyingSignatureMarker = "0F0000F002";
    // These two pseudo-network keys are the fixed policy targets nlmgp.dll uses for the "Unidentified"
    // and "Identifying" networks. Identified network signatures are discovered per machine.
    private const string CanonicalUnidentifiedSignatureKey = "010103000F0000F0010000000F0000F0C967A3643C3AD745950DA7859209176EF5B87C875FA20DF21951640E807D7C24";
    private const string CanonicalIdentifyingSignatureKey = "010103000F0000F0020000000F0000F0ABA0226144020107D469B778399BF3083A7EBB37586084F5B7A71A633E24B5AF";
    // Earlier OneMMC builds also wrote the pseudo-network policies to a neutral-hash variant of the key.
    // It is still read when the canonical key is empty and is cleaned up on the next save.
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

    private static readonly GroupPolicyExtension[] NetworkListPolicyExtensions =
    [
        new(GroupPolicyGuids.RegistryExtensionGuid, GroupPolicyGuids.NetworkListManagerSnapInGuid)
    ];

    private readonly ILogger<NetworkListPolicyService> _logger;
    private readonly LocalGroupPolicyObjectFactory _gpoFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkListPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="gpoFactory">Opens the local Group Policy Object.</param>
    public NetworkListPolicyService(
        ILogger<NetworkListPolicyService> logger,
        LocalGroupPolicyObjectFactory gpoFactory)
    {
        _logger = logger;
        _gpoFactory = gpoFactory;
    }

    /// <summary>
    /// Loads all nodes displayed on the Network List Manager page.
    /// </summary>
    /// <returns>A sorted collection of policy nodes.</returns>
    public IReadOnlyList<NetworkListPolicyNode> LoadNodes()
    {
        return _gpoFactory.RunMachinePolicySession(forEditing: false, session => LoadNodes(session.Root));
    }

    /// <summary>
    /// Saves the configured network name.
    /// </summary>
    /// <param name="signatureId">The backing signature identifier.</param>
    /// <param name="hasCustomName"><see langword="true"/> to persist a name; otherwise clear it.</param>
    /// <param name="networkName">The configured network name.</param>
    public void SaveNetworkName(string signatureId, bool hasCustomName, string? networkName)
    {
        SavePolicy(signatureId, policyKey =>
        {
            if (hasCustomName && !string.IsNullOrWhiteSpace(networkName))
            {
                policyKey.SetValue("NetworkName", networkName.Trim(), RegistryValueKind.String);
            }
            else
            {
                policyKey.DeleteValue("NetworkName", throwOnMissingValue: false);
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
        SavePolicy(signatureId, policyKey =>
        {
            if (payload?.IsConfigured == true)
            {
                policyKey.SetValue("Icon16", payload.Icon16Hex, RegistryValueKind.String);
                policyKey.SetValue("Icon24", payload.Icon24Hex, RegistryValueKind.String);
                policyKey.SetValue("Icon32", payload.Icon32Hex, RegistryValueKind.String);
                policyKey.SetValue("Icon48", payload.Icon48Hex, RegistryValueKind.String);
            }
            else
            {
                policyKey.DeleteValue("Icon16", throwOnMissingValue: false);
                policyKey.DeleteValue("Icon24", throwOnMissingValue: false);
                policyKey.DeleteValue("Icon32", throwOnMissingValue: false);
                policyKey.DeleteValue("Icon48", throwOnMissingValue: false);
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
        SavePolicy(signatureId, policyKey =>
        {
            if (mode == NetworkListLocationMode.NotConfigured)
            {
                policyKey.DeleteValue("Category", throwOnMissingValue: false);
            }
            else
            {
                policyKey.SetValue(
                    "Category",
                    mode == NetworkListLocationMode.Private ? 1 : 0,
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

    private IReadOnlyList<NetworkListPolicyNode> LoadNodes(RegistryKey root)
    {
        List<NetworkListPolicyNode> nodes = LoadIdentifiedNetworkNodes(root);

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.UnidentifiedNetworksDescription),
            SignatureId = CanonicalUnidentifiedSignatureKey,
            Kind = NetworkListPolicyNodeKind.UnidentifiedNetworks,
            State = ReadPolicyState(root, CanonicalUnidentifiedSignatureKey)
        });

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.IdentifyingNetworksDescription),
            SignatureId = CanonicalIdentifyingSignatureKey,
            Kind = NetworkListPolicyNodeKind.IdentifyingNetworks,
            State = ReadPolicyState(root, CanonicalIdentifyingSignatureKey)
        });

        nodes.Add(new NetworkListPolicyNode
        {
            DisplayName = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksHeader),
            Description = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.AllNetworksDescription),
            SignatureId = EveryNetworkKey,
            Kind = NetworkListPolicyNodeKind.AllNetworks,
            State = ReadPolicyState(root, EveryNetworkKey)
        });

        return nodes;
    }

    private List<NetworkListPolicyNode> LoadIdentifiedNetworkNodes(RegistryKey root)
    {
        List<NetworkListPolicyNode> nodes = [];
        HashSet<string> seenSignatures = new(StringComparer.OrdinalIgnoreCase);

        foreach (NetworkListEntry network in EnumerateIdentifiedNetworks())
        {
            string? signatureId = FindNetworkSignature(root, network);
            if (signatureId is null || !seenSignatures.Add(signatureId))
            {
                _logger.LogDebug("[NetworkListPolicyService] No NLA signature found for network {NetworkId}.", network.Id);
                continue;
            }

            nodes.Add(new NetworkListPolicyNode
            {
                DisplayName = string.IsNullOrWhiteSpace(network.Name) ? signatureId : network.Name,
                Description = string.Empty,
                SignatureId = signatureId,
                Kind = NetworkListPolicyNodeKind.IdentifiedNetwork,
                IsDomainAuthenticated = network.IsDomainAuthenticated,
                State = ReadPolicyState(root, signatureId)
            });
        }

        nodes.Sort(static (left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        AddOrphanedPolicyNodes(root, seenSignatures, nodes);
        return nodes;
    }

    /// <summary>
    /// Enumerates every identified network (connected or not) known to the Network List Manager.
    /// </summary>
    private List<NetworkListEntry> EnumerateIdentifiedNetworks()
    {
        List<NetworkListEntry> networks = [];
        INetworkListManager? manager = null;
        IEnumNetworks? enumerator = null;
        try
        {
            manager = ComActivator.CreateInstance<INetworkListManager>(NetworkListManagerClsid.NetworkListManager);
            manager.GetNetworks(NlmEnumNetwork.All, out enumerator);
            while (enumerator.Next(1, out INetwork? network, out uint fetched) == 0 && fetched == 1 && network is not null)
            {
                try
                {
                    if (ReadNetworkClass(network) == NlmNetworkClass.Identified)
                    {
                        networks.Add(ReadNetwork(network));
                    }
                }
                catch (COMException ex)
                {
                    _logger.LogWarning(ex, "[NetworkListPolicyService] Failed to read a network from the Network List Manager.");
                }
                finally
                {
                    ComActivator.Release(network);
                }
            }
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "[NetworkListPolicyService] Network List Manager enumeration failed.");
        }
        finally
        {
            ComActivator.Release(enumerator);
            ComActivator.Release(manager);
        }

        return networks;
    }

    private static NetworkListEntry ReadNetwork(INetwork network)
    {
        network.GetNetworkId(out Guid id);
        network.GetName(out string name);
        network.GetDomainType(out NlmDomainType domainType);
        network.GetCategory(out NlmNetworkCategory category);
        return new NetworkListEntry(id, name, domainType, category);
    }

    /// <summary>
    /// Reads <c>NA_NetworkClass</c> from the network's property bag. A network that does not report the
    /// property is a regular (identified) network.
    /// </summary>
    private static NlmNetworkClass ReadNetworkClass(INetwork network)
    {
        if (network is not IPropertyBag propertyBag)
        {
            return NlmNetworkClass.Identified;
        }

        Variant value = default;
        try
        {
            return propertyBag.Read(NetworkClassPropertyName, ref value, 0) >= 0
                && uint.TryParse(value.ToInvariantString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint networkClass)
                ? (NlmNetworkClass)networkClass
                : NlmNetworkClass.Identified;
        }
        finally
        {
            value.Clear();
        }
    }

    /// <summary>
    /// Finds the NLA signature whose <c>ProfileGuid</c> is the network's id. Domain networks are filed
    /// under the "Managed" container and others under "Unmanaged", but NLA re-files signatures as networks
    /// are re-identified, so both are searched. When a network has several signatures the one that already
    /// carries a policy wins, as in nlmgp.
    /// </summary>
    private static string? FindNetworkSignature(RegistryKey root, NetworkListEntry network)
    {
        string profileGuid = network.Id.ToString("B");
        string[] containers = network.DomainType == NlmDomainType.NonDomainNetwork
            ? [UnmanagedSignaturesContainer, ManagedSignaturesContainer]
            : [ManagedSignaturesContainer, UnmanagedSignaturesContainer];

        string? firstMatch = null;
        foreach (string containerName in containers)
        {
            using RegistryKey? containerKey = Registry.LocalMachine.OpenSubKey($@"{LiveSignaturesRoot}\{containerName}");
            if (containerKey is null)
            {
                continue;
            }

            foreach (string signatureId in containerKey.GetSubKeyNames())
            {
                using RegistryKey? signatureKey = containerKey.OpenSubKey(signatureId);
                if (signatureKey?.GetValue(ProfileGuidValueName) is not string signatureProfile
                    || !string.Equals(signatureProfile, profileGuid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PolicyKeyExists(root, signatureId))
                {
                    return signatureId;
                }

                firstMatch ??= signatureId;
            }
        }

        return firstMatch;
    }

    /// <summary>
    /// Lists policy keys whose network signature no longer exists, as nlmgp does, so their policies can
    /// still be edited or cleared. They are named "New Network", "New Network 2", ….
    /// </summary>
    private static void AddOrphanedPolicyNodes(RegistryKey root, HashSet<string> seenSignatures, List<NetworkListPolicyNode> nodes)
    {
        using RegistryKey? policyRootKey = root.OpenSubKey(PolicyRoot);
        if (policyRootKey is null)
        {
            return;
        }

        int orphanCount = 0;
        foreach (string signatureId in policyRootKey.GetSubKeyNames())
        {
            if (seenSignatures.Contains(signatureId)
                || IsPseudoNetworkPolicyKey(signatureId)
                || LiveSignatureExists(signatureId))
            {
                continue;
            }

            orphanCount++;
            nodes.Add(new NetworkListPolicyNode
            {
                DisplayName = orphanCount == 1
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.NewNetworkName)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, NetworkListManagerKeys.NewNetworkNumberedNameFormat),
                        orphanCount),
                Description = string.Empty,
                SignatureId = signatureId,
                Kind = NetworkListPolicyNodeKind.IdentifiedNetwork,
                State = ReadPolicyState(root, signatureId)
            });
        }
    }

    private static bool IsPseudoNetworkPolicyKey(string signatureId)
    {
        return signatureId.Equals(EveryNetworkKey, StringComparison.OrdinalIgnoreCase)
            || TryResolveSyntheticSignature(signatureId, out _, out _);
    }

    private static bool LiveSignatureExists(string signatureId)
    {
        foreach (string containerName in new[] { ManagedSignaturesContainer, UnmanagedSignaturesContainer })
        {
            using RegistryKey? signatureKey = Registry.LocalMachine.OpenSubKey($@"{LiveSignaturesRoot}\{containerName}\{signatureId}");
            if (signatureKey is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PolicyKeyExists(RegistryKey root, string signatureId)
    {
        using RegistryKey? policyKey = root.OpenSubKey(GetPolicyKeyPath(signatureId));
        return policyKey is not null;
    }

    private static NetworkListPolicyState ReadPolicyState(RegistryKey root, string signatureId)
    {
        string stateSignatureId = ResolveStateSignatureId(root, signatureId);
        using RegistryKey? policyKey = root.OpenSubKey(GetPolicyKeyPath(stateSignatureId));
        if (policyKey is null)
        {
            return new NetworkListPolicyState();
        }

        string? networkName = policyKey.GetValue("NetworkName") as string;
        string? icon16 = policyKey.GetValue("Icon16") as string;
        string? icon24 = policyKey.GetValue("Icon24") as string;
        string? icon32 = policyKey.GetValue("Icon32") as string;
        string? icon48 = policyKey.GetValue("Icon48") as string;

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
            NamePermission = ReadPermissionMode(policyKey, "NameReadOnly"),
            IconPermission = ReadPermissionMode(policyKey, "IconReadOnly"),
            LocationType = ReadLocationMode(policyKey, "Category"),
            LocationPermission = ReadPermissionMode(policyKey, "CategoryReadOnly")
        };
    }

    private static uint? ReadDword(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName) is int value ? unchecked((uint)value) : null;
    }

    private static NetworkListPermissionMode ReadPermissionMode(RegistryKey policyKey, string valueName)
    {
        return ReadDword(policyKey, valueName) switch
        {
            0U => NetworkListPermissionMode.Allow,
            1U => NetworkListPermissionMode.Deny,
            _ => NetworkListPermissionMode.NotConfigured
        };
    }

    private static NetworkListLocationMode ReadLocationMode(RegistryKey policyKey, string valueName)
    {
        return ReadDword(policyKey, valueName) switch
        {
            1U => NetworkListLocationMode.Private,
            0U => NetworkListLocationMode.Public,
            _ => NetworkListLocationMode.NotConfigured
        };
    }

    private void SavePermissionValue(string signatureId, string valueName, NetworkListPermissionMode mode)
    {
        SavePolicy(signatureId, policyKey =>
        {
            if (mode == NetworkListPermissionMode.NotConfigured)
            {
                policyKey.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                policyKey.SetValue(
                    valueName,
                    mode == NetworkListPermissionMode.Allow ? 0 : 1,
                    RegistryValueKind.DWord);
            }
        });
    }

    private void SavePolicy(string signatureId, Action<RegistryKey> mutatePolicyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        ArgumentNullException.ThrowIfNull(mutatePolicyKey);

        string resolvedSignatureId = ResolveSignatureIdForSave(signatureId);
        bool refreshed = _gpoFactory.RunMachinePolicySession(forEditing: true, session =>
        {
            string keyPath = GetPolicyKeyPath(resolvedSignatureId);
            using (RegistryKey policyKey = session.Root.CreateSubKey(keyPath, writable: true))
            {
                mutatePolicyKey(policyKey);
            }

            DeleteKeyIfEmpty(session.Root, keyPath);
            CleanupNeutralSyntheticKey(session.Root, resolvedSignatureId);
            return session.Commit(NetworkListPolicyExtensions);
        });

        _logger.LogInformation(
            "[NetworkListPolicyService] Saved policy for signature {SignatureId}; policy refresh started: {Refreshed}",
            resolvedSignatureId,
            refreshed);
    }

    /// <summary>Removes a signature key that has no values or subkeys left (nlmgp's <c>SHDeleteEmptyKey</c>).</summary>
    private static void DeleteKeyIfEmpty(RegistryKey root, string keyPath)
    {
        using (RegistryKey? key = root.OpenSubKey(keyPath))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0)
            {
                return;
            }
        }

        root.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
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

    private static string ResolveStateSignatureId(RegistryKey root, string signatureId)
    {
        if (!TryResolveSyntheticSignature(signatureId, out string marker, out string canonicalSignatureKey)
            || !signatureId.Equals(canonicalSignatureKey, StringComparison.OrdinalIgnoreCase)
            || HasManagedValues(root, signatureId))
        {
            return signatureId;
        }

        string neutralSignatureKey = CreateNeutralSyntheticSignatureKey(marker);
        return HasManagedValues(root, neutralSignatureKey)
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

    private static void CleanupNeutralSyntheticKey(RegistryKey root, string signatureId)
    {
        if (!TryResolveSyntheticSignature(signatureId, out string marker, out _))
        {
            return;
        }

        string neutralKeyPath = GetPolicyKeyPath(CreateNeutralSyntheticSignatureKey(marker));
        using (RegistryKey? neutralKey = root.OpenSubKey(neutralKeyPath, writable: true))
        {
            if (neutralKey is null)
            {
                return;
            }

            foreach (string valueName in ManagedValueNames)
            {
                neutralKey.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        DeleteKeyIfEmpty(root, neutralKeyPath);
    }

    private static bool HasManagedValues(RegistryKey root, string signatureId)
    {
        using RegistryKey? policyKey = root.OpenSubKey(GetPolicyKeyPath(signatureId));
        return policyKey?.GetValueNames()
            .Any(valueName => ManagedValueNames.Contains(valueName, StringComparer.OrdinalIgnoreCase)) == true;
    }

    /// <summary>An identified network reported by the Network List Manager.</summary>
    private readonly record struct NetworkListEntry(Guid Id, string Name, NlmDomainType DomainType, NlmNetworkCategory Category)
    {
        /// <summary>
        /// Gets a value indicating whether the network is domain-authenticated, and therefore has no
        /// configurable network location (nlmgp checks the category the same way).
        /// </summary>
        public bool IsDomainAuthenticated =>
            Category == NlmNetworkCategory.DomainAuthenticated
            || DomainType == NlmDomainType.DomainAuthenticated;
    }
}
