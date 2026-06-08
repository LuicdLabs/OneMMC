using System.Globalization;
using System.Runtime.InteropServices;
using ManagementTools.Core.Abstractions.Services;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Reads the legacy static local IPsec policy store through the native <c>polstore.dll</c>
/// enum APIs (<c>IPSecEnumPolicyData</c>, <c>IPSecEnumFilterData</c>, etc.).
/// </summary>
public sealed class IPSecurityStaticPolicyStoreService
{
    /// <summary>Well-known NegPol action GUID: Block.</summary>
    private static readonly Guid NegPolActionBlock = new("3f91a819-7647-11d1-864d-d46a00000000");

    /// <summary>Well-known NegPol action GUID: Negotiate security.</summary>
    private static readonly Guid NegPolActionNegotiate = new("8a171dd3-77e3-11d1-8659-a04f00000000");

    private readonly ILogger<IPSecurityStaticPolicyStoreService> _logger;
    private readonly IAdminService _adminService;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyStoreService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator service.</param>
    public IPSecurityStaticPolicyStoreService(
        ILogger<IPSecurityStaticPolicyStoreService> logger,
        IAdminService adminService)
    {
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Loads the legacy static local IPsec policy store by enumerating native objects.
    /// </summary>
    /// <returns>A typed snapshot of policies, shared filter lists, and shared filter actions.</returns>
    public IPSecurityStaticStoreSnapshot LoadSnapshot()
    {
        if (!IPSecurityPolicyNativeMethods.IsAvailable)
        {
            _logger.LogWarning(
                "The legacy IPsec policy store APIs are not available on this system. " +
                "Returning an empty snapshot.");
            return new IPSecurityStaticStoreSnapshot();
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
            return new IPSecurityStaticStoreSnapshot();
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

    private IPSecurityStaticStoreSnapshot LoadFromNativeStore(IntPtr hStore)
    {
        Guid? activePolicyGuid = ReadActivePolicyGuid();

        // Build GUID→name maps first so NFA rule references can be resolved.
        Dictionary<Guid, string> filterNamesByGuid = [];
        Dictionary<Guid, string> negPolNamesByGuid = [];

        List<IPSecurityFilterListDefinition> filterLists = EnumFilterLists(hStore, filterNamesByGuid);
        List<IPSecurityFilterActionDefinition> filterActions = EnumFilterActions(hStore, negPolNamesByGuid);
        List<IPSecurityPolicyDefinition> policies = EnumPolicies(
            hStore, activePolicyGuid, filterNamesByGuid, negPolNamesByGuid);

        return new IPSecurityStaticStoreSnapshot
        {
            Policies = policies.OrderBy(static p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            FilterLists = filterLists.OrderBy(static f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            FilterActions = filterActions.OrderBy(static f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    // ===== Policy Enumeration =====

    /// <remarks>
    /// <c>IPSEC_POLICY_DATA</c> layout (80 bytes, x64, verified):
    /// <code>
    /// +0   GUID    PolicyIdentifier      (16)
    /// +16  DWORD   dwPollingInterval     (4, seconds)
    /// +24  PTR     pIpsecISAKMPData      (8)
    /// +32  PTR     ppIpsecNFAData        (8)
    /// +40  DWORD   dwNumNFACount         (4)
    /// +44  DWORD   dwWhenChanged         (4, Unix seconds)
    /// +48  PTR     pszIpsecName          (8)
    /// +56  PTR     pszDescription        (8)
    /// +64  GUID    ISAKMPIdentifier      (16)
    /// </code>
    /// </remarks>
    private List<IPSecurityPolicyDefinition> EnumPolicies(
        IntPtr hStore,
        Guid? activePolicyGuid,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames)
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
                hStore, id, name, filterNames, negPolNames);

            policies.Add(new IPSecurityPolicyDefinition
            {
                Name = name,
                Description = ReadString(p, 56),
                IsAssigned = activePolicyGuid.HasValue && activePolicyGuid.Value == id,
                PollingIntervalMinutes = pollingSeconds > 0 ? pollingSeconds / 60 : 0,
                LastModifiedTime = whenChanged > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(whenChanged)
                    : null,
                Rules = rules
            });
        }

        return policies;
    }

    // ===== Rule (NFA) Enumeration =====

    /// <remarks>
    /// <c>IPSEC_NFA_DATA</c> layout (112 bytes, x64, derived from NT headers):
    /// <code>
    /// +0   GUID    NFAIdentifier         (16)
    /// +16  PTR     pszIpsecName          (8)
    /// +24  PTR     pszDescription        (8)
    /// +32  DWORD   dwWhenChanged         (4)
    /// +40  PTR     pszInterfaceName      (8)
    /// +48  DWORD   dwInterfaceType       (4)
    /// +52  DWORD   dwActiveFlag          (4)
    /// +56  DWORD   dwTunnelIpAddr        (4)
    /// +60  DWORD   dwTunnelFlags         (4)
    /// +64  GUID    NegPolIdentifier      (16)
    /// +80  GUID    FilterIdentifier      (16)
    /// +96  DWORD   dwAuthMethodCount     (4)
    /// +104 PTR     pIpsecAuthMethods     (8)
    /// </code>
    /// </remarks>
    private List<IPSecurityRuleDefinition> EnumRulesForPolicy(
        IntPtr hStore,
        Guid policyId,
        string policyName,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames)
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

            // Dump first NFA struct for layout verification.
            IntPtr firstNfa = Marshal.ReadIntPtr(pp, 0);
            if (firstNfa != IntPtr.Zero)
            {
                _logger.LogDebug(
                    "IPSEC_NFA_DATA hex dump (first 128 bytes, policy '{Policy}'):\n{Hex}",
                    policyName, DumpHex(firstNfa, 128));
            }

            List<IPSecurityRuleDefinition> rules = new(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                rules.Add(ReadNfaData(p, policyName, filterNames, negPolNames));
            }

            return rules;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static IPSecurityRuleDefinition ReadNfaData(
        IntPtr p,
        string policyName,
        Dictionary<Guid, string> filterNames,
        Dictionary<Guid, string> negPolNames)
    {
        // Offsets are derived from NT headers and not yet verified on this build.
        // Read only the GUID at +0 (known safe) and return a placeholder until
        // the hex dump above confirms the real field positions.
        return new IPSecurityRuleDefinition
        {
            Name = string.Empty,
            PolicyName = policyName,
        };
    }

    // ===== Filter List Enumeration =====

    /// <remarks>
    /// <c>IPSEC_FILTER_DATA</c> layout (56 bytes, x64, verified):
    /// <code>
    /// +0   GUID    FilterIdentifier      (16)
    /// +16  DWORD   dwNumFilterSpecs      (4)
    /// +24  PTR     ppFilterSpecs         (8, IPSEC_FILTER_SPEC**)
    /// +32  DWORD   dwWhenChanged         (4)
    /// +40  PTR     pszIpsecName          (8)
    /// +48  PTR     pszDescription        (8)
    /// </code>
    /// </remarks>
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

    /// <remarks>
    /// <c>IPSEC_FILTER_SPEC</c> layout (x64, derived from NT headers):
    /// <code>
    /// +0   PTR     pszSrcDNSName         (8)
    /// +8   PTR     pszDestDNSName        (8)
    /// +16  PTR     pszDescription        (8)
    /// +24  GUID    FilterSpecGUID        (16)
    /// +40  DWORD   dwMirrorFlag          (4)
    /// +44  IPSEC_FILTER (embedded, 40 bytes):
    ///   +44  ULONG SrcAddr
    ///   +48  ULONG SrcMask
    ///   +52  ULONG DestAddr
    ///   +56  ULONG DestMask
    ///   +60  ULONG TunnelAddr
    ///   +64  ULONG Protocol
    ///   +68  ULONG SrcPort
    ///   +72  ULONG DestPort
    ///   +76  ULONG TunnelFilter
    ///   +80  ULONG Flags
    /// </code>
    /// </remarks>
    private List<IPSecurityFilterDefinition> ReadFilterSpecs(
        IntPtr filterData,
        string filterListName,
        int count)
    {
        IntPtr ppSpecs = Marshal.ReadIntPtr(filterData, 24);
        if (ppSpecs == IntPtr.Zero) return [];

        // Dump first filter spec for layout verification.
        IntPtr firstSpec = Marshal.ReadIntPtr(ppSpecs, 0);
        if (firstSpec != IntPtr.Zero)
        {
            _logger.LogDebug(
                "IPSEC_FILTER_SPEC hex dump (first 96 bytes, filter list '{FilterList}'):\n{Hex}",
                filterListName, DumpHex(firstSpec, 96));
        }

        // Offsets are derived from NT headers and not yet verified on this build.
        // Return empty until the hex dump confirms the real field positions.
        return [];
    }

    private static IPSecurityFilterDefinition ReadFilterSpec(IntPtr spec, string filterListName)
    {
        string srcDns = ReadString(spec, 0);
        string dstDns = ReadString(spec, 8);

        uint srcAddr = unchecked((uint)Marshal.ReadInt32(spec, 44));
        uint srcMask = unchecked((uint)Marshal.ReadInt32(spec, 48));
        uint dstAddr = unchecked((uint)Marshal.ReadInt32(spec, 52));
        uint dstMask = unchecked((uint)Marshal.ReadInt32(spec, 56));
        uint protocol = unchecked((uint)Marshal.ReadInt32(spec, 64));
        uint srcPort = unchecked((uint)Marshal.ReadInt32(spec, 68));
        uint dstPort = unchecked((uint)Marshal.ReadInt32(spec, 72));
        int mirrorFlag = Marshal.ReadInt32(spec, 40);

        return new IPSecurityFilterDefinition
        {
            FilterListName = filterListName,
            Description = ReadString(spec, 16),
            SourceAddress = FormatAddress(srcAddr, srcDns),
            SourceMask = FormatIpAddress(srcMask),
            DestinationAddress = FormatAddress(dstAddr, dstDns),
            DestinationMask = FormatIpAddress(dstMask),
            Protocol = FormatProtocol(protocol),
            SourcePort = (int)srcPort,
            DestinationPort = (int)dstPort,
            IsMirrored = mirrorFlag != 0
        };
    }

    // ===== Filter Action (NegPol) Enumeration =====

    /// <remarks>
    /// <c>IPSEC_NEGPOL_DATA</c> layout (88 bytes, x64, verified):
    /// <code>
    /// +0   GUID    NegPolIdentifier      (16)
    /// +16  GUID    NegPolAction          (16)
    /// +32  GUID    NegPolType            (16)
    /// +48  DWORD   dwSecurityMethodCount (4)
    /// +56  PTR     pIpsecSecurityMethods (8)
    /// +64  DWORD   dwWhenChanged         (4)
    /// +72  PTR     pszIpsecName          (8)
    /// +80  PTR     pszDescription        (8)
    /// </code>
    /// </remarks>
    private List<IPSecurityFilterActionDefinition> EnumFilterActions(
        IntPtr hStore,
        Dictionary<Guid, string> guidToName)
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
            if (string.IsNullOrEmpty(name)) continue;

            guidToName[id] = name;

            Guid actionGuid = ReadGuid(p, 16);
            IPSecurityFilterActionKind action = actionGuid == NegPolActionBlock
                ? IPSecurityFilterActionKind.Block
                : actionGuid == NegPolActionNegotiate
                    ? IPSecurityFilterActionKind.Negotiate
                    : IPSecurityFilterActionKind.Permit;

            actions.Add(new IPSecurityFilterActionDefinition
            {
                Name = name,
                Description = ReadString(p, 80),
                Action = action
            });
        }

        return actions;
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

    private static string DumpHex(IntPtr ptr, int length)
    {
        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        var sb = new System.Text.StringBuilder(length * 4);
        for (int row = 0; row < length; row += 16)
        {
            sb.Append($"+{row,3:D3}  ");
            int end = Math.Min(row + 16, length);
            for (int col = row; col < end; col++)
            {
                sb.Append($"{buffer[col]:X2} ");
                if (col == row + 7) sb.Append(' ');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ===== Formatting Helpers =====

    private static string FormatIpAddress(uint addr)
    {
        if (addr == 0) return "0.0.0.0";
        byte[] bytes = BitConverter.GetBytes(addr);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    private static string FormatAddress(uint addr, string dnsName)
    {
        if (!string.IsNullOrEmpty(dnsName)) return dnsName;
        return addr == 0 ? "any" : FormatIpAddress(addr);
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

    private static string FormatConnectionType(int interfaceType)
    {
        return interfaceType switch
        {
            1 => "lan",
            2 => "dialup",
            _ => "all"
        };
    }
}
