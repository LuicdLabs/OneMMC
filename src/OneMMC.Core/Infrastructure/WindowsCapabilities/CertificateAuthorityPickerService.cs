using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Shows the native Windows certificate authority picker and returns the selected CA distinguished name.
/// </summary>
public sealed class CertificateAuthorityPickerService
{
    private readonly ILogger<CertificateAuthorityPickerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateAuthorityPickerService"/> class.
    /// </summary>
    public CertificateAuthorityPickerService()
        : this(NullLogger<CertificateAuthorityPickerService>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateAuthorityPickerService"/> class.
    /// </summary>
    /// <param name="logger">The logger used for picker diagnostics.</param>
    public CertificateAuthorityPickerService(ILogger<CertificateAuthorityPickerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens the native certificate picker for the certificate authority store.
    /// </summary>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <param name="storeLocation">The certificate store location to inspect.</param>
    /// <param name="storeName">The certificate store name to inspect.</param>
    /// <returns>The selected certificate subject, or <see langword="null"/> when no certificate is selected.</returns>
    public unsafe string? PickCaDistinguishedName(IntPtr ownerWindowHandle, StoreLocation storeLocation, StoreName storeName)
    {
        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        if (store.Certificates.Count == 0)
        {
            return null;
        }

        CERT_CONTEXT* selectedContext = Win32PInvoke.CryptUIDlgSelectCertificateFromStore(
            (HCERTSTORE)store.StoreHandle,
            new HWND(ownerWindowHandle),
            "Select a certification authority",
            "Choose the certification authority to use for this authentication method.",
            0,
            0,
            null);

        if (selectedContext is null)
        {
            return null;
        }

        try
        {
            using var selected = new X509Certificate2((IntPtr)selectedContext);
            _logger.LogInformation("Selected certification authority {Subject}.", selected.Subject);
            return selected.Subject;
        }
        finally
        {
            Win32PInvoke.CertFreeCertificateContext(selectedContext);
        }
    }
}
