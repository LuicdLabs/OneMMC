using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32.System.Registry;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.PolicyStorage
{
    /// <summary>
    /// Provides an IPolicySource implementation that directly wraps a registry key.
    /// </summary>
    public sealed class RegistryPolicyProxy : IPolicySource
    {
        private static ILogger _logger = NullLogger.Instance;
        private RegistryKey? _rootKey;

        /// <summary>
        /// Gets the encapsulated registry key.
        /// </summary>
        public RegistryKey? EncapsulatedRegistry => _rootKey;

        /// <summary>
        /// Gets the standard policy registry key paths.
        /// </summary>
        public static IReadOnlyList<string> PolicyKeys { get; } = new[]
        {
            @"software\policies",
            @"software\microsoft\windows\currentversion\policies",
            @"system\currentcontrolset\policies"
        };

        private RegistryPolicyProxy(RegistryKey rootKey)
        {
            _rootKey = rootKey ?? throw new ArgumentNullException(nameof(rootKey));
        }

        /// <summary>
        /// Creates a RegistryPolicyProxy wrapping the specified registry key.
        /// </summary>
        /// <param name="key">The registry key to wrap.</param>
        /// <returns>A new RegistryPolicyProxy instance.</returns>
        public static RegistryPolicyProxy EncapsulateKey(RegistryKey key)
        {
            return new RegistryPolicyProxy(key);
        }

        /// <summary>
        /// Creates a RegistryPolicyProxy for the specified registry hive.
        /// </summary>
        /// <param name="hive">The registry hive to wrap.</param>
        /// <returns>A new RegistryPolicyProxy instance.</returns>
        public static RegistryPolicyProxy EncapsulateKey(RegistryHive hive)
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            return new RegistryPolicyProxy(baseKey);
        }

        public static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Checks if a key path is a standard policy key.
        /// </summary>
        /// <param name="keyPath">The key path to check.</param>
        /// <returns>True if the path is a policy key.</returns>
        public static bool IsPolicyKey(string keyPath)
        {
            if (string.IsNullOrEmpty(keyPath)) return false;

            return PolicyKeys.Any(pk =>
                keyPath.StartsWith(pk + "\\", StringComparison.OrdinalIgnoreCase) ||
                keyPath.Equals(pk, StringComparison.OrdinalIgnoreCase));
        }

        #region IPolicySource Implementation

        public void DeleteValue(string key, string value)
        {
            EnsureNotDisposed();
            LogDebug($"DeleteValue: key={key}, value={value}");

            using var regKey = _rootKey!.OpenSubKey(key, writable: true);
            if (regKey is null) return;

            try
            {
                regKey.DeleteValue(value, throwOnMissingValue: false);
                FlushKey(regKey);
            }
            catch (Exception ex)
            {
                LogDebug($"[ERROR] DeleteValue failed: {ex.Message}");
            }
        }

        public void ForgetValue(string key, string value)
        {
            // The Registry has no concept of "will delete this when I see it"
            // So ForgetValue is equivalent to DeleteValue
            DeleteValue(key, value);
        }

        public void SetValue(string key, string value, object data, RegistryValueKind dataType)
        {
            EnsureNotDisposed();
            LogDebug($"SetValue: key={key}, value={value}, data={data}, type={dataType}");

            // Handle unsigned -> signed conversion for Registry SetValue
            data = ConvertToRegistryCompatible(data);

            using var regKey = _rootKey!.CreateSubKey(key);
            regKey.SetValue(value, data, dataType);
            FlushKey(regKey);
        }

        public bool ContainsValue(string key, string value)
        {
            EnsureNotDisposed();

            using var regKey = _rootKey!.OpenSubKey(key);
            if (regKey is null) return false;
            if (string.IsNullOrEmpty(value)) return true;

            return regKey.GetValueNames()
                .Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        public object? GetValue(string key, string value)
        {
            EnsureNotDisposed();

            using var regKey = _rootKey!.OpenSubKey(key, writable: false);
            if (regKey is null) return null;

            var data = regKey.GetValue(value, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return ConvertFromRegistry(data);
        }

        public List<string> GetValueNames(string key)
        {
            EnsureNotDisposed();

            using var regKey = _rootKey!.OpenSubKey(key);
            if (regKey is null) return new List<string>();

            return regKey.GetValueNames().ToList();
        }

        public bool WillDeleteValue(string key, string value)
        {
            // Direct registry access doesn't support deferred deletions
            return false;
        }

        public void ClearKey(string key)
        {
            EnsureNotDisposed();

            var valueNames = GetValueNames(key);
            foreach (var valueName in valueNames)
            {
                ForgetValue(key, valueName);
            }

            // Flush the key after clearing
            using var regKey = _rootKey!.OpenSubKey(key, writable: true);
            if (regKey is not null)
            {
                FlushKey(regKey);
            }
        }

        public void ForgetKeyClearance(string key)
        {
            // No-op for direct Registry access
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Flushes all changes to the root registry key to disk.
        /// </summary>
        public void Flush()
        {
            EnsureNotDisposed();

            try
            {
                if (_rootKey is not null && !_rootKey.Handle.IsClosed && !_rootKey.Handle.IsInvalid)
                {
                    _ = Win32PInvoke.RegFlushKey((HKEY)_rootKey.Handle.DangerousGetHandle());
                    LogDebug("Root key flushed");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Flush root key failed: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private static object ConvertToRegistryCompatible(object data)
        {
            return data switch
            {
                uint uval => unchecked((int)uval),
                ulong qval => unchecked((long)qval),
                _ => data
            };
        }

        private static object? ConvertFromRegistry(object? data)
        {
            return data switch
            {
                int ival => unchecked((uint)ival),
                long lval => unchecked((ulong)lval),
                _ => data
            };
        }

        private void FlushKey(RegistryKey key)
        {
            try
            {
                if (key is not null && !key.Handle.IsClosed && !key.Handle.IsInvalid)
                {
                    _ = Win32PInvoke.RegFlushKey((HKEY)key.Handle.DangerousGetHandle());
                }
            }
            catch (Exception ex)
            {
                LogDebug($"FlushKey failed: {ex.Message}");
            }
        }

        private void EnsureNotDisposed()
        {
            if (_rootKey is null)
            {
                throw new ObjectDisposedException(nameof(RegistryPolicyProxy));
            }
        }

        private static void LogDebug(string message)
        {
            _logger.LogDebug($"[RegistryPolicyProxy] {message}");
        }

        #endregion
    }
}


