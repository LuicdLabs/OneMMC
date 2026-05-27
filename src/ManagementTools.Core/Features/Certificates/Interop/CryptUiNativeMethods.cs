using System.Runtime.InteropServices;
using Windows.Win32.Security.Cryptography;

namespace ManagementTools.Core.Features.Certificates.Interop;

/// <summary>
/// Provides handwritten CryptoUI interop for certificate dialogs and wizards.
/// CsWin32 projects the lower-level CryptoAPI types we use elsewhere, but these
/// CryptoUI entry points rely on anonymous unions and UI-oriented structures that
/// are easier to consume and review through a dedicated wrapper.
/// </summary>
internal static partial class CryptUiNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct CRYPTUI_VIEWCERTIFICATE_STRUCTW
    {
        public uint dwSize;
        public nint hwndParent;
        public uint dwFlags;
        public char* szTitle;
        public CERT_CONTEXT* pCertContext;
        public char** rgszPurposes;
        public uint cPurposes;
        public nint pCryptProviderDataOrHWVTStateData;
        public int fpCryptProviderDataTrustedUsage;
        public uint idxSigner;
        public uint idxCert;
        public int fCounterSigner;
        public uint idxCounterSigner;
        public uint cStores;
        public HCERTSTORE* rghStores;
        public uint cPropSheetPages;
        public nint rgPropSheetPages;
        public uint nStartPage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPTUI_WIZ_IMPORT_SRC_INFO
    {
        public uint dwSize;
        public uint dwSubjectChoice;
        public nint Subject;
        public uint dwFlags;
        public nint pwszPassword;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPTUI_WIZ_EXPORT_INFO
    {
        public uint dwSize;
        public nint pwszExportFileName;
        public uint dwSubjectChoice;
        public nint Subject;
        public uint cStores;
        public nint rghStores;
    }

    [LibraryImport("Cryptui.dll", EntryPoint = "CryptUIDlgViewContext", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CryptUIDlgViewContext(
        uint dwContextType,
        void* pvContext,
        nint hwnd,
        string? pwszTitle,
        uint dwFlags,
        void* pvReserved);

    [LibraryImport("Cryptui.dll", EntryPoint = "CryptUIDlgViewCertificateW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CryptUIDlgViewCertificateW(
        CRYPTUI_VIEWCERTIFICATE_STRUCTW* pCertViewInfo,
        [MarshalAs(UnmanagedType.Bool)] out bool pfPropertiesChanged);

    [LibraryImport("Cryptui.dll", EntryPoint = "CryptUIWizImport", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CryptUIWizImport(
        uint dwFlags,
        nint hwndParent,
        string? pwszWizardTitle,
        CRYPTUI_WIZ_IMPORT_SRC_INFO* pImportSrc,
        nint hDestCertStore);

    [LibraryImport("Cryptui.dll", EntryPoint = "CryptUIWizExport", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CryptUIWizExport(
        uint dwFlags,
        nint hwndParent,
        string? pwszWizardTitle,
        CRYPTUI_WIZ_EXPORT_INFO* pExportInfo,
        void* pvoid);
}
