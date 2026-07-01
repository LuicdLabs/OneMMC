using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.Certificates.Interop;

/// <summary>
/// Provides the callback-based system-store enumeration entry point used by the certificates feature.
/// CsWin32 projects the related structs we use elsewhere, but this callback signature is simpler to
/// consume through a dedicated wrapper than through the generated delegate projection.
/// </summary>
internal static partial class Crypt32CertificateNativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    internal unsafe delegate bool CertEnumSystemStoreCallback(
        char* pvSystemStore,
        uint dwFlags,
        void* pStoreInfo,
        void* pvReserved,
        void* pvArg);

    [LibraryImport("Crypt32.dll", EntryPoint = "CertEnumSystemStore", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CertEnumSystemStore(
        uint dwFlags,
        void* pvSystemStoreLocationPara,
        void* pvArg,
        CertEnumSystemStoreCallback pfnEnum);
}
