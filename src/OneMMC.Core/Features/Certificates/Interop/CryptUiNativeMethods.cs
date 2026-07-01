using System.Runtime.InteropServices;
using Windows.Win32.Security.Cryptography;

namespace OneMMC.Core.Features.Certificates.Interop;

/// <summary>
/// Provides handwritten CryptoUI interop for certificate dialogs and wizards.
/// CsWin32 projects the lower-level CryptoAPI types we use elsewhere, but these
/// CryptoUI entry points rely on anonymous unions and UI-oriented structures that
/// are easier to consume and review through a dedicated wrapper.
/// </summary>
internal static partial class CryptUiNativeMethods
{
    private const string CryptUiLibraryName = "Cryptui.dll";
    private const string ViewCertificatePropertiesExportName = "CryptUIDlgViewCertificatePropertiesW";

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

    /// <summary>
    /// Represents the private certificate property-sheet input used by the built-in certificate MMC snap-in.
    /// </summary>
    /// <remarks>
    /// This layout is limited to fields populated by the installed Windows certificate snap-in before it calls
    /// <c>CryptUIDlgViewCertificatePropertiesW</c>. The SDK does not publish a declaration for this export.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Size = 0x58)]
    internal unsafe struct CRYPTUI_VIEWCERTIFICATE_PROPERTIES_STRUCTW
    {
        [FieldOffset(0x00)]
        public uint dwSize;

        [FieldOffset(0x08)]
        public nint hwndParent;

        [FieldOffset(0x10)]
        public uint dwFlags;

        [FieldOffset(0x18)]
        public char* szTitle;

        [FieldOffset(0x20)]
        public CERT_CONTEXT* pCertContext;

        [FieldOffset(0x38)]
        public uint cStores;

        [FieldOffset(0x40)]
        public HCERTSTORE* rghStores;
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private unsafe delegate bool ViewCertificatePropertiesDelegate(
        CRYPTUI_VIEWCERTIFICATE_PROPERTIES_STRUCTW* certViewInfo,
        [MarshalAs(UnmanagedType.Bool)] out bool propertiesChanged);

    /// <summary>
    /// Displays the MMC-compatible certificate property sheet exported by Windows CryptoUI.
    /// </summary>
    /// <remarks>
    /// The certificate MMC snap-in imports this named export from <c>Cryptui.dll</c>, but it is
    /// not declared by the public Windows SDK header. It is therefore isolated behind dynamic
    /// binding rather than presented as a public or generally supported feature API.
    /// </remarks>
    internal static unsafe bool CryptUIDlgViewCertificatePropertiesW(
        CRYPTUI_VIEWCERTIFICATE_PROPERTIES_STRUCTW* certViewInfo,
        out bool propertiesChanged)
    {
        nint libraryHandle = NativeLibrary.Load(CryptUiLibraryName);

        try
        {
            nint procAddress = NativeLibrary.GetExport(libraryHandle, ViewCertificatePropertiesExportName);
            var viewProperties = Marshal.GetDelegateForFunctionPointer<ViewCertificatePropertiesDelegate>(procAddress);
            return viewProperties(certViewInfo, out propertiesChanged);
        }
        finally
        {
            NativeLibrary.Free(libraryHandle);
        }
    }

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
