using System;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.ActiveDirectory;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.Interop.Adsi;

/// <summary>
/// Entry points for the marshal-free ADSI COM layer — the Native-AOT-compatible replacement for
/// <c>System.DirectoryServices</c> (<c>DirectoryEntry</c>/<c>DirectorySearcher</c>) used by the
/// PrintManagement GPO printer-deployment services and the AzMan AD-store schema accessor.
/// <para>
/// Like the Windows Firewall <c>Wbem</c> layer, this uses CsWin32's <c>allowMarshaling:false</c>
/// function-pointer-vtable structs (<c>IADs*</c>/<c>IADsContainer*</c>/<c>IDirectorySearch*</c>/
/// <c>IADsDeleteOps*</c>, raw AddRef/Release, all <c>unsafe</c>): the vtable order comes from
/// Windows metadata, so hand-authoring (and its silent vtable-corruption risk) is avoided. See
/// doc/NativeAotMigration.md (M4).
/// </para>
/// <para>
/// Threading: ADSI LDAP objects aggregate the free-threaded marshaler, so binding and using them
/// from thread-pool (MTA) threads is safe — the same model the previous
/// <c>System.DirectoryServices</c> callers relied on under <c>Task.Run</c>.
/// </para>
/// </summary>
internal static unsafe class Adsi
{
    // Canonical iads.h interface IDs.
    internal static readonly Guid IID_IADs = new("FD8256D0-FD15-11CE-ABC4-02608C9E7553");
    internal static readonly Guid IID_IADsContainer = new("001677D0-FD16-11CE-ABC4-02608C9E7553");
    internal static readonly Guid IID_IDirectorySearch = new("109BA8EC-92F0-11D0-A790-00C04FD8D5A8");
    internal static readonly Guid IID_IADsDeleteOps = new("B2BD0902-8878-11D1-8C21-00C04FD8D503");

    /// <summary>
    /// Binds to <paramref name="adsPath"/> (e.g. <c>LDAP://CN=...,DC=...</c>) under the caller's
    /// identity with secure authentication and returns the object wrapper.
    /// Throws <see cref="COMException"/> when the bind fails (server down, no domain, access denied…).
    /// </summary>
    internal static AdsiObject BindObject(string adsPath)
    {
        Guid iid = IID_IADs;
        void* pObject = null;
        Win32PInvoke.ADsOpenObject(adsPath, null, null, ADS_AUTHENTICATION_ENUM.ADS_SECURE_AUTHENTICATION, in iid, ref pObject).ThrowOnFailure();
        return new AdsiObject((IADs*)pObject);
    }

    /// <summary>
    /// Binds to <paramref name="adsPath"/> as a search root and returns the searcher wrapper.
    /// Throws <see cref="COMException"/> when the bind fails.
    /// </summary>
    internal static AdsiSearcher BindSearcher(string adsPath)
    {
        Guid iid = IID_IDirectorySearch;
        void* pSearch = null;
        Win32PInvoke.ADsOpenObject(adsPath, null, null, ADS_AUTHENTICATION_ENUM.ADS_SECURE_AUTHENTICATION, in iid, ref pSearch).ThrowOnFailure();
        return new AdsiSearcher((IDirectorySearch*)pSearch);
    }

    /// <summary>
    /// Reads the domain's default naming context (its distinguished name) from RootDSE — the
    /// replacement for <c>Domain.GetCurrentDomain().GetDirectoryEntry()</c>.
    /// Throws <see cref="COMException"/> off-domain / when no domain controller is reachable;
    /// returns an empty string if RootDSE answers without the attribute.
    /// </summary>
    internal static string GetDefaultNamingContext()
    {
        using AdsiObject rootDse = BindObject("LDAP://RootDSE");
        using Interop.Variant value = rootDse.GetOrDefault("defaultNamingContext");
        return value.ToInvariantString() ?? string.Empty;
    }

    /// <summary>
    /// Whether <paramref name="hresult"/> means "Active Directory is not reachable from this
    /// machine" (workgroup PC, DC down, no such domain) — the conditions the old code recognized
    /// via <c>ActiveDirectoryObjectNotFoundException</c>/<c>ActiveDirectoryOperationException</c>.
    /// </summary>
    internal static bool IsDirectoryUnavailable(int hresult) => (uint)hresult
        is 0x8007203A  // ERROR_DS_SERVER_DOWN - no server could be contacted
        or 0x8007054B  // ERROR_NO_SUCH_DOMAIN - the specified domain does not exist
        or 0x80070035  // ERROR_BAD_NETPATH
        or 0x8007052E  // ERROR_LOGON_FAILURE
        or 0x8007203B  // ERROR_DS_UNAVAILABLE
        or 0x800704CF; // ERROR_NETWORK_UNREACHABLE

    /// <inheritdoc cref="IsDirectoryUnavailable(int)"/>
    internal static bool IsDirectoryUnavailable(Exception exception) =>
        exception is COMException comException && IsDirectoryUnavailable(comException.ErrorCode);
}
