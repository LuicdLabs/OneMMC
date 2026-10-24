using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.PublicKeyPolicies;

/// <summary>
/// A Group Policy certificate store (EFS, DPNGRA, FVE, Root, …) opened inside a policy hive the way
/// secpol.msc's Public Key Policies node (certmgr.dll <c>CCertStoreGPE</c>) opens it: as the
/// <c>CERT_SYSTEM_STORE_LOCAL_MACHINE_GROUP_POLICY</c> system store, relocated onto the hive root that
/// <c>IGPEInformation::GetRegistryKey</c> returned.
/// </summary>
/// <remarks>
/// Relocation makes CryptoAPI itself own the store layout under
/// <c>&lt;root&gt;\Software\Policies\Microsoft\SystemCertificates\&lt;store&gt;</c> (the Certificates/CRLs/CTLs
/// keys and each certificate's serialized <c>Blob</c>), so certificates are added and removed through the
/// certificate store API rather than by writing registry values. The hive root must stay open for the
/// lifetime of this object. <c>CertOpenStore</c>, <c>CertControlStore</c> and
/// <c>CertGetCertificateContextProperty</c> come from CsWin32; the relocation flags are not part of the
/// generated <see cref="CERT_OPEN_STORE_FLAGS"/> enum and are declared here.
/// </remarks>
internal sealed unsafe partial class GroupPolicyCertificateStore : IDisposable
{
    // CERT_STORE_PROV_SYSTEM_W is the integer provider id 10 passed in place of an LPCSTR.
    private const nint CertStoreProvSystemW = 10;
    private const uint CertSystemStoreRelocateFlag = 0x80000000;

    // CERT_SYSTEM_STORE_LOCAL_MACHINE_GROUP_POLICY_ID (8) << CERT_SYSTEM_STORE_LOCATION_SHIFT (16).
    private const uint CertSystemStoreLocalMachineGroupPolicy = 0x00080000;
    private const uint CertStoreCtrlCommit = 3;
    private const uint CertKeyIdentifierPropId = 20;
    private const int ErrorFileNotFound = 2;

    private readonly HCERTSTORE _handle;
    private readonly X509Store _store;
    private bool _disposed;

    private GroupPolicyCertificateStore(HCERTSTORE handle)
    {
        _handle = handle;

        // X509Store duplicates the handle, so this object still closes its own reference.
        _store = new X509Store((nint)handle.Value);
    }

    /// <summary>
    /// Opens <paramref name="storeName"/> relocated onto <paramref name="hiveRoot"/>.
    /// </summary>
    /// <param name="hiveRoot">The GPO machine registry buffer, or <c>HKEY_LOCAL_MACHINE</c>.</param>
    /// <param name="storeName">The system store name, for example <c>EFS</c>.</param>
    /// <param name="writable">True to open read-write, creating the store when it does not exist yet.</param>
    /// <returns>The store, or <see langword="null"/> when a read-only open finds no such store.</returns>
    /// <exception cref="Win32Exception">The store could not be opened.</exception>
    public static GroupPolicyCertificateStore? Open(RegistryKey hiveRoot, string storeName, bool writable)
    {
        ArgumentNullException.ThrowIfNull(hiveRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        uint flags = CertSystemStoreRelocateFlag | CertSystemStoreLocalMachineGroupPolicy;
        flags |= writable
            ? (uint)CERT_OPEN_STORE_FLAGS.CERT_STORE_MAXIMUM_ALLOWED_FLAG
            : (uint)(CERT_OPEN_STORE_FLAGS.CERT_STORE_READONLY_FLAG | CERT_OPEN_STORE_FLAGS.CERT_STORE_OPEN_EXISTING_FLAG);

        bool addedRef = false;
        try
        {
            hiveRoot.Handle.DangerousAddRef(ref addedRef);
            fixed (char* storeNamePointer = storeName)
            {
                CERT_SYSTEM_STORE_RELOCATE_PARA relocate = default;
                relocate.hKeyBase = (Windows.Win32.System.Registry.HKEY)hiveRoot.Handle.DangerousGetHandle();
                relocate.pwszSystemStore = storeNamePointer;

                HCERTSTORE handle = Win32PInvoke.CertOpenStore(
                    new PCSTR((byte*)CertStoreProvSystemW),
                    default,
                    default,
                    (CERT_OPEN_STORE_FLAGS)flags,
                    &relocate);
                if (handle.IsNull)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (!writable && error == ErrorFileNotFound)
                    {
                        return null;
                    }

                    throw new Win32Exception(error);
                }

                return new GroupPolicyCertificateStore(handle);
            }
        }
        finally
        {
            if (addedRef)
            {
                hiveRoot.Handle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the certificates in the store. The caller owns and disposes them.
    /// </summary>
    public X509Certificate2Collection GetCertificates()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _store.Certificates;
    }

    /// <summary>Adds <paramref name="certificate"/>, replacing an existing copy.</summary>
    public void Add(X509Certificate2 certificate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _store.Add(certificate);
    }

    /// <summary>
    /// Removes the certificate whose SHA-1 thumbprint is <paramref name="thumbprint"/>.
    /// </summary>
    /// <returns><see langword="true"/> when a certificate was removed.</returns>
    public bool Remove(string thumbprint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        X509Certificate2Collection certificates = _store.Certificates;
        try
        {
            foreach (X509Certificate2 certificate in certificates)
            {
                if (string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    _store.Remove(certificate);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            foreach (X509Certificate2 certificate in certificates)
            {
                certificate.Dispose();
            }
        }
    }

    /// <summary>
    /// Commits pending changes (<c>CERT_STORE_CTRL_COMMIT</c>), as certmgr does before notifying Group Policy.
    /// </summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Win32PInvoke.CertControlStore(_handle, default, CertStoreCtrlCommit, null))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>
    /// Reads <c>CERT_KEY_IDENTIFIER_PROP_ID</c>: the subject key identifier extension when present,
    /// otherwise the identifier CryptoAPI derives from the public key.
    /// </summary>
    public static byte[]? GetKeyIdentifier(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        CERT_CONTEXT* context = (CERT_CONTEXT*)certificate.Handle;
        uint size = 0;
        if (!Win32PInvoke.CertGetCertificateContextProperty(context, CertKeyIdentifierPropId, null, &size) || size == 0)
        {
            return null;
        }

        byte[] keyIdentifier = new byte[size];
        fixed (byte* buffer = keyIdentifier)
        {
            return Win32PInvoke.CertGetCertificateContextProperty(context, CertKeyIdentifierPropId, buffer, &size)
                ? keyIdentifier[..(int)size]
                : null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Dispose();
        Win32PInvoke.CertCloseStore(_handle, 0);
    }
}
