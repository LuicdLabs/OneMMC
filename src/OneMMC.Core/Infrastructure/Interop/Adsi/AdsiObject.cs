using System;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.ActiveDirectory;
using Windows.Win32.System.Variant;
using ComIDispatch = Windows.Win32.System.Com.IDispatch;

namespace OneMMC.Core.Infrastructure.Interop.Adsi;

/// <summary>
/// Managed wrapper over a raw <c>IADs*</c> — the marshal-free equivalent of
/// <c>System.DirectoryServices.DirectoryEntry</c> for the operations OneMMC uses:
/// property-cache read/write (<c>GetInfo</c>/<c>Get</c>/<c>Put</c>/<c>SetInfo</c>), child
/// management through <c>IADsContainer</c>, and subtree delete through <c>IADsDeleteOps</c>.
/// </summary>
internal sealed unsafe partial class AdsiObject : IDisposable
{
    private IADs* _object;

    internal AdsiObject(IADs* adsObject) => _object = adsObject;

    /// <summary>Loads the property cache from the directory (DirectoryEntry.RefreshCache parity).</summary>
    internal void GetInfo() => _object->GetInfo();

    /// <summary>Commits pending <see cref="Put"/> values to the directory (CommitChanges parity).</summary>
    internal void SetInfo() => _object->SetInfo();

    /// <summary>
    /// Reads a property without throwing: an absent attribute returns a failure HRESULT
    /// (<c>E_ADS_PROPERTY_NOT_FOUND</c>) instead of a first-chance <see cref="COMException"/> —
    /// the raw vtable-slot call (slot 15, verified against the CsWin32 metadata projection)
    /// mirrors the PreserveSig approach the AzMan and Wbem layers use for probe reads.
    /// The caller owns (must dispose) <paramref name="value"/> on success.
    /// </summary>
    internal int TryGet(string name, out Interop.Variant value)
    {
        value = default;
        nint nameBstr = Marshal.StringToBSTR(name);
        try
        {
            fixed (Interop.Variant* pValue = &value)
            {
                void** vtbl = *(void***)_object;
                return ((delegate* unmanaged[Stdcall]<IADs*, nint, VARIANT*, int>)vtbl[15])(_object, nameBstr, (VARIANT*)pValue);
            }
        }
        finally
        {
            Marshal.FreeBSTR(nameBstr);
        }
    }

    /// <summary>
    /// Reads a property, returning an empty variant when the attribute is absent or unreadable.
    /// The caller owns (must dispose) the returned variant.
    /// </summary>
    internal Interop.Variant GetOrDefault(string name)
    {
        return TryGet(name, out Interop.Variant value) >= 0 ? value : default;
    }

    /// <summary>
    /// Puts a value into the property cache (flushed by <see cref="SetInfo"/>). The variant is
    /// copied by ADSI; the caller keeps ownership of <paramref name="value"/>.
    /// </summary>
    internal void Put(string name, in Interop.Variant value)
    {
        nint nameBstr = Marshal.StringToBSTR(name);
        try
        {
            fixed (Interop.Variant* pValue = &value)
            {
                _object->Put(new BSTR(nameBstr), *(VARIANT*)pValue);
            }
        }
        finally
        {
            Marshal.FreeBSTR(nameBstr);
        }
    }

    /// <summary>
    /// Opens a child object (<c>IADsContainer::GetObject</c>; <c>DirectoryEntry.Children.Find</c>
    /// parity — including throwing <see cref="COMException"/> when the child does not exist).
    /// </summary>
    /// <param name="className">Schema class filter (e.g. "container"), or null for any class.</param>
    /// <param name="relativeName">Relative distinguished name (e.g. "CN=Printers").</param>
    internal AdsiObject GetChild(string? className, string relativeName)
    {
        return WithContainer(container =>
        {
            nint classBstr = className is null ? 0 : Marshal.StringToBSTR(className);
            nint nameBstr = Marshal.StringToBSTR(relativeName);
            try
            {
                ComIDispatch* child;
                container->GetObject(new BSTR(classBstr), new BSTR(nameBstr), &child);
                return WrapDispatchAsObject(child);
            }
            finally
            {
                Marshal.FreeBSTR(nameBstr);
                if (classBstr != 0)
                {
                    Marshal.FreeBSTR(classBstr);
                }
            }
        });
    }

    /// <summary>
    /// Creates a child object in the container (<c>IADsContainer::Create</c>;
    /// <c>DirectoryEntry.Children.Add</c> parity). The child exists in the directory only after
    /// its <see cref="SetInfo"/> is called.
    /// </summary>
    internal AdsiObject CreateChild(string className, string relativeName)
    {
        return WithContainer(container =>
        {
            nint classBstr = Marshal.StringToBSTR(className);
            nint nameBstr = Marshal.StringToBSTR(relativeName);
            try
            {
                ComIDispatch* child;
                container->Create(new BSTR(classBstr), new BSTR(nameBstr), &child);
                return WrapDispatchAsObject(child);
            }
            finally
            {
                Marshal.FreeBSTR(nameBstr);
                Marshal.FreeBSTR(classBstr);
            }
        });
    }

    /// <summary>
    /// Deletes this object and all of its descendants (<c>IADsDeleteOps::DeleteObject</c>, which
    /// the LDAP provider implements as a subtree delete — <c>DirectoryEntry.DeleteTree</c> parity).
    /// </summary>
    internal void DeleteTree()
    {
        Guid iid = Adsi.IID_IADsDeleteOps;
        void* pDeleteOps;
        HRESULT hr = _object->QueryInterface(&iid, &pDeleteOps);
        hr.ThrowOnFailure();

        var deleteOps = (IADsDeleteOps*)pDeleteOps;
        try
        {
            deleteOps->DeleteObject(0);
        }
        finally
        {
            deleteOps->Release();
        }
    }

    /// <summary>Runs an action against the object's IADsContainer identity.</summary>
    private T WithContainer<T>(ContainerFunc<T> action)
    {
        Guid iid = Adsi.IID_IADsContainer;
        void* pContainer;
        HRESULT hr = _object->QueryInterface(&iid, &pContainer);
        hr.ThrowOnFailure();

        var container = (IADsContainer*)pContainer;
        try
        {
            return action(container);
        }
        finally
        {
            container->Release();
        }
    }

    private delegate T ContainerFunc<T>(IADsContainer* container);

    /// <summary>QIs a returned IDispatch child to IADs and wraps it, releasing the IDispatch.</summary>
    private static AdsiObject WrapDispatchAsObject(ComIDispatch* dispatch)
    {
        Guid iid = Adsi.IID_IADs;
        void* pObject;
        HRESULT hr = dispatch->QueryInterface(&iid, &pObject);
        dispatch->Release();
        hr.ThrowOnFailure();
        return new AdsiObject((IADs*)pObject);
    }

    public void Dispose()
    {
        if (_object is not null)
        {
            _object->Release();
            _object = null;
        }
    }
}
