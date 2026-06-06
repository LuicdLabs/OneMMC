using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.Native;

/// <summary>
/// Native interop for the legacy IPsec policy store exported by <c>polstore.dll</c>.
/// </summary>
/// <remarks>
/// These exports are not present in CsWin32 metadata or the public Windows SDK. The DLL is loaded
/// dynamically so unsupported platforms fail gracefully without a static P/Invoke dependency.
/// </remarks>
internal static class IPSecurityPolicyNativeMethods
{
    /// <summary>
    /// Value of the <c>IPSEC_STORE_TYPE</c> enum that selects the local registry store
    /// (<c>IPSEC_STORE_TYPE_LOCAL_REGISTRY</c>). The other valid values are
    /// <c>IPSEC_STORE_TYPE_DIRECTORY</c> (0) and <c>IPSEC_STORE_TYPE_LOCAL_FILE</c> (1); the
    /// directory value returns <c>ERROR_INVALID_PARAMETER</c> from <c>IPSecExportPolicies</c>.
    /// </summary>
    internal const int RegistryProvider = 2;

    /// <summary>
    /// HRESULT for <c>ERR IPsec[05073]</c> (unable to open the local policy store).
    /// </summary>
    internal const int HResultPolicyStoreOpenFailure = unchecked((int)0x800713D1);

    private static readonly Lock ApiLock = new();
    private static bool _loadAttempted;
    private static IntPtr _moduleHandle;
    private static IPSecOpenPolicyStoreDelegate? _openPolicyStore;
    private static IPSecClosePolicyStoreDelegate? _closePolicyStore;
    private static IPSecExportPoliciesDelegate? _exportPolicies;
    private static IPSecImportPoliciesDelegate? _importPolicies;
    private static IPSecFreePolStrDelegate? _freePolStr;

    internal static bool IsAvailable => TryEnsureLoaded();

    internal static bool TryOpenRegistryStore(out IntPtr policyStore, out int errorCode)
    {
        policyStore = IntPtr.Zero;
        errorCode = 0;

        if (!TryEnsureLoaded())
        {
            errorCode = unchecked((int)0x8007007E);
            return false;
        }

        errorCode = _openPolicyStore!(string.Empty, RegistryProvider, string.Empty, out policyStore);
        return errorCode == 0 && policyStore != IntPtr.Zero;
    }

    internal static void CloseStore(IntPtr policyStore)
    {
        if (policyStore != IntPtr.Zero && TryEnsureLoaded())
        {
            _closePolicyStore!(policyStore);
        }
    }

    internal static string? TryExportPolicies(IntPtr policyStore, out int errorCode)
    {
        errorCode = 0;
        EnsureLoaded();

        errorCode = _exportPolicies!(policyStore, out IntPtr scriptPointer);
        if (errorCode != 0 || scriptPointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(scriptPointer);
        }
        finally
        {
            _freePolStr!(scriptPointer);
        }
    }

    internal static int ImportPolicies(IntPtr policyStore, string script)
    {
        EnsureLoaded();
        return _importPolicies!(policyStore, script);
    }

    internal static bool IsStoreOpenFailure(int errorCode)
    {
        return errorCode == HResultPolicyStoreOpenFailure
            || unchecked((uint)errorCode) == 0x13D1u
            || errorCode == 5
            || errorCode == unchecked((int)0x80070005);
    }

    private static bool TryEnsureLoaded()
    {
        try
        {
            EnsureLoaded();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void EnsureLoaded()
    {
        lock (ApiLock)
        {
            if (_loadAttempted)
            {
                if (_openPolicyStore is null
                    || _closePolicyStore is null
                    || _exportPolicies is null
                    || _importPolicies is null
                    || _freePolStr is null)
                {
                    throw new PlatformNotSupportedException(
                        "This version of Windows does not expose the legacy IPsec policy store APIs.");
                }

                return;
            }

            _loadAttempted = true;
            _moduleHandle = NativeLibrary.Load("polstore.dll");
            _openPolicyStore = LoadDelegate<IPSecOpenPolicyStoreDelegate>("IPSecOpenPolicyStore");
            _closePolicyStore = LoadDelegate<IPSecClosePolicyStoreDelegate>("IPSecClosePolicyStore");
            _exportPolicies = LoadDelegate<IPSecExportPoliciesDelegate>("IPSecExportPolicies");
            _importPolicies = LoadDelegate<IPSecImportPoliciesDelegate>("IPSecImportPolicies");
            _freePolStr = LoadDelegate<IPSecFreePolStrDelegate>("IPSecFreePolStr");
        }
    }

    private static T LoadDelegate<T>(string exportName)
        where T : Delegate
    {
        IntPtr functionPointer = NativeLibrary.GetExport(_moduleHandle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int IPSecOpenPolicyStoreDelegate(
        string machineName,
        int storeType,
        string fileName,
        out IntPtr policyStore);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecClosePolicyStoreDelegate(IntPtr policyStore);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecExportPoliciesDelegate(IntPtr policyStore, out IntPtr policyScript);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int IPSecImportPoliciesDelegate(IntPtr policyStore, string policyScript);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecFreePolStrDelegate(IntPtr value);
}
