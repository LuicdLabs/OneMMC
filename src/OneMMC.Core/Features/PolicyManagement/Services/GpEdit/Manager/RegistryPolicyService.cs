using System;
using System.Collections.Generic;
using System.IO;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Utilities;
using OneMMC.Core.Localization;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Infrastructure.PolicyStorage;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Manager
{
    /// <summary>
    /// Provides policy management using direct registry access.
    /// This is the primary mode for Windows Home editions or when direct registry access is preferred.
    /// </summary>
    public sealed partial class RegistryPolicyService : IPolicyService
    {
        private static ILogger _logger = NullLogger.Instance;
        private readonly string _rootKeyPath;
        private RegistryKey? _rootKey;
        private RegistryPolicyProxy? _policySource;
        private bool _disposed;

        public bool IsUserPolicy { get; }
        public bool IsWritable { get; private set; }
        public bool IsInitialized => _policySource != null;
        public string? LastError { get; private set; }

        /// <summary>
        /// Creates a new RegistryPolicyService.
        /// </summary>
        /// <param name="isUser">True for user policy (HKCU), false for machine policy (HKLM).</param>
        public RegistryPolicyService(bool isUser)
        {
            IsUserPolicy = isUser;
            _rootKeyPath = isUser ? "HKCU" : "HKLM";
        }

        /// <summary>
        /// Creates a new RegistryPolicyService with a custom root key path.
        /// </summary>
        /// <param name="rootKeyPath">The root registry key path (e.g., "HKLM", "HKCU", or "HKLM\Software\Policies").</param>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        public RegistryPolicyService(string rootKeyPath, bool isUser)
        {
            IsUserPolicy = isUser;
            _rootKeyPath = rootKeyPath;
        }

        public static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        public bool Initialize()
        {
            if (_disposed)
            {
                LastError = "Service has been disposed";
                return false;
            }

            try
            {
                _rootKey = OpenRootKey(_rootKeyPath);
                if (_rootKey == null)
                {
                    LastError = $"Failed to open registry key: {_rootKeyPath}";
                    return false;
                }

                _policySource = RegistryPolicyProxy.EncapsulateKey(_rootKey);
                IsWritable = TestWriteAccess();

                LogDebug($"Initialized registry policy service for {_rootKeyPath}, Writable: {IsWritable}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Failed to initialize registry policy service: {ex.Message}";
                LogDebug($"[ERROR] {LastError}");
                return false;
            }
        }

        public PolicyState GetPolicyState(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            return PolicyProcessing.GetPolicyState(_policySource!, policy);
        }

        public Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            return PolicyProcessing.GetPolicyOptionStates(_policySource!, policy);
        }

        public bool SetPolicyState(PolicyManagerPolicy policy, PolicyState state, Dictionary<string, object>? options)
        {
            EnsureInitialized();

            if (!IsWritable)
            {
                LastError = IsUserPolicy
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_User)
                    : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine);
                return false;
            }

            try
            {
                PolicyProcessing.SetPolicyState(_policySource!, policy, state, options);
                LastError = null;
                LogDebug($"Set policy state: {policy.DisplayName} -> {state}");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastError = IsUserPolicy
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_User)
                    : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine);
                LogDebug($"[ERROR] SetPolicyState unauthorized: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"Failed to set policy state: {ex.Message}";
                LogDebug($"[ERROR] SetPolicyState failed: {ex.Message}");
                return false;
            }
        }

        public string Save()
        {
            EnsureInitialized();

            try
            {
                // Flush registry changes to disk
                if (_rootKey != null)
                {
                    PInvoke.RegFlushKey(_rootKey.Handle.DangerousGetHandle());
                }

                // Broadcast setting change notification
                PInvoke.BroadcastSettingChange();

                // If Group Policy infrastructure exists, trigger a refresh
                if (SystemInfo.HasGroupPolicyInfrastructure())
                {
                    PInvoke.RefreshPolicyEx(!IsUserPolicy, PInvoke.RP_FORCE);
                    return "saved registry changes and refreshed policy";
                }

                return "saved registry changes";
            }
            catch (Exception ex)
            {
                LastError = $"Failed to save: {ex.Message}";
                LogDebug($"[ERROR] Save failed: {ex.Message}");
                return $"save failed: {ex.Message}";
            }
        }

        public void Reload()
        {
            if (_disposed) return;

            // For registry-based access, we just need to ensure the key is still valid
            // The RegistryPolicyProxy reads directly from the registry each time
            LogDebug("Reload called - registry proxy reads live data");
        }

        private RegistryKey? OpenRootKey(string path)
        {
            var parts = path.Split(new[] { '\\' }, 2);
            var baseName = parts[0].ToUpperInvariant();

            RegistryKey baseKey = baseName switch
            {
                "HKCU" or "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
                "HKU" or "HKEY_USERS" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default),
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default),
                _ => throw new ArgumentException($"Invalid registry root: {baseName}")
            };

            if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
            {
                return baseKey.CreateSubKey(parts[1]);
            }

            return baseKey;
        }

        private bool TestWriteAccess()
        {
            if (_policySource == null) return false;

            try
            {
                const string testKey = @"Software\Policies";
                const string testValue = "_PolicyManagerWriteTest";

                _policySource.SetValue(testKey, testValue, "test", RegistryValueKind.String);
                _policySource.DeleteValue(testKey, testValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Policy service has not been initialized. Call Initialize() first.");
            }
        }

        private static void LogDebug(string message)
        {
            _logger.LogDebug($"[RegistryPolicyService] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;

            _rootKey?.Dispose();
            _rootKey = null;
            _policySource = null;
            _disposed = true;
        }
    }
}


