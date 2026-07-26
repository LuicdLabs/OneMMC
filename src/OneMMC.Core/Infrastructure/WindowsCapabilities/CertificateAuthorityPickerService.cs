using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Identifies which local-machine certificate authority store the picker browses,
/// mirroring the "Certificate store type" choice in the native Windows Firewall dialogs.
/// </summary>
public enum CertificateAuthorityStoreKind
{
    /// <summary>The local computer's Trusted Root Certification Authorities store.</summary>
    RootCA,

    /// <summary>The local computer's Intermediate Certification Authorities store.</summary>
    IntermediateCA
}

/// <summary>
/// Localized display strings for the certificate authority picker dialog.
/// </summary>
/// <param name="Title">The localized dialog title.</param>
/// <param name="Prompt">The localized selection prompt shown inside the dialog.</param>
public sealed record CertificateAuthorityPickerStrings(string Title, string Prompt);

/// <summary>
/// The certificate authority selected by the user in the native picker dialog.
/// </summary>
public sealed record CertificateAuthorityPickResult
{
    /// <summary>
    /// The CA subject as a forward-order (root-first) CERT_X500_NAME_STR string,
    /// e.g. <c>C=US, O=Contoso, CN=Contoso Root CA</c>. This is the format the Windows
    /// Firewall IPsec stack re-encodes with CertStrToName default flags, so the string's
    /// left-to-right RDN order must match the certificate's encoded subject order.
    /// </summary>
    public required string DistinguishedName { get; init; }

    /// <summary>The store the certificate was selected from.</summary>
    public required CertificateAuthorityStoreKind StoreKind { get; init; }

    /// <summary>The SHA-1 thumbprint of the selected certificate (uppercase hex).</summary>
    public required string Thumbprint { get; init; }

    /// <summary>A short display name for the selected certificate (simple subject name).</summary>
    public required string FriendlyDisplayName { get; init; }

    /// <summary>
    /// Whether <see cref="DistinguishedName"/> re-encodes byte-identically to the certificate's
    /// actual encoded subject. <see langword="false"/> only for certificates whose subject uses
    /// UTF8String encoding for printable-charset values — a re-encoding limitation shared with
    /// the native Windows Firewall snap-in.
    /// </summary>
    public required bool SubjectRoundTripsExactly { get; init; }
}

/// <summary>
/// Shows the native Windows certificate picker over the local computer's certification
/// authority stores and returns the selected CA in the format accepted by the Windows
/// Firewall IPsec configuration surfaces (WMI <c>MSFT_NetIKECertAuthProposal.TrustedCA</c>).
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
    /// Opens the native certificate picker over the requested local-machine CA store and
    /// returns the selected certification authority.
    /// </summary>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <param name="storeKind">Which local-machine certification authority store to browse.</param>
    /// <param name="strings">Localized title and prompt for the picker dialog.</param>
    /// <returns>The selected certification authority, or <see langword="null"/> when the user cancels.</returns>
    public unsafe CertificateAuthorityPickResult? PickCertificateAuthority(
        IntPtr ownerWindowHandle,
        CertificateAuthorityStoreKind storeKind,
        CertificateAuthorityPickerStrings strings)
    {
        // WF.msc parity: Root CA / Intermediate CA always map to the LOCAL COMPUTER stores
        // (the IPsec trust list is machine policy; IKEEXT evaluates it as SYSTEM).
        StoreName storeName = storeKind == CertificateAuthorityStoreKind.IntermediateCA
            ? StoreName.CertificateAuthority
            : StoreName.Root;

        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        CERT_CONTEXT* selectedContext = Win32PInvoke.CryptUIDlgSelectCertificateFromStore(
            (HCERTSTORE)store.StoreHandle,
            new HWND(ownerWindowHandle),
            strings.Title,
            strings.Prompt,
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

            // The firewall service re-encodes TrustedCA with CertStrToName default flags
            // (no REVERSE), so the string must be in forward (root-first) RDN order —
            // never X509Certificate2.Subject, which renders CN-first per RFC 4514.
            string distinguishedName = selected.SubjectName.Decode(X500DistinguishedNameFlags.UseCommas);

            bool roundTripsExactly;
            try
            {
                byte[] reEncoded = new X500DistinguishedName(distinguishedName, X500DistinguishedNameFlags.UseCommas).RawData;
                roundTripsExactly = reEncoded.AsSpan().SequenceEqual(selected.SubjectName.RawData);
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Decoded certification authority name {DistinguishedName} could not be re-encoded.", distinguishedName);
                return null;
            }

            if (!roundTripsExactly)
            {
                // Typically a subject whose printable-charset values were originally encoded as
                // UTF8String; CertStrToName re-encodes them as PrintableString. The native
                // firewall snap-in has the identical limitation, so the name is still returned.
                _logger.LogWarning(
                    "Certification authority name {DistinguishedName} does not re-encode byte-identically to the certificate subject.",
                    distinguishedName);
            }

            _logger.LogInformation("Selected certification authority {DistinguishedName}.", distinguishedName);
            return new CertificateAuthorityPickResult
            {
                DistinguishedName = distinguishedName,
                StoreKind = storeKind,
                Thumbprint = selected.Thumbprint,
                FriendlyDisplayName = selected.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                SubjectRoundTripsExactly = roundTripsExactly
            };
        }
        finally
        {
            Win32PInvoke.CertFreeCertificateContext(selectedContext);
        }
    }
}
