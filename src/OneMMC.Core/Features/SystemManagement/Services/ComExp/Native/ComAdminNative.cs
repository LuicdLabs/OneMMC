using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.SystemManagement.Services.ComExp.Native;

// Source-generated ([GeneratedComInterface]) dual interfaces for the minimal COM+ Administration
// (COMAdmin) surface OneMMC uses to enumerate COM+ applications and application instances. Ported
// from the previous IDispatch late binding + ProgID-reflection activation for Native AOT
// (doc/NativeAotMigration.md, M3): late binding and reflection COM activation are unsupported there.
// These derive from the source-generated IDispatch base (Infrastructure/Interop/IDispatch.cs) to
// reproduce the dual vtable layout (IUnknown[3] + IDispatch[4] + members) and are called by vtable
// with no runtime marshaller.
//
// Member ORDER is the authoritative vtable order and must not change. It was verified against the type
// library embedded in %SystemRoot%\System32\Com\comadmin.dll on the target OS (the interface members
// begin at vtable slot 7, i.e. immediately after IUnknown[3] + IDispatch[4]). Members OneMMC never
// calls are kept as vtable placeholders with opaque signatures so the slots OneMMC does call land on
// the correct offsets. Only the read surface is modeled — COM+ configuration is never written here.

/// <summary>
/// Top-level COM+ catalog. Activated from the <c>COMAdmin.COMAdminCatalog</c> coclass
/// (see <see cref="ComAdminCatalogClsid"/>) via <see cref="ComActivator"/>.
/// </summary>
[GeneratedComInterface, Guid("DD662187-DFC2-11D1-A2CF-00805FC79235")]
internal partial interface ICOMAdminCatalog : IDispatch
{
    /// <summary>Gets a top-level catalog collection by name (e.g. "Applications", "ApplicationInstances").</summary>
    void GetCollection([MarshalAs(UnmanagedType.BStr)] string bstrCollName, out ICatalogCollection ppCatalogCollection);
}

/// <summary>
/// A COM+ catalog collection. OneMMC enumerates it by index (<see cref="Populate"/> then
/// <see cref="get_Count"/> + <see cref="get_Item"/>) rather than via <c>IEnumVARIANT</c>.
/// </summary>
[GeneratedComInterface, Guid("6EB22872-8A19-11D0-81B6-00A0C9231C29")]
internal partial interface ICatalogCollection : IDispatch
{
    /// <summary>_NewEnum (IEnumVARIANT). Unused; vtable placeholder (slot 7).</summary>
    nint get__NewEnum();

    /// <summary>Gets the object at <paramref name="lIndex"/> (0-based). (slot 8)</summary>
    void get_Item(int lIndex, out ICatalogObject ppCatalogObject);

    /// <summary>Gets the number of objects currently populated in the collection. (slot 9)</summary>
    int get_Count();

    /// <summary>Removes the object at the given index. Unused; vtable placeholder (slot 10).</summary>
    void Remove(int lIndex);

    /// <summary>Adds a new object. Unused; vtable placeholder (slot 11, IDispatch out).</summary>
    void Add(out nint ppCatalogObject);

    /// <summary>Loads this collection's objects from the catalog. (slot 12)</summary>
    void Populate();
}

/// <summary>A single COM+ catalog object; OneMMC reads its named property values via <see cref="get_Value"/>.</summary>
[GeneratedComInterface, Guid("6EB22871-8A19-11D0-81B6-00A0C9231C29")]
internal partial interface ICatalogObject : IDispatch
{
    /// <summary>
    /// Gets the value of the named property as a VARIANT (the <c>propget Value(bstrPropName)</c> accessor,
    /// vtable slot 7). Throws <see cref="COMException"/> when the property does not exist on this object.
    /// </summary>
    void get_Value([MarshalAs(UnmanagedType.BStr)] string bstrPropName, out Variant pvarRetVal);
}

/// <summary>CLSID of the <c>COMAdmin.COMAdminCatalog</c> coclass (registered as <c>Catalog2 Class</c>),
/// activated via <see cref="ComActivator"/> (AOT-safe) rather than a reflection-activated ProgID.</summary>
internal static class ComAdminCatalogClsid
{
    /// <summary>{F618C514-DFB8-11D1-A2CF-00805FC79235}. Verified as the target of the version-independent
    /// ProgID <c>COMAdmin.COMAdminCatalog</c>. Registered ThreadingModel=Both, so it activates directly
    /// on the caller's (MTA) thread with no cross-apartment marshalling.</summary>
    internal static readonly Guid ComAdminCatalog = new("F618C514-DFB8-11D1-A2CF-00805FC79235");
}
