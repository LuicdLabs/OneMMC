using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using ManagementTools.Core.Features.Certificates.Interop;
using ManagementTools.Core.Features.Certificates.Models;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;
using Windows.Win32.Security.Cryptography;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.Certificates.Services;

/// <summary>
/// Launches native Windows CryptoUI dialogs for certificate store items and store-level wizards.
/// </summary>
public sealed unsafe class CertificateNativeUiService
{
    private const uint CertStoreCertificateContext = 1;
    private const uint CertStoreCrlContext = 2;
    private const uint CertStoreCtlContext = 3;

    private const uint CryptUiDisableEditProperties = 0x00000004;

    private const uint CryptUiWizImportNoChangeDestStore = 0x00010000;
    private const uint CryptUiWizImportAllowCert = 0x00020000;
    private const uint CryptUiWizImportAllowCrl = 0x00040000;
    private const uint CryptUiWizImportAllowCtl = 0x00080000;
    private const uint CryptUiWizImportToLocalMachine = 0x00100000;
    private const uint CryptUiWizImportToCurrentUser = 0x00200000;

    private const uint CryptUiWizExportCertContext = 1;
    private const uint CryptUiWizExportCtlContext = 2;
    private const uint CryptUiWizExportCrlContext = 3;
    private const uint CryptUiWizExportCertStore = 4;

    private const int ErrorCancelled = 1223;

    private readonly CertificateStoreService _certificateStoreService;
    private readonly ILogger<CertificateNativeUiService> _logger;
    private readonly string _propertiesTitleFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateNativeUiService"/> class.
    /// </summary>
    /// <param name="certificateStoreService">The certificate store resolver.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public CertificateNativeUiService(
        CertificateStoreService certificateStoreService,
        ILogger<CertificateNativeUiService> logger)
    {
        _certificateStoreService = certificateStoreService;
        _logger = logger;
        _propertiesTitleFormat = LocalizationProvider.Current.GetString(
            ResourceFileNames.Certificates,
            CertificateKeys.PropertiesTitleFormat);
    }

    /// <summary>
    /// Opens a native viewer or properties window for the specified entry.
    /// </summary>
    /// <param name="entry">The entry to display.</param>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <param name="openProperties">Whether the properties command was invoked.</param>
    /// <param name="allowEditProperties">Whether editing should be enabled for certificate properties.</param>
    /// <returns><see langword="true"/> when the entry changed and the UI should refresh.</returns>
    public bool OpenEntry(CertificateEntry entry, nint ownerWindowHandle, bool openProperties, bool allowEditProperties)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind switch
        {
            CertificateEntryKind.Certificate => openProperties
                ? OpenCertificateProperties(entry, ownerWindowHandle, allowEditProperties)
                : OpenCertificate(entry, ownerWindowHandle),
            CertificateEntryKind.CertificateRevocationList => OpenCrl(entry, ownerWindowHandle),
            CertificateEntryKind.CertificateTrustList => OpenCtl(entry, ownerWindowHandle),
            _ => false
        };
    }

    /// <summary>
    /// Opens the native import wizard and locks it to the requested destination store.
    /// </summary>
    /// <param name="storeLocation">The destination store location.</param>
    /// <param name="storeName">The destination store name.</param>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <returns><see langword="true"/> when an import completed and the UI should refresh.</returns>
    public bool ImportToStore(StoreLocation storeLocation, string storeName, nint ownerWindowHandle)
    {
        using var store = _certificateStoreService.OpenStore(storeLocation, storeName, writable: true);

        uint flags = CryptUiWizImportNoChangeDestStore
            | CryptUiWizImportAllowCert
            | CryptUiWizImportAllowCrl
            | CryptUiWizImportAllowCtl
            | (storeLocation == StoreLocation.LocalMachine
                ? CryptUiWizImportToLocalMachine
                : CryptUiWizImportToCurrentUser);

        if (!CryptUiNativeMethods.CryptUIWizImport(flags, ownerWindowHandle, null, null, store.StoreHandle))
        {
            return HandleWizardResult("import", storeName);
        }

        _logger.LogInformation("Certificate import wizard completed for store {StoreName}.", storeName);
        return true;
    }

    /// <summary>
    /// Opens the native export wizard for the specified logical store.
    /// </summary>
    /// <param name="storeLocation">The store location.</param>
    /// <param name="storeName">The store name.</param>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <returns><see langword="true"/> when the wizard completed successfully.</returns>
    public bool ExportStore(StoreLocation storeLocation, string storeName, nint ownerWindowHandle)
    {
        using var store = _certificateStoreService.OpenStore(storeLocation, storeName, writable: false);

        var exportInfo = new CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO
        {
            dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO>(),
            dwSubjectChoice = CryptUiWizExportCertStore,
            Subject = store.StoreHandle
        };

        if (!CryptUiNativeMethods.CryptUIWizExport(0, ownerWindowHandle, null, &exportInfo, null))
        {
            return HandleWizardResult("export store", storeName);
        }

        _logger.LogInformation("Certificate export wizard completed for store {StoreName}.", storeName);
        return true;
    }

    /// <summary>
    /// Opens the native export wizard for a single entry.
    /// </summary>
    /// <param name="entry">The entry to export.</param>
    /// <param name="ownerWindowHandle">The owner window handle.</param>
    /// <returns><see langword="true"/> when the wizard completed successfully.</returns>
    public bool ExportEntry(CertificateEntry entry, nint ownerWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind switch
        {
            CertificateEntryKind.Certificate => ExportCertificate(entry, ownerWindowHandle),
            CertificateEntryKind.CertificateRevocationList => ExportCrl(entry, ownerWindowHandle),
            CertificateEntryKind.CertificateTrustList => ExportCtl(entry, ownerWindowHandle),
            _ => false
        };
    }

    private bool OpenCertificate(CertificateEntry entry, nint ownerWindowHandle)
    {
        CERT_CONTEXT* certificateContext = _certificateStoreService.DuplicateCertificateContext(entry);

        try
        {
            var viewInfo = new CryptUiNativeMethods.CRYPTUI_VIEWCERTIFICATE_STRUCTW
            {
                dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_VIEWCERTIFICATE_STRUCTW>(),
                hwndParent = ownerWindowHandle,
                dwFlags = CryptUiDisableEditProperties,
                pCertContext = certificateContext
            };

            if (!CryptUiNativeMethods.CryptUIDlgViewCertificateW(&viewInfo, out bool propertiesChanged))
            {
                return HandleDialogResult("view certificate", entry.DisplayName);
            }

            return propertiesChanged;
        }
        finally
        {
            Win32PInvoke.CertFreeCertificateContext(certificateContext);
        }
    }

    private bool OpenCertificateProperties(CertificateEntry entry, nint ownerWindowHandle, bool allowEditProperties)
    {
        using var store = _certificateStoreService.OpenStore(
            entry.StoreLocation,
            entry.StoreName,
            writable: allowEditProperties);
        CERT_CONTEXT* certificateContext = _certificateStoreService.DuplicateCertificateContext(entry, store);

        try
        {
            HCERTSTORE storeHandle = (HCERTSTORE)store.StoreHandle;
            string title = string.Format(_propertiesTitleFormat, entry.DisplayName);

            fixed (char* titlePointer = title)
            {
                var viewInfo = new CryptUiNativeMethods.CRYPTUI_VIEWCERTIFICATE_PROPERTIES_STRUCTW
                {
                    dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_VIEWCERTIFICATE_PROPERTIES_STRUCTW>(),
                    hwndParent = ownerWindowHandle,
                    szTitle = titlePointer,
                    pCertContext = certificateContext,
                    cStores = 1,
                    rghStores = &storeHandle
                };

                if (!CryptUiNativeMethods.CryptUIDlgViewCertificatePropertiesW(&viewInfo, out bool propertiesChanged))
                {
                    return HandleDialogResult("view certificate properties", entry.DisplayName);
                }

                return propertiesChanged;
            }
        }
        finally
        {
            Win32PInvoke.CertFreeCertificateContext(certificateContext);
        }
    }

    private bool OpenCrl(CertificateEntry entry, nint ownerWindowHandle)
    {
        CRL_CONTEXT* crlContext = _certificateStoreService.DuplicateCrlContext(entry);

        try
        {
            if (!CryptUiNativeMethods.CryptUIDlgViewContext(CertStoreCrlContext, crlContext, ownerWindowHandle, null, 0, null))
            {
                return HandleDialogResult("view CRL", entry.DisplayName);
            }

            return false;
        }
        finally
        {
            Win32PInvoke.CertFreeCRLContext(crlContext);
        }
    }

    private bool OpenCtl(CertificateEntry entry, nint ownerWindowHandle)
    {
        CTL_CONTEXT* ctlContext = _certificateStoreService.DuplicateCtlContext(entry);

        try
        {
            if (!CryptUiNativeMethods.CryptUIDlgViewContext(CertStoreCtlContext, ctlContext, ownerWindowHandle, null, 0, null))
            {
                return HandleDialogResult("view CTL", entry.DisplayName);
            }

            return false;
        }
        finally
        {
            Win32PInvoke.CertFreeCTLContext(ctlContext);
        }
    }

    private bool ExportCertificate(CertificateEntry entry, nint ownerWindowHandle)
    {
        CERT_CONTEXT* certificateContext = _certificateStoreService.DuplicateCertificateContext(entry);

        try
        {
            var exportInfo = new CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO
            {
                dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO>(),
                dwSubjectChoice = CryptUiWizExportCertContext,
                Subject = (nint)certificateContext
            };

            if (!CryptUiNativeMethods.CryptUIWizExport(0, ownerWindowHandle, null, &exportInfo, null))
            {
                return HandleWizardResult("export certificate", entry.DisplayName);
            }

            return true;
        }
        finally
        {
            Win32PInvoke.CertFreeCertificateContext(certificateContext);
        }
    }

    private bool ExportCrl(CertificateEntry entry, nint ownerWindowHandle)
    {
        CRL_CONTEXT* crlContext = _certificateStoreService.DuplicateCrlContext(entry);

        try
        {
            var exportInfo = new CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO
            {
                dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO>(),
                dwSubjectChoice = CryptUiWizExportCrlContext,
                Subject = (nint)crlContext
            };

            if (!CryptUiNativeMethods.CryptUIWizExport(0, ownerWindowHandle, null, &exportInfo, null))
            {
                return HandleWizardResult("export CRL", entry.DisplayName);
            }

            return true;
        }
        finally
        {
            Win32PInvoke.CertFreeCRLContext(crlContext);
        }
    }

    private bool ExportCtl(CertificateEntry entry, nint ownerWindowHandle)
    {
        CTL_CONTEXT* ctlContext = _certificateStoreService.DuplicateCtlContext(entry);

        try
        {
            var exportInfo = new CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO
            {
                dwSize = (uint)Marshal.SizeOf<CryptUiNativeMethods.CRYPTUI_WIZ_EXPORT_INFO>(),
                dwSubjectChoice = CryptUiWizExportCtlContext,
                Subject = (nint)ctlContext
            };

            if (!CryptUiNativeMethods.CryptUIWizExport(0, ownerWindowHandle, null, &exportInfo, null))
            {
                return HandleWizardResult("export CTL", entry.DisplayName);
            }

            return true;
        }
        finally
        {
            Win32PInvoke.CertFreeCTLContext(ctlContext);
        }
    }

    private bool HandleWizardResult(string operationName, string targetName)
    {
        int error = Marshal.GetLastWin32Error();
        if (error == 0 || error == ErrorCancelled)
        {
            _logger.LogDebug("User cancelled certificate {OperationName} for {TargetName}.", operationName, targetName);
            return false;
        }

        throw new Win32Exception(error);
    }

    private bool HandleDialogResult(string operationName, string targetName)
    {
        int error = Marshal.GetLastWin32Error();
        if (error == 0 || error == ErrorCancelled)
        {
            _logger.LogDebug("User closed certificate {OperationName} for {TargetName}.", operationName, targetName);
            return false;
        }

        throw new Win32Exception(error);
    }
}
