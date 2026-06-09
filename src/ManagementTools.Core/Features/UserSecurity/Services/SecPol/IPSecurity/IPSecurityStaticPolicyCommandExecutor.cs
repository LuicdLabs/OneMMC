using System.Net;
using System.Runtime.InteropServices;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;

/// <summary>
/// Executes validated mutation commands against the legacy static local IPsec policy store
/// using the native <c>polstore.dll</c> struct-based create, set, and delete APIs.
/// </summary>
public sealed class IPSecurityStaticPolicyCommandExecutor
{
    private static readonly HashSet<string> AllowedVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "delete" };

    private static readonly HashSet<string> AllowedObjectKinds =
        new(StringComparer.OrdinalIgnoreCase) { "policy", "filterlist", "filter", "filteraction", "rule" };

    private static readonly Guid NegPolActionBlock = new("3f91a819-7647-11d1-864d-d46a00000000");
    private static readonly Guid NegPolActionNegotiate = new("8a171dd3-77e3-11d1-8659-a04f00000000");
    private static readonly Guid NegPolActionPermit = new("3f91a81a-7647-11d1-864d-d46a00000000");
    private static readonly Guid NegPolTypeStandard = new("62F49E13-6C37-11d1-864C-14A300000000");

    private readonly ILogger<IPSecurityStaticPolicyCommandExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPSecurityStaticPolicyCommandExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public IPSecurityStaticPolicyCommandExecutor(
        ILogger<IPSecurityStaticPolicyCommandExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes a validated legacy static IPsec mutation command via the native struct-based APIs.
    /// </summary>
    internal Task ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(arguments);

        if (!IPSecurityPolicyNativeMethods.TryOpenRegistryStore(out IntPtr store, out int errorCode))
        {
            ThrowOpenStoreFailure(errorCode);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCore(store, arguments);
        }
        catch (UnauthorizedAccessException)
        {
            LogFailure(arguments);
            throw;
        }
        catch (InvalidOperationException)
        {
            LogFailure(arguments);
            throw;
        }
        finally
        {
            IPSecurityPolicyNativeMethods.CloseStore(store);
        }

        return Task.CompletedTask;
    }

    private static void ExecuteCore(IntPtr store, IReadOnlyList<string> arguments)
    {
        var parameters = ParseParameters(arguments, startIndex: 4);

        switch ((arguments[2].ToLowerInvariant(), arguments[3].ToLowerInvariant()))
        {
            case ("add", "policy"):          AddPolicy(store, parameters); break;
            case ("set", "policy"):          SetPolicy(store, parameters); break;
            case ("delete", "policy"):       DeletePolicy(store, parameters); break;
            case ("add", "filterlist"):      AddFilterList(store, parameters); break;
            case ("set", "filterlist"):      SetFilterList(store, parameters); break;
            case ("delete", "filterlist"):   DeleteByName(store, parameters, IPSecurityPolicyNativeMethods.EnumFilterData, IPSecurityPolicyNativeMethods.DeleteFilterData, 40, "filterlist"); break;
            case ("add", "filter"):          AddFilter(store, parameters); break;
            case ("delete", "filter"):       DeleteFilter(store, parameters); break;
            case ("add", "filteraction"):    AddFilterAction(store, parameters); break;
            case ("set", "filteraction"):    SetFilterAction(store, parameters); break;
            case ("delete", "filteraction"): DeleteByName(store, parameters, IPSecurityPolicyNativeMethods.EnumNegPolData, IPSecurityPolicyNativeMethods.DeleteNegPolData, 72, "filteraction"); break;
            case ("add", "rule"):            AddRule(store, parameters, arguments); break;
            case ("set", "rule"):            SetRule(store, parameters, arguments); break;
            case ("delete", "rule"):         DeleteRule(store, parameters); break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported IPsec operation: {arguments[2]} {arguments[3]}.");
        }
    }

    // ===== Policy Operations =====

    /// <remarks>
    /// IPSEC_POLICY_DATA (80 bytes, x64, verified):
    /// +0 GUID PolicyIdentifier, +16 DWORD dwPollingInterval,
    /// +24 PTR pIpsecISAKMPData, +32 PTR ppIpsecNFAData,
    /// +40 DWORD dwNumNFACount, +44 DWORD dwWhenChanged,
    /// +48 PTR pszIpsecName, +56 PTR pszDescription,
    /// +64 GUID ISAKMPIdentifier.
    /// </remarks>
    private static void AddPolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string description = GetOptional(parameters, "description") ?? string.Empty;
        int pollingMinutes = GetOptionalInt(parameters, "pollinginterval") ?? 180;
        bool assign = GetOptionalBool(parameters, "assign") ?? false;

        (Guid isakmpGuid, IntPtr pIsakmp) = GetFirstIsakmp(store);
        Guid policyGuid = Guid.NewGuid();

        const int size = 80;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            unsafe { NativeMemory.Clear((void*)pData, size); }
            WriteGuid(pData, 0, policyGuid);
            Marshal.WriteInt32(pData, 16, pollingMinutes * 60);
            Marshal.WriteIntPtr(pData, 24, pIsakmp);
            Marshal.WriteInt32(pData, 44, CurrentUnixSeconds());
            Marshal.WriteIntPtr(pData, 48, pName);
            Marshal.WriteIntPtr(pData, 56, pDesc);
            WriteGuid(pData, 64, isakmpGuid);

            int hr = IPSecurityPolicyNativeMethods.CreatePolicyData(store, pData);
            if (hr != 0)
            {
                throw new InvalidOperationException(
                    $"IPSecCreatePolicyData failed with native error 0x{hr:X8}. " +
                    $"Policy GUID: {policyGuid}, ISAKMP GUID: {isakmpGuid}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pDesc);
            Marshal.FreeHGlobal(pName);
            Marshal.FreeHGlobal(pData);
        }

        // polstore.dll omits ipsecNFAReference and description when the struct has
        // no NFAs and an empty description.  EnumPolicyData requires these values
        // to exist in order to parse the entry, so we write them directly.
        EnsurePolicyRegistryValues(policyGuid, description, isakmpGuid);

        // Remove orphaned ipsecPolicy registry keys left by earlier failed attempts.
        // These cause secpol.msc to fail with ERROR_NO_DATA (0x800700E8).
        CleanOrphanedPolicyRegistryKeys(store);

        if (assign)
        {
            IPSecurityPolicyNativeMethods.AssignPolicy(store, policyGuid);
        }
    }

    private static void SetPolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        int? pollingMinutes = GetOptionalInt(parameters, "pollinginterval");
        bool? assign = GetOptionalBool(parameters, "assign");

        (Guid policyGuid, IntPtr original) = FindPolicyByName(store, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Policy '{name}' not found.");
        }

        const int size = 80;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            unsafe { Buffer.MemoryCopy((void*)original, (void*)pData, size, size); }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                Marshal.WriteIntPtr(pData, 48, pNewName);
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                Marshal.WriteIntPtr(pData, 56, pNewDesc);
            }

            if (pollingMinutes is not null)
            {
                Marshal.WriteInt32(pData, 16, pollingMinutes.Value * 60);
            }

            Marshal.WriteInt32(pData, 44, CurrentUnixSeconds());
            ThrowOnError(IPSecurityPolicyNativeMethods.SetPolicyData(store, pData), "set policy");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
            Marshal.FreeHGlobal(pData);
        }

        if (assign is true)
        {
            IPSecurityPolicyNativeMethods.AssignPolicy(store, policyGuid);
        }
        else if (assign is false)
        {
            IgnoreUnassignFailure(store, policyGuid);
        }
    }

    private static void DeletePolicy(IntPtr store, Dictionary<string, string> parameters)
    {
        string policyName = GetRequired(parameters, "name");
        (Guid policyId, IntPtr policyPtr) = FindPolicyByName(store, policyName);
        if (policyId == Guid.Empty)
        {
            return;
        }

        IgnoreUnassignFailure(store, policyId);
        DeleteAllRules(store, policyId);
        ThrowOnError(IPSecurityPolicyNativeMethods.DeletePolicyData(store, policyPtr), "delete policy");
    }

    private static void DeleteAllRules(IntPtr store, Guid policyId)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(store, policyId, ppp, pCount);
            if (hr != 0)
            {
                // ERROR_NO_DATA (0xE8) means the policy has no NFA references.
                if (hr == 0xE8) return;
                ThrowOnError(hr, "enumerate policy rules");
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                ThrowOnError(IPSecurityPolicyNativeMethods.DeleteNFAData(store, policyId, p), "delete rule");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static void IgnoreUnassignFailure(IntPtr store, Guid policyId)
    {
        int hr = IPSecurityPolicyNativeMethods.UnassignPolicy(store, policyId);
        if (hr != 0 && (hr == 5 || hr == unchecked((int)0x80070005)))
        {
            ThrowOnError(hr, "unassign policy");
        }
    }

    // ===== FilterList Operations =====

    /// <remarks>
    /// IPSEC_FILTER_DATA (56 bytes, x64, verified):
    /// +0 GUID FilterIdentifier, +16 DWORD dwNumFilterSpecs,
    /// +24 PTR ppFilterSpecs, +32 DWORD dwWhenChanged,
    /// +40 PTR pszIpsecName, +48 PTR pszDescription.
    /// </remarks>
    private static void AddFilterList(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? description = GetOptional(parameters, "description");

        const int size = 56;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            unsafe { NativeMemory.Clear((void*)pData, size); }
            WriteGuid(pData, 0, Guid.NewGuid());
            Marshal.WriteInt32(pData, 32, CurrentUnixSeconds());
            Marshal.WriteIntPtr(pData, 40, pName);
            Marshal.WriteIntPtr(pData, 48, pDesc);

            ThrowOnError(IPSecurityPolicyNativeMethods.CreateFilterData(store, pData), "add filterlist");
        }
        finally
        {
            FreeIfNotZero(pDesc);
            Marshal.FreeHGlobal(pName);
            Marshal.FreeHGlobal(pData);
        }
    }

    private static void SetFilterList(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");

        (_, IntPtr original) = FindByName(store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{name}' not found.");
        }

        const int size = 56;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            unsafe { Buffer.MemoryCopy((void*)original, (void*)pData, size, size); }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                Marshal.WriteIntPtr(pData, 40, pNewName);
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                Marshal.WriteIntPtr(pData, 48, pNewDesc);
            }

            Marshal.WriteInt32(pData, 32, CurrentUnixSeconds());
            ThrowOnError(IPSecurityPolicyNativeMethods.SetFilterData(store, pData), "set filterlist");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
            Marshal.FreeHGlobal(pData);
        }
    }

    // ===== Filter Operations =====

    /// <remarks>
    /// IPSEC_FILTER_SPEC (84 bytes, x64, derived from NT headers):
    /// +0 PTR pszSrcDNSName, +8 PTR pszDestDNSName,
    /// +16 PTR pszDescription, +24 GUID FilterSpecGUID,
    /// +40 DWORD dwMirrorFlag,
    /// +44 IPSEC_FILTER (embedded 40 bytes):
    ///   +44 SrcAddr, +48 SrcMask, +52 DestAddr, +56 DestMask,
    ///   +60 TunnelAddr, +64 Protocol, +68 SrcPort, +72 DestPort,
    ///   +76 TunnelFilter, +80 Flags.
    /// </remarks>
    private static void AddFilter(IntPtr store, Dictionary<string, string> parameters)
    {
        string filterListName = GetRequired(parameters, "filterlist");
        string srcAddr = GetRequired(parameters, "srcaddr");
        string dstAddr = GetRequired(parameters, "dstaddr");
        string? description = GetOptional(parameters, "description");
        string? protocol = GetOptional(parameters, "protocol");
        bool mirrored = GetOptionalBool(parameters, "mirrored") ?? true;
        string? srcMask = GetOptional(parameters, "srcmask");
        string? dstMask = GetOptional(parameters, "dstmask");
        int srcPort = GetOptionalInt(parameters, "srcport") ?? 0;
        int dstPort = GetOptionalInt(parameters, "dstport") ?? 0;

        (_, IntPtr filterListPtr) = FindByName(
            store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterListPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        int oldCount = Marshal.ReadInt32(filterListPtr, 16);
        IntPtr oldSpecs = Marshal.ReadIntPtr(filterListPtr, 24);

        const int specSize = 84;
        const int filterListSize = 56;
        IntPtr pSpec = Marshal.AllocHGlobal(specSize);
        IntPtr pNewArray = Marshal.AllocHGlobal(IntPtr.Size * (oldCount + 1));
        IntPtr pFilterList = Marshal.AllocHGlobal(filterListSize);
        IntPtr pSrcDns = IntPtr.Zero;
        IntPtr pDstDns = IntPtr.Zero;
        IntPtr pDescStr = IntPtr.Zero;
        try
        {
            unsafe { NativeMemory.Clear((void*)pSpec, specSize); }

            (uint srcIp, uint srcMaskVal, string? srcDnsName, int srcFlags) = ParseAddress(srcAddr, srcMask, isSrc: true);
            (uint dstIp, uint dstMaskVal, string? dstDnsName, int dstFlags) = ParseAddress(dstAddr, dstMask, isSrc: false);

            if (srcDnsName is not null) { pSrcDns = AllocString(srcDnsName); Marshal.WriteIntPtr(pSpec, 0, pSrcDns); }
            if (dstDnsName is not null) { pDstDns = AllocString(dstDnsName); Marshal.WriteIntPtr(pSpec, 8, pDstDns); }
            if (description is not null) { pDescStr = AllocString(description); Marshal.WriteIntPtr(pSpec, 16, pDescStr); }

            WriteGuid(pSpec, 24, Guid.NewGuid());
            Marshal.WriteInt32(pSpec, 40, mirrored ? 1 : 0);
            Marshal.WriteInt32(pSpec, 44, unchecked((int)srcIp));
            Marshal.WriteInt32(pSpec, 48, unchecked((int)srcMaskVal));
            Marshal.WriteInt32(pSpec, 52, unchecked((int)dstIp));
            Marshal.WriteInt32(pSpec, 56, unchecked((int)dstMaskVal));
            Marshal.WriteInt32(pSpec, 64, ParseProtocolNumber(protocol));
            Marshal.WriteInt32(pSpec, 68, srcPort);
            Marshal.WriteInt32(pSpec, 72, dstPort);
            Marshal.WriteInt32(pSpec, 80, srcFlags | dstFlags);

            for (int i = 0; i < oldCount; i++)
            {
                Marshal.WriteIntPtr(pNewArray, IntPtr.Size * i, Marshal.ReadIntPtr(oldSpecs, IntPtr.Size * i));
            }

            Marshal.WriteIntPtr(pNewArray, IntPtr.Size * oldCount, pSpec);

            unsafe { Buffer.MemoryCopy((void*)filterListPtr, (void*)pFilterList, filterListSize, filterListSize); }
            Marshal.WriteInt32(pFilterList, 16, oldCount + 1);
            Marshal.WriteIntPtr(pFilterList, 24, pNewArray);
            Marshal.WriteInt32(pFilterList, 32, CurrentUnixSeconds());

            ThrowOnError(IPSecurityPolicyNativeMethods.SetFilterData(store, pFilterList), "add filter");
        }
        finally
        {
            FreeIfNotZero(pDescStr);
            FreeIfNotZero(pDstDns);
            FreeIfNotZero(pSrcDns);
            Marshal.FreeHGlobal(pFilterList);
            Marshal.FreeHGlobal(pNewArray);
            Marshal.FreeHGlobal(pSpec);
        }
    }

    private static void DeleteFilter(IntPtr store, Dictionary<string, string> parameters)
    {
        string filterListName = GetRequired(parameters, "filterlist");
        string srcAddr = GetRequired(parameters, "srcaddr");
        string dstAddr = GetRequired(parameters, "dstaddr");
        string? protocol = GetOptional(parameters, "protocol");
        bool mirrored = GetOptionalBool(parameters, "mirrored") ?? true;
        string? srcMask = GetOptional(parameters, "srcmask");
        string? dstMask = GetOptional(parameters, "dstmask");
        int srcPort = GetOptionalInt(parameters, "srcport") ?? 0;
        int dstPort = GetOptionalInt(parameters, "dstport") ?? 0;

        (_, IntPtr filterListPtr) = FindByName(
            store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterListPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        int oldCount = Marshal.ReadInt32(filterListPtr, 16);
        IntPtr oldSpecs = Marshal.ReadIntPtr(filterListPtr, 24);
        if (oldCount == 0 || oldSpecs == IntPtr.Zero) return;

        (uint targetSrcIp, uint targetSrcMask, _, _) = ParseAddress(srcAddr, srcMask, isSrc: true);
        (uint targetDstIp, uint targetDstMask, _, _) = ParseAddress(dstAddr, dstMask, isSrc: false);
        int targetProtocol = ParseProtocolNumber(protocol);

        int matchIndex = -1;
        for (int i = 0; i < oldCount; i++)
        {
            IntPtr spec = Marshal.ReadIntPtr(oldSpecs, IntPtr.Size * i);
            if (spec == IntPtr.Zero) continue;

            if (unchecked((uint)Marshal.ReadInt32(spec, 44)) == targetSrcIp
                && unchecked((uint)Marshal.ReadInt32(spec, 48)) == targetSrcMask
                && unchecked((uint)Marshal.ReadInt32(spec, 52)) == targetDstIp
                && unchecked((uint)Marshal.ReadInt32(spec, 56)) == targetDstMask
                && Marshal.ReadInt32(spec, 64) == targetProtocol
                && Marshal.ReadInt32(spec, 68) == srcPort
                && Marshal.ReadInt32(spec, 72) == dstPort
                && (Marshal.ReadInt32(spec, 40) != 0) == mirrored)
            {
                matchIndex = i;
                break;
            }
        }

        if (matchIndex < 0) return;

        const int filterListSize = 56;
        int newCount = oldCount - 1;
        IntPtr pNewArray = newCount > 0 ? Marshal.AllocHGlobal(IntPtr.Size * newCount) : IntPtr.Zero;
        IntPtr pFilterList = Marshal.AllocHGlobal(filterListSize);
        try
        {
            int dest = 0;
            for (int i = 0; i < oldCount; i++)
            {
                if (i == matchIndex) continue;
                Marshal.WriteIntPtr(pNewArray, IntPtr.Size * dest, Marshal.ReadIntPtr(oldSpecs, IntPtr.Size * i));
                dest++;
            }

            unsafe { Buffer.MemoryCopy((void*)filterListPtr, (void*)pFilterList, filterListSize, filterListSize); }
            Marshal.WriteInt32(pFilterList, 16, newCount);
            Marshal.WriteIntPtr(pFilterList, 24, pNewArray);
            Marshal.WriteInt32(pFilterList, 32, CurrentUnixSeconds());

            ThrowOnError(IPSecurityPolicyNativeMethods.SetFilterData(store, pFilterList), "delete filter");
        }
        finally
        {
            Marshal.FreeHGlobal(pFilterList);
            FreeIfNotZero(pNewArray);
        }
    }

    // ===== FilterAction Operations =====

    /// <remarks>
    /// IPSEC_NEGPOL_DATA (88 bytes, x64, verified):
    /// +0 GUID NegPolIdentifier, +16 GUID NegPolAction,
    /// +32 GUID NegPolType, +48 DWORD dwSecurityMethodCount,
    /// +56 PTR pIpsecSecurityMethods, +64 DWORD dwWhenChanged,
    /// +72 PTR pszIpsecName, +80 PTR pszDescription.
    /// </remarks>
    private static void AddFilterAction(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? description = GetOptional(parameters, "description");
        string actionStr = GetOptional(parameters, "action") ?? "negotiate";

        Guid actionGuid = actionStr.ToLowerInvariant() switch
        {
            "block" => NegPolActionBlock,
            "permit" => NegPolActionPermit,
            _ => NegPolActionNegotiate
        };

        const int size = 88;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            unsafe { NativeMemory.Clear((void*)pData, size); }
            WriteGuid(pData, 0, Guid.NewGuid());
            WriteGuid(pData, 16, actionGuid);
            WriteGuid(pData, 32, NegPolTypeStandard);
            Marshal.WriteInt32(pData, 64, CurrentUnixSeconds());
            Marshal.WriteIntPtr(pData, 72, pName);
            Marshal.WriteIntPtr(pData, 80, pDesc);

            ThrowOnError(IPSecurityPolicyNativeMethods.CreateNegPolData(store, pData), "add filteraction");
        }
        finally
        {
            FreeIfNotZero(pDesc);
            Marshal.FreeHGlobal(pName);
            Marshal.FreeHGlobal(pData);
        }
    }

    private static void SetFilterAction(IntPtr store, Dictionary<string, string> parameters)
    {
        string name = GetRequired(parameters, "name");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        string? actionStr = GetOptional(parameters, "action");

        (_, IntPtr original) = FindByName(store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Filter action '{name}' not found.");
        }

        const int size = 88;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            unsafe { Buffer.MemoryCopy((void*)original, (void*)pData, size, size); }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                Marshal.WriteIntPtr(pData, 72, pNewName);
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                Marshal.WriteIntPtr(pData, 80, pNewDesc);
            }

            if (actionStr is not null)
            {
                Guid actionGuid = actionStr.ToLowerInvariant() switch
                {
                    "block" => NegPolActionBlock,
                    "permit" => NegPolActionPermit,
                    _ => NegPolActionNegotiate
                };
                WriteGuid(pData, 16, actionGuid);
            }

            Marshal.WriteInt32(pData, 64, CurrentUnixSeconds());
            ThrowOnError(IPSecurityPolicyNativeMethods.SetNegPolData(store, pData), "set filteraction");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
            Marshal.FreeHGlobal(pData);
        }
    }

    // ===== Rule Operations =====

    /// <remarks>
    /// IPSEC_NFA_DATA (112 bytes, x64, derived from NT headers):
    /// +0 GUID NFAIdentifier, +16 PTR pszIpsecName,
    /// +24 PTR pszDescription, +32 DWORD dwWhenChanged,
    /// +40 PTR pszInterfaceName, +48 DWORD dwInterfaceType,
    /// +52 DWORD dwActiveFlag, +56 DWORD dwTunnelIpAddr,
    /// +60 DWORD dwTunnelFlags, +64 GUID NegPolIdentifier,
    /// +80 GUID FilterIdentifier, +96 DWORD dwAuthMethodCount,
    /// +104 PTR pIpsecAuthMethods.
    /// </remarks>
    private static void AddRule(IntPtr store, Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        string name = GetRequired(parameters, "name");
        string policyName = GetRequired(parameters, "policy");
        string filterListName = GetRequired(parameters, "filterlist");
        string filterActionName = GetRequired(parameters, "filteraction");
        string? description = GetOptional(parameters, "description");
        string? tunnel = GetOptional(parameters, "tunnel");
        string? connType = GetOptional(parameters, "conntype");
        bool active = GetOptionalBool(parameters, "activate") ?? true;

        (Guid policyGuid, IntPtr policyPtr) = FindPolicyByName(store, policyName);
        if (policyPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Policy '{policyName}' not found.");
        }

        (Guid filterGuid, _) = FindByName(store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
        if (filterGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
        }

        (Guid negPolGuid, _) = FindByName(store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, filterActionName);
        if (negPolGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Filter action '{filterActionName}' not found.");
        }

        int interfaceType = connType?.ToLowerInvariant() switch
        {
            "lan" => 1,
            "dialup" => 2,
            _ => 0
        };

        (int authCount, IntPtr pAuth, List<IntPtr> authAllocs) = BuildAuthMethods(parameters, arguments);

        const int size = 112;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pName = AllocString(name);
        IntPtr pDesc = AllocString(description);
        try
        {
            unsafe { NativeMemory.Clear((void*)pData, size); }
            WriteGuid(pData, 0, Guid.NewGuid());
            Marshal.WriteIntPtr(pData, 16, pName);
            Marshal.WriteIntPtr(pData, 24, pDesc);
            Marshal.WriteInt32(pData, 32, CurrentUnixSeconds());
            Marshal.WriteInt32(pData, 48, interfaceType);
            Marshal.WriteInt32(pData, 52, active ? 1 : 0);

            if (tunnel is not null && IPAddress.TryParse(tunnel, out IPAddress? tunnelIp))
            {
                byte[] bytes = tunnelIp.GetAddressBytes();
                Marshal.WriteInt32(pData, 56, BitConverter.ToInt32(bytes, 0));
                Marshal.WriteInt32(pData, 60, 1);
            }

            WriteGuid(pData, 64, negPolGuid);
            WriteGuid(pData, 80, filterGuid);
            Marshal.WriteInt32(pData, 96, authCount);
            Marshal.WriteIntPtr(pData, 104, pAuth);

            ThrowOnError(IPSecurityPolicyNativeMethods.CreateNFAData(store, policyGuid, pData), "add rule");
        }
        finally
        {
            foreach (IntPtr alloc in authAllocs) Marshal.FreeHGlobal(alloc);
            FreeIfNotZero(pAuth);
            FreeIfNotZero(pDesc);
            Marshal.FreeHGlobal(pName);
            Marshal.FreeHGlobal(pData);
        }
    }

    private static void SetRule(IntPtr store, Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        string name = GetRequired(parameters, "name");
        string policyName = GetRequired(parameters, "policy");
        string? newName = GetOptional(parameters, "newname");
        string? description = GetOptional(parameters, "description");
        string? filterListName = GetOptional(parameters, "filterlist");
        string? filterActionName = GetOptional(parameters, "filteraction");
        string? connType = GetOptional(parameters, "conntype");
        bool? active = GetOptionalBool(parameters, "activate");

        (Guid policyGuid, _) = FindPolicyByName(store, policyName);
        if (policyGuid == Guid.Empty)
        {
            throw new InvalidOperationException($"Policy '{policyName}' not found.");
        }

        IntPtr original = FindNfaByName(store, policyGuid, name);
        if (original == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Rule '{name}' in policy '{policyName}' not found.");
        }

        const int size = 112;
        IntPtr pData = Marshal.AllocHGlobal(size);
        IntPtr pNewName = IntPtr.Zero;
        IntPtr pNewDesc = IntPtr.Zero;
        try
        {
            unsafe { Buffer.MemoryCopy((void*)original, (void*)pData, size, size); }

            if (newName is not null)
            {
                pNewName = AllocString(newName);
                Marshal.WriteIntPtr(pData, 16, pNewName);
            }

            if (description is not null)
            {
                pNewDesc = AllocString(description);
                Marshal.WriteIntPtr(pData, 24, pNewDesc);
            }

            if (filterListName is not null)
            {
                (Guid filterGuid, _) = FindByName(
                    store, IPSecurityPolicyNativeMethods.EnumFilterData, 40, filterListName);
                if (filterGuid == Guid.Empty)
                {
                    throw new InvalidOperationException($"Filter list '{filterListName}' not found.");
                }

                WriteGuid(pData, 80, filterGuid);
            }

            if (filterActionName is not null)
            {
                (Guid negPolGuid, _) = FindByName(
                    store, IPSecurityPolicyNativeMethods.EnumNegPolData, 72, filterActionName);
                if (negPolGuid == Guid.Empty)
                {
                    throw new InvalidOperationException($"Filter action '{filterActionName}' not found.");
                }

                WriteGuid(pData, 64, negPolGuid);
            }

            if (connType is not null)
            {
                int interfaceType = connType.ToLowerInvariant() switch
                {
                    "lan" => 1,
                    "dialup" => 2,
                    _ => 0
                };
                Marshal.WriteInt32(pData, 48, interfaceType);
            }

            if (active is not null)
            {
                Marshal.WriteInt32(pData, 52, active.Value ? 1 : 0);
            }

            Marshal.WriteInt32(pData, 32, CurrentUnixSeconds());
            ThrowOnError(IPSecurityPolicyNativeMethods.SetNFAData(store, policyGuid, pData), "set rule");
        }
        finally
        {
            FreeIfNotZero(pNewDesc);
            FreeIfNotZero(pNewName);
            Marshal.FreeHGlobal(pData);
        }
    }

    private static void DeleteRule(IntPtr store, Dictionary<string, string> parameters)
    {
        string policyName = GetRequired(parameters, "policy");
        string ruleName = GetRequired(parameters, "name");

        (Guid policyGuid, _) = FindPolicyByName(store, policyName);
        if (policyGuid == Guid.Empty) return;

        IntPtr nfaPtr = FindNfaByName(store, policyGuid, ruleName);
        if (nfaPtr == IntPtr.Zero) return;

        ThrowOnError(IPSecurityPolicyNativeMethods.DeleteNFAData(store, policyGuid, nfaPtr), "delete rule");
    }

    // ===== Auth Method Builder =====

    /// <remarks>
    /// IPSEC_AUTH_METHOD (32 bytes, x64, derived from NT headers):
    /// +0 DWORD dwAuthType (1=PSK, 2=Certificate, 3=Kerberos),
    /// +4 DWORD dwAuthLen, +8 PTR pszAuthMethod,
    /// +16 DWORD dwAltAuthLen, +24 PTR pszAltAuthMethod.
    /// </remarks>
    private static (int Count, IntPtr Array, List<IntPtr> Allocs) BuildAuthMethods(
        Dictionary<string, string> parameters, IReadOnlyList<string> arguments)
    {
        List<(int Type, string? Value)> methods = [];
        List<IntPtr> allocs = [];

        if (GetOptionalBool(parameters, "kerberos") is true)
        {
            methods.Add((3, null));
        }

        string? psk = GetOptional(parameters, "psk");
        if (psk is not null)
        {
            methods.Add((1, psk));
        }

        const string rootcaPrefix = "rootca=";
        for (int i = 4; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith(rootcaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                methods.Add((2, arguments[i][rootcaPrefix.Length..]));
            }
        }

        if (methods.Count == 0)
        {
            methods.Add((3, null));
        }

        const int authMethodSize = 32;
        IntPtr pArray = Marshal.AllocHGlobal(authMethodSize * methods.Count);
        unsafe { NativeMemory.Clear((void*)pArray, (nuint)(authMethodSize * methods.Count)); }

        for (int i = 0; i < methods.Count; i++)
        {
            IntPtr entry = pArray + (authMethodSize * i);
            Marshal.WriteInt32(entry, 0, methods[i].Type);

            if (methods[i].Value is { } value)
            {
                IntPtr pValue = AllocString(value);
                allocs.Add(pValue);
                int byteLen = value.Length * 2;
                Marshal.WriteInt32(entry, 4, byteLen);
                Marshal.WriteIntPtr(entry, 8, pValue);
            }
        }

        return (methods.Count, pArray, allocs);
    }

    // ===== Generic Find / Delete Helpers =====

    private delegate int EnumFunc(IntPtr store, IntPtr pppData, IntPtr pdwCount);

    private delegate int DeleteFunc(IntPtr store, IntPtr pData);

    private static (Guid Id, IntPtr Ptr) FindByName(
        IntPtr store, EnumFunc enumFunc, int nameOffset, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = enumFunc(store, ppp, pCount);
            if (hr != 0) return (Guid.Empty, IntPtr.Zero);

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                IntPtr pName = Marshal.ReadIntPtr(p, nameOffset);
                string? itemName = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                if (name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return (ReadGuid(p, 0), p);
                }
            }

            return (Guid.Empty, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static (Guid Id, IntPtr Ptr) FindPolicyByName(IntPtr store, string name)
    {
        return FindByName(store, IPSecurityPolicyNativeMethods.EnumPolicyData, 48, name);
    }

    private static IntPtr FindNfaByName(IntPtr store, Guid policyGuid, string name)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumNFAData(store, policyGuid, ppp, pCount);
            if (hr != 0) return IntPtr.Zero;

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p == IntPtr.Zero) continue;

                IntPtr pName = Marshal.ReadIntPtr(p, 16);
                string? nfaName = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                if (name.Equals(nfaName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    private static void DeleteByName(
        IntPtr store, Dictionary<string, string> parameters,
        EnumFunc enumFunc, DeleteFunc deleteFunc,
        int nameOffset, string objectKind)
    {
        string name = GetRequired(parameters, "name");
        (_, IntPtr ptr) = FindByName(store, enumFunc, nameOffset, name);
        if (ptr == IntPtr.Zero) return;

        ThrowOnError(deleteFunc(store, ptr), $"delete {objectKind}");
    }

    /// <summary>
    /// Returns the GUID and store-owned pointer of the first ISAKMP in the store.
    /// The returned pointer is owned by the store; the caller must NOT free it.
    /// </summary>
    private static (Guid Guid, IntPtr Ptr) GetFirstIsakmp(IntPtr store)
    {
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumISAKMPData(store, ppp, pCount);
            if (hr != 0)
            {
                throw new InvalidOperationException(
                    "Cannot create a policy: the ISAKMP store could not be enumerated " +
                    $"(native error 0x{hr:X8}).");
            }

            int count = Marshal.ReadInt32(pCount);
            IntPtr pp = Marshal.ReadIntPtr(ppp);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                if (p != IntPtr.Zero)
                {
                    Guid guid = ReadGuid(p, 0);
                    return (guid, p);
                }
            }

            throw new InvalidOperationException(
                "Cannot create a policy: no ISAKMP objects exist in the store. " +
                "Create an initial policy using the Windows IP Security Policy snap-in first.");
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }
    }

    // ===== Struct Helpers =====

    // ===== Registry Helpers =====

    private const string PolicyStoreRegistryPath =
        @"SOFTWARE\Policies\Microsoft\Windows\IPSec\Policy\Local";

    /// <summary>
    /// Writes registry values that <c>IPSecCreatePolicyData</c> omits when the struct
    /// has no NFA references or description. Without these values, <c>EnumPolicyData</c>
    /// silently skips the entry and <c>secpol.msc</c> fails with <c>ERROR_NO_DATA</c>.
    /// </summary>
    private static void EnsurePolicyRegistryValues(Guid policyGuid, string description, Guid isakmpGuid)
    {
        string keyPath = $@"{PolicyStoreRegistryPath}\ipsecPolicy{{{policyGuid}}}";
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
        if (key is null) return;

        if (key.GetValue("ipsecNFAReference") is null)
        {
            key.SetValue("ipsecNFAReference", Array.Empty<string>(), RegistryValueKind.MultiString);
        }

        if (key.GetValue("description") is null)
        {
            key.SetValue("description", description, RegistryValueKind.String);
        }

        if (key.GetValue("ipsecOwnersReference") is null)
        {
            key.SetValue("ipsecOwnersReference", Array.Empty<string>(), RegistryValueKind.MultiString);
        }

        if (key.GetValue("ipsecISAKMPReference") is null)
        {
            string isakmpRef = $@"{PolicyStoreRegistryPath}\ipsecISAKMPPolicy{{{isakmpGuid}}}";
            key.SetValue("ipsecISAKMPReference", isakmpRef, RegistryValueKind.String);
        }
    }

    /// <summary>
    /// Removes orphaned <c>ipsecPolicy{...}</c> registry keys that were created by
    /// earlier failed attempts and are not recognized by <c>EnumPolicyData</c>.
    /// </summary>
    internal static void CleanOrphanedPolicyRegistryKeys(IntPtr store)
    {
        using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(PolicyStoreRegistryPath, writable: true);
        if (baseKey is null) return;

        HashSet<string> knownGuids = [];
        IntPtr ppp = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr pCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            int hr = IPSecurityPolicyNativeMethods.EnumPolicyData(store, ppp, pCount);
            if (hr == 0)
            {
                int count = Marshal.ReadInt32(pCount);
                IntPtr pp = Marshal.ReadIntPtr(ppp);
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(pp, IntPtr.Size * i);
                    if (p == IntPtr.Zero) continue;
                    Guid id = ReadGuid(p, 0);
                    knownGuids.Add(id.ToString("B").ToLowerInvariant());
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ppp);
            Marshal.FreeHGlobal(pCount);
        }

        foreach (string keyName in baseKey.GetSubKeyNames())
        {
            if (!keyName.StartsWith("ipsecPolicy{", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string guidPart = keyName["ipsecPolicy".Length..].ToLowerInvariant();
            if (!knownGuids.Contains(guidPart))
            {
                baseKey.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            }
        }
    }

    // ===== Struct Helpers =====

    private static void WriteGuid(IntPtr ptr, int offset, Guid value)
    {
        byte[] bytes = value.ToByteArray();
        Marshal.Copy(bytes, 0, ptr + offset, 16);
    }

    private static Guid ReadGuid(IntPtr ptr, int offset)
    {
        byte[] bytes = new byte[16];
        Marshal.Copy(ptr + offset, bytes, 0, 16);
        return new Guid(bytes);
    }

    private static IntPtr AllocString(string? value)
    {
        return value is not null ? Marshal.StringToHGlobalUni(value) : IntPtr.Zero;
    }

    private static void FreeIfNotZero(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private static int CurrentUnixSeconds()
    {
        return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static (uint Addr, uint Mask, string? DnsName, int Flags) ParseAddress(
        string address, string? mask, bool isSrc)
    {
        if (address.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0, null, 0);
        }

        if (address.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0xFFFFFFFF, null, isSrc ? 0x01 : 0x02);
        }

        if (IPAddress.TryParse(address, out IPAddress? ip))
        {
            uint addr = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
            uint maskVal = 0xFFFFFFFF;
            if (mask is not null && IPAddress.TryParse(mask, out IPAddress? maskIp))
            {
                maskVal = BitConverter.ToUInt32(maskIp.GetAddressBytes(), 0);
            }

            return (addr, maskVal, null, 0);
        }

        return (0, 0, address, 0);
    }

    private static int ParseProtocolNumber(string? protocol)
    {
        if (protocol is null || protocol.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return protocol.ToUpperInvariant() switch
        {
            "ICMP" => 1,
            "TCP" => 6,
            "UDP" => 17,
            "RAW" => 255,
            _ => int.TryParse(protocol, out int num) ? num : 0
        };
    }

    // ===== Command Parsing =====

    private static void ValidateCommand(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4
            || !arguments[0].Equals("ipsec", StringComparison.OrdinalIgnoreCase)
            || !arguments[1].Equals("static", StringComparison.OrdinalIgnoreCase)
            || !AllowedVerbs.Contains(arguments[2])
            || !AllowedObjectKinds.Contains(arguments[3]))
        {
            throw new ArgumentException(
                "Only legacy static IPsec add, set, and delete commands are allowed.",
                nameof(arguments));
        }

        if (arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("IPsec command tokens cannot be empty.", nameof(arguments));
        }
    }

    private static Dictionary<string, string> ParseParameters(IReadOnlyList<string> arguments, int startIndex)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                result[argument[..equalsIndex]] = argument[(equalsIndex + 1)..];
            }
        }

        return result;
    }

    private static string GetRequired(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value) || string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        }

        return value;
    }

    private static string? GetOptional(Dictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out string? value) ? value : null;
    }

    private static bool? GetOptionalBool(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value)) return null;
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetOptionalInt(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out string? value)) return null;
        return int.TryParse(value, out int result) ? result : null;
    }

    // ===== Error Handling =====

    private static void ThrowOpenStoreFailure(int errorCode)
    {
        if (IPSecurityPolicyNativeMethods.IsStoreOpenFailure(errorCode) || errorCode == 5)
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"Failed to open the legacy IPsec policy store (native error 0x{errorCode:X8}).");
    }

    private static void ThrowOnError(int hr, string operation)
    {
        if (hr == 0) return;

        if (hr == 5 || hr == unchecked((int)0x80070005))
        {
            throw new UnauthorizedAccessException(
                "The local IPsec policy store cannot be modified because the operation requires elevation.");
        }

        throw new InvalidOperationException(
            $"The legacy IPsec policy {operation} command failed with native error 0x{hr:X8}.");
    }

    private void LogFailure(IReadOnlyList<string> arguments)
    {
        _logger.LogWarning(
            "The legacy IPsec static {Verb} {ObjectKind} command failed. Arguments and output were omitted because the command may contain policy secrets.",
            arguments[2],
            arguments[3]);
    }
}
