using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Native Win32 interop declarations for security policy APIs.
    /// This file is an explicit CsWin32 exception because it carries the
    /// undocumented Security Configuration Engine exports alongside the
    /// hand-authored audit, registry and SID layouts the Security Settings
    /// features share. The imports use source-generated <see cref="LibraryImportAttribute"/> for
    /// Native AOT (no runtime marshalling stubs). Because <c>[LibraryImport]</c>
    /// never appends the ANSI/Unicode suffix that <c>CharSet.Unicode</c> did,
    /// every Unicode export names its <c>W</c> entry point explicitly.
    /// <para>
    /// Every import is pinned to System32 with <see cref="DefaultDllImportSearchPathsAttribute"/>,
    /// as CsWin32 does for its own projections. Without it the runtime probes the
    /// application directory first, and modules such as <c>scecli.dll</c> are not
    /// KnownDLLs, so a DLL planted beside an unpackaged OneMMC would load into the
    /// elevated process.
    /// </para>
    /// </summary>
    internal static partial class SecurityPolicyNativeMethods
    {
        #region SID Lookup

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ConvertSidToStringSid(
            IntPtr sid,
            out string stringSid);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ConvertStringSidToSid(
            string stringSid,
            out IntPtr sid);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr LocalFree(IntPtr hMem);

        /// <summary>Check if a SID is valid.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsValidSid(IntPtr sid);

        /// <summary>Gets the length of a SID.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll")]
        internal static partial int GetLengthSid(IntPtr sid);

        /// <summary>Copies a SID.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditQuerySystemPolicy(
            Guid[] subCategoryGuids,
            uint policyCount,
            out IntPtr auditPolicy);

        /// <summary>Sets audit policy for subcategories.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditSetSystemPolicy(
            IntPtr auditPolicy,
            uint policyCount);

        /// <summary>Enumerates audit subcategories for a given category.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditEnumerateSubCategories(
            ref Guid auditCategoryGuid,
            [MarshalAs(UnmanagedType.Bool)] bool retrieveAllSubCategories,
            out IntPtr subCategoryGuids,
            out uint subCategoryCount);

        /// <summary>Enumerates audit policy categories.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditEnumerateCategories(
            out IntPtr categoryGuids,
            out uint categoryCount);

        /// <summary>Looks up the display name for an audit category.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "AuditLookupCategoryNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditLookupCategoryName(
            ref Guid pAuditCategoryGuid,
            out IntPtr ppszCategoryName);

        /// <summary>Looks up the display name for an audit subcategory.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "AuditLookupSubCategoryNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditLookupSubCategoryName(
            ref Guid pAuditSubCategoryGuid,
            out IntPtr ppszSubCategoryName);

        /// <summary>Free memory returned by Audit* functions.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll")]
        internal static partial void AuditFree(IntPtr buffer);

        /// <summary>Queries the effective Global Object Access Auditing SACL for a resource manager.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "AuditQueryGlobalSaclW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditQueryGlobalSacl(
            string objectTypeName,
            out IntPtr sacl);

        /// <summary>Sets the effective Global Object Access Auditing SACL for a resource manager.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "AuditSetGlobalSaclW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AuditSetGlobalSacl(
            string objectTypeName,
            IntPtr sacl);

        // Audit policy flags
        internal const uint POLICY_AUDIT_EVENT_SUCCESS = 0x00000001;
        internal const uint POLICY_AUDIT_EVENT_FAILURE = 0x00000002;

        #endregion

        #region Registry API (for Security Options)

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegOpenKeyEx(
            IntPtr hKey,
            string subKey,
            int options,
            int samDesired,
            out IntPtr phkResult);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegQueryValueEx(
            IntPtr hKey,
            string valueName,
            IntPtr reserved,
            out int type,
            IntPtr data,
            ref int dataSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        internal static partial int RegCloseKey(IntPtr hKey);

        /// <summary>
        /// Enumerates the subkeys of an open key. Used to read the Security Configuration
        /// Engine's registry-value catalog under
        /// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SeCEdit\Reg Values</c>.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int RegEnumKeyEx(
            IntPtr hKey,
            int index,
            Span<char> name,
            ref int nameLength,
            IntPtr reserved,
            IntPtr className,
            IntPtr classLength,
            IntPtr lastWriteTime);

        internal static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));

        internal const int KEY_READ = 0x20019;
        internal const int REG_SZ = 1;
        internal const int REG_EXPAND_SZ = 2;
        internal const int REG_BINARY = 3;
        internal const int REG_DWORD = 4;
        internal const int REG_MULTI_SZ = 7;
        internal const int ERROR_SUCCESS = 0;
        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_PATH_NOT_FOUND = 3;
        internal const int ERROR_MORE_DATA = 234;
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const int ERROR_NOT_ALL_ASSIGNED = 1300;

        #endregion

        #region Token Privileges (for SeSecurityPrivilege)

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupPrivilegeValue(
            string? systemName,
            string name,
            out LUID luid);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeDisplayNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupPrivilegeDisplayName(
            string? systemName,
            string name,
            Span<char> displayName,
            ref int cchDisplayName,
            out int languageId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GetCurrentProcess();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr LoadLibraryEx(
            string lpFileName,
            IntPtr hFile,
            uint dwFlags);

        /// <summary>Loads a string resource from a loaded module.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "LoadStringW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int LoadString(
            IntPtr hInstance,
            uint uID,
            Span<char> lpBuffer,
            int nBufferMax);

        /// <summary>Frees a loaded library module.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FreeLibrary(IntPtr hModule);

        /// <summary>Reads the calling thread's UI language.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll")]
        internal static partial ushort GetThreadUILanguage();

        /// <summary>
        /// Sets the calling thread's UI language, which selects the language Win32
        /// returns text in. Used to read a privilege's name in a fixed language so it can
        /// be paired with its explain text; the previous value is always restored.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll")]
        internal static partial ushort SetThreadUILanguage(ushort langId);

        /// <summary>LANGID for en-US, the language the policy pairing is keyed on.</summary>
        internal const ushort LANG_EN_US = 0x0409;

        internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002; // LOAD_LIBRARY_AS_DATAFILE

        /// <summary>
        /// Maps the module for resource access only. Combined with
        /// <see cref="LOAD_LIBRARY_AS_DATAFILE"/> this lets the loader apply MUI
        /// redirection, so a neutral system DLL still yields localized strings.
        /// </summary>
        internal const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

        /// <summary>
        /// Resolves an indirect string such as <c>@wsecedit.dll,-59001</c> to its
        /// localized value via the MUI resource system.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int SHLoadIndirectString(
            string pszSource,
            Span<char> pszOutBuf,
            uint cchOutBuf,
            IntPtr ppvReserved);

        #endregion

        #region Security Configuration Engine (scecli.dll)

        /// <summary>
        /// Returns the privilege/logon-right constant at <paramref name="privilegeIndex"/>
        /// from the Security Configuration Engine's own table, which is the enumeration
        /// <c>secpol.msc</c> uses to build the User Rights Assignment list. Indices are
        /// dense starting at zero; a non-zero return marks the end of the table.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceLookupPrivRightName", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int SceLookupPrivRightName(
            int privilegeIndex,
            Span<char> buffer,
            ref int bufferLength);

        /// <summary>
        /// Opens a security profile. With <see cref="SCE_INF_FORMAT"/> the engine parses a security
        /// template file into its own profile structure, so the application never lays it out.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceOpenProfile", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SceOpenProfile(string profileName, int format, out IntPtr profileHandle);

        /// <summary>Closes a profile opened by <see cref="SceOpenProfile"/>; takes the handle by reference.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceCloseProfile")]
        internal static partial int SceCloseProfile(ref IntPtr profileHandle);

        /// <summary>
        /// Reads a profile's settings for the given areas into an engine-allocated
        /// <c>SCE_PROFILE_INFO</c>, released with <see cref="SceFreeProfileMemory"/>. A template handle
        /// requires <see cref="SCE_PROFILE_TEMPLATE"/>; a NULL handle with <see cref="SCE_ENGINE_SYSTEM"/>
        /// reads the system's current settings.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceGetSecurityProfileInfo")]
        internal static partial int SceGetSecurityProfileInfo(IntPtr profileHandle, int profileType, uint area, out IntPtr profileInfo, out IntPtr errorLog);

        /// <summary>
        /// Updates a profile. With a NULL handle and <see cref="SCE_UPDATE_LOCAL_POLICY"/> this merges the
        /// profile's configured settings into the machine's local security policy and applies them —
        /// the call secpol.msc makes when a Local Policies setting is saved. In that mode the area may
        /// only contain <see cref="AREA_SECURITY_POLICY"/> and <see cref="AREA_PRIVILEGES"/>.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceUpdateSecurityProfile")]
        internal static partial int SceUpdateSecurityProfile(IntPtr profileHandle, uint area, IntPtr profileInfo, uint mode);

        /// <summary>
        /// Writes a profile out as a security template, in the same text format <c>secedit /export</c>
        /// produces. Used to read the system's settings without laying out <c>SCE_PROFILE_INFO</c>.
        /// </summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceWriteSecurityProfileInfo", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SceWriteSecurityProfileInfo(string infFileName, uint area, IntPtr profileInfo, out IntPtr errorLog);

        /// <summary>Releases a profile returned by <see cref="SceGetSecurityProfileInfo"/>.</summary>
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("scecli.dll", EntryPoint = "SceFreeProfileMemory")]
        internal static partial int SceFreeProfileMemory(IntPtr profileInfo);

        internal const int SCESTATUS_SUCCESS = 0;
        internal const int SCESTATUS_ACCESS_DENIED = 9;

        /// <summary>Profile format: a security template (.inf) file.</summary>
        internal const int SCE_INF_FORMAT = 1;

        /// <summary>
        /// Profile type for reading the system's current settings with a NULL handle: the live SAM, LSA
        /// and registry values, as <c>secedit /export</c> reports them.
        /// </summary>
        internal const int SCE_ENGINE_SYSTEM = 300;

        /// <summary>Profile type for reading a security template opened with <see cref="SCE_INF_FORMAT"/>.</summary>
        internal const int SCE_PROFILE_TEMPLATE = 302;

        /// <summary>
        /// Type tag the local-policy update requires on the profile it is given. A profile parsed from a
        /// template carries the template tag (0x133) and is rejected with SCESTATUS_OTHER_ERROR; the
        /// local-policy table itself is returned with this tag. Both tag the same structure.
        /// </summary>
        internal const int SCE_PROFILE_TAG_ENGINE = 0x132;

        /// <summary><see cref="SceUpdateSecurityProfile"/> mode: merge into the local security policy.</summary>
        internal const uint SCE_UPDATE_LOCAL_POLICY = 4;

        /// <summary>Area carrying [System Access], [Event Audit] and [Registry Values].</summary>
        internal const uint AREA_SECURITY_POLICY = 0x1;

        /// <summary>Area carrying [Privilege Rights].</summary>
        internal const uint AREA_PRIVILEGES = 0x8;

        #endregion

    }
}
