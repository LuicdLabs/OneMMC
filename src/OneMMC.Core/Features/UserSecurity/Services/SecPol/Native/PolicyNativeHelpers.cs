using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Shared utility methods for policy providers. Centralises native API wrappers,
    /// scope/RAII helpers and common read/write logic so that individual providers
    /// remain focused on their category-specific concerns.
    /// </summary>
    internal static partial class PolicyNativeHelpers
    {
        private static ILogger _logger = NullLogger.Instance;

        internal static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        private const string SupportedEncryptionTypesKey = "SupportedEncryptionTypes";
        private const long KerberosLowFlagsMask = 0x1F; // 1|2|4|8|16
        private const long KerberosLegacyFutureBit = 0x20;
        private const long KerberosFutureMask = 0x7FFFFFE0;

        #region Scope / RAII Helpers

        internal sealed partial class HGlobalBuffer : IDisposable
        {
            public IntPtr Pointer { get; }

            public HGlobalBuffer(int size)
            {
                Pointer = Marshal.AllocHGlobal(size);
            }

            public void Dispose()
            {
                if (Pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(Pointer);
            }
        }

        internal sealed partial class LsaPolicyHandleScope : IDisposable
        {
            public IntPtr Handle { get; }

            public LsaPolicyHandleScope(IntPtr handle) => Handle = handle;

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    LsaClose(Handle);
            }
        }

        internal sealed partial class LsaMemoryScope : IDisposable
        {
            public IntPtr Pointer { get; }

            public LsaMemoryScope(IntPtr pointer) => Pointer = pointer;

            public void Dispose()
            {
                if (Pointer != IntPtr.Zero)
                    LsaFreeMemory(Pointer);
            }
        }

        internal sealed partial class LsaUnicodeStringScope : IDisposable
        {
            public LSA_UNICODE_STRING Value;

            public LsaUnicodeStringScope(string value)
            {
                Value = new LSA_UNICODE_STRING
                {
                    Length = (ushort)(value.Length * 2),
                    MaximumLength = (ushort)((value.Length + 1) * 2),
                    Buffer = Marshal.StringToHGlobalUni(value)
                };
            }

            public void Dispose()
            {
                if (Value.Buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Value.Buffer);
                    Value.Buffer = IntPtr.Zero;
                }
            }
        }

        internal sealed partial class RegistryKeyScope : IDisposable
        {
            public IntPtr Handle { get; }

            public RegistryKeyScope(IntPtr handle) => Handle = handle;

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    RegCloseKey(Handle);
            }
        }

        internal sealed partial class RegistryValueBuffer : IDisposable
        {
            public IntPtr Pointer { get; }
            public int Size { get; }
            public int RegistryType { get; }

            public RegistryValueBuffer(IntPtr pointer, int size, int registryType)
            {
                Pointer = pointer;
                Size = size;
                RegistryType = registryType;
            }

            public void Dispose()
            {
                if (Pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(Pointer);
            }
        }

        #endregion

        #region NetUserModals Helpers

        internal static bool TryGetUserModalsInfo<T>(int level, out T info, out int result) where T : struct
        {
            result = NetUserModalsGet(null, level, out IntPtr bufPtr);
            if (result != NERR_Success)
            {
                info = default;
                return false;
            }

            try
            {
                info = Marshal.PtrToStructure<T>(bufPtr);
                return true;
            }
            finally
            {
                NetApiBufferFree(bufPtr);
            }
        }

        internal static T GetUserModalsInfoOrThrow<T>(int level) where T : struct
        {
            if (!TryGetUserModalsInfo(level, out T info, out int result))
                throw new InvalidOperationException($"NetUserModalsGet({level}) failed with error {result}");

            return info;
        }

        internal static void SetUserModalsInfoOrThrow<T>(int level, T info) where T : struct
        {
            using var writeBuffer = new HGlobalBuffer(Marshal.SizeOf<T>());
            Marshal.StructureToPtr(info, writeBuffer.Pointer, false);
            int result = NetUserModalsSet(null, level, writeBuffer.Pointer, out int paramErr);
            if (result != NERR_Success)
                throw new InvalidOperationException($"NetUserModalsSet({level}) failed with error {result}, param={paramErr}");
        }

        #endregion

        #region LSA Policy Helpers

        internal static IntPtr OpenLsaPolicy(uint desiredAccess)
        {
            var objectAttributes = new LSA_OBJECT_ATTRIBUTES
            {
                Length = (uint)Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>()
            };

            var systemName = new LSA_UNICODE_STRING();
            uint status = LsaOpenPolicy(ref systemName, ref objectAttributes, desiredAccess, out IntPtr policyHandle);

            if (status != STATUS_SUCCESS)
            {
                int win32Error = LsaNtStatusToWinError(status);
                _logger.LogDebug($"[PolicyNativeHelpers] LsaOpenPolicy failed: NTSTATUS=0x{status:X8}, Win32={win32Error}");
                return IntPtr.Zero;
            }

            return policyHandle;
        }

        internal static LsaPolicyHandleScope? TryOpenLsaPolicyScope(uint desiredAccess)
        {
            IntPtr handle = OpenLsaPolicy(desiredAccess);
            return handle == IntPtr.Zero ? null : new LsaPolicyHandleScope(handle);
        }

        internal static LsaPolicyHandleScope OpenLsaPolicyOrThrow(uint desiredAccess, string errorMessage)
        {
            return TryOpenLsaPolicyScope(desiredAccess) ?? throw new UnauthorizedAccessException(errorMessage);
        }

        internal static bool TryQueryLsaPolicyBuffer(IntPtr policyHandle, int informationClass, out LsaMemoryScope bufferScope, out uint status)
        {
            status = LsaQueryInformationPolicy(policyHandle, informationClass, out IntPtr buffer);
            if (status != STATUS_SUCCESS)
            {
                bufferScope = null!;
                return false;
            }

            bufferScope = new LsaMemoryScope(buffer);
            return true;
        }

        #endregion

        #region Registry Helpers

        internal static bool TryOpenRegistryKey(string keyPath, int access, out RegistryKeyScope keyScope, out int result)
        {
            result = RegOpenKeyEx(HKEY_LOCAL_MACHINE, keyPath, 0, access, out IntPtr keyHandle);
            if (result != ERROR_SUCCESS)
            {
                keyScope = null!;
                return false;
            }

            keyScope = new RegistryKeyScope(keyHandle);
            return true;
        }

        internal static bool TryCreateRegistryKey(string keyPath, int access, out RegistryKeyScope keyScope, out int result)
        {
            result = RegCreateKeyEx(
                HKEY_LOCAL_MACHINE,
                keyPath,
                0,
                null,
                REG_OPTION_NON_VOLATILE,
                access,
                IntPtr.Zero,
                out IntPtr keyHandle,
                out int disposition);

            if (result != ERROR_SUCCESS)
            {
                keyScope = null!;
                return false;
            }

            _logger.LogDebug($"[PolicyNativeHelpers] RegCreateKeyEx '{keyPath}' disposition={disposition}");
            keyScope = new RegistryKeyScope(keyHandle);
            return true;
        }

        internal static RegistryKeyScope OpenRegistryKeyOrThrow(string keyPath, int access, string operation)
        {
            if (TryOpenRegistryKey(keyPath, access, out RegistryKeyScope keyScope, out int result))
                return keyScope;

            bool isWriteAccess = (access & KEY_WRITE) == KEY_WRITE;
            if (result == ERROR_FILE_NOT_FOUND && isWriteAccess)
            {
                _logger.LogDebug($"[PolicyNativeHelpers] Registry key '{keyPath}' missing for write; attempting creation");
                if (TryCreateRegistryKey(keyPath, access, out RegistryKeyScope createdKeyScope, out int createResult))
                    return createdKeyScope;

                if (createResult == ERROR_ACCESS_DENIED)
                    throw new UnauthorizedAccessException($"Failed to create registry key '{keyPath}' for {operation}: access denied (error {createResult})");

                throw new InvalidOperationException($"Failed to create registry key '{keyPath}' for {operation}: error {createResult}");
            }

            if (result == ERROR_ACCESS_DENIED)
                throw new UnauthorizedAccessException($"Failed to open registry key '{keyPath}' for {operation}: access denied (error {result})");

            if (result == ERROR_FILE_NOT_FOUND)
                throw new InvalidOperationException($"Registry key '{keyPath}' not found for {operation}.");

            throw new InvalidOperationException($"Failed to open registry key '{keyPath}' for {operation}: error {result}");
        }

        internal static bool TryReadRegistryValue(IntPtr keyHandle, string valueName, out RegistryValueBuffer valueBuffer, out int result)
        {
            const int initialSize = 4096;
            int dataSize = initialSize;
            IntPtr dataPtr = Marshal.AllocHGlobal(dataSize);

            result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out int regType, dataPtr, ref dataSize);
            if (result != ERROR_SUCCESS)
            {
                Marshal.FreeHGlobal(dataPtr);
                valueBuffer = null!;
                return false;
            }

            valueBuffer = new RegistryValueBuffer(dataPtr, dataSize, regType);
            return true;
        }

        internal static int WriteRegistryDword(IntPtr keyHandle, string valueName, int value)
        {
            using var buffer = new HGlobalBuffer(sizeof(int));
            Marshal.WriteInt32(buffer.Pointer, value);
            return RegSetValueEx(keyHandle, valueName, 0, REG_DWORD, buffer.Pointer, sizeof(int));
        }

        internal static int WriteRegistryString(IntPtr keyHandle, string valueName, string value)
        {
            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            return RegSetValueEx(keyHandle, valueName, 0, REG_SZ, data, data.Length);
        }

        internal static int WriteRegistryMultiString(IntPtr keyHandle, string valueName, string value)
        {
            string[] lines = (value ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.None);

            string normalized = string.Join('\0', lines) + "\0\0";
            byte[] data = Encoding.Unicode.GetBytes(normalized);
            return RegSetValueEx(keyHandle, valueName, 0, REG_MULTI_SZ, data, data.Length);
        }

        internal static string ReadMultiString(IntPtr ptr, int size)
        {
            var parts = new List<string>();
            int offset = 0;
            while (offset < size - 1)
            {
                string part = Marshal.PtrToStringUni(ptr + offset) ?? string.Empty;
                if (string.IsNullOrEmpty(part))
                    break;
                parts.Add(part);
                offset += (part.Length + 1) * 2;
            }
            return string.Join("\n", parts);
        }

        /// <summary>
        /// Reads a registry-based policy value.
        /// Shared by PasswordPolicyProvider, AccountLockoutPolicyProvider and SecurityOptionsPolicyProvider.
        /// </summary>
        internal static SecurityPolicyValue ReadRegistryValue(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue { Definition = definition };

            if (!TryOpenRegistryKey(definition.RegistryKeyPath, KEY_READ, out RegistryKeyScope keyScope, out int result))
            {
                if (result == ERROR_FILE_NOT_FOUND)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] Registry key '{definition.RegistryKeyPath}' not configured");
                }
                else
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] RegOpenKeyEx failed for '{definition.RegistryKeyPath}': {result}");
                }
                value.IsDefined = false;
                return value;
            }

            using (keyScope)
            {
                if (!TryReadRegistryValue(keyScope.Handle, definition.RegistryValueName, out RegistryValueBuffer valueBuffer, out result))
                {
                    if (result == ERROR_FILE_NOT_FOUND)
                    {
                        _logger.LogDebug($"[PolicyNativeHelpers] Registry value '{definition.RegistryValueName}' not configured");
                    }
                    else
                    {
                        _logger.LogDebug($"[PolicyNativeHelpers] RegQueryValueEx failed: {result}");
                    }
                    value.IsDefined = false;
                    return value;
                }

                using (valueBuffer)
                {
                    value.IsDefined = true;

                    switch (valueBuffer.RegistryType)
                    {
                        case REG_DWORD:
                            value.NumericValue = Marshal.ReadInt32(valueBuffer.Pointer);
                            break;
                        case REG_SZ:
                            string strValue = Marshal.PtrToStringUni(valueBuffer.Pointer) ?? string.Empty;
                            value.StringValue = strValue;
                            if (definition.PolicyType is SecurityPolicyType.Numeric or SecurityPolicyType.Boolean or SecurityPolicyType.Dropdown or SecurityPolicyType.BitmaskFlags)
                            {
                                if (long.TryParse(strValue, out long parsed))
                                    value.NumericValue = parsed;
                            }
                            break;
                        case REG_MULTI_SZ:
                            value.StringValue = ReadMultiString(valueBuffer.Pointer, valueBuffer.Size);
                            break;
                        case REG_BINARY:
                            if (valueBuffer.Size > 0)
                            {
                                value.NumericValue = Marshal.ReadByte(valueBuffer.Pointer);
                            }
                            break;
                        default:
                            value.StringValue = $"(unsupported registry type: {valueBuffer.RegistryType})";
                            break;
                    }

                    if (definition.Key.Equals(SupportedEncryptionTypesKey, StringComparison.OrdinalIgnoreCase))
                    {
                        long normalized = NormalizeKerberosEncryptionTypes(value.NumericValue);
                        if (normalized != value.NumericValue)
                        {
                            _logger.LogDebug($"[PolicyNativeHelpers] Normalized legacy SupportedEncryptionTypes from 0x{value.NumericValue:X} to 0x{normalized:X}");
                            value.NumericValue = normalized;
                        }
                    }

                    _logger.LogDebug($"[PolicyNativeHelpers] Registry policy '{definition.Key}' = numeric:{value.NumericValue}, string:'{value.StringValue}'");
                }
            }

            return value;
        }

        /// <summary>
        /// Writes a registry-based policy value.
        /// If the value is marked as not defined and the definition supports it,
        /// the registry value is deleted (returning to "Not Defined" state).
        /// Shared by PasswordPolicyProvider, AccountLockoutPolicyProvider and SecurityOptionsPolicyProvider.
        /// </summary>
        internal static void WriteRegistryValue(SecurityPolicyValue value)
        {
            // Handle "Not Defined" ??delete the registry value
            if (!value.IsDefined && value.Definition.AllowNotDefined)
            {
                DeleteRegistryValue(value.Definition);
                return;
            }

            using var keyScope = OpenRegistryKeyOrThrow(value.Definition.RegistryKeyPath, KEY_WRITE, "writing");

            if (value.Definition.Key.Equals(SupportedEncryptionTypesKey, StringComparison.OrdinalIgnoreCase))
            {
                value.NumericValue = NormalizeKerberosEncryptionTypes(value.NumericValue);
            }

            int result;
            switch (value.Definition.PolicyType)
            {
                case SecurityPolicyType.Numeric:
                case SecurityPolicyType.Boolean:
                case SecurityPolicyType.BitmaskFlags:
                {
                    result = WriteRegistryDword(keyScope.Handle, value.Definition.RegistryValueName, (int)value.NumericValue);
                    break;
                }
                case SecurityPolicyType.String:
                {
                    result = WriteRegistryString(keyScope.Handle, value.Definition.RegistryValueName, value.StringValue ?? string.Empty);
                    break;
                }
                case SecurityPolicyType.Dropdown:
                {
                    bool isStringDropdown = false;
                    foreach (var opt in value.Definition.DropdownOptions)
                    {
                        if (opt.Value is string)
                        {
                            isStringDropdown = true;
                            break;
                        }
                    }

                    result = isStringDropdown
                        ? WriteRegistryString(keyScope.Handle, value.Definition.RegistryValueName, value.StringValue ?? value.NumericValue.ToString())
                        : WriteRegistryDword(keyScope.Handle, value.Definition.RegistryValueName, (int)value.NumericValue);
                    break;
                }
                case SecurityPolicyType.MultiString:
                {
                    result = WriteRegistryMultiString(keyScope.Handle, value.Definition.RegistryValueName, value.StringValue ?? string.Empty);
                    break;
                }
                default:
                    throw new NotSupportedException($"Registry write type '{value.Definition.PolicyType}' is not supported.");
            }

            if (result != ERROR_SUCCESS)
                throw new InvalidOperationException($"RegSetValueEx failed for '{value.Definition.RegistryValueName}': error {result}");

            _logger.LogDebug($"[PolicyNativeHelpers] Wrote registry policy '{value.Definition.Key}'");
        }

        private static long NormalizeKerberosEncryptionTypes(long rawValue)
        {
            // Compatibility repair for earlier builds that incorrectly encoded
            // "Future encryption types" as 0x20 instead of the canonical mask.
            // If only low bits are present and bit 0x20 is set, expand it.
            if ((rawValue & KerberosLegacyFutureBit) != 0 && (rawValue & ~0x3F) == 0)
            {
                long lowFlags = rawValue & KerberosLowFlagsMask;
                return lowFlags | KerberosFutureMask;
            }

            return rawValue;
        }

        /// <summary>
        /// Deletes a registry value, returning the policy to the "Not Defined" state.
        /// This is the native equivalent of what <c>secpol.msc</c> does when a user
        /// unchecks "Define this policy setting in the database".
        /// </summary>
        internal static void DeleteRegistryValue(SecurityPolicyDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.RegistryKeyPath) || string.IsNullOrEmpty(definition.RegistryValueName))
            {
                _logger.LogDebug($"[PolicyNativeHelpers] Cannot delete value for '{definition.Key}': no registry path configured");
                return;
            }

            if (!TryOpenRegistryKey(definition.RegistryKeyPath, KEY_WRITE, out RegistryKeyScope keyScope, out int openResult))
            {
                if (openResult == ERROR_FILE_NOT_FOUND)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] Registry key not found (already undefined): '{definition.RegistryKeyPath}'");
                    return;
                }
                throw new InvalidOperationException($"RegOpenKeyEx failed for '{definition.RegistryKeyPath}': error {openResult}");
            }

            using (keyScope)
            {
                int result = RegDeleteValue(keyScope.Handle, definition.RegistryValueName);
                if (result != ERROR_SUCCESS && result != ERROR_FILE_NOT_FOUND)
                {
                    throw new InvalidOperationException($"RegDeleteValue failed for '{definition.RegistryValueName}': error {result}");
                }

                _logger.LogDebug($"[PolicyNativeHelpers] Deleted registry value '{definition.Key}' (set to Not Defined)");
            }
        }

        #endregion

        #region SID Lookup

        internal static string LookupSid(IntPtr sid)
        {
            if (sid == IntPtr.Zero || !IsValidSid(sid))
                return string.Empty;

            var name = new StringBuilder(256);
            var domain = new StringBuilder(256);
            int nameLen = name.Capacity;
            int domainLen = domain.Capacity;

            if (LookupAccountSid(null, sid, name, ref nameLen, domain, ref domainLen, out _))
            {
                string domainStr = domain.ToString();
                string nameStr = name.ToString();

                if (!string.IsNullOrEmpty(domainStr) &&
                    !domainStr.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase) &&
                    !domainStr.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{domainStr}\\{nameStr}";
                }
                return nameStr;
            }

            if (ConvertSidToStringSid(sid, out string sidString))
                return sidString;

            return string.Empty;
        }

        internal static IntPtr LookupAccountSidByName(string accountName)
        {
            int sidLen = 0;
            int domainLen = 256;
            var domain = new StringBuilder(domainLen);

            LookupAccountName(null, accountName, IntPtr.Zero, ref sidLen, domain, ref domainLen, out _);

            if (sidLen == 0)
            {
                if (ConvertStringSidToSid(accountName, out IntPtr sidPtr))
                {
                    int len = GetLengthSid(sidPtr);
                    IntPtr copy = Marshal.AllocHGlobal(len);
                    CopySid(len, copy, sidPtr);
                    LocalFree(sidPtr);
                    return copy;
                }
                return IntPtr.Zero;
            }

            IntPtr sid = Marshal.AllocHGlobal(sidLen);
            domainLen = domain.Capacity;

            if (LookupAccountName(null, accountName, sid, ref sidLen, domain, ref domainLen, out _))
                return sid;

            Marshal.FreeHGlobal(sid);
            return IntPtr.Zero;
        }

        #endregion

        #region SAM Domain Password Properties

        internal static uint ReadSamPasswordProperties()
        {
            var serverName = new LSA_UNICODE_STRING();
            int status = SamConnect(ref serverName, out IntPtr serverHandle,
                SAM_SERVER_CONNECT | SAM_SERVER_LOOKUP_DOMAIN, IntPtr.Zero);

            if (status != 0)
            {
                _logger.LogDebug($"[PolicyNativeHelpers] SamConnect failed: 0x{status:X8}");
                return 0;
            }

            try
            {
                if (!TryGetUserModalsInfo(2, out SecurityPolicyNativeMethods.USER_MODALS_INFO_2 modals2, out int netResult))
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] NetUserModalsGet(2) failed: {netResult}");
                    return 0;
                }

                IntPtr domainSid;
                domainSid = modals2.usrmod2_domain_id;

                int sidLength = GetLengthSid(domainSid);
                IntPtr sidCopy = Marshal.AllocHGlobal(sidLength);
                CopySid(sidLength, sidCopy, domainSid);
                domainSid = sidCopy;

                try
                {
                    status = SamOpenDomain(serverHandle, DOMAIN_READ_PASSWORD_PARAMETERS, domainSid, out IntPtr domainHandle);
                    if (status != 0)
                    {
                        _logger.LogDebug($"[PolicyNativeHelpers] SamOpenDomain failed: 0x{status:X8}");
                        return 0;
                    }

                    try
                    {
                        status = SamQueryInformationDomain(domainHandle, DomainPasswordInformation, out IntPtr buffer);
                        if (status != 0)
                        {
                            _logger.LogDebug($"[PolicyNativeHelpers] SamQueryInformationDomain failed: 0x{status:X8}");
                            return 0;
                        }

                        try
                        {
                            var pwdInfo = Marshal.PtrToStructure<DOMAIN_PASSWORD_INFORMATION>(buffer);
                            _logger.LogDebug($"[PolicyNativeHelpers] SAM PasswordProperties = 0x{pwdInfo.PasswordProperties:X8}");
                            return pwdInfo.PasswordProperties;
                        }
                        finally
                        {
                            SamFreeMemory(buffer);
                        }
                    }
                    finally
                    {
                        SamCloseHandle(domainHandle);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(domainSid);
                }
            }
            finally
            {
                SamCloseHandle(serverHandle);
            }
        }

        internal static void WriteSamPasswordProperty(uint flag, bool enable)
        {
            var serverName = new LSA_UNICODE_STRING();
            int status = SamConnect(ref serverName, out IntPtr serverHandle,
                SAM_SERVER_CONNECT | SAM_SERVER_LOOKUP_DOMAIN, IntPtr.Zero);
            if (status != 0)
                throw new InvalidOperationException($"SamConnect failed: 0x{status:X8}");

            try
            {
                var modals2 = GetUserModalsInfoOrThrow<SecurityPolicyNativeMethods.USER_MODALS_INFO_2>(2);

                IntPtr domainSid;
                int sidLen = GetLengthSid(modals2.usrmod2_domain_id);
                domainSid = Marshal.AllocHGlobal(sidLen);
                CopySid(sidLen, domainSid, modals2.usrmod2_domain_id);

                try
                {
                    status = SamOpenDomain(serverHandle, DOMAIN_READ_PASSWORD_PARAMETERS | DOMAIN_WRITE_PASSWORD_PARAMS,
                        domainSid, out IntPtr domainHandle);
                    if (status != 0)
                        throw new InvalidOperationException($"SamOpenDomain failed: 0x{status:X8}");

                    try
                    {
                        status = SamQueryInformationDomain(domainHandle, DomainPasswordInformation, out IntPtr buffer);
                        if (status != 0)
                            throw new InvalidOperationException($"SamQueryInformationDomain failed: 0x{status:X8}");

                        try
                        {
                            var pwdInfo = Marshal.PtrToStructure<DOMAIN_PASSWORD_INFORMATION>(buffer);

                            if (enable)
                                pwdInfo.PasswordProperties |= flag;
                            else
                                pwdInfo.PasswordProperties &= ~flag;

                            Marshal.StructureToPtr(pwdInfo, buffer, false);
                            status = SamSetInformationDomain(domainHandle, DomainPasswordInformation, buffer);
                            if (status != 0)
                                throw new InvalidOperationException($"SamSetInformationDomain failed: 0x{status:X8}");

                            _logger.LogDebug($"[PolicyNativeHelpers] SAM PasswordProperties updated: flag=0x{flag:X8}, enable={enable}");
                        }
                        finally
                        {
                            SamFreeMemory(buffer);
                        }
                    }
                    finally
                    {
                        SamCloseHandle(domainHandle);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(domainSid);
                }
            }
            finally
            {
                SamCloseHandle(serverHandle);
            }
        }

        #endregion

        #region Built-in Account Helpers

        internal static string ReadBuiltInAccountName(int rid)
        {
            try
            {
                if (!TryGetUserModalsInfo(2, out SecurityPolicyNativeMethods.USER_MODALS_INFO_2 modals2, out int netResult))
                    return rid == 500 ? "Administrator" : "Guest";

                string machineSidStr;
                if (!ConvertSidToStringSid(modals2.usrmod2_domain_id, out machineSidStr))
                    return rid == 500 ? "Administrator" : "Guest";

                string accountSidStr = $"{machineSidStr}-{rid}";
                if (!ConvertStringSidToSid(accountSidStr, out IntPtr accountSid))
                    return rid == 500 ? "Administrator" : "Guest";

                try
                {
                    string name = LookupSid(accountSid);
                    return string.IsNullOrEmpty(name) ? (rid == 500 ? "Administrator" : "Guest") : name;
                }
                finally
                {
                    LocalFree(accountSid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[PolicyNativeHelpers] ReadBuiltInAccountName({rid}) failed: {ex.Message}");
                return rid == 500 ? "Administrator" : "Guest";
            }
        }

        internal static bool ReadBuiltInAccountEnabled(int rid)
        {
            try
            {
                string accountName = ReadBuiltInAccountName(rid);
                int netResult = NetUserGetInfo(null, accountName, 1, out IntPtr bufPtr);
                if (netResult != NERR_Success)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] NetUserGetInfo failed for '{accountName}': {netResult}");
                    return false;
                }

                try
                {
                    var userInfo = Marshal.PtrToStructure<SecurityPolicyNativeMethods.USER_INFO_1>(bufPtr);
                    uint flags = userInfo.usri1_flags;
                    bool isDisabled = (flags & UF_ACCOUNTDISABLE) != 0;
                    return !isDisabled;
                }
                finally
                {
                    NetApiBufferFree(bufPtr);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[PolicyNativeHelpers] ReadBuiltInAccountEnabled({rid}) failed: {ex.Message}");
                return false;
            }
        }

        internal static void WriteBuiltInAccountEnabled(int rid, bool enable)
        {
            string accountName = ReadBuiltInAccountName(rid);

            int netResult = NetUserGetInfo(null, accountName, 1, out IntPtr bufPtr);
            if (netResult != NERR_Success)
                throw new InvalidOperationException($"NetUserGetInfo failed for '{accountName}': {netResult}");

            try
            {
                var userInfo = Marshal.PtrToStructure<SecurityPolicyNativeMethods.USER_INFO_1>(bufPtr);
                uint flags = userInfo.usri1_flags;

                if (enable)
                    flags &= ~UF_ACCOUNTDISABLE;
                else
                    flags |= UF_ACCOUNTDISABLE;

                using var writeBuffer = new HGlobalBuffer(sizeof(uint));
                Marshal.WriteInt32(writeBuffer.Pointer, (int)flags);
                netResult = NetUserSetInfo(null, accountName, 1008, writeBuffer.Pointer, out int paramErr);
                if (netResult != NERR_Success)
                    throw new InvalidOperationException($"NetUserSetInfo(1008) failed for '{accountName}': {netResult}, param={paramErr}");

                _logger.LogDebug($"[PolicyNativeHelpers] Set account '{accountName}' enabled={enable}");
            }
            finally
            {
                NetApiBufferFree(bufPtr);
            }
        }

        #endregion

        #region Token Privilege Management

        internal static void EnsurePrivilegeEnabled(string privilegeName)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogDebug($"[PolicyNativeHelpers] OpenProcessToken failed: {error}");
                throw new UnauthorizedAccessException($"OpenProcessToken failed while enabling '{privilegeName}': {error}");
            }

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.LogDebug($"[PolicyNativeHelpers] LookupPrivilegeValue failed for '{privilegeName}': {error}");
                    throw new UnauthorizedAccessException($"LookupPrivilegeValue failed for '{privilegeName}': {error}");
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.LogDebug($"[PolicyNativeHelpers] AdjustTokenPrivileges failed: {error}");
                    throw new UnauthorizedAccessException($"AdjustTokenPrivileges failed for '{privilegeName}': {error}");
                }

                int lastError = Marshal.GetLastWin32Error();
                if (lastError == ERROR_NOT_ALL_ASSIGNED)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] Privilege not assigned in token: {privilegeName}");
                    throw new UnauthorizedAccessException(
                        $"The current process token does not include '{privilegeName}'. Run OneMMC elevated (Administrator).");
                }

                if (lastError != ERROR_SUCCESS)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] AdjustTokenPrivileges post-check failed: {lastError}");
                    throw new UnauthorizedAccessException($"Could not enable '{privilegeName}': {lastError}");
                }

                _logger.LogDebug($"[PolicyNativeHelpers] Enabled privilege: {privilegeName}");
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }

        #endregion
    }
}





