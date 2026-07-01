using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.SystemManagement.Interop.WF;

[ComImport]
[Guid("98325047-C671-4174-8D81-DEFCD3F03186")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface INetFwPolicy2
{
    [DispId(1)]
    int CurrentProfileTypes { get; }
    
    [DispId(2)]
    bool get_FirewallEnabled(int profileType);
    [DispId(2)]
    void set_FirewallEnabled(int profileType, bool enabled);
    
    [DispId(3)]
    object get_ExcludedInterfaces(int profileType);
    [DispId(3)]
    void set_ExcludedInterfaces(int profileType, object interfaces);
    
    [DispId(4)]
    bool get_BlockAllInboundTraffic(int profileType);
    [DispId(4)]
    void set_BlockAllInboundTraffic(int profileType, bool block);
    
    [DispId(5)]
    bool get_NotificationsDisabled(int profileType);
    [DispId(5)]
    void set_NotificationsDisabled(int profileType, bool disabled);
    
    [DispId(6)]
    bool get_UnicastResponsesToMulticastBroadcastDisabled(int profileType);
    [DispId(6)]
    void set_UnicastResponsesToMulticastBroadcastDisabled(int profileType, bool disabled);
    
    [DispId(7)]
    INetFwRules Rules { get; }
    
    [DispId(8)]
    object ServiceRestriction { get; }
    
    [DispId(9)]
    void EnableRuleGroup([In] int profileTypesBitmask, [In, MarshalAs(UnmanagedType.BStr)] string group, [In] bool enable);
    
    [DispId(10)]
    bool IsRuleGroupEnabled([In] int profileTypesBitmask, [In, MarshalAs(UnmanagedType.BStr)] string group);
    
    [DispId(11)]
    void RestoreLocalFirewallDefaults();
    
    [DispId(12)]
    int get_DefaultInboundAction(int profileType);
    [DispId(12)]
    void set_DefaultInboundAction(int profileType, int action);
    
    [DispId(13)]
    int get_DefaultOutboundAction(int profileType);
    [DispId(13)]
    void set_DefaultOutboundAction(int profileType, int action);
    
    [DispId(14)]
    bool IsRuleGroupCurrentlyEnabled([In, MarshalAs(UnmanagedType.BStr)] string group);
    
    [DispId(15)]
    int LocalPolicyModifyState { get; }
}

[ComImport]
[Guid("9C4C6277-5027-441E-AFAE-CA1F542DA009")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface INetFwRules : IEnumerable
{
    [DispId(1)]
    int Count { get; }
    
    [DispId(2)]
    void Add([In, MarshalAs(UnmanagedType.Interface)] INetFwRule rule);
    
    [DispId(3)]
    void Remove([In, MarshalAs(UnmanagedType.BStr)] string name);
    
    [DispId(4)]
    [return: MarshalAs(UnmanagedType.Interface)]
    INetFwRule Item([In, MarshalAs(UnmanagedType.BStr)] string name);
}

[ComImport]
[Guid("AF230D27-BABA-4E42-ACED-F524F22CFCE2")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface INetFwRule
{
    [DispId(1)]
    string Name { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(2)]
    string Description { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(3)]
    string ApplicationName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(4)]
    string serviceName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(5)]
    int Protocol { get; set; }
    
    [DispId(6)]
    string LocalPorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(7)]
    string RemotePorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(8)]
    string LocalAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(9)]
    string RemoteAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(10)]
    string IcmpTypesAndCodes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(11)]
    int Direction { get; set; }
    
    [DispId(12)]
    object Interfaces { get; set; }
    
    [DispId(13)]
    string InterfaceTypes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(14)]
    bool Enabled { get; set; }
    
    [DispId(15)]
    string Grouping { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(16)]
    int Profiles { get; set; }
    
    [DispId(17)]
    bool EdgeTraversal { get; set; }
    
    [DispId(18)]
    int Action { get; set; }
}

[ComImport]
[Guid("9C27C8DA-189B-4DDE-89F7-8B39A316782C")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface INetFwRule2 : INetFwRule
{
    // Properties from INetFwRule
    [DispId(1)]
    new string Name { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(2)]
    new string Description { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(3)]
    new string ApplicationName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(4)]
    new string serviceName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(5)]
    new int Protocol { get; set; }
    [DispId(6)]
    new string LocalPorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(7)]
    new string RemotePorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(8)]
    new string LocalAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(9)]
    new string RemoteAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(10)]
    new string IcmpTypesAndCodes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(11)]
    new int Direction { get; set; }
    [DispId(12)]
    new object Interfaces { get; set; }
    [DispId(13)]
    new string InterfaceTypes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(14)]
    new bool Enabled { get; set; }
    [DispId(15)]
    new string Grouping { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(16)]
    new int Profiles { get; set; }
    [DispId(17)]
    new bool EdgeTraversal { get; set; }
    [DispId(18)]
    new int Action { get; set; }

    // Properties from INetFwRule2
    [DispId(19)]
    int EdgeTraversalOptions { get; set; }
}

[ComImport]
[Guid("B21563FF-D696-4222-AB46-4E89B73AB34A")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface INetFwRule3 : INetFwRule2
{
    // Properties from INetFwRule
    [DispId(1)]
    new string Name { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(2)]
    new string Description { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(3)]
    new string ApplicationName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(4)]
    new string serviceName { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(5)]
    new int Protocol { get; set; }
    [DispId(6)]
    new string LocalPorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(7)]
    new string RemotePorts { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(8)]
    new string LocalAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(9)]
    new string RemoteAddresses { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(10)]
    new string IcmpTypesAndCodes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(11)]
    new int Direction { get; set; }
    [DispId(12)]
    new object Interfaces { get; set; }
    [DispId(13)]
    new string InterfaceTypes { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(14)]
    new bool Enabled { get; set; }
    [DispId(15)]
    new string Grouping { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    [DispId(16)]
    new int Profiles { get; set; }
    [DispId(17)]
    new bool EdgeTraversal { get; set; }
    [DispId(18)]
    new int Action { get; set; }

    // Properties from INetFwRule2
    [DispId(19)]
    new int EdgeTraversalOptions { get; set; }

    // Properties from INetFwRule3
    [DispId(20)]
    string LocalAppPackageId { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(21)]
    string LocalUserOwner { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(22)]
    string LocalUserAuthorizedList { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(23)]
    string RemoteUserAuthorizedList { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(24)]
    string RemoteMachineAuthorizedList { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    
    [DispId(25)]
    int SecureFlags { get; set; }
}


