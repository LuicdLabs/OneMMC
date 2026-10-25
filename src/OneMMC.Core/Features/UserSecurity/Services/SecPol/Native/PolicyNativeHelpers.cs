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
    /// Native helpers shared by the Security Settings features: registry reads for the
    /// policy catalogs, the engine's user-right enumeration and names, SID/name
    /// translation, and token privilege adjustment.
    /// </summary>
    internal static partial class PolicyNativeHelpers
    {
        private static ILogger _logger = NullLogger.Instance;

        internal static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        #region Scope / RAII Helpers

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

        private static string LookupSid(IntPtr sid)
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

        private static IntPtr LookupAccountSidByName(string accountName)
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

        /// <summary>
        /// Resolves a string SID (<c>S-1-5-...</c>) to the account name shown for it, or returns the
        /// SID itself when it names no account, as for a deleted one.
        /// </summary>
        internal static string LookupSidString(string stringSid)
        {
            if (!ConvertStringSidToSid(stringSid, out IntPtr sid))
                return stringSid;

            try
            {
                string name = LookupSid(sid);
                return name.Length > 0 ? name : stringSid;
            }
            finally
            {
                LocalFree(sid);
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





