using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ManagementTools.Core.Features.Certificates.Interop;
using ManagementTools.Core.Features.Certificates.Models;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.Certificates.Services;

/// <summary>
/// Enumerates logical certificate stores and resolves store entries for the certificate pages.
/// </summary>
public sealed unsafe class CertificateStoreService
{
    private const uint CertSystemStoreCurrentUser = 0x00010000;
    private const uint CertSystemStoreLocalMachine = 0x00020000;

    private readonly ILogger<CertificateStoreService> _logger;
    private readonly string _sectionCertificates;
    private readonly string _sectionCrls;
    private readonly string _sectionCtls;
    private readonly string _emptyCertificates;
    private readonly string _emptyCrls;
    private readonly string _emptyCtls;
    private readonly string _certificateIssuerFormat;
    private readonly string _certificateValidityFormat;
    private readonly string _contextValidityFormat;
    private readonly string _certificateSummaryFormat;
    private readonly string _crlSummaryFormat;
    private readonly string _ctlSummaryFormat;
    private readonly string _itemHashFormat;
    private readonly string _notAvailable;
    private readonly string _operationFailedMessage;
    private static readonly Crypt32CertificateNativeMethods.CertEnumSystemStoreCallback _systemStoreCallback = OnSystemStoreEnumerated;

    private static readonly IReadOnlyDictionary<string, string> KnownStoreDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MY"] = "Personal",
            ["CA"] = "Intermediate Certification Authorities",
            ["Root"] = "Trusted Root Certification Authorities",
            ["ROOT"] = "Trusted Root Certification Authorities",
            ["trust"] = "Enterprise Trust",
            ["AuthRoot"] = "Third-Party Root Certification Authorities",
            ["TrustedPublisher"] = "Trusted Publishers",
            ["Disallowed"] = "Untrusted Certificates",
            ["TrustedPeople"] = "Trusted People",
            ["UserDS"] = "Active Directory User Object",
            ["ClientAuthIssuer"] = "Client Authentication Issuers",
            ["SmartCardRoot"] = "Smart Card Trusted Roots",
            ["FlightRoot"] = "Preview Build Roots",
            ["TestSignRoot"] = "Test Roots",
            ["TrustedAppRoot"] = "Trusted Packaged App Installation Authorities",
            ["OemEsim"] = "OEM eSIM Certification Authorities",
            ["PasspointTrustedRoots"] = "Passpoint Trusted Roots"
        };

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateStoreService"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    public CertificateStoreService(ILogger<CertificateStoreService> logger)
    {
        _logger = logger;

        var localization = LocalizationProvider.Current;
        _sectionCertificates = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.SectionCertificates);
        _sectionCrls = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.SectionCRLs);
        _sectionCtls = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.SectionCTLs);
        _emptyCertificates = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.EmptyCertificates);
        _emptyCrls = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.EmptyCRLs);
        _emptyCtls = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.EmptyCTLs);
        _certificateIssuerFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.CertificateIssuerFormat);
        _certificateValidityFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.CertificateValidityFormat);
        _contextValidityFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.ContextValidityFormat);
        _certificateSummaryFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.CertificateSummaryFormat);
        _crlSummaryFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.CrlSummaryFormat);
        _ctlSummaryFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.CtlSummaryFormat);
        _itemHashFormat = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.ItemHashFormat);
        _notAvailable = localization.GetString(ResourceFileNames.Certificates, CertificateKeys.NotAvailable);
        _operationFailedMessage = localization.GetString(ResourceFileNames.Common, CommonKeys.OperationFailed);
    }

    /// <summary>
    /// Enumerates all logical stores for the requested location.
    /// </summary>
    /// <param name="storeLocation">The store location to enumerate.</param>
    /// <returns>The logical stores shown by the UI.</returns>
    public Task<IReadOnlyList<CertificateStoreNode>> GetStoresAsync(StoreLocation storeLocation) =>
        Task.Run(() => (IReadOnlyList<CertificateStoreNode>)LoadStores(storeLocation));

    /// <summary>
    /// Reloads a single logical store.
    /// </summary>
    /// <param name="storeLocation">The store location.</param>
    /// <param name="storeName">The store name.</param>
    /// <returns>The refreshed logical store.</returns>
    public Task<CertificateStoreNode?> GetStoreAsync(StoreLocation storeLocation, string storeName) =>
        Task.Run<CertificateStoreNode?>(() => LoadStore(storeLocation, storeName));

    /// <summary>
    /// Deletes the provided store entry from its logical store.
    /// </summary>
    /// <param name="entry">The entry to delete.</param>
    public Task DeleteEntryAsync(CertificateEntry entry) =>
        Task.Run(() => DeleteEntry(entry));

    /// <summary>
    /// Opens an <see cref="X509Store"/> for the given store location and store name.
    /// </summary>
    /// <param name="storeLocation">The store location.</param>
    /// <param name="storeName">The store name.</param>
    /// <param name="writable">Whether the store should be opened for write operations.</param>
    /// <returns>An opened <see cref="X509Store"/> instance.</returns>
    internal X509Store OpenStore(StoreLocation storeLocation, string storeName, bool writable)
    {
        var store = new X509Store(storeName, storeLocation);
        var flags = OpenFlags.OpenExistingOnly | (writable ? OpenFlags.ReadWrite : OpenFlags.ReadOnly);
        store.Open(flags);
        return store;
    }

    /// <summary>
    /// Resolves and duplicates a certificate context for the provided UI entry.
    /// </summary>
    /// <param name="entry">The certificate entry to resolve.</param>
    /// <returns>A duplicated native certificate context. The caller must free it.</returns>
    internal CERT_CONTEXT* DuplicateCertificateContext(CertificateEntry entry)
    {
        using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: false);
        return DuplicateCertificateContext(entry, store);
    }

    /// <summary>
    /// Resolves and duplicates a certificate context while the caller retains the source store lifetime.
    /// </summary>
    /// <param name="entry">The certificate entry to resolve.</param>
    /// <param name="store">The opened source store to use for resolving the context.</param>
    /// <returns>A duplicated native certificate context. The caller must free it.</returns>
    internal CERT_CONTEXT* DuplicateCertificateContext(CertificateEntry entry, X509Store store)
    {
        var handle = (HCERTSTORE)store.StoreHandle;
        CERT_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCertificatesInStore(handle, current)) is not null)
        {
            using var certificate = new X509Certificate2((nint)current);
            if (string.Equals(NormalizeIdentifier(certificate.Thumbprint), entry.Identifier, StringComparison.Ordinal))
            {
                CERT_CONTEXT* duplicate = Win32PInvoke.CertDuplicateCertificateContext(current);
                if (duplicate is null)
                {
                    ThrowLastWin32Error(_operationFailedMessage);
                }

                return duplicate;
            }
        }

        throw new InvalidOperationException(_operationFailedMessage);
    }

    /// <summary>
    /// Resolves and duplicates a CRL context for the provided UI entry.
    /// </summary>
    /// <param name="entry">The CRL entry to resolve.</param>
    /// <returns>A duplicated native CRL context. The caller must free it.</returns>
    internal CRL_CONTEXT* DuplicateCrlContext(CertificateEntry entry)
    {
        using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: false);
        return DuplicateCrlContext(entry, store);
    }

    /// <summary>
    /// Resolves and duplicates a CRL context while the caller retains the source store lifetime.
    /// </summary>
    /// <param name="entry">The CRL entry to resolve.</param>
    /// <param name="store">The opened source store to use for resolving the context.</param>
    /// <returns>A duplicated native CRL context. The caller must free it.</returns>
    internal CRL_CONTEXT* DuplicateCrlContext(CertificateEntry entry, X509Store store)
    {
        var handle = (HCERTSTORE)store.StoreHandle;
        CRL_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCRLsInStore(handle, current)) is not null)
        {
            string identifier = ComputeSha1Hex(GetBytes(current->pbCrlEncoded, checked((int)current->cbCrlEncoded)));
            if (string.Equals(identifier, entry.Identifier, StringComparison.Ordinal))
            {
                CRL_CONTEXT* duplicate = Win32PInvoke.CertDuplicateCRLContext(current);
                if (duplicate is null)
                {
                    ThrowLastWin32Error(_operationFailedMessage);
                }

                return duplicate;
            }
        }

        throw new InvalidOperationException(_operationFailedMessage);
    }

    /// <summary>
    /// Resolves and duplicates a CTL context for the provided UI entry.
    /// </summary>
    /// <param name="entry">The CTL entry to resolve.</param>
    /// <returns>A duplicated native CTL context. The caller must free it.</returns>
    internal CTL_CONTEXT* DuplicateCtlContext(CertificateEntry entry)
    {
        using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: false);
        return DuplicateCtlContext(entry, store);
    }

    /// <summary>
    /// Resolves and duplicates a CTL context while the caller retains the source store lifetime.
    /// </summary>
    /// <param name="entry">The CTL entry to resolve.</param>
    /// <param name="store">The opened source store to use for resolving the context.</param>
    /// <returns>A duplicated native CTL context. The caller must free it.</returns>
    internal CTL_CONTEXT* DuplicateCtlContext(CertificateEntry entry, X509Store store)
    {
        var handle = (HCERTSTORE)store.StoreHandle;
        CTL_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCTLsInStore(handle, current)) is not null)
        {
            string identifier = ComputeSha1Hex(GetBytes(current->pbCtlEncoded, checked((int)current->cbCtlEncoded)));
            if (string.Equals(identifier, entry.Identifier, StringComparison.Ordinal))
            {
                CTL_CONTEXT* duplicate = Win32PInvoke.CertDuplicateCTLContext(current);
                if (duplicate is null)
                {
                    ThrowLastWin32Error(_operationFailedMessage);
                }

                return duplicate;
            }
        }

        throw new InvalidOperationException(_operationFailedMessage);
    }

    private List<CertificateStoreNode> LoadStores(StoreLocation storeLocation)
    {
        var nodes = new List<CertificateStoreNode>();

        foreach (string storeName in EnumerateSystemStoreNames(storeLocation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(storeName => GetStoreDisplayOrder(storeLocation, storeName))
            .ThenBy(GetStoreDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            nodes.Add(LoadStore(storeLocation, storeName));
        }

        return nodes;
    }

    private CertificateStoreNode LoadStore(StoreLocation storeLocation, string storeName)
    {
        try
        {
            using var store = OpenStore(storeLocation, storeName, writable: false);
            var handle = (HCERTSTORE)store.StoreHandle;

            IReadOnlyList<CertificateSection> sections =
            [
                new CertificateSection(
                    CertificateEntryKind.Certificate,
                    _sectionCertificates,
                    _emptyCertificates,
                    EnumerateCertificates(storeLocation, storeName, handle)),
                new CertificateSection(
                    CertificateEntryKind.CertificateRevocationList,
                    _sectionCrls,
                    _emptyCrls,
                    EnumerateCrls(storeLocation, storeName, handle)),
                new CertificateSection(
                    CertificateEntryKind.CertificateTrustList,
                    _sectionCtls,
                    _emptyCtls,
                    EnumerateCtls(storeLocation, storeName, handle))
            ];

            return new CertificateStoreNode(storeLocation, storeName, GetStoreDisplayName(storeName), sections);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate certificate store {StoreName} ({StoreLocation}).", storeName, storeLocation);

            return new CertificateStoreNode(
                storeLocation,
                storeName,
                GetStoreDisplayName(storeName),
                [
                    new CertificateSection(CertificateEntryKind.Certificate, _sectionCertificates, _emptyCertificates, []),
                    new CertificateSection(CertificateEntryKind.CertificateRevocationList, _sectionCrls, _emptyCrls, []),
                    new CertificateSection(CertificateEntryKind.CertificateTrustList, _sectionCtls, _emptyCtls, [])
                ]);
        }
    }

    private List<CertificateEntry> EnumerateCertificates(StoreLocation storeLocation, string storeName, HCERTSTORE storeHandle)
    {
        var entries = new List<CertificateEntry>();
        CERT_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCertificatesInStore(storeHandle, current)) is not null)
        {
            using var certificate = new X509Certificate2((nint)current);

            string friendlyName = certificate.FriendlyName;
            string subjectName = GetPreferredSubjectName(certificate);
            string issuerName = GetPreferredIssuerName(certificate);
            string displayName = string.IsNullOrWhiteSpace(friendlyName) ? subjectName : friendlyName;
            string identifier = NormalizeIdentifier(certificate.Thumbprint)
                ?? ComputeSha1Hex(GetBytes(current->pbCertEncoded, checked((int)current->cbCertEncoded)));
            string secondaryText = string.Format(
                CultureInfo.CurrentCulture,
                _certificateSummaryFormat,
                issuerName,
                FormatDate(certificate.NotAfter));
            string tertiaryText = string.Join(
                '\n',
                string.Format(CultureInfo.CurrentCulture, _certificateIssuerFormat, issuerName),
                string.Format(
                CultureInfo.CurrentCulture,
                _certificateValidityFormat,
                FormatDate(certificate.NotBefore),
                FormatDate(certificate.NotAfter)));

            entries.Add(new CertificateEntry(
                storeLocation,
                storeName,
                CertificateEntryKind.Certificate,
                identifier,
                displayName,
                secondaryText,
                tertiaryText,
                string.Join(
                    '\n',
                    displayName,
                    subjectName,
                    friendlyName,
                    issuerName,
                    certificate.Thumbprint,
                    certificate.Subject,
                    certificate.Issuer)));
        }

        return entries;
    }

    private List<CertificateEntry> EnumerateCrls(StoreLocation storeLocation, string storeName, HCERTSTORE storeHandle)
    {
        var entries = new List<CertificateEntry>();
        CRL_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCRLsInStore(storeHandle, current)) is not null)
        {
            string identifier = ComputeSha1Hex(GetBytes(current->pbCrlEncoded, checked((int)current->cbCrlEncoded)));
            string hashText = string.Format(CultureInfo.CurrentCulture, _itemHashFormat, ShortenIdentifier(identifier));
            NativeCrlInfo? crlInfo = TryReadCrlInfo(current);
            string issuer = crlInfo.HasValue
                ? GetDisplayNameFromNameBlob(crlInfo.Value.Issuer)
                : _notAvailable;
            string secondaryText = string.Format(
                CultureInfo.CurrentCulture,
                _crlSummaryFormat,
                FormatDate(ToDateTime(crlInfo?.ThisUpdate ?? default)),
                FormatDate(ToDateTime(crlInfo?.NextUpdate ?? default)));
            string tertiaryText = string.Join(
                '\n',
                hashText,
                string.Format(
                    CultureInfo.CurrentCulture,
                    _contextValidityFormat,
                    FormatDate(ToDateTime(crlInfo?.ThisUpdate ?? default)),
                    FormatDate(ToDateTime(crlInfo?.NextUpdate ?? default))));

            entries.Add(new CertificateEntry(
                storeLocation,
                storeName,
                CertificateEntryKind.CertificateRevocationList,
                identifier,
                issuer,
                secondaryText,
                tertiaryText,
                string.Join('\n', issuer, secondaryText, tertiaryText, identifier)));
        }

        return entries;
    }

    private List<CertificateEntry> EnumerateCtls(StoreLocation storeLocation, string storeName, HCERTSTORE storeHandle)
    {
        var entries = new List<CertificateEntry>();
        CTL_CONTEXT* current = null;

        while ((current = Win32PInvoke.CertEnumCTLsInStore(storeHandle, current)) is not null)
        {
            string identifier = ComputeSha1Hex(GetBytes(current->pbCtlEncoded, checked((int)current->cbCtlEncoded)));
            string hashText = string.Format(CultureInfo.CurrentCulture, _itemHashFormat, ShortenIdentifier(identifier));
            NativeCtlInfo? ctlInfo = TryReadCtlInfo(current);
            string displayName = ctlInfo.HasValue
                ? TryDecodeTextBlob(ctlInfo.Value.ListIdentifier) ?? _notAvailable
                : _notAvailable;
            string secondaryText = string.Format(
                CultureInfo.CurrentCulture,
                _ctlSummaryFormat,
                FormatDate(ToDateTime(ctlInfo?.ThisUpdate ?? default)));
            string tertiaryText = string.Join(
                '\n',
                hashText,
                string.Format(
                    CultureInfo.CurrentCulture,
                    _contextValidityFormat,
                    FormatDate(ToDateTime(ctlInfo?.ThisUpdate ?? default)),
                    FormatDate(ToDateTime(ctlInfo?.NextUpdate ?? default))));

            entries.Add(new CertificateEntry(
                storeLocation,
                storeName,
                CertificateEntryKind.CertificateTrustList,
                identifier,
                displayName,
                secondaryText,
                tertiaryText,
                string.Join('\n', displayName, secondaryText, tertiaryText, identifier)));
        }

        return entries;
    }

    private void DeleteEntry(CertificateEntry entry)
    {
        switch (entry.Kind)
        {
            case CertificateEntryKind.Certificate:
            {
                using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: true);
                CERT_CONTEXT* certificateContext = DuplicateCertificateContext(entry, store);
                if (!Win32PInvoke.CertDeleteCertificateFromStore(certificateContext))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, _operationFailedMessage);
                }

                break;
            }

            case CertificateEntryKind.CertificateRevocationList:
            {
                using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: true);
                CRL_CONTEXT* crlContext = DuplicateCrlContext(entry, store);
                if (!Win32PInvoke.CertDeleteCRLFromStore(crlContext))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, _operationFailedMessage);
                }

                break;
            }

            case CertificateEntryKind.CertificateTrustList:
            {
                using var store = OpenStore(entry.StoreLocation, entry.StoreName, writable: true);
                CTL_CONTEXT* ctlContext = DuplicateCtlContext(entry, store);
                if (!Win32PInvoke.CertDeleteCTLFromStore(ctlContext))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, _operationFailedMessage);
                }

                break;
            }
        }
    }

    private List<string> EnumerateSystemStoreNames(StoreLocation storeLocation)
    {
        var storeNames = new List<string>();
        GCHandle listHandle = GCHandle.Alloc(storeNames);

        try
        {
            void* pvArg = (void*)GCHandle.ToIntPtr(listHandle);
            if (!Crypt32CertificateNativeMethods.CertEnumSystemStore(
                GetStoreLocationFlag(storeLocation),
                null,
                pvArg,
                _systemStoreCallback))
            {
                ThrowLastWin32Error(_operationFailedMessage);
            }
        }
        finally
        {
            listHandle.Free();
        }

        return storeNames;
    }

    private static bool OnSystemStoreEnumerated(
        char* pvSystemStore,
        uint dwFlags,
        void* pStoreInfo,
        void* pvReserved,
        void* pvArg)
    {
        if (pvArg is null || pvSystemStore is null)
        {
            return true;
        }

        if (GCHandle.FromIntPtr((nint)pvArg).Target is not List<string> storeNames)
        {
            return false;
        }

        string? storeName = Marshal.PtrToStringUni((nint)pvSystemStore);
        if (!string.IsNullOrWhiteSpace(storeName))
        {
            storeNames.Add(storeName);
        }

        return true;
    }

    private static uint GetStoreLocationFlag(StoreLocation storeLocation) =>
        storeLocation == StoreLocation.LocalMachine ? CertSystemStoreLocalMachine : CertSystemStoreCurrentUser;

    private string GetStoreDisplayName(string storeName) =>
        KnownStoreDisplayNames.TryGetValue(storeName, out string? displayName) ? displayName : storeName;

    private static int GetStoreDisplayOrder(StoreLocation storeLocation, string storeName)
    {
        if (storeLocation == StoreLocation.CurrentUser)
        {
            return storeName.ToUpperInvariant() switch
            {
                "MY" => 0,
                "CA" => 1,
                "DISALLOWED" => 2,
                _ => 100
            };
        }

        return storeName.ToUpperInvariant() switch
        {
            "ROOT" => 0,
            "CA" => 1,
            "DISALLOWED" => 2,
            _ => 100
        };
    }

    private static string GetPreferredSubjectName(X509Certificate2 certificate)
    {
        string simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            return simpleName;
        }

        return string.IsNullOrWhiteSpace(certificate.Subject)
            ? certificate.Thumbprint
            : certificate.Subject;
    }

    private static string GetPreferredIssuerName(X509Certificate2 certificate)
    {
        string simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            return simpleName;
        }

        return string.IsNullOrWhiteSpace(certificate.Issuer)
            ? certificate.Thumbprint
            : certificate.Issuer;
    }

    private string GetDisplayNameFromNameBlob(NativeDataBlob nameBlob)
    {
        if (nameBlob.pbData is null || nameBlob.cbData == 0)
        {
            return _notAvailable;
        }

        try
        {
            byte[] encodedName = GetBytes(nameBlob.pbData, checked((int)nameBlob.cbData));
            string distinguishedName = new X500DistinguishedName(encodedName).Name;
            return SimplifyDistinguishedName(distinguishedName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to decode X.500 name blob.");
            return _notAvailable;
        }
    }

    private static string SimplifyDistinguishedName(string distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return distinguishedName;
        }

        foreach (string part in distinguishedName.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 3)
            {
                return trimmed[3..];
            }
        }

        return distinguishedName;
    }

    private static string? TryDecodeTextBlob(NativeDataBlob blob)
    {
        if (blob.pbData is null || blob.cbData == 0)
        {
            return null;
        }

        byte[] bytes = GetBytes(blob.pbData, checked((int)blob.cbData));

        foreach (EncodingCandidate encodingCandidate in Enum.GetValues<EncodingCandidate>())
        {
            string? decoded = TryDecode(bytes, encodingCandidate);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        return null;
    }

    private static string? TryDecode(byte[] bytes, EncodingCandidate encodingCandidate)
    {
        try
        {
            string decoded = encodingCandidate switch
            {
                EncodingCandidate.Unicode => System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n'),
                EncodingCandidate.Utf8 => System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n'),
                _ => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n')
            };

            return decoded.All(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t')
                ? decoded
                : null;
        }
        catch
        {
            return null;
        }
    }

    private string FormatDate(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.LocalDateTime.ToString("d", CultureInfo.CurrentCulture)
            : _notAvailable;

    private static DateTimeOffset? ToDateTime(NativeFileTime fileTime)
    {
        ulong rawValue = ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        if (rawValue == 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(unchecked((long)rawValue));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] GetBytes(byte* source, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy((nint)source, bytes, 0, length);
        return bytes;
    }

    private static string ComputeSha1Hex(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string ShortenIdentifier(string identifier) =>
        identifier.Length <= 16 ? identifier : identifier[..16];

    private static void ThrowLastWin32Error(string message) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);

    private static NativeCrlInfo? TryReadCrlInfo(CRL_CONTEXT* context)
    {
        if (context is null || context->pCrlInfo is null)
        {
            return null;
        }

        return Marshal.PtrToStructure<NativeCrlInfo>((nint)context->pCrlInfo);
    }

    private static NativeCtlInfo? TryReadCtlInfo(CTL_CONTEXT* context)
    {
        if (context is null || context->pCtlInfo is null)
        {
            return null;
        }

        return Marshal.PtrToStructure<NativeCtlInfo>((nint)context->pCtlInfo);
    }

    private enum EncodingCandidate
    {
        Unicode,
        Utf8,
        Ascii
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeDataBlob
    {
        public readonly uint cbData;
        public readonly byte* pbData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeAlgorithmIdentifier
    {
        public readonly nint pszObjId;
        public readonly NativeDataBlob Parameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeCtlUsage
    {
        public readonly uint cUsageIdentifier;
        public readonly nint rgpszUsageIdentifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeFileTime
    {
        public readonly uint dwLowDateTime;
        public readonly uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeCrlInfo
    {
        public readonly uint dwVersion;
        public readonly NativeAlgorithmIdentifier SignatureAlgorithm;
        public readonly NativeDataBlob Issuer;
        public readonly NativeFileTime ThisUpdate;
        public readonly NativeFileTime NextUpdate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeCtlInfo
    {
        public readonly uint dwVersion;
        public readonly NativeCtlUsage SubjectUsage;
        public readonly NativeDataBlob ListIdentifier;
        public readonly NativeDataBlob SequenceNumber;
        public readonly NativeFileTime ThisUpdate;
        public readonly NativeFileTime NextUpdate;
    }
}
