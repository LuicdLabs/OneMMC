using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;

namespace OneMMC.Core.Features.SystemManagement.Interop.WF;

/// <summary>
/// Provides access to the Windows 11 23H2+ binary Windows Firewall policy APIs.
/// </summary>
/// <remarks>
/// These FirewallAPI.dll exports have no SDK header, import library, or CsWin32 metadata.
/// They are loaded dynamically so the rest of the firewall stack can continue to use COM/CIM.
/// </remarks>
internal static class FirewallBinaryNativeMethods
{
    private const ushort CurrentBinaryVersion = 0x021B;
    private const int StoreTypeLocal = 2;
    private const int PolicyAccessRead = 1;
    private const int PolicyAccessReadWrite = 2;
    private const uint ProfileTypeAll = 0x7FFFFFFF;
    private const uint RuleStatusClassAll = 0xFFFF0000;

    private static readonly Lock ApiLock = new();
    private static bool _loadAttempted;
    private static IntPtr _moduleHandle;
    private static FWOpenPolicyStoreDelegate? _openPolicyStore;
    private static FWClosePolicyStoreDelegate? _closePolicyStore;
    private static FWEnumFirewallRulesDelegate? _enumFirewallRules;
    private static FWFreeFirewallRulesByHandleDelegate? _freeFirewallRulesByHandle;
    private static FWSetFirewallRuleDelegate? _setFirewallRule;

    internal static IReadOnlyDictionary<string, uint> GetCompartmentIdsByRuleName()
    {
        try
        {
            return GetCompartmentIdsByRuleNameCore();
        }
        catch (EntryPointNotFoundException)
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }
        catch (DllNotFoundException)
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Win32Exception)
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static bool IsAvailable => TryEnsureLoaded();

    private static IReadOnlyDictionary<string, uint> GetCompartmentIdsByRuleNameCore()
    {
        if (!IsAvailable)
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }

        IntPtr policyStore = IntPtr.Zero;
        IntPtr ruleList = IntPtr.Zero;

        try
        {
            ThrowIfFailed(_openPolicyStore!(
                CurrentBinaryVersion,
                IntPtr.Zero,
                StoreTypeLocal,
                PolicyAccessRead,
                0,
                out policyStore));

            ThrowIfFailed(_enumFirewallRules!(
                policyStore,
                RuleStatusClassAll,
                ProfileTypeAll,
                0,
                out _,
                out ruleList));

            Dictionary<string, uint> compartmentsByName = new(StringComparer.OrdinalIgnoreCase);
            for (IntPtr current = ruleList; current != IntPtr.Zero;)
            {
                FwRule rule = Marshal.PtrToStructure<FwRule>(current);
                AddRuleName(compartmentsByName, rule.RuleId, rule.CompartmentId);
                AddRuleName(compartmentsByName, rule.Name, rule.CompartmentId);
                current = rule.Next;
            }

            return compartmentsByName;
        }
        finally
        {
            if (ruleList != IntPtr.Zero)
            {
                ThrowIfFailed(_freeFirewallRulesByHandle!(policyStore, ruleList));
            }

            if (policyStore != IntPtr.Zero)
            {
                ThrowIfFailed(_closePolicyStore!(policyStore));
            }
        }
    }

    internal static void SetCompartmentId(FirewallRuleModel rule, uint compartmentId)
    {
        EnsureLoaded();

        IntPtr policyStore = IntPtr.Zero;
        IntPtr ruleList = IntPtr.Zero;

        try
        {
            ThrowIfFailed(_openPolicyStore!(
                CurrentBinaryVersion,
                IntPtr.Zero,
                StoreTypeLocal,
                PolicyAccessReadWrite,
                0,
                out policyStore));

            ThrowIfFailed(_enumFirewallRules!(
                policyStore,
                RuleStatusClassAll,
                ProfileTypeAll,
                0,
                out _,
                out ruleList));

            IntPtr matchingRule = FindRule(ruleList, rule);
            if (matchingRule == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Windows Firewall binary policy rule '{rule.Name}' was not found.");
            }

            FwRule nativeRule = Marshal.PtrToStructure<FwRule>(matchingRule);
            nativeRule.CompartmentId = compartmentId;
            nativeRule.Status = 0x00010000;
            Marshal.StructureToPtr(nativeRule, matchingRule, false);

            ThrowIfFailed(_setFirewallRule!(policyStore, matchingRule));
        }
        finally
        {
            if (ruleList != IntPtr.Zero)
            {
                ThrowIfFailed(_freeFirewallRulesByHandle!(policyStore, ruleList));
            }

            if (policyStore != IntPtr.Zero)
            {
                ThrowIfFailed(_closePolicyStore!(policyStore));
            }
        }
    }

    private static IntPtr FindRule(IntPtr ruleList, FirewallRuleModel rule)
    {
        HashSet<string> candidateNames = new(StringComparer.OrdinalIgnoreCase);
        AddCandidateName(candidateNames, rule.Name);
        AddCandidateName(candidateNames, rule.OriginalName);
        AddCandidateName(candidateNames, rule.DisplayName);

        for (IntPtr current = ruleList; current != IntPtr.Zero;)
        {
            FwRule nativeRule = Marshal.PtrToStructure<FwRule>(current);
            if (MatchesCandidate(candidateNames, nativeRule.RuleId) ||
                MatchesCandidate(candidateNames, nativeRule.Name))
            {
                return current;
            }

            current = nativeRule.Next;
        }

        return IntPtr.Zero;
    }

    private static bool MatchesCandidate(ISet<string> candidateNames, IntPtr value)
    {
        string? text = Marshal.PtrToStringUni(value);
        return !string.IsNullOrWhiteSpace(text) && candidateNames.Contains(text);
    }

    private static void AddCandidateName(ISet<string> candidateNames, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            candidateNames.Add(name);
        }
    }

    private static void AddRuleName(IDictionary<string, uint> compartmentsByName, IntPtr value, uint compartmentId)
    {
        string? name = Marshal.PtrToStringUni(value);
        if (!string.IsNullOrWhiteSpace(name))
        {
            compartmentsByName[name] = compartmentId;
        }
    }

    private static bool TryEnsureLoaded()
    {
        try
        {
            EnsureLoaded();
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void EnsureLoaded()
    {
        lock (ApiLock)
        {
            if (_loadAttempted)
            {
                if (_openPolicyStore is null ||
                    _closePolicyStore is null ||
                    _enumFirewallRules is null ||
                    _freeFirewallRulesByHandle is null ||
                    _setFirewallRule is null)
                {
                    throw new PlatformNotSupportedException("This version of Windows does not expose the binary Windows Firewall policy APIs required to write rule compartments.");
                }

                return;
            }

            _loadAttempted = true;
            _moduleHandle = NativeLibrary.Load("FirewallAPI.dll");
            _openPolicyStore = LoadDelegate<FWOpenPolicyStoreDelegate>("FWOpenPolicyStore");
            _closePolicyStore = LoadDelegate<FWClosePolicyStoreDelegate>("FWClosePolicyStore");
            _enumFirewallRules = LoadDelegate<FWEnumFirewallRulesDelegate>("FWEnumFirewallRules");
            _freeFirewallRulesByHandle = LoadDelegate<FWFreeFirewallRulesByHandleDelegate>("FWFreeFirewallRulesByHandle");
            _setFirewallRule = LoadDelegate<FWSetFirewallRuleDelegate>("FWSetFirewallRule");
        }
    }

    private static T LoadDelegate<T>(string exportName)
        where T : Delegate
    {
        IntPtr functionPointer = NativeLibrary.GetExport(_moduleHandle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
    }

    private static void ThrowIfFailed(uint errorCode)
    {
        if (errorCode != 0)
        {
            throw new Win32Exception(unchecked((int)errorCode));
        }
    }

    internal static bool TryParseCompartmentId(string? value, out uint compartmentId)
    {
        compartmentId = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsedValue) ||
            parsedValue > ushort.MaxValue)
        {
            return false;
        }

        compartmentId = parsedValue;
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FWOpenPolicyStoreDelegate(
        ushort binaryVersion,
        IntPtr machineOrGpo,
        int storeType,
        int accessRight,
        uint flags,
        out IntPtr policyStore);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FWClosePolicyStoreDelegate(IntPtr policyStore);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FWEnumFirewallRulesDelegate(
        IntPtr policyStore,
        uint filteredByStatus,
        uint profileFilter,
        ushort flags,
        out uint ruleCount,
        out IntPtr rules);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FWFreeFirewallRulesByHandleDelegate(IntPtr policyStore, IntPtr rules);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FWSetFirewallRuleDelegate(IntPtr policyStore, IntPtr rule);

    [StructLayout(LayoutKind.Sequential)]
    private struct FwRule
    {
        internal IntPtr Next;
        internal ushort SchemaVersion;
        internal IntPtr RuleId;
        internal IntPtr Name;
        internal IntPtr Description;
        internal uint Profiles;
        internal int Direction;
        internal ushort IpProtocol;
        internal FwProtocolData ProtocolData;
        internal FwAddresses LocalAddresses;
        internal FwAddresses RemoteAddresses;
        internal FwInterfaceLuids LocalInterfaceIds;
        internal uint LocalInterfaceTypes;
        internal IntPtr LocalApplication;
        internal IntPtr LocalService;
        internal int Action;
        internal ushort Flags;
        internal IntPtr RemoteMachineAuthorizationList;
        internal IntPtr RemoteUserAuthorizationList;
        internal IntPtr EmbeddedContext;
        internal FwOsPlatformList PlatformValidityList;
        internal uint Status;
        internal int Origin;
        internal IntPtr GpoName;
        internal uint Reserved;
        internal IntPtr Metadata;
        internal IntPtr LocalUserAuthorizationList;
        internal IntPtr PackageId;
        internal IntPtr LocalUserOwner;
        internal uint TrustTupleKeywords;
        internal FwNetworkNames OnNetworkNames;
        internal IntPtr SecurityRealmId;
        internal ushort Flags2;
        internal FwNetworkNames RemoteOutServerNames;
        internal IntPtr Fqbn;
        internal uint CompartmentId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwProtocolData
    {
        internal FwPorts LocalPorts;
        internal FwPorts RemotePorts;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwPorts
    {
        internal ushort PortKeywords;
        internal FwPortRangeList Ports;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwPortRangeList
    {
        internal uint Count;
        internal IntPtr Ports;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwAddresses
    {
        internal uint V4AddressKeywords;
        internal uint V6AddressKeywords;
        internal FwCountedPointer V4Subnets;
        internal FwCountedPointer V4Ranges;
        internal FwCountedPointer V6Subnets;
        internal FwCountedPointer V6Ranges;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwCountedPointer
    {
        internal uint Count;
        internal IntPtr Values;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwInterfaceLuids
    {
        internal uint Count;
        internal IntPtr Luids;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwOsPlatformList
    {
        internal uint Count;
        internal IntPtr Platforms;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwNetworkNames
    {
        internal uint Count;
        internal IntPtr Names;
    }
}
