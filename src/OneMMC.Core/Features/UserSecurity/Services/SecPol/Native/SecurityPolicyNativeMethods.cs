using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Native Win32 interop declarations for security policy APIs.
    /// This file is an explicit CsWin32 exception because the workflow mixes
    /// unsupported SAM exports with shared hand-authored LSA/NetAPI/registry
    /// layouts; splitting only part of the graph into generated projections
    /// would add duplicate marshalling models without improving safety. The
    /// imports use source-generated <see cref="LibraryImportAttribute"/> for
    /// Native AOT (no runtime marshalling stubs). Because <c>[LibraryImport]</c>
    /// never appends the ANSI/Unicode suffix that <c>CharSet.Unicode</c> did,
    /// every Unicode export names its <c>W</c> entry point explicitly.
    /// </summary>
    internal static partial class SecurityPolicyNativeMethods
    {
        #region Net API (Password Policy / Account Lockout)

        /// <summary>
        /// Retrieves global information for all users and groups in the security database.
        /// Level 0: password policy, Level 1: server role, Level 2: not used, Level 3: lockout policy.
        /// </summary>
        [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int NetUserModalsGet(
            string? serverName,
            int level,
            out IntPtr bufPtr);

        /// <summary>
        /// Sets global information for all users and groups in the security database.
        /// </summary>
        [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int NetUserModalsSet(
            string? serverName,
            int level,
            IntPtr buf,
            out int paramErr);

        /// <summary>Frees memory allocated by Net* functions.</summary>
        [LibraryImport("netapi32.dll")]
        internal static partial int NetApiBufferFree(IntPtr buffer);

        // USER_MODALS_INFO_0 - Password policy
        [StructLayout(LayoutKind.Sequential)]
        public struct USER_MODALS_INFO_0
        {
            public uint usrmod0_min_passwd_len;
            public uint usrmod0_max_passwd_age;    // seconds
            public uint usrmod0_min_passwd_age;    // seconds
            public uint usrmod0_force_logoff;      // seconds, 0xFFFFFFFF = never
            public uint usrmod0_password_hist_len;
        }

        // USER_MODALS_INFO_3 - Account lockout policy
        [StructLayout(LayoutKind.Sequential)]
        public struct USER_MODALS_INFO_3
        {
            public uint usrmod3_lockout_duration;    // seconds
            public uint usrmod3_lockout_observation_window;  // seconds
            public uint usrmod3_lockout_threshold;
        }

        internal const int NERR_Success = 0;
        internal const uint TIMEQ_FOREVER = 0xFFFFFFFF;

        #endregion

        #region LSA Policy API (User Rights Assignment)

        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_OBJECT_ATTRIBUTES
        {
            public uint Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        /// <summary>Opens the local LSA policy object.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaOpenPolicy(
            ref LSA_UNICODE_STRING systemName,
            ref LSA_OBJECT_ATTRIBUTES objectAttributes,
            uint desiredAccess,
            out IntPtr policyHandle);

        /// <summary>Closes an LSA handle.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaClose(IntPtr objectHandle);

        /// <summary>Frees LSA-allocated memory.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaFreeMemory(IntPtr buffer);

        /// <summary>Enumerates the rights (privileges) assigned to an account SID.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaEnumerateAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            out IntPtr userRights,
            out uint countOfRights);

        /// <summary>Enumerates accounts that have a specified right.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaEnumerateAccountsWithUserRight(
            IntPtr policyHandle,
            ref LSA_UNICODE_STRING userRight,
            out IntPtr enumerationBuffer,
            out uint countReturned);

        /// <summary>Adds rights to an account.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaAddAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            LSA_UNICODE_STRING[] userRights,
            uint countOfRights);

        /// <summary>Removes rights from an account.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaRemoveAccountRights(
            IntPtr policyHandle,
            IntPtr accountSid,
            [MarshalAs(UnmanagedType.Bool)] bool allRights,
            LSA_UNICODE_STRING[] userRights,
            uint countOfRights);

        /// <summary>Converts an NTSTATUS to a Win32 error code.</summary>
        [LibraryImport("advapi32.dll")]
        internal static partial int LsaNtStatusToWinError(uint status);

        // LSA_ENUMERATION_INFORMATION structure
        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_ENUMERATION_INFORMATION
        {
            public IntPtr Sid;
        }

        // LSA access masks
        internal const uint POLICY_VIEW_LOCAL_INFORMATION = 0x00000001;
        internal const uint POLICY_VIEW_AUDIT_INFORMATION = 0x00000002;
        internal const uint POLICY_LOOKUP_NAMES = 0x00000800;
        internal const uint POLICY_CREATE_ACCOUNT = 0x00000010;
        internal const uint POLICY_ALL_ACCESS = 0x00F0FFFF;
        internal const uint POLICY_READ = 0x00020006;
        internal const uint POLICY_WRITE = 0x000207F8;

        internal const uint STATUS_SUCCESS = 0x00000000;
        internal const uint STATUS_NO_MORE_ENTRIES = 0x8000001A;
        internal const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;

        #endregion

        #region SID Lookup

        [LibraryImport("advapi32.dll", EntryPoint = "LookupAccountSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupAccountSid(
            string? systemName,
            IntPtr sid,
            Span<char> name,
            ref int nameLength,
            Span<char> domainName,
            ref int domainNameLength,
            out int use);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupAccountNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupAccountName(
            string? systemName,
            string accountName,
            IntPtr sid,
            ref int sidLength,
            Span<char> domainName,
            ref int domainNameLength,
            out int use);

        [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ConvertSidToStringSid(
            IntPtr sid,
            out string stringSid);

        [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ConvertStringSidToSid(
            string stringSid,
            out IntPtr sid);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr LocalFree(IntPtr hMem);

        /// <summary>Check if a SID is valid.</summary>
        [LibraryImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsValidSid(IntPtr sid);

        /// <summary>Gets the length of a SID.</summary>
        [LibraryImport("advapi32.dll")]
        internal static partial int GetLengthSid(IntPtr sid);

        /// <summary>Copies a SID.</summary>
        [LibraryImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CopySid(int destinationSidLength, IntPtr destinationSid, IntPtr sourceSid);

        #endregion

        #region Audit Policy (AuditQuerySystemPolicy / AuditSetSystemPolicy - Vista+)

        // AUDIT_POLICY_INFORMATION
        [StructLayout(LayoutKind.Sequential)]
        public struct AUDIT_POLICY_INFORMATION
        {
            public Guid AuditSubCategoryGuid;
            public uint AuditingInformation;
            public Guid AuditCategoryGuid;
        }

        /// <summary>Queries audit policy for a set of subcategories.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditQuerySystemPolicy(
            Guid[] subCategoryGuids,
            uint policyCount,
            out IntPtr auditPolicy);

        /// <summary>Sets audit policy for subcategories.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditSetSystemPolicy(
            IntPtr auditPolicy,
            uint policyCount);

        /// <summary>Enumerates audit subcategories for a given category.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditEnumerateSubCategories(
            ref Guid auditCategoryGuid,
            [MarshalAs(UnmanagedType.Bool)] bool retrieveAllSubCategories,
            out IntPtr subCategoryGuids,
            out uint subCategoryCount);

        /// <summary>Enumerates audit policy categories.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditEnumerateCategories(
            out IntPtr categoryGuids,
            out uint categoryCount);

        /// <summary>Looks up the display name for an audit category.</summary>
        [LibraryImport("advapi32.dll", EntryPoint = "AuditLookupCategoryNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditLookupCategoryName(
            ref Guid pAuditCategoryGuid,
            out IntPtr ppszCategoryName);

        /// <summary>Looks up the display name for an audit subcategory.</summary>
        [LibraryImport("advapi32.dll", EntryPoint = "AuditLookupSubCategoryNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditLookupSubCategoryName(
            ref Guid pAuditSubCategoryGuid,
            out IntPtr ppszSubCategoryName);

        /// <summary>Free memory returned by Audit* functions.</summary>
        [LibraryImport("advapi32.dll")]
        internal static partial void AuditFree(IntPtr buffer);

        /// <summary>Queries the effective Global Object Access Auditing SACL for a resource manager.</summary>
        [LibraryImport("advapi32.dll", EntryPoint = "AuditQueryGlobalSaclW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditQueryGlobalSacl(
            string objectTypeName,
            out IntPtr sacl);

        /// <summary>Sets the effective Global Object Access Auditing SACL for a resource manager.</summary>
        [LibraryImport("advapi32.dll", EntryPoint = "AuditSetGlobalSaclW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditSetGlobalSacl(
            string objectTypeName,
            IntPtr sacl);

        // Audit policy flags
        internal const uint POLICY_AUDIT_EVENT_UNCHANGED = 0x00000000;
        internal const uint POLICY_AUDIT_EVENT_SUCCESS = 0x00000001;
        internal const uint POLICY_AUDIT_EVENT_FAILURE = 0x00000002;
        internal const uint POLICY_AUDIT_EVENT_NONE = 0x00000004;

        #endregion

        #region Registry API (for Security Options)

        [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegOpenKeyEx(
            IntPtr hKey,
            string subKey,
            int options,
            int samDesired,
            out IntPtr phkResult);

        [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegQueryValueEx(
            IntPtr hKey,
            string valueName,
            IntPtr reserved,
            out int type,
            IntPtr data,
            ref int dataSize);

        [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegSetValueEx(
            IntPtr hKey,
            string valueName,
            int reserved,
            int type,
            IntPtr data,
            int dataSize);

        [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegSetValueEx(
            IntPtr hKey,
            string valueName,
            int reserved,
            int type,
            byte[] data,
            int dataSize);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial int RegCloseKey(IntPtr hKey);

        [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegDeleteValue(IntPtr hKey, string valueName);

        [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegCreateKeyEx(
            IntPtr hKey,
            string subKey,
            int reserved,
            string? className,
            int options,
            int samDesired,
            IntPtr securityAttributes,
            out IntPtr phkResult,
            out int disposition);

        internal static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));

        internal const int KEY_READ = 0x20019;
        internal const int KEY_WRITE = 0x20006;
        internal const int KEY_ALL_ACCESS = 0xF003F;
        internal const int REG_SZ = 1;
        internal const int REG_BINARY = 3;
        internal const int REG_DWORD = 4;
        internal const int REG_MULTI_SZ = 7;
        internal const int REG_OPTION_NON_VOLATILE = 0;
        internal const int ERROR_SUCCESS = 0;
        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_ACCESS_DENIED = 5;
        internal const int ERROR_NOT_ALL_ASSIGNED = 1300;

        #endregion

        #region LSA Account Domain Information (Password complexity via SAM)

        /// <summary>Connects to the SAM server.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamConnect(
            ref LSA_UNICODE_STRING serverName,
            out IntPtr serverHandle,
            uint desiredAccess,
            IntPtr objectAttributes);

        /// <summary>Closes a SAM handle.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamCloseHandle(IntPtr handle);

        /// <summary>Opens a domain handle in the SAM database.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamOpenDomain(
            IntPtr serverHandle,
            uint desiredAccess,
            IntPtr domainId,
            out IntPtr domainHandle);

        /// <summary>Queries information about a SAM domain.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamQueryInformationDomain(
            IntPtr domainHandle,
            int domainInformationClass,
            out IntPtr buffer);

        /// <summary>Sets information about a SAM domain.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamSetInformationDomain(
            IntPtr domainHandle,
            int domainInformationClass,
            IntPtr buffer);

        /// <summary>Frees SAM-allocated memory.</summary>
        [LibraryImport("samlib.dll")]
        internal static partial int SamFreeMemory(IntPtr buffer);

        // DOMAIN_PASSWORD_INFORMATION structure (DomainPasswordInformation class = 1)
        [StructLayout(LayoutKind.Sequential)]
        public struct DOMAIN_PASSWORD_INFORMATION
        {
            public ushort MinPasswordLength;
            public ushort PasswordHistoryLength;
            public uint PasswordProperties;
            public long MaxPasswordAge;   // LARGE_INTEGER (100-ns intervals, negative = relative)
            public long MinPasswordAge;   // LARGE_INTEGER (100-ns intervals, negative = relative)
        }

        // SAM access masks
        internal const uint SAM_SERVER_CONNECT = 0x00000001; // required to connect to the SAM server
        internal const uint SAM_SERVER_LOOKUP_DOMAIN = 0x00000020; // required to lookup a domain in the SAM server
        internal const uint DOMAIN_READ_PASSWORD_PARAMETERS = 0x00000001; // required to read password parameters
        internal const uint DOMAIN_WRITE_PASSWORD_PARAMS = 0x00000002; // required to write password parameters

        // DomainInformationClass
        internal const int DomainPasswordInformation = 1;

        // Password property flags
        internal const uint DOMAIN_PASSWORD_COMPLEX = 0x00000001; // password must meet complexity requirements (Windows 10+)
        internal const uint DOMAIN_PASSWORD_NO_ANON_CHANGE = 0x00000002; // users cannot change their password anonymously
        internal const uint DOMAIN_PASSWORD_NO_CLEAR_CHANGE = 0x00000004; // users cannot change their password using clear text passwords
        internal const uint DOMAIN_PASSWORD_LOCKOUT_ADMINS = 0x00000008; // administrators are not locked out
        internal const uint DOMAIN_PASSWORD_STORE_CLEARTEXT = 0x00000010; // passwords are stored in clear text
        internal const uint DOMAIN_REFUSE_PASSWORD_CHANGE = 0x00000020; // users are not allowed to change their password

        #endregion

        #region Account status (UF_* flags)

        // NetUserGetInfo/NetUserSetInfo and their USER_INFO_* buffers moved to the CsWin32
        // projection (NativeMethods.txt) during the M4 Native AOT migration - the handwritten
        // imports duplicated generated ones. Only the flag constants remain here.

        // USER_MODALS_INFO_2 - Domain name and SID
        [StructLayout(LayoutKind.Sequential)]
        public struct USER_MODALS_INFO_2
        {
            public IntPtr usrmod2_domain_name;  // LPWSTR
            public IntPtr usrmod2_domain_id;    // PSID
        }

        // User account flags
        internal const uint UF_ACCOUNTDISABLE = 0x0002; // set/cleared via NetUserSetInfo to enable/disable an account, but we also need to preserve other flags like password never expires
        internal const uint UF_LOCKOUT = 0x0010; // read-only flag returned by NetUserGetInfo to indicate the account is currently locked out, but cannot be set directly (lockout is controlled by lockout policy and failed login attempts)

        #endregion

        #region Token Privileges (for SeSecurityPrivilege)

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupPrivilegeValue(
            string? systemName,
            string name,
            out LUID luid);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeDisplayNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupPrivilegeDisplayName(
            string? systemName,
            string name,
            Span<char> displayName,
            ref int cchDisplayName,
            out int languageId);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020; // enable/disable privileges in the token
        internal const uint TOKEN_QUERY = 0x0008; // required to lookup privilege values and get current token privileges
        internal const uint SE_PRIVILEGE_ENABLED = 0x00000002; // SE_PRIVILEGE_ENABLED_BY_DEFAULT
        internal const string SE_SECURITY_NAME = "SeSecurityPrivilege";
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        #endregion

        #region DLL String Resource Loading

        /// <summary>Loads a DLL as a data file for reading resources.</summary>
        [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr LoadLibraryEx(
            string lpFileName,
            IntPtr hFile,
            uint dwFlags);

        /// <summary>Loads a string resource from a loaded module.</summary>
        [LibraryImport("user32.dll", EntryPoint = "LoadStringW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int LoadString(
            IntPtr hInstance,
            uint uID,
            Span<char> lpBuffer,
            int nBufferMax);

        /// <summary>Frees a loaded library module.</summary>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FreeLibrary(IntPtr hModule);

        internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002; // LOAD_LIBRARY_AS_DATAFILE

        /// <summary>
        /// Resolves an indirect string such as <c>@wsecedit.dll,-59001</c> to its
        /// localized value via the MUI resource system.
        /// </summary>
        [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int SHLoadIndirectString(
            string pszSource,
            Span<char> pszOutBuf,
            uint cchOutBuf,
            IntPtr ppvReserved);

        #endregion

        #region LsaQueryInformationPolicy / LsaSetInformationPolicy (Audit)

        /// <summary>Queries information about the LSA policy.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaQueryInformationPolicy(
            IntPtr policyHandle,
            int informationClass,
            out IntPtr buffer);

        /// <summary>Sets information about the LSA policy.</summary>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial uint LsaSetInformationPolicy(
            IntPtr policyHandle,
            int informationClass,
            IntPtr buffer);

        internal const int PolicyAuditEventsInformation = 2; // Used for both query and set, but only set is supported (Vista+)
        internal const int PolicyAccountDomainInformation = 5; // Used for password complexity via SAM domain info, but can also be queried/set via LSA for consistency

        // POLICY_AUDIT_EVENTS_INFO
        [StructLayout(LayoutKind.Sequential)]
        public struct POLICY_AUDIT_EVENTS_INFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool AuditingMode;
            public IntPtr EventAuditingOptions; // PPOLICY_AUDIT_EVENT_OPTIONS (array of uint)
            public uint MaximumAuditEventCount;
        }

        // POLICY_ACCOUNT_DOMAIN_INFO
        [StructLayout(LayoutKind.Sequential)]
        public struct POLICY_ACCOUNT_DOMAIN_INFO
        {
            public LSA_UNICODE_STRING DomainName;
            public IntPtr DomainSid;    // PSID
        }

        internal const uint POLICY_SET_AUDIT_REQUIREMENTS = 0x00000100; // POLICY_AUDIT_EVENT_SUCCESS | POLICY_AUDIT_EVENT_FAILURE

        #endregion
    }
}
