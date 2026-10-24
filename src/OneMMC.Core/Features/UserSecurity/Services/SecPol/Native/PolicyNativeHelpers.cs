using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// Registry value name of the Kerberos encryption-types policy, matched on the
        /// value name because a definition's key is its full registry path.
        /// </summary>
        private const string SupportedEncryptionTypesValueName = "SupportedEncryptionTypes";
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

        internal static unsafe bool TryGetUserModalsInfo<T>(int level, out T info, out int result) where T : unmanaged
        {
            result = NetUserModalsGet(null, level, out IntPtr bufPtr);
            if (result != NERR_Success)
            {
                info = default;
                return false;
            }

            try
            {
                // USER_MODALS_INFO_* are blittable, so a direct read avoids the
                // reflection-based Marshal.PtrToStructure (AOT/trim unsafe, IL2091).
                info = System.Runtime.CompilerServices.Unsafe.Read<T>((void*)bufPtr);
                return true;
            }
            finally
            {
                NetApiBufferFree(bufPtr);
            }
        }

        internal static T GetUserModalsInfoOrThrow<T>(int level) where T : unmanaged
        {
            if (!TryGetUserModalsInfo(level, out T info, out int result))
                throw new InvalidOperationException($"NetUserModalsGet({level}) failed with error {result}");

            return info;
        }

        internal static unsafe void SetUserModalsInfoOrThrow<T>(int level, T info) where T : unmanaged
        {
            using var writeBuffer = new HGlobalBuffer(sizeof(T));
            System.Runtime.CompilerServices.Unsafe.Write((void*)writeBuffer.Pointer, info);
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

        /// <summary>
        /// Reads a registry value of any size. The buffer grows to the size the registry
        /// reports, so long values such as a logon banner or a path list are not lost to
        /// <c>ERROR_MORE_DATA</c>. The value may change between attempts, so a few retries
        /// are allowed before giving up.
        /// </summary>
        internal static bool TryReadRegistryValue(IntPtr keyHandle, string valueName, out RegistryValueBuffer valueBuffer, out int result)
        {
            const int initialSize = 4096;
            const int maxAttempts = 4;

            int dataSize = initialSize;
            IntPtr dataPtr = Marshal.AllocHGlobal(dataSize);

            result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out int regType, dataPtr, ref dataSize);
            for (int attempt = 1; result == ERROR_MORE_DATA && attempt < maxAttempts; attempt++)
            {
                Marshal.FreeHGlobal(dataPtr);
                dataPtr = Marshal.AllocHGlobal(Math.Max(dataSize, sizeof(char)));
                result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out regType, dataPtr, ref dataSize);
            }

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

        /// <summary>
        /// Writes a text policy, keeping the registry type the value already has.
        /// Windows describes a few policies with a type that does not match what ships
        /// in the registry, and silently rewriting REG_MULTI_SZ data as REG_SZ (or the
        /// reverse) would change how the component that reads it behaves. A new value
        /// takes the type Windows declares, or <paramref name="defaultType"/> if none.
        /// </summary>
        private static int WriteRegistryText(IntPtr keyHandle, SecurityPolicyDefinition definition, string text, int defaultType)
        {
            int declaredType = definition.RegistryValueType is REG_SZ or REG_MULTI_SZ
                ? definition.RegistryValueType
                : defaultType;
            int targetType = declaredType;

            if (TryReadRegistryValue(keyHandle, definition.RegistryValueName, out RegistryValueBuffer existing, out _))
            {
                using (existing)
                {
                    if (existing.RegistryType is REG_SZ or REG_MULTI_SZ)
                        targetType = existing.RegistryType;
                }

                if (targetType != declaredType)
                {
                    _logger.LogDebug(
                        $"[PolicyNativeHelpers] Keeping existing registry type {targetType} for '{definition.RegistryValueName}' (declared {declaredType})");
                }
            }

            return targetType == REG_MULTI_SZ
                ? WriteRegistryMultiString(keyHandle, definition.RegistryValueName, text)
                : WriteRegistryString(keyHandle, definition.RegistryValueName, text);
        }

        /// <summary>
        /// Writes a numeric, Boolean or choice policy in the registry type Windows declares
        /// for it. Most are REG_DWORD, but a few are not, and their consumers ignore a value
        /// of the wrong type: Winlogon reads <c>CachedLogonsCount</c> and
        /// <c>AllocateDASD</c> as REG_SZ numbers, and the LSA reads
        /// <c>FullPrivilegeAuditing</c> as a REG_BINARY byte.
        /// </summary>
        private static int WriteRegistryNumber(IntPtr keyHandle, SecurityPolicyDefinition definition, long number)
        {
            switch (definition.RegistryValueType)
            {
                case REG_SZ:
                    return WriteRegistryString(keyHandle, definition.RegistryValueName, number.ToString(CultureInfo.InvariantCulture));

                case REG_BINARY:
                {
                    byte[] data = number is >= 0 and <= byte.MaxValue
                        ? [(byte)number]
                        : BitConverter.GetBytes(unchecked((uint)number));
                    return RegSetValueEx(keyHandle, definition.RegistryValueName, 0, REG_BINARY, data, data.Length);
                }

                default:
                    return WriteRegistryDword(keyHandle, definition.RegistryValueName, unchecked((int)number));
            }
        }

        /// <summary>
        /// Reads REG_SZ data within the bounds the registry returned. Registry strings are
        /// not guaranteed to be null-terminated, so the data size, not a terminator, limits
        /// the read.
        /// </summary>
        internal static string ReadRegistryString(RegistryValueBuffer buffer)
        {
            int length = buffer.Size / sizeof(char);
            if (length <= 0)
                return string.Empty;

            string raw = Marshal.PtrToStringUni(buffer.Pointer, length);
            int end = raw.IndexOf('\0');
            return end >= 0 ? raw.Substring(0, end) : raw;
        }

        /// <summary>
        /// Reads a REG_DWORD as an unsigned value, so flags with the high bit set are not
        /// turned negative. Returns false for any other type or a short value.
        /// </summary>
        internal static bool TryReadRegistryDword(RegistryValueBuffer buffer, out uint value)
        {
            if (buffer.RegistryType == REG_DWORD && buffer.Size >= sizeof(uint))
            {
                value = unchecked((uint)Marshal.ReadInt32(buffer.Pointer));
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>
        /// Reads REG_MULTI_SZ data within the bounds the registry returned, as lines
        /// separated by <c>\n</c>. Reading stops at the empty string that ends the list.
        /// </summary>
        internal static string ReadMultiString(IntPtr ptr, int size)
        {
            int length = size / sizeof(char);
            if (length <= 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (string part in Marshal.PtrToStringUni(ptr, length).Split('\0'))
            {
                if (part.Length == 0)
                    break;

                parts.Add(part);
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
                            if (TryReadRegistryDword(valueBuffer, out uint dword))
                                value.NumericValue = dword;
                            break;
                        case REG_SZ:
                            string strValue = ReadRegistryString(valueBuffer);
                            value.StringValue = strValue;
                            if (definition.PolicyType is SecurityPolicyType.Numeric or SecurityPolicyType.Boolean or SecurityPolicyType.Dropdown or SecurityPolicyType.BitmaskFlags)
                            {
                                if (long.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
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

                    if (definition.RegistryValueName.Equals(SupportedEncryptionTypesValueName, StringComparison.OrdinalIgnoreCase))
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

            // Read access is requested alongside write access so that a text policy can
            // keep the registry type its value already has. A key that grants write but
            // not read still has to be writable, so fall back to write-only access.
            using RegistryKeyScope keyScope =
                TryOpenRegistryKey(value.Definition.RegistryKeyPath, KEY_READ | KEY_WRITE, out RegistryKeyScope readWriteScope, out _)
                    ? readWriteScope
                    : OpenRegistryKeyOrThrow(value.Definition.RegistryKeyPath, KEY_WRITE, "writing");

            if (value.Definition.RegistryValueName.Equals(SupportedEncryptionTypesValueName, StringComparison.OrdinalIgnoreCase))
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
                    result = WriteRegistryNumber(keyScope.Handle, value.Definition, value.NumericValue);
                    break;
                }
                case SecurityPolicyType.String:
                {
                    result = WriteRegistryText(keyScope.Handle, value.Definition, value.StringValue ?? string.Empty, REG_SZ);
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
                        ? WriteRegistryString(keyScope.Handle, value.Definition.RegistryValueName, value.StringValue ?? value.NumericValue.ToString(CultureInfo.InvariantCulture))
                        : WriteRegistryNumber(keyScope.Handle, value.Definition, value.NumericValue);
                    break;
                }
                case SecurityPolicyType.MultiString:
                {
                    result = WriteRegistryText(keyScope.Handle, value.Definition, value.StringValue ?? string.Empty, REG_MULTI_SZ);
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

        #region User Rights Enumeration

        /// <summary>
        /// Enumerates the privilege and logon-right constants the Security Configuration
        /// Engine knows about, in its own order. The engine's table is the authority for
        /// which user rights exist on this build of Windows.
        /// </summary>
        internal static IEnumerable<string> EnumeratePrivilegeConstants()
        {
            // No privilege constant approaches this length; the engine fails the call
            // rather than truncating if the buffer is too small.
            const int bufferLength = 256;

            // The engine's table holds a few dozen rights; the bound only guards against
            // a table that never reports its end.
            const int maxPrivileges = 256;

            for (int index = 0; index < maxPrivileges; index++)
            {
                char[] buffer = new char[bufferLength];
                int length = bufferLength;
                int status = SceLookupPrivRightName(index, buffer, ref length);
                if (status != SCESTATUS_SUCCESS)
                    break;

                int end = Array.IndexOf(buffer, '\0');
                string constant = new string(buffer, 0, end >= 0 ? end : buffer.Length).Trim();
                if (constant.Length == 0)
                    break;

                yield return constant;
            }
        }

        /// <summary>
        /// Item strings for the logon rights, which <c>LookupPrivilegeDisplayName</c>
        /// cannot resolve because they are account rights rather than privileges.
        /// The snap-in carries the same fallback.
        /// </summary>
        private static readonly Dictionary<string, int> LogonRightItemIds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SeNetworkLogonRight"] = 233,
            ["SeInteractiveLogonRight"] = 234,
            ["SeBatchLogonRight"] = 286,
            ["SeServiceLogonRight"] = 287,
            ["SeDenyInteractiveLogonRight"] = 407,
            ["SeDenyNetworkLogonRight"] = 408,
            ["SeDenyServiceLogonRight"] = 409,
            ["SeDenyBatchLogonRight"] = 410,
            ["SeDenyRemoteInteractiveLogonRight"] = 57449,
            ["SeRemoteInteractiveLogonRight"] = 57450
        };

        /// <summary>
        /// Resolves the localized name of a privilege or logon right, falling back to the
        /// constant itself if Windows offers no name for it.
        /// </summary>
        internal static string ResolveUserRightDisplayName(string privilegeConstant)
        {
            string? displayName = SecurityPolicyResourceLoader.Instance.ResolvePrivilegeDisplayName(privilegeConstant);
            if (!string.IsNullOrEmpty(displayName))
                return displayName;

            if (LogonRightItemIds.TryGetValue(privilegeConstant, out int itemId))
            {
                PolicyItemText itemText = SecurityPolicyResourceLoader.Instance.LoadItemText(itemId);
                if (itemText.HasName)
                    return itemText.Name;
            }

            _logger.LogDebug($"[PolicyNativeHelpers] No display name for user right '{privilegeConstant}'");
            return privilegeConstant;
        }

        /// <summary>
        /// Resolves the en-US name of a privilege or logon right, used only to pair it with
        /// its explain text. Returns an empty string when no such name is available.
        /// Privilege names are read by switching the calling thread's UI language, so call
        /// this while building the catalog, never on the UI thread.
        /// </summary>
        internal static string ResolveInvariantUserRightDisplayName(string privilegeConstant)
        {
            string? displayName = SecurityPolicyResourceLoader.Instance.ResolveInvariantPrivilegeDisplayName(privilegeConstant);
            if (!string.IsNullOrEmpty(displayName))
                return displayName;

            if (LogonRightItemIds.TryGetValue(privilegeConstant, out int itemId))
            {
                PolicyItemText itemText = SecurityPolicyResourceLoader.Instance.LoadInvariantItemText(itemId);
                if (itemText.HasName)
                    return itemText.Name;
            }

            return string.Empty;
        }

        #endregion

        #region SID Lookup

        internal static string LookupSid(IntPtr sid)
        {
            if (sid == IntPtr.Zero || !IsValidSid(sid))
                return string.Empty;

            Span<char> name = stackalloc char[256];
            Span<char> domain = stackalloc char[256];
            int nameLen = name.Length;
            int domainLen = domain.Length;

            if (LookupAccountSid(null, sid, name, ref nameLen, domain, ref domainLen, out _))
            {
                string domainStr = domain[..domainLen].ToString();
                string nameStr = name[..nameLen].ToString();

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
            Span<char> domain = stackalloc char[256];
            int domainLen = domain.Length;

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
            domainLen = domain.Length;

            if (LookupAccountName(null, accountName, sid, ref sidLen, domain, ref domainLen, out _))
                return sid;

            Marshal.FreeHGlobal(sid);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Resolves an account name to its string SID (<c>S-1-5-...</c>). This is the form the Security
        /// Configuration Engine's template stores user-right holders in, prefixed with <c>*</c>.
        /// Returns null when the name cannot be resolved, so the caller can keep the name verbatim —
        /// the engine accepts either.
        /// </summary>
        internal static string? ResolveAccountSidString(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return null;

            IntPtr sid = LookupAccountSidByName(accountName);
            if (sid == IntPtr.Zero)
                return null;

            try
            {
                return ConvertSidToStringSid(sid, out string stringSid) ? stringSid : null;
            }
            finally
            {
                Marshal.FreeHGlobal(sid);
            }
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

        internal static unsafe bool ReadBuiltInAccountEnabled(int rid)
        {
            try
            {
                string accountName = ReadBuiltInAccountName(rid);
                uint netResult = Windows.Win32.PInvoke.NetUserGetInfo(default, accountName, 1, out byte* buffer);
                if (netResult != NERR_Success)
                {
                    _logger.LogDebug($"[PolicyNativeHelpers] NetUserGetInfo failed for '{accountName}': {netResult}");
                    return false;
                }

                try
                {
                    uint flags = (uint)((Windows.Win32.NetworkManagement.NetManagement.USER_INFO_1*)buffer)->usri1_flags;
                    bool isDisabled = (flags & UF_ACCOUNTDISABLE) != 0;
                    return !isDisabled;
                }
                finally
                {
                    _ = Windows.Win32.PInvoke.NetApiBufferFree(buffer);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[PolicyNativeHelpers] ReadBuiltInAccountEnabled({rid}) failed: {ex.Message}");
                return false;
            }
        }

        internal static unsafe void WriteBuiltInAccountEnabled(int rid, bool enable)
        {
            string accountName = ReadBuiltInAccountName(rid);

            uint netResult = Windows.Win32.PInvoke.NetUserGetInfo(default, accountName, 1, out byte* buffer);
            if (netResult != NERR_Success)
                throw new InvalidOperationException($"NetUserGetInfo failed for '{accountName}': {netResult}");

            uint flags;
            try
            {
                flags = (uint)((Windows.Win32.NetworkManagement.NetManagement.USER_INFO_1*)buffer)->usri1_flags;
            }
            finally
            {
                _ = Windows.Win32.PInvoke.NetApiBufferFree(buffer);
            }

            if (enable)
                flags &= ~UF_ACCOUNTDISABLE;
            else
                flags |= UF_ACCOUNTDISABLE;

            var info = new Windows.Win32.NetworkManagement.NetManagement.USER_INFO_1008
            {
                usri1008_flags = (Windows.Win32.NetworkManagement.NetManagement.USER_ACCOUNT_FLAGS)flags
            };
            netResult = Windows.Win32.PInvoke.NetUserSetInfo(
                default,
                accountName,
                1008,
                new Span<byte>(&info, sizeof(Windows.Win32.NetworkManagement.NetManagement.USER_INFO_1008)),
                out uint paramErr);
            if (netResult != NERR_Success)
                throw new InvalidOperationException($"NetUserSetInfo(1008) failed for '{accountName}': {netResult}, param={paramErr}");

            _logger.LogDebug($"[PolicyNativeHelpers] Set account '{accountName}' enabled={enable}");
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





