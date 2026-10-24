using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.NetworkListManager.Native;

// Source-generated ([GeneratedComInterface]) ports of the public Network List Manager API
// (netlistmgr.h) used to enumerate the networks secpol.msc's Network List Manager Policies node
// lists; see doc/NativeAot.md ("COM interop"). nlmgp.dll enumerates the same networks through a
// private, non-dual NLM interface; these are the documented dual equivalents, so they derive from the
// source-generated IDispatch base to reproduce the dual vtable layout (IUnknown[3] + IDispatch[4] +
// members). Member ORDER is the authoritative vtable order and must not change.
//
// Conversion rules applied:
//   * BSTR strings -> [MarshalAs(UnmanagedType.BStr)] string.
//   * VARIANT_BOOL properties -> raw short (-1 = true, 0 = false).
//   * Interface out-params -> the specific source-generated interface type.
//   * Members OneMMC never calls keep their vtable slots with opaque nint signatures.

/// <summary>NLM_ENUM_NETWORK: which networks <see cref="INetworkListManager.GetNetworks"/> returns.</summary>
internal enum NlmEnumNetwork
{
    Connected = 0x01,
    Disconnected = 0x02,
    All = 0x03
}

/// <summary>NLM_DOMAIN_TYPE: the domain type of a network.</summary>
internal enum NlmDomainType
{
    NonDomainNetwork = 0x00,
    DomainNetwork = 0x01,
    DomainAuthenticated = 0x02
}

/// <summary>NLM_NETWORK_CATEGORY: the network location category of a network.</summary>
internal enum NlmNetworkCategory
{
    Public = 0x00,
    Private = 0x01,
    DomainAuthenticated = 0x02
}

/// <summary>
/// NLM_NETWORK_CLASS: the <c>NA_NetworkClass</c> property-bag value distinguishing identified networks
/// from the special "Identifying" and "Unidentified" pseudo-networks.
/// </summary>
internal enum NlmNetworkClass : uint
{
    Identifying = 0x01,
    Identified = 0x02,
    Unidentified = 0x03
}

/// <summary>CLSID of the <c>NetworkListManager</c> coclass (netprofm.dll, ThreadingModel=Both).</summary>
internal static class NetworkListManagerClsid
{
    /// <summary>{DCB00C01-570F-4A9B-8D69-199FDBA5723B}</summary>
    public static readonly Guid NetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
}

/// <summary>Network list management functions (netlistmgr.h).</summary>
[GeneratedComInterface, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetworkListManager : IDispatch
{
    void GetNetworks(NlmEnumNetwork flags, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IEnumNetworks>))] out IEnumNetworks ppEnumNetwork);
    void GetNetwork(Guid gdNetworkId, out nint ppNetwork); // unused (placeholder)
    void GetNetworkConnections(out nint ppEnum); // unused (placeholder)
    void GetNetworkConnection(Guid gdNetworkConnectionId, out nint ppNetworkConnection); // unused (placeholder)
    void get_IsConnectedToInternet(out short pbIsConnected); // unused (placeholder)
    void get_IsConnected(out short pbIsConnected); // unused (placeholder)
    void GetConnectivity(out int pConnectivity); // unused (placeholder)
    void SetSimulatedProfileInfo(nint pSimulatedInfo); // unused (placeholder)
    void ClearSimulatedProfileInfo(); // unused (placeholder)
}

/// <summary>Standard enumerator for networks.</summary>
[GeneratedComInterface, Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface IEnumNetworks : IDispatch
{
    void get__NewEnum(out nint ppEnumVar); // unused (placeholder)

    /// <summary>Returns S_OK when <paramref name="pceltFetched"/> equals <paramref name="celt"/>, otherwise S_FALSE.</summary>
    [PreserveSig]
    int Next(uint celt, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<INetwork>))] out INetwork? rgelt, out uint pceltFetched);

    void Skip(uint celt); // unused (placeholder)
    void Reset(); // unused (placeholder)
    void Clone(out nint ppEnumNetwork); // unused (placeholder)
}

/// <summary>
/// A network on the local machine. The implementing object also exposes an
/// <see cref="IPropertyBag"/> with the <c>NA_*</c> properties.
/// </summary>
[GeneratedComInterface, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetwork : IDispatch
{
    void GetName([MarshalAs(UnmanagedType.BStr)] out string pszNetworkName);
    void SetName([MarshalAs(UnmanagedType.BStr)] string szNetworkNewName); // unused (placeholder)
    void GetDescription([MarshalAs(UnmanagedType.BStr)] out string pszDescription);
    void SetDescription([MarshalAs(UnmanagedType.BStr)] string szDescription); // unused (placeholder)
    void GetNetworkId(out Guid pgdGuidNetworkId);
    void GetDomainType(out NlmDomainType pNetworkType);
    void GetNetworkConnections(out nint ppEnumNetworkConnection); // unused (placeholder)
    void GetTimeCreatedAndConnected(out uint pdwLowDateTimeCreated, out uint pdwHighDateTimeCreated, out uint pdwLowDateTimeConnected, out uint pdwHighDateTimeConnected); // unused (placeholder)
    void get_IsConnectedToInternet(out short pbIsConnected); // unused (placeholder)
    void get_IsConnected(out short pbIsConnected); // unused (placeholder)
    void GetConnectivity(out int pConnectivity); // unused (placeholder)
    void GetCategory(out NlmNetworkCategory pCategory);
    void SetCategory(NlmNetworkCategory newCategory); // unused (placeholder)
}

/// <summary>
/// OLE property bag (oaidl.h). <see cref="INetwork"/> objects implement it for the documented
/// <c>NA_*</c> network properties such as <c>NA_NetworkClass</c>.
/// </summary>
[GeneratedComInterface, Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
internal partial interface IPropertyBag
{
    [PreserveSig]
    int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref Variant pVar, nint pErrorLog);

    [PreserveSig]
    int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, in Variant pVar); // unused (placeholder)
}
