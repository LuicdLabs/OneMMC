using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.SystemManagement.Interop.WF;

// Source-generated ([GeneratedComInterface]) ports of the HNetCfg Windows Firewall automation
// interfaces (INetFwPolicy2 + the INetFwRule hierarchy), following the repository's Native AOT COM
// guidance; see doc/NativeAot.md ("COM interop"). Ported from the previous handwritten
// [ComImport, InterfaceIsDual] declarations: built-in COM interop / RCW dual-interface dispatch is
// unsupported under AOT, so these derive from the source-generated IDispatch base
// (Infrastructure/Interop/IDispatch.cs) to reproduce the dual vtable (IUnknown[3] + IDispatch[4]
// + members) and are called by vtable. Coclasses are activated via
// ComActivator (FirewallCom.CreatePolicy2/CreateRule), not Type.GetTypeFromProgID + Activator.
//
// Member ORDER is the authoritative vtable order and must not change; it was transcribed from the
// type library in %SystemRoot%\System32\FirewallAPI.dll via the typelib vtable-dump tool (retired
// after the migration; in git history — interface members begin at slot 7). Conventions (all
// typelib-verified):
//   * IDispatch properties -> explicit get_/put_ methods in declaration (vtable) order.
//   * NET_FW_* enums are LONG (I4) in the vtable -> `int`.
//   * VARIANT_BOOL (Enabled, EdgeTraversal, FirewallEnabled, ...) -> raw `short` (-1 true / 0 false);
//     callers convert via FirewallCom.ToBool/ToVariantBool.
//   * The `Interfaces` VARIANT (a SAFEARRAY of BSTR, or empty = "all interfaces") -> Variant.
//   * BSTR strings -> [MarshalAs(UnmanagedType.BStr)] string.
//   * INetFwRule2/3 use interface inheritance so a wrapper IS-A its bases (correct QI on Add()).
// Interfaces are `internal` because their signatures reference the internal Variant type.

/// <summary>A single Windows Firewall rule (HNetCfg <c>INetFwRule</c>).</summary>
[GeneratedComInterface, Guid("AF230D27-BABA-4E42-ACED-F524F22CFCE2")]
internal partial interface INetFwRule : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string get_Name();
    void put_Name([MarshalAs(UnmanagedType.BStr)] string name);
    [return: MarshalAs(UnmanagedType.BStr)] string get_Description();
    void put_Description([MarshalAs(UnmanagedType.BStr)] string description);
    [return: MarshalAs(UnmanagedType.BStr)] string get_ApplicationName();
    void put_ApplicationName([MarshalAs(UnmanagedType.BStr)] string applicationName);
    [return: MarshalAs(UnmanagedType.BStr)] string get_serviceName();
    void put_serviceName([MarshalAs(UnmanagedType.BStr)] string serviceName);
    int get_Protocol();
    void put_Protocol(int protocol);
    [return: MarshalAs(UnmanagedType.BStr)] string get_LocalPorts();
    void put_LocalPorts([MarshalAs(UnmanagedType.BStr)] string localPorts);
    [return: MarshalAs(UnmanagedType.BStr)] string get_RemotePorts();
    void put_RemotePorts([MarshalAs(UnmanagedType.BStr)] string remotePorts);
    [return: MarshalAs(UnmanagedType.BStr)] string get_LocalAddresses();
    void put_LocalAddresses([MarshalAs(UnmanagedType.BStr)] string localAddresses);
    [return: MarshalAs(UnmanagedType.BStr)] string get_RemoteAddresses();
    void put_RemoteAddresses([MarshalAs(UnmanagedType.BStr)] string remoteAddresses);
    [return: MarshalAs(UnmanagedType.BStr)] string get_IcmpTypesAndCodes();
    void put_IcmpTypesAndCodes([MarshalAs(UnmanagedType.BStr)] string icmpTypesAndCodes);
    int get_Direction();
    void put_Direction(int direction);
    void get_Interfaces(out Variant interfaces);
    void put_Interfaces(Variant interfaces);
    [return: MarshalAs(UnmanagedType.BStr)] string get_InterfaceTypes();
    void put_InterfaceTypes([MarshalAs(UnmanagedType.BStr)] string interfaceTypes);
    short get_Enabled(); // VARIANT_BOOL
    void put_Enabled(short enabled); // VARIANT_BOOL
    [return: MarshalAs(UnmanagedType.BStr)] string get_Grouping();
    void put_Grouping([MarshalAs(UnmanagedType.BStr)] string grouping);
    int get_Profiles();
    void put_Profiles(int profiles);
    short get_EdgeTraversal(); // VARIANT_BOOL
    void put_EdgeTraversal(short edgeTraversal); // VARIANT_BOOL
    int get_Action();
    void put_Action(int action);
}

/// <summary>Firewall rule with edge-traversal options (HNetCfg <c>INetFwRule2</c>).</summary>
[GeneratedComInterface, Guid("9C27C8DA-189B-4DDE-89F7-8B39A316782C")]
internal partial interface INetFwRule2 : INetFwRule
{
    int get_EdgeTraversalOptions();
    void put_EdgeTraversalOptions(int edgeTraversalOptions);
}

/// <summary>Firewall rule with app-container / security members (HNetCfg <c>INetFwRule3</c>).</summary>
[GeneratedComInterface, Guid("B21563FF-D696-4222-AB46-4E89B73AB34A")]
internal partial interface INetFwRule3 : INetFwRule2
{
    [return: MarshalAs(UnmanagedType.BStr)] string get_LocalAppPackageId();
    void put_LocalAppPackageId([MarshalAs(UnmanagedType.BStr)] string localAppPackageId);
    [return: MarshalAs(UnmanagedType.BStr)] string get_LocalUserOwner();
    void put_LocalUserOwner([MarshalAs(UnmanagedType.BStr)] string localUserOwner);
    [return: MarshalAs(UnmanagedType.BStr)] string get_LocalUserAuthorizedList();
    void put_LocalUserAuthorizedList([MarshalAs(UnmanagedType.BStr)] string localUserAuthorizedList);
    [return: MarshalAs(UnmanagedType.BStr)] string get_RemoteUserAuthorizedList();
    void put_RemoteUserAuthorizedList([MarshalAs(UnmanagedType.BStr)] string remoteUserAuthorizedList);
    [return: MarshalAs(UnmanagedType.BStr)] string get_RemoteMachineAuthorizedList();
    void put_RemoteMachineAuthorizedList([MarshalAs(UnmanagedType.BStr)] string remoteMachineAuthorizedList);
    int get_SecureFlags();
    void put_SecureFlags(int secureFlags);
}

/// <summary>Collection of firewall rules (HNetCfg <c>INetFwRules</c>); enumerated via
/// <see cref="get__NewEnum"/> (IEnumVARIANT), since it exposes no index-based accessor.</summary>
[GeneratedComInterface, Guid("9C4C6277-5027-441E-AFAE-CA1F542DA009")]
internal partial interface INetFwRules : IDispatch
{
    int get_Count();
    void Add(INetFwRule rule);
    void Remove([MarshalAs(UnmanagedType.BStr)] string name);
    void get_Item([MarshalAs(UnmanagedType.BStr)] string name, out INetFwRule3 rule);
    nint get__NewEnum(); // IUnknown* -> IEnumVARIANT; wrapped by FirewallCom.EnumerateRules
}

/// <summary>Windows Firewall policy (HNetCfg <c>INetFwPolicy2</c>). Members OneMMC never calls keep
/// their vtable slots so the ones it does call land on the correct offsets.</summary>
[GeneratedComInterface, Guid("98325047-C671-4174-8D81-DEFCD3F03186")]
internal partial interface INetFwPolicy2 : IDispatch
{
    int get_CurrentProfileTypes();
    short get_FirewallEnabled(int profileType); // VARIANT_BOOL
    void set_FirewallEnabled(int profileType, short enabled); // VARIANT_BOOL
    void get_ExcludedInterfaces(int profileType, out Variant interfaces);
    void set_ExcludedInterfaces(int profileType, Variant interfaces);
    short get_BlockAllInboundTraffic(int profileType); // VARIANT_BOOL
    void set_BlockAllInboundTraffic(int profileType, short block); // VARIANT_BOOL
    short get_NotificationsDisabled(int profileType); // VARIANT_BOOL
    void set_NotificationsDisabled(int profileType, short disabled); // VARIANT_BOOL
    short get_UnicastResponsesToMulticastBroadcastDisabled(int profileType); // VARIANT_BOOL
    void set_UnicastResponsesToMulticastBroadcastDisabled(int profileType, short disabled); // VARIANT_BOOL
    void get_Rules(out INetFwRules rules);
    void get_ServiceRestriction(out nint serviceRestriction); // unused (INetFwServiceRestriction)
    void EnableRuleGroup(int profileTypesBitmask, [MarshalAs(UnmanagedType.BStr)] string group, short enable);
    short IsRuleGroupEnabled(int profileTypesBitmask, [MarshalAs(UnmanagedType.BStr)] string group);
    void RestoreLocalFirewallDefaults();
    int get_DefaultInboundAction(int profileType);
    void set_DefaultInboundAction(int profileType, int action);
    int get_DefaultOutboundAction(int profileType);
    void set_DefaultOutboundAction(int profileType, int action);
    short get_IsRuleGroupCurrentlyEnabled([MarshalAs(UnmanagedType.BStr)] string group); // unused
    int get_LocalPolicyModifyState();
}

/// <summary>Standard OLE <c>IEnumVARIANT</c> (IUnknown-derived, non-dual). Only <c>Next</c> is used,
/// one element at a time, to walk an <see cref="INetFwRules"/> collection.</summary>
[GeneratedComInterface, Guid("00020404-0000-0000-C000-000000000046")]
internal partial interface IEnumVariant
{
    [PreserveSig]
    int Next(uint celt, out Variant rgVar, out uint pCeltFetched);
}
