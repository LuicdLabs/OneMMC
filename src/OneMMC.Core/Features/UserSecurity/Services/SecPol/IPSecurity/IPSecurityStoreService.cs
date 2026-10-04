using System.Globalization;
using System.Runtime.InteropServices;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Reads the legacy static local IPsec policy store through the native <c>polstore.dll</c>
/// enum APIs (<c>IPSecEnumPolicyData</c>, <c>IPSecEnumFilterData</c>, etc.).
/// </summary>
public sealed class IPSecurityStoreService
{
    /// <summary>Well-known NegPol action GUID: Block.</summary>
    private static readonly Guid NegPolActionBlock = new("3f91a819-7647-11d1-864d-d46a00000000");

    /// <summary>Well-known NegPol action GUID: Negotiate security.</summary>
    private static readonly Guid NegPolActionNegotiate = new("8a171dd3-77e3-11d1-8659-a04f00000000");

    private readonly ILogger<IPSecurityStoreService> _logger;
    private readonly IAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStoreService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator service.</param>
    public IPSecurityStoreService(
        ILogger<IPSecurityStoreService> logger,
        IAdminService adminService)
    {
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Loads the legacy static local IPsec policy store by enumerating native objects.
    /// </summary>
    /// <returns>A typed snapshot of policies, shared filter lists, and shared filter actions.</returns>
    public IPSecurityStoreSnapshot LoadSnapshot()
    {
        if (!IPSecurityPolicyNativeMethods.IsAvailable)
        {
            _logger.LogWarning(
                "The legacy IPsec policy store APIs are not available on this system. " +
                "Returning an empty snapshot.");
            return new IPSecurityStoreSnapshot();
        }

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr hStore, out int openError))
        {
            if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(openError) || openError == 5)
            {
                throw new UnauthorizedAccessException(
                    $"The local IPsec policy store could not be opened (native error 0x{openError:X8}).");
            }

            _logger.LogWarning(
                "The legacy IPsec policy store could not be opened (native error 0x{ErrorCode:X8}).",
                openError);
            return new IPSecurityStoreSnapshot();
        }

        try
        {
            return LoadFromNativeStore(hStore);
        }
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(hStore);
        }
    }

    private IPSecurityStoreSnapshot LoadFromNativeStore(IntPtr hStore)
    {
        Guid? activePolicyGuid = ReadActivePolicyGuid();

        // Build GUID→name maps first so NFA rule references can be resolved.
        Dictionary<Guid, string> filterNamesByGuid = [];
        Dictionary<Guid, string> negPolNamesByGuid = [];
        Dictionary<Guid, IPSecurityFilterActionDefinition> negPolsByGuid = [];

        List<IPSecurityFilterListDefinition> filterLists = EnumFilterLists(hStore, filterNamesByGuid);
        List<IPSecurityFilterActionDefinition> filterActions = EnumFilterActions(
            hStore, negPolNamesByGuid, negPolsByGuid);
        List<IpsecIsakmpData> mainModeObjects = EnumMainModeObjects(hStore);
        List<IPSecurityPolicyDefinition> policies = EnumPolicies(
            hStore, activePolicyGuid, filterNamesByGuid, negPolNamesByGuid, negPolsByGuid, mainModeObjects);

        return new IPSecurityStoreSnapshot
        {
            Policies = policies.OrderBy(static p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            FilterLists = filterLists.OrderBy(static f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            FilterActions = filterActions.OrderBy(static f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    /// <summary>
    /// Enumerates the store's main-mode (ISAKMP) objects so policies can resolve their reference.
    /// </summary>
    private static List<IpsecIsakmpData> EnumMainModeObjects(IntPtr hStore)
    {
        (IntPtr pp, int count) = EnumData(hStore, IPSecurityPolicyNativeMethods.EnumISAKMPData);
        List<IpsecIsakmpData> objects = [];
        if (count == 0 || pp == IntPtr.Zero)
        {
            return objects;
        }

        for (int i = 0; i < count; i++)
        {
            IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
            if (p == IntPtr.Zero)
            {
                continue;
            }

            unsafe
            {
                objects.Add(*(IpsecIsakmpData*)p);
            }
        }

        return objects;
    }

    /// <summary>Formats a main-mode object as netsh-style security-method tokens.</summary>
    private static List<string> FormatMainModeMethods(IpsecIsakmpData isakmp)
    {
        List<string> methods = [];
        int count = (int)isakmp.OfferCount;
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<IpsecMmOffer>();
        for (int index = 0; index < count && isakmp.Offers != IntPtr.Zero; index++)
        {
            IpsecMmOffer offer;
            unsafe { offer = *(IpsecMmOffer*)(isakmp.Offers + (size * index)); }
            methods.Add($"{FormatConfidentiality(offer.EncryptionAlgorithm)}-{FormatIntegrity(offer.HashAlgorithm)}-{offer.DiffieHellmanGroup}");
        }

        return methods;
    }

    /// <summary>Formats a confidentiality algorithm identifier.</summary>
    private static string FormatConfidentiality(uint algorithm)
    {
        return algorithm switch
        {
            IPSecurityPolicyLayout.EncryptionDes => "DES",
            IPSecurityPolicyLayout.EncryptionTripleDes => "3DES",
            0 => "None",
            _ => algorithm.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Formats an integrity algorithm identifier.</summary>
    private static string FormatIntegrity(uint algorithm)
    {
        return algorithm switch
        {
            IPSecurityPolicyLayout.HashMd5 => "MD5",
            IPSecurityPolicyLayout.HashSha1 => "SHA1",
            0 => "None",
            _ => algorithm.ToString(CultureInfo.InvariantCulture)
        };
    }

    // ===== Policy Enumeration =====

    /// <remarks>See <see cref="IpsecPolicyData"/> for the layout.</remarks>
    private List<IPSecurityPolicyDefinition> EnumPolicies(
        IntPtr hStore,
        Guid? activePolicyGuid,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames,
        Dictionary<Guid, IPSecurityFilterActionDefinition> negPols,
        List<IpsecIsakmpData> mainModeObjects)
    {
        (IntPtr pp, int count) = EnumData(
            hStore, IPSecurityPolicyNativeMethods.EnumPolicyData);
        if (count == 0 || pp == IntPtr.Zero) return [];

        List<IPSecurityPolicyDefinition> policies = new(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
            if (p == IntPtr.Zero) continue;

            Guid id = ReadGuid(p, 0);
            string name = ReadString(p, 48);
            if (string.IsNullOrEmpty(name)) continue;

            int pollingSeconds = Marshal.ReadInt32(p, 16);
            int whenChanged = Marshal.ReadInt32(p, 44);

            List<IPSecurityRuleDefinition> rules = EnumRulesForPolicy(
                hStore, id, name, filterNames, negPolNames, negPols);

            IpsecPolicyData policyData;
            unsafe { policyData = *(IpsecPolicyData*)p; }

            // Resolve the referenced main-mode object to read the settings the snap-in surfaces.
            IpsecIsakmpData? isakmp = null;
            foreach (IpsecIsakmpData candidate in mainModeObjects)
            {
                if (candidate.IsakmpIdentifier == policyData.IsakmpIdentifier)
                {
                    isakmp = candidate;
                    break;
                }
            }

            bool defaultRuleActive = rules.Exists(static rule =>
                string.IsNullOrEmpty(rule.FilterListName) && rule.IsActive);

            policies.Add(new IPSecurityPolicyDefinition
            {
                Name = name,
                Description = ReadString(p, 56),
                IsAssigned = activePolicyGuid.HasValue && activePolicyGuid.Value == id,
                UseMasterPerfectForwardSecrecy = isakmp?.MasterPfsEnabled != 0,
                QuickModeSessionsPerMainMode = (int)(isakmp?.QuickModeSessionsPerMainMode ?? 0),
                MainModeLifetimeMinutes = isakmp is { } mode && mode.MainModeLifetimeSeconds > 0
                    ? (int)(mode.MainModeLifetimeSeconds / 60)
                    : 0,
                IsDefaultResponseRuleActive = defaultRuleActive,
                PollingIntervalMinutes = pollingSeconds > 0 ? pollingSeconds / 60 : 0,
                MainModeSecurityMethods = isakmp is { } current ? FormatMainModeMethods(current) : [],
                LastModifiedTime = whenChanged > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(whenChanged)
                    : null,
                Rules = rules
            });
        }

        return policies;
    }

    // ===== Rule (NFA) Enumeration =====

    /// <remarks>See <see cref="IpsecNfaData"/> for the layout.</remarks>
    private List<IPSecurityRuleDefinition> EnumRulesForPolicy(
        IntPtr hStore,
        Guid policyId,
        string policyName,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames,
        Dictionary<Guid, IPSecurityFilterActionDefinition> negPols)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(4);
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(
                hStore, policyId, ppp, pCount);
            if (hr != 0) return [];

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            if (count == 0 || pp == IntPtr.Zero) return [];

            List<IPSecurityRuleDefinition> rules = new(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                rules.Add(ReadNfaData(p, policyName, filterNames, negPolNames, negPols));
            }

            return rules;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    /// <remarks>See <see cref="IpsecNfaData"/> for the layout this reads.</remarks>
    private static IPSecurityRuleDefinition ReadNfaData(
        IntPtr p,
        string policyName,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames,
        Dictionary<Guid, IPSecurityFilterActionDefinition> negPols)
    {
        IpsecNfaData nfa;
        unsafe { nfa = *(IpsecNfaData*)p; }

        filterNames.TryGetValue(nfa.FilterIdentifier, out string? filterListName);
        negPolNames.TryGetValue(nfa.NegPolIdentifier, out string? filterActionName);
        negPols.TryGetValue(nfa.NegPolIdentifier, out IPSecurityFilterActionDefinition? filterAction);

        return new IPSecurityRuleDefinition
        {
            Identifier = nfa.NfaIdentifier,
            IsDefaultResponseRule = nfa.FilterIdentifier == Guid.Empty,
            Name = ReadNativeString(nfa.Name),
            PolicyName = policyName,
            Description = ReadNativeString(nfa.Description),
            FilterListName = filterListName ?? string.Empty,
            FilterActionName = filterActionName ?? string.Empty,
            FilterAction = filterAction,
            ConnectionType = DescribeInterfaceType(nfa.InterfaceType),
            IsActive = nfa.ActiveFlag != 0,
            AuthenticationMethods = ReadAuthMethods(nfa)
        };
    }

    /// <summary>Reads the rule's authentication methods, which are an array of pointers.</summary>
    private static List<IPSecurityAuthenticationMethodDefinition> ReadAuthMethods(IpsecNfaData nfa)
    {
        List<IPSecurityAuthenticationMethodDefinition> methods = [];
        if (nfa.AuthMethods == IntPtr.Zero)
        {
            return methods;
        }

        for (int index = 0; index < (int)nfa.AuthMethodCount; index++)
        {
            IntPtr entry = Marshal.ReadIntPtr(nfa.AuthMethods, IntPtr.Size * index);
            if (entry == IntPtr.Zero)
            {
                continue;
            }

            IpsecAuthMethod method;
            unsafe { method = *(IpsecAuthMethod*)entry; }

            IPSecurityAuthenticationMethodKind kind = method.AuthType switch
            {
                IPSecurityPolicyLayout.AuthPreSharedKey => IPSecurityAuthenticationMethodKind.PreSharedKey,
                IPSecurityPolicyLayout.AuthCertificate => IPSecurityAuthenticationMethodKind.CertificateAuthority,
                _ => IPSecurityAuthenticationMethodKind.Kerberos
            };

            // The pre-shared key itself is never surfaced. Certificate command flags are encoded
            // by the reference writer as suffixes on the CA value.
            string rawDetail = kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
                ? ReadNativeString(method.AuthMethodValue)
                : string.Empty;
            bool certMap = rawDetail.EndsWith(" certmap:yes excludecaname:no", StringComparison.OrdinalIgnoreCase)
                || rawDetail.EndsWith(" certmap:yes excludecaname:yes", StringComparison.OrdinalIgnoreCase);
            bool excludeCaName = rawDetail.EndsWith(" excludecaname:yes", StringComparison.OrdinalIgnoreCase);
            int flagsIndex = rawDetail.IndexOf(" certmap:", StringComparison.OrdinalIgnoreCase);
            string detail = flagsIndex >= 0 ? rawDetail[..flagsIndex] : rawDetail;

            methods.Add(new IPSecurityAuthenticationMethodDefinition
            {
                Kind = kind,
                Detail = detail,
                EnableCertificateToAccountMapping = certMap,
                ExcludeCertificateAuthorityName = excludeCaName
            });
        }

        return methods;
    }

    private static string DescribeInterfaceType(uint interfaceType)
    {
        return interfaceType switch
        {
            IPSecurityPolicyLayout.InterfaceTypeLan => "lan",
            IPSecurityPolicyLayout.InterfaceTypeDialup => "dialup",
            IPSecurityPolicyLayout.InterfaceTypeAll => "all",
            _ => string.Empty
        };
    }

    private static string ReadNativeString(IntPtr pointer)
        => pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;

    // ===== Filter List Enumeration =====

    /// <remarks>See <see cref="IpsecFilterData"/> for the layout.</remarks>
    private List<IPSecurityFilterListDefinition> EnumFilterLists(
        IntPtr hStore,
        Dictionary<Guid, string> guidToName)
    {
        (IntPtr pp, int count) = EnumData(
            hStore, IPSecurityPolicyNativeMethods.EnumFilterData);
        if (count == 0 || pp == IntPtr.Zero) return [];

        List<IPSecurityFilterListDefinition> filterLists = new(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
            if (p == IntPtr.Zero) continue;

            Guid id = ReadGuid(p, 0);
            string name = ReadString(p, 40);
            if (string.IsNullOrEmpty(name)) continue;

            guidToName[id] = name;

            int numFilterSpecs = Marshal.ReadInt32(p, 16);
            List<IPSecurityFilterDefinition> filters = numFilterSpecs > 0
                ? ReadFilterSpecs(p, name, numFilterSpecs)
                : [];

            filterLists.Add(new IPSecurityFilterListDefinition
            {
                Name = name,
                Description = ReadString(p, 48),
                Filters = filters
            });
        }

        return filterLists;
    }

    /// <remarks>See <see cref="IpsecFilterSpec"/> for the layout this reads.</remarks>
    private static List<IPSecurityFilterDefinition> ReadFilterSpecs(
        IntPtr filterData,
        string filterListName,
        int count)
    {
        IpsecFilterData list;
        unsafe { list = *(IpsecFilterData*)filterData; }
        if (list.FilterSpecs == IntPtr.Zero) return [];

        List<IPSecurityFilterDefinition> filters = new(count);
        for (int index = 0; index < count; index++)
        {
            IntPtr entry = Marshal.ReadIntPtr(list.FilterSpecs, IntPtr.Size * index);
            if (entry == IntPtr.Zero) continue;

            filters.Add(ReadFilterSpec(entry, filterListName));
        }

        return filters;
    }

    private static IPSecurityFilterDefinition ReadFilterSpec(IntPtr spec, string filterListName)
    {
        IpsecFilterSpec data;
        unsafe { data = *(IpsecFilterSpec*)spec; }

        return new IPSecurityFilterDefinition
        {
            FilterListName = filterListName,
            Description = ReadNativeString(data.Description),
            SourceAddress = DescribeEndpoint(data.SourceAddress, ReadNativeString(data.SourceDnsName)),
            SourceMask = FormatIpAddress(data.SourceAddress.SubnetMask),
            DestinationAddress = DescribeEndpoint(data.DestinationAddress, ReadNativeString(data.DestinationDnsName)),
            DestinationMask = FormatIpAddress(data.DestinationAddress.SubnetMask),
            Protocol = FormatProtocol(data.Protocol),
            SourcePort = data.SourcePort.PortType == IPSecurityPolicyLayout.PortTypeAny ? 0 : (int)data.SourcePort.Port,
            DestinationPort = data.DestinationPort.PortType == IPSecurityPolicyLayout.PortTypeAny ? 0 : (int)data.DestinationPort.Port,
            IsMirrored = data.MirrorFlag != 0
        };
    }

    /// <summary>Renders one filter endpoint the way the legacy snap-in labels it.</summary>
    private static string DescribeEndpoint(IpsecAddress address, string dnsName)
    {
        if (!string.IsNullOrEmpty(dnsName))
        {
            return dnsName;
        }

        return address.AddressType switch
        {
            IPSecurityPolicyLayout.AddressTypeMe => "me",
            IPSecurityPolicyLayout.AddressTypeDnsServer => "dns",
            IPSecurityPolicyLayout.AddressTypeSpecific when address.IpAddress != 0
                => FormatIpAddress(address.IpAddress),
            _ => "any"
        };
    }


    // ===== Filter Action (NegPol) Enumeration =====

    /// <remarks>See <see cref="IpsecNegPolData"/> for the layout.</remarks>
    private List<IPSecurityFilterActionDefinition> EnumFilterActions(
        IntPtr hStore,
        Dictionary<Guid, string> guidToName,
        Dictionary<Guid, IPSecurityFilterActionDefinition> definitionsByGuid)
    {
        (IntPtr pp, int count) = EnumData(
            hStore, IPSecurityPolicyNativeMethods.EnumNegPolData);
        if (count == 0 || pp == IntPtr.Zero) return [];

        List<IPSecurityFilterActionDefinition> actions = new(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
            if (p == IntPtr.Zero) continue;

            Guid id = ReadGuid(p, 0);
            string name = ReadString(p, 72);
            IpsecNegPolData negPol;
            unsafe { negPol = *(IpsecNegPolData*)p; }

            Guid actionGuid = ReadGuid(p, 16);
            bool negotiateWithInboundPassthrough =
                actionGuid == IPSecurityPolicyLayout.ActionNegotiateAcceptUnsecuredInbound
                && negPol.NegPolType == IPSecurityPolicyLayout.NegPolTypeNegotiate;
            IPSecurityFilterActionKind action = actionGuid == NegPolActionBlock
                ? IPSecurityFilterActionKind.Block
                : actionGuid == NegPolActionNegotiate
                    || negotiateWithInboundPassthrough
                    ? IPSecurityFilterActionKind.Negotiate
                    : IPSecurityFilterActionKind.Permit;

            bool acceptUnsecuredInbound = negotiateWithInboundPassthrough;
            bool hasTerminator = HasSoftTerminator(negPol);
            bool quickModePfs = ReadQuickModePfs(negPol);

            var definition = new IPSecurityFilterActionDefinition
            {
                Name = name,
                Description = ReadString(p, 80),
                Action = action,
                UseQuickModePerfectForwardSecrecy = quickModePfs,
                AcceptUnsecuredInbound = acceptUnsecuredInbound,
                AllowUnsecuredFallback = hasTerminator,
                QuickModeSecurityMethods = FormatQuickModeMethods(negPol, hasTerminator)
            };
            definitionsByGuid[id] = definition;

            // Only named actions are shared and therefore belong in the Manage Filter Actions list.
            if (!string.IsNullOrEmpty(name))
            {
                guidToName[id] = name;
                actions.Add(definition);
            }
        }

        return actions;
    }

    /// <summary>Reads the quick-mode PFS flag from the first security method.</summary>
    private static bool ReadQuickModePfs(IpsecNegPolData negPol)
    {
        if (negPol.SecurityMethods == IntPtr.Zero || negPol.SecurityMethodCount == 0)
        {
            return false;
        }

        IpsecSecurityMethod first;
        unsafe { first = *(IpsecSecurityMethod*)negPol.SecurityMethods; }
        return first.QuickModePfsEnabled != 0;
    }

    /// <summary>
    /// Detects the soft encoding: the last method is an all-zero terminator entry.
    /// </summary>
    private static bool HasSoftTerminator(IpsecNegPolData negPol)
    {
        if (negPol.SecurityMethods == IntPtr.Zero || negPol.SecurityMethodCount == 0)
        {
            return false;
        }

        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<IpsecSecurityMethod>();
        IpsecSecurityMethod last;
        unsafe
        {
            last = *(IpsecSecurityMethod*)(
                negPol.SecurityMethods + (size * ((int)negPol.SecurityMethodCount - 1)));
        }

        return last.Transform == 0 && last.PrimaryAlgorithm == 0;
    }

    /// <summary>Formats a negotiation policy's methods as netsh-style tokens with lifetimes.</summary>
    private static List<string> FormatQuickModeMethods(IpsecNegPolData negPol, bool skipTerminator)
    {
        List<string> methods = [];
        int count = (int)negPol.SecurityMethodCount;
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<IpsecSecurityMethod>();
        for (int index = 0; index < count && negPol.SecurityMethods != IntPtr.Zero; index++)
        {
            IpsecSecurityMethod method;
            unsafe { method = *(IpsecSecurityMethod*)(negPol.SecurityMethods + (size * index)); }

            bool isTerminator = method.Transform == 0 && method.PrimaryAlgorithm == 0;
            if (isTerminator && skipTerminator)
            {
                continue;
            }

            methods.Add(FormatQuickModeMethod(method, isTerminator));
        }

        return methods;
    }

    private static string FormatQuickModeMethod(IpsecSecurityMethod method, bool isTerminator)
    {
        if (isTerminator)
        {
            return "NONE";
        }

        string lifetime = method.LifetimeSeconds > 0 || method.LifetimeKilobytes > 0
            ? $":{method.LifetimeKilobytes}k/{method.LifetimeSeconds}s"
            : string.Empty;

        if (method.Transform == IPSecurityPolicyLayout.TransformAh)
        {
            return $"AH[{FormatIntegrity(method.PrimaryAlgorithm)}]{lifetime}";
        }

        string confidentiality = FormatConfidentiality(method.PrimaryAlgorithm);
        string integrity = FormatIntegrity(method.SecondaryAlgorithm);
        return $"ESP[{confidentiality},{integrity}]{lifetime}";
    }

    // ===== Authentication Methods =====

    /// <remarks>
    /// <c>IPSEC_AUTH_METHOD</c> layout (32 bytes, x64, derived from NT headers):
    /// <code>
    /// +0   DWORD   dwAuthType            (4: 1=PSK, 2=Certificate, 3=Kerberos)
    /// +4   DWORD   dwAuthLen             (4)
    /// +8   PTR     pszAuthMethod         (8)
    /// +16  DWORD   dwAltAuthLen          (4)
    /// +24  PTR     pszAltAuthMethod      (8)
    /// </code>
    /// </remarks>
    private static List<IPSecurityAuthenticationMethodDefinition> ReadAuthenticationMethods(
        IntPtr pAuth,
        int count)
    {
        const int authMethodSize = 32;
        List<IPSecurityAuthenticationMethodDefinition> methods = new(count);

        for (int i = 0; i < count; i++)
        {
            IntPtr entry = pAuth + (authMethodSize * i);
            int authType = Marshal.ReadInt32(entry, 0);

            methods.Add(authType switch
            {
                1 => new IPSecurityAuthenticationMethodDefinition
                {
                    Kind = IPSecurityAuthenticationMethodKind.PreSharedKey
                },
                2 => new IPSecurityAuthenticationMethodDefinition
                {
                    Kind = IPSecurityAuthenticationMethodKind.CertificateAuthority,
                    Detail = ReadString(entry, 8)
                },
                _ => new IPSecurityAuthenticationMethodDefinition
                {
                    Kind = IPSecurityAuthenticationMethodKind.Kerberos
                }
            });
        }

        return methods;
    }

    // ===== Active Policy =====

    /// <summary>
    /// Reads the assigned (active) policy GUID from the local IPsec registry store.
    /// </summary>
    /// <remarks>
    /// The <c>ActivePolicy</c> value under
    /// <c>HKLM\SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local</c>
    /// contains the DN-style path to the assigned policy key (e.g.
    /// <c>SOFTWARE\...\ipsecPolicy{GUID}</c>). There is no <c>polstore.dll</c>
    /// export that returns this value, so a single registry read is required.
    /// </remarks>
    private static Guid? ReadActivePolicyGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local");
            string? activePath = key?.GetValue("ActivePolicy") as string;
            if (string.IsNullOrEmpty(activePath)) return null;

            int braceStart = activePath.LastIndexOf('{');
            int braceEnd = activePath.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart &&
                Guid.TryParse(activePath[braceStart..(braceEnd + 1)], out Guid guid))
            {
                return guid;
            }
        }
        catch
        {
            // Non-critical; IsAssigned will default to false.
        }

        return null;
    }

    // ===== Native Enum Helper =====

    private delegate int EnumDataFunc(IntPtr hStore, IntPtr pppData, IntPtr pdwCount);

    private static (IntPtr Array, int Count) EnumData(IntPtr hStore, EnumDataFunc enumFunc)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(4);
        try
        {
            int hr = enumFunc(hStore, ppp, pCount);
            if (hr != 0) return (IntPtr.Zero, 0);

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            return (pp, count);
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    // ===== Struct Reading Helpers =====

    private static string ReadString(IntPtr structPtr, int offset)
    {
        IntPtr strPtr = Marshal.ReadIntPtr(structPtr, offset);
        return strPtr != IntPtr.Zero
            ? Marshal.PtrToStringUni(strPtr) ?? string.Empty
            : string.Empty;
    }

    private static Guid ReadGuid(IntPtr structPtr, int offset)
    {
        byte[] bytes = new byte[16];
        Marshal.Copy(structPtr + offset, bytes, 0, 16);
        return new Guid(bytes);
    }


    // ===== Formatting Helpers =====

    private static string FormatIpAddress(uint addr)
    {
        if (addr == 0) return "0.0.0.0";
        byte[] bytes = BitConverter.GetBytes(addr);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }


    private static string FormatProtocol(uint protocol)
    {
        return protocol switch
        {
            0 => "any",
            1 => "ICMP",
            6 => "TCP",
            17 => "UDP",
            _ => protocol.ToString(CultureInfo.InvariantCulture)
        };
    }

}
