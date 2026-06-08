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
    /// (<c>IPSEC_REGISTRY_PROVIDER</c>). The enum values are:
    /// <c>IPSEC_REGISTRY_PROVIDER</c> (0), <c>IPSEC_DIRECTORY_PROVIDER</c> (1),
    /// <c>IPSEC_FILE_PROVIDER</c> (2).
    /// </summary>
    internal const int RegistryProvider = 0;

    /// <summary>
    /// HRESULT for <c>ERR IPsec[05073]</c> (unable to open the local policy store).
    /// </summary>
    internal const int HResultPolicyStoreOpenFailure = unchecked((int)0x800713D1);

    private static readonly Lock ApiLock = new();
    private static bool _loadAttempted;
    private static IntPtr _moduleHandle;

    // Read/export APIs
    private static IPSecOpenPolicyStoreDelegate? _openPolicyStore;
    private static IPSecClosePolicyStoreDelegate? _closePolicyStore;
    private static IPSecExportPoliciesDelegate? _exportPolicies;
    private static IPSecImportPoliciesDelegate? _importPolicies;
    private static IPSecFreePolStrDelegate? _freePolStr;

    // Enum APIs
    private static IPSecEnumDataDelegate? _enumPolicyData;
    private static IPSecEnumDataDelegate? _enumFilterData;
    private static IPSecEnumDataDelegate? _enumNegPolData;
    private static IPSecEnumDataDelegate? _enumISAKMPData;
    private static IPSecEnumNFADataDelegate? _enumNFAData;

    // Create APIs
    private static IPSecStoreDataDelegate? _createPolicyData;
    private static IPSecStoreDataDelegate? _createFilterData;
    private static IPSecStoreDataDelegate? _createNegPolData;
    private static IPSecStoreDataDelegate? _createISAKMPData;
    private static IPSecCreateNFADataDelegate? _createNFAData;

    // Set APIs
    private static IPSecStoreDataDelegate? _setPolicyData;
    private static IPSecStoreDataDelegate? _setFilterData;
    private static IPSecStoreDataDelegate? _setNegPolData;
    private static IPSecStoreDataDelegate? _setISAKMPData;
    private static IPSecSetNFADataDelegate? _setNFAData;

    // Delete APIs
    private static IPSecDeleteByGuidDelegate? _deletePolicyData;
    private static IPSecDeleteByGuidDelegate? _deleteFilterData;
    private static IPSecDeleteByGuidDelegate? _deleteNegPolData;
    private static IPSecDeleteByGuidDelegate? _deleteISAKMPData;
    private static IPSecDeleteNFADataDelegate? _deleteNFAData;

    // Assign/Unassign
    private static IPSecAssignPolicyDelegate? _assignPolicy;
    private static IPSecAssignPolicyDelegate? _unassignPolicy;

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

    // ===== Enum APIs =====

    internal static int EnumPolicyData(IntPtr policyStore, IntPtr pppData, IntPtr pdwCount)
    {
        EnsureLoaded();
        return _enumPolicyData!(policyStore, pppData, pdwCount);
    }

    internal static int EnumFilterData(IntPtr policyStore, IntPtr pppData, IntPtr pdwCount)
    {
        EnsureLoaded();
        return _enumFilterData!(policyStore, pppData, pdwCount);
    }

    internal static int EnumNegPolData(IntPtr policyStore, IntPtr pppData, IntPtr pdwCount)
    {
        EnsureLoaded();
        return _enumNegPolData!(policyStore, pppData, pdwCount);
    }

    internal static int EnumISAKMPData(IntPtr policyStore, IntPtr pppData, IntPtr pdwCount)
    {
        EnsureLoaded();
        return _enumISAKMPData!(policyStore, pppData, pdwCount);
    }

    internal static int EnumNFAData(IntPtr policyStore, Guid policyIdentifier, IntPtr pppData, IntPtr pdwCount)
    {
        EnsureLoaded();
        return _enumNFAData!(policyStore, policyIdentifier, pppData, pdwCount);
    }

    // ===== Create APIs =====

    internal static int CreatePolicyData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _createPolicyData!(policyStore, pData);
    }

    internal static int CreateFilterData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _createFilterData!(policyStore, pData);
    }

    internal static int CreateNegPolData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _createNegPolData!(policyStore, pData);
    }

    internal static int CreateISAKMPData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _createISAKMPData!(policyStore, pData);
    }

    internal static int CreateNFAData(IntPtr policyStore, Guid policyIdentifier, IntPtr pData)
    {
        EnsureLoaded();
        return _createNFAData!(policyStore, policyIdentifier, pData);
    }

    // ===== Set APIs =====

    internal static int SetPolicyData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _setPolicyData!(policyStore, pData);
    }

    internal static int SetFilterData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _setFilterData!(policyStore, pData);
    }

    internal static int SetNegPolData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _setNegPolData!(policyStore, pData);
    }

    internal static int SetISAKMPData(IntPtr policyStore, IntPtr pData)
    {
        EnsureLoaded();
        return _setISAKMPData!(policyStore, pData);
    }

    internal static int SetNFAData(IntPtr policyStore, Guid policyIdentifier, IntPtr pData)
    {
        EnsureLoaded();
        return _setNFAData!(policyStore, policyIdentifier, pData);
    }

    // ===== Delete APIs =====

    internal static int DeletePolicyData(IntPtr policyStore, Guid policyIdentifier)
    {
        EnsureLoaded();
        return _deletePolicyData!(policyStore, policyIdentifier);
    }

    internal static int DeleteFilterData(IntPtr policyStore, Guid filterIdentifier)
    {
        EnsureLoaded();
        return _deleteFilterData!(policyStore, filterIdentifier);
    }

    internal static int DeleteNegPolData(IntPtr policyStore, Guid negPolIdentifier)
    {
        EnsureLoaded();
        return _deleteNegPolData!(policyStore, negPolIdentifier);
    }

    internal static int DeleteISAKMPData(IntPtr policyStore, Guid isakmpIdentifier)
    {
        EnsureLoaded();
        return _deleteISAKMPData!(policyStore, isakmpIdentifier);
    }

    internal static int DeleteNFAData(IntPtr policyStore, Guid policyIdentifier, Guid nfaIdentifier)
    {
        EnsureLoaded();
        return _deleteNFAData!(policyStore, policyIdentifier, nfaIdentifier);
    }

    // ===== Assign/Unassign =====

    internal static int AssignPolicy(IntPtr policyStore, Guid policyIdentifier)
    {
        EnsureLoaded();
        return _assignPolicy!(policyStore, policyIdentifier);
    }

    internal static int UnassignPolicy(IntPtr policyStore, Guid policyIdentifier)
    {
        EnsureLoaded();
        return _unassignPolicy!(policyStore, policyIdentifier);
    }

    // ===== Helpers =====

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
                if (_openPolicyStore is null || _closePolicyStore is null)
                {
                    throw new PlatformNotSupportedException(
                        "This version of Windows does not expose the legacy IPsec policy store APIs.");
                }

                return;
            }

            _loadAttempted = true;
            _moduleHandle = NativeLibrary.Load("polstore.dll");

            // Core store management
            _openPolicyStore = LoadDelegate<IPSecOpenPolicyStoreDelegate>("IPSecOpenPolicyStore");
            _closePolicyStore = LoadDelegate<IPSecClosePolicyStoreDelegate>("IPSecClosePolicyStore");
            _exportPolicies = LoadDelegate<IPSecExportPoliciesDelegate>("IPSecExportPolicies");
            _importPolicies = LoadDelegate<IPSecImportPoliciesDelegate>("IPSecImportPolicies");
            _freePolStr = LoadDelegate<IPSecFreePolStrDelegate>("IPSecFreePolStr");

            // Enum
            _enumPolicyData = LoadDelegate<IPSecEnumDataDelegate>("IPSecEnumPolicyData");
            _enumFilterData = LoadDelegate<IPSecEnumDataDelegate>("IPSecEnumFilterData");
            _enumNegPolData = LoadDelegate<IPSecEnumDataDelegate>("IPSecEnumNegPolData");
            _enumISAKMPData = LoadDelegate<IPSecEnumDataDelegate>("IPSecEnumISAKMPData");
            _enumNFAData = LoadDelegate<IPSecEnumNFADataDelegate>("IPSecEnumNFAData");

            // Create
            _createPolicyData = LoadDelegate<IPSecStoreDataDelegate>("IPSecCreatePolicyData");
            _createFilterData = LoadDelegate<IPSecStoreDataDelegate>("IPSecCreateFilterData");
            _createNegPolData = LoadDelegate<IPSecStoreDataDelegate>("IPSecCreateNegPolData");
            _createISAKMPData = LoadDelegate<IPSecStoreDataDelegate>("IPSecCreateISAKMPData");
            _createNFAData = LoadDelegate<IPSecCreateNFADataDelegate>("IPSecCreateNFAData");

            // Set
            _setPolicyData = LoadDelegate<IPSecStoreDataDelegate>("IPSecSetPolicyData");
            _setFilterData = LoadDelegate<IPSecStoreDataDelegate>("IPSecSetFilterData");
            _setNegPolData = LoadDelegate<IPSecStoreDataDelegate>("IPSecSetNegPolData");
            _setISAKMPData = LoadDelegate<IPSecStoreDataDelegate>("IPSecSetISAKMPData");
            _setNFAData = LoadDelegate<IPSecSetNFADataDelegate>("IPSecSetNFAData");

            // Delete
            _deletePolicyData = LoadDelegate<IPSecDeleteByGuidDelegate>("IPSecDeletePolicyData");
            _deleteFilterData = LoadDelegate<IPSecDeleteByGuidDelegate>("IPSecDeleteFilterData");
            _deleteNegPolData = LoadDelegate<IPSecDeleteByGuidDelegate>("IPSecDeleteNegPolData");
            _deleteISAKMPData = LoadDelegate<IPSecDeleteByGuidDelegate>("IPSecDeleteISAKMPData");
            _deleteNFAData = LoadDelegate<IPSecDeleteNFADataDelegate>("IPSecDeleteNFAData");

            // Assign
            _assignPolicy = LoadDelegate<IPSecAssignPolicyDelegate>("IPSecAssignPolicy");
            _unassignPolicy = LoadDelegate<IPSecAssignPolicyDelegate>("IPSecUnassignPolicy");
        }
    }

    private static T LoadDelegate<T>(string exportName)
        where T : Delegate
    {
        IntPtr functionPointer = NativeLibrary.GetExport(_moduleHandle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
    }

    // ===== Delegate Types =====

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

    /// <summary>Signature for IPSecEnum{Policy|Filter|NegPol|ISAKMP}Data.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecEnumDataDelegate(IntPtr policyStore, IntPtr pppData, IntPtr pdwCount);

    /// <summary>Signature for IPSecEnumNFAData (requires policy GUID).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecEnumNFADataDelegate(IntPtr policyStore, Guid policyIdentifier, IntPtr pppData, IntPtr pdwCount);

    /// <summary>Signature for IPSecCreate/Set{Policy|Filter|NegPol|ISAKMP}Data.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecStoreDataDelegate(IntPtr policyStore, IntPtr pData);

    /// <summary>Signature for IPSecCreateNFAData (requires policy GUID).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecCreateNFADataDelegate(IntPtr policyStore, Guid policyIdentifier, IntPtr pData);

    /// <summary>Signature for IPSecSetNFAData (requires policy GUID).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecSetNFADataDelegate(IntPtr policyStore, Guid policyIdentifier, IntPtr pData);

    /// <summary>Signature for IPSecDelete{Policy|Filter|NegPol|ISAKMP}Data.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecDeleteByGuidDelegate(IntPtr policyStore, Guid identifier);

    /// <summary>Signature for IPSecDeleteNFAData (requires policy + NFA GUID).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecDeleteNFADataDelegate(IntPtr policyStore, Guid policyIdentifier, Guid nfaIdentifier);

    /// <summary>Signature for IPSecAssignPolicy / IPSecUnassignPolicy.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IPSecAssignPolicyDelegate(IntPtr policyStore, Guid policyIdentifier);
}
