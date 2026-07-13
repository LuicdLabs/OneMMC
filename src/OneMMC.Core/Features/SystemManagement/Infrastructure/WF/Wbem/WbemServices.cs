using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Wmi;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF.Wbem;

/// <summary>
/// Managed session over the classic-WMI COM API (<c>IWbemLocator</c>/<c>IWbemServices</c>) — the
/// marshal-free, Native-AOT-compatible replacement for
/// <c>Microsoft.Management.Infrastructure.CimSession</c> in the Windows Firewall feature. Bound to a
/// single WMI namespace (all WF operations use <c>root\StandardCimv2</c>); connect with
/// <see cref="Connect"/>, enumerate/read/write, then <see cref="Dispose"/>.
/// <para>
/// This layer uses CsWin32's <c>allowMarshaling:false</c> function-pointer-vtable structs (raw
/// <c>IWbemServices*</c> + manual AddRef/Release, all <c>unsafe</c>) because those interfaces live in
/// <c>wbemcli.h</c> (not a type library), so CsWin32 emits their vtables correctly from Windows metadata —
/// hand-authoring would risk silent vtable-order corruption. See <c>doc/NativeAot.md</c>,
/// "WMI and CIM".
/// </para>
/// </summary>
internal sealed unsafe partial class WbemServices : IDisposable
{
    private static readonly Guid CLSID_WbemLocator = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid IID_IWbemLocator = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    // COM authentication constants for CoSetProxyBlanket (rpcdce.h / objidl.h).
    private const uint RPC_C_AUTHN_WINNT = 10;
    private const uint RPC_C_AUTHZ_NONE = 0;
    private const uint RPC_C_AUTHN_LEVEL_PKT_PRIVACY = 6;
    private const uint RPC_C_IMP_LEVEL_IMPERSONATE = 3;
    private const uint EOAC_NONE = 0;

    private IWbemServices* _services;

    private WbemServices(IWbemServices* services) => _services = services;

    /// <summary>
    /// Connects to <paramref name="namespacePath"/> (e.g. <c>root\StandardCimv2</c>) on the local machine
    /// under the caller's identity. Throws <see cref="COMException"/> if activation or connection fails.
    /// </summary>
    internal static WbemServices Connect(string namespacePath)
    {
        // Activate IWbemLocator (coclass ThreadingModel=Both) via CoCreateInstance — raw pointer, no ComWrappers.
        Guid clsid = CLSID_WbemLocator;
        Guid iid = IID_IWbemLocator;
        void* pLocator;
        PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &pLocator).ThrowOnFailure();

        var locator = (IWbemLocator*)pLocator;
        nint nsBstr = Marshal.StringToBSTR(namespacePath);
        try
        {
            IWbemServices* services;
            // Local, same-user connection: user/password/locale/authority all null; no security flags.
            locator->ConnectServer(new BSTR(nsBstr), default, default, default, 0, default, null, &services);

            // A freshly-connected WMI proxy must have its security blanket set before any call, or
            // operations (CreateInstanceEnum/Next/PutInstance/…) fail with WBEM_E_ACCESS_DENIED. MMI did
            // this internally; replicate the canonical client blanket. NTLM auth, packet-privacy, caller
            // impersonation, default identity. CoSetProxyBlanket is not projected by CsWin32, so it is a
            // documented handwritten import.
            int blanket = CoSetProxyBlanket(
                (nint)services,
                RPC_C_AUTHN_WINNT,
                RPC_C_AUTHZ_NONE,
                0,
                RPC_C_AUTHN_LEVEL_PKT_PRIVACY,
                RPC_C_IMP_LEVEL_IMPERSONATE,
                0,
                EOAC_NONE);
            Marshal.ThrowExceptionForHR(blanket);

            return new WbemServices(services);
        }
        finally
        {
            Marshal.FreeBSTR(nsBstr);
            Marshal.Release((nint)pLocator);
        }
    }

    /// <summary>
    /// Enumerates every instance of <paramref name="className"/> (forward-only, semi-synchronous). Each
    /// returned <see cref="WbemObject"/> is owned by the caller and must be disposed; enumeration is lazy,
    /// so breaking early (disposing the objects seen so far) does not materialise the rest.
    /// </summary>
    internal IEnumerable<WbemObject> EnumerateInstances(string className)
    {
        nint pEnum = CreateInstanceEnum(className);
        try
        {
            for (WbemObject? o = NextObject(pEnum); o is not null; o = NextObject(pEnum))
            {
                yield return o;
            }
        }
        finally
        {
            if (pEnum != 0)
            {
                Marshal.Release(pEnum);
            }
        }
    }

    /// <summary>
    /// Enumerates the instances associated with <paramref name="source"/> through
    /// <paramref name="associationClass"/> that are of <paramref name="resultClass"/> — the
    /// <c>ASSOCIATORS OF</c> WQL equivalent of <c>CimSession.EnumerateAssociatedInstances</c>.
    /// </summary>
    internal IEnumerable<WbemObject> EnumerateAssociatedInstances(WbemObject source, string associationClass, string resultClass)
    {
        string? path = source.Path;
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        string query = $"ASSOCIATORS OF {{{path}}} WHERE AssocClass={associationClass} ResultClass={resultClass}";
        nint pEnum = ExecQuery(query);
        try
        {
            for (WbemObject? o = NextObject(pEnum); o is not null; o = NextObject(pEnum))
            {
                yield return o;
            }
        }
        finally
        {
            if (pEnum != 0)
            {
                Marshal.Release(pEnum);
            }
        }
    }

    /// <summary>
    /// Creates a new, empty instance of <paramref name="className"/> to populate and then
    /// <see cref="CreateInstance"/> — the equivalent of <c>new CimInstance(className, ns)</c>. Fetches the
    /// class definition and spawns a blank instance from it.
    /// </summary>
    internal WbemObject SpawnInstance(string className)
    {
        nint classBstr = Marshal.StringToBSTR(className);
        IWbemClassObject* classObject = null;
        try
        {
            _services->GetObject(new BSTR(classBstr), 0, null, &classObject, null);
            IWbemClassObject* instance;
            classObject->SpawnInstance(0, &instance);
            return new WbemObject(instance);
        }
        finally
        {
            if (classObject is not null)
            {
                Marshal.Release((nint)classObject);
            }
            Marshal.FreeBSTR(classBstr);
        }
    }

    /// <summary>Creates <paramref name="instance"/> in WMI (<c>PutInstance</c>, create-only) — throws if it already exists.</summary>
    internal void CreateInstance(WbemObject instance)
        => _services->PutInstance(instance.Pointer, (WBEM_GENERIC_FLAG_TYPE)WbemCimType.WBEM_FLAG_CREATE_ONLY, null, null);

    /// <summary>Persists changes to an existing <paramref name="instance"/> (<c>PutInstance</c>, update-only).</summary>
    internal void ModifyInstance(WbemObject instance)
        => _services->PutInstance(instance.Pointer, (WBEM_GENERIC_FLAG_TYPE)WbemCimType.WBEM_FLAG_UPDATE_ONLY, null, null);

    /// <summary>
    /// Invokes the parameterless WMI method <paramref name="methodName"/> on <paramref name="instance"/>
    /// (<c>ExecMethod</c> against its <c>__PATH</c>, no in-parameters, output ignored) — the equivalent of
    /// <c>CimSession.InvokeMethod(ns, instance, methodName, new CimMethodParametersCollection())</c>. Used
    /// for the firewall <c>Enable</c>/<c>Disable</c> connection-security rule methods.
    /// </summary>
    internal void ExecMethod(WbemObject instance, string methodName)
    {
        string? path = instance.Path;
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("Cannot invoke a WMI method on an instance without a __PATH (was it enumerated/created?).");
        }

        nint pathBstr = Marshal.StringToBSTR(path);
        nint methodBstr = Marshal.StringToBSTR(methodName);
        try
        {
            // No in-parameters (pInParams = null); output parameters and call result are ignored.
            _services->ExecMethod(new BSTR(pathBstr), new BSTR(methodBstr), 0, null, null);
        }
        finally
        {
            Marshal.FreeBSTR(methodBstr);
            Marshal.FreeBSTR(pathBstr);
        }
    }

    /// <summary>Deletes <paramref name="instance"/> from WMI by its <c>__PATH</c>.</summary>
    internal void DeleteInstance(WbemObject instance)
    {
        string? path = instance.Path;
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("Cannot delete a WMI instance without a __PATH (was it enumerated/created?).");
        }

        nint pathBstr = Marshal.StringToBSTR(path);
        try
        {
            _services->DeleteInstance(new BSTR(pathBstr), 0, null, null);
        }
        finally
        {
            Marshal.FreeBSTR(pathBstr);
        }
    }

    private nint CreateInstanceEnum(string className)
    {
        nint filter = Marshal.StringToBSTR(className);
        try
        {
            IEnumWbemClassObject* pEnum;
            _services->CreateInstanceEnum(
                new BSTR(filter),
                (WBEM_GENERIC_FLAG_TYPE)(WbemCimType.WBEM_FLAG_FORWARD_ONLY | WbemCimType.WBEM_FLAG_RETURN_IMMEDIATELY),
                null,
                &pEnum);
            return (nint)pEnum;
        }
        finally
        {
            Marshal.FreeBSTR(filter);
        }
    }

    private nint ExecQuery(string query)
    {
        nint lang = Marshal.StringToBSTR("WQL");
        nint wql = Marshal.StringToBSTR(query);
        try
        {
            IEnumWbemClassObject* pEnum;
            _services->ExecQuery(
                new BSTR(lang),
                new BSTR(wql),
                (WBEM_GENERIC_FLAG_TYPE)(WbemCimType.WBEM_FLAG_FORWARD_ONLY | WbemCimType.WBEM_FLAG_RETURN_IMMEDIATELY),
                null,
                &pEnum);
            return (nint)pEnum;
        }
        finally
        {
            Marshal.FreeBSTR(lang);
            Marshal.FreeBSTR(wql);
        }
    }

    /// <summary>Pulls the next instance from an enumerator (blocking), or <see langword="null"/> at the end.</summary>
    private static WbemObject? NextObject(nint pEnum)
    {
        if (pEnum == 0)
        {
            return null;
        }

        var enumerator = (IEnumWbemClassObject*)pEnum;
        IWbemClassObject* pObj;
        uint returned;
        HRESULT hr = enumerator->Next(WbemCimType.WBEM_INFINITE, 1, &pObj, &returned);
        return hr.Succeeded && returned == 1 ? new WbemObject(pObj) : null;
    }

    public void Dispose()
    {
        if (_services is not null)
        {
            Marshal.Release((nint)_services);
            _services = null;
        }
    }

    /// <summary>
    /// Sets the DCOM security blanket on a WMI proxy. Not projected by CsWin32 (a documented handwritten
    /// interop exception); the signature and constants are from <c>objidl.h</c>/<c>rpcdce.h</c>.
    /// </summary>
    [LibraryImport("ole32.dll")]
    private static partial int CoSetProxyBlanket(
        nint pProxy,
        uint dwAuthnSvc,
        uint dwAuthzSvc,
        nint pServerPrincName,
        uint dwAuthnLevel,
        uint dwImpLevel,
        nint pAuthInfo,
        uint dwCapabilities);
}
