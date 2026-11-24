using System;
using System.Collections.Generic;
using System.IO;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Utilities;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Infrastructure.PolicyStorage;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Manager
{
    /// <summary>
    /// Provides policy management using POL (Registry.pol) files.
    /// This is the standard mode for Windows Pro/Enterprise editions with full Group Policy infrastructure.
    /// </summary>
    public sealed partial class PolFilePolicyService : IPolicyService
    {
        private static ILogger _logger = NullLogger.Instance;
        private readonly string _polFilePath;
        private readonly string _gptIniPath;
        private PolFile? _polFile;
        private bool _disposed;

        public bool IsUserPolicy { get; }
        public bool IsWritable { get; private set; }
        public bool IsInitialized => _polFile != null;
        public string? LastError { get; private set; }

        /// <summary>
        /// Creates a new PolFilePolicyService for local Group Policy.
        /// </summary>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        public PolFilePolicyService(bool isUser)
        {
            IsUserPolicy = isUser;
            var section = isUser ? "User" : "Machine";
            _polFilePath = Environment.ExpandEnvironmentVariables($@"%SYSTEMROOT%\System32\GroupPolicy\{section}\Registry.pol");
            _gptIniPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\System32\GroupPolicy\gpt.ini");
        }

        /// <summary>
        /// Creates a new PolFilePolicyService with a custom POL file path.
        /// </summary>
        /// <param name="polFilePath">Path to the POL file.</param>
        /// <param name="gptIniPath">Path to the gpt.ini file (optional).</param>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        public PolFilePolicyService(string polFilePath, string? gptIniPath, bool isUser)
        {
            IsUserPolicy = isUser;
            _polFilePath = polFilePath;
            _gptIniPath = gptIniPath ?? string.Empty;
        }

        public static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        public bool Initialize()
        {
            if (_disposed)
            {
                LastError = LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.ErrorServiceDisposed);
                return false;
            }

            try
            {
                LoadOrCreatePolFile();
                TestWriteAccess();
                LogDebug($"Initialized POL file policy service: {_polFilePath}, Writable: {IsWritable}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorInitPolServiceFormat, ex.Message);
                LogDebug($"[ERROR] {LastError}");
                return false;
            }
        }

        public PolicyState GetPolicyState(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            return PolicyProcessing.GetPolicyState(_polFile!, policy);
        }

        public Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            return PolicyProcessing.GetPolicyOptionStates(_polFile!, policy);
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
                PolicyProcessing.SetPolicyState(_polFile!, policy, state, options);
                LastError = null;
                LogDebug($"Set policy state: {policy.DisplayName} -> {state}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorSetStateFormat, ex.Message);
                LogDebug($"[ERROR] SetPolicyState failed: {ex.Message}");
                return false;
            }
        }

        public string Save()
        {
            EnsureInitialized();
            LastError = null;

            try
            {
                var hasGroupPolicyInfrastructure = SystemInfo.HasGroupPolicyInfrastructure();

                // Without the Group Policy service the registry is updated by diffing against the previous
                // file, so it has to be read before the save replaces it.
                var previous = hasGroupPolicyInfrastructure ? null : TryLoadPrevious();

                _polFile!.Save(_polFilePath);
                LogDebug($"Saved POL file to: {_polFilePath}");

                UpdateGptIni();

                if (hasGroupPolicyInfrastructure)
                {
                    PInvoke.RefreshPolicyEx(!IsUserPolicy, PInvoke.RP_FORCE);
                    LogDebug($"Triggered policy refresh for {(IsUserPolicy ? "User" : "Machine")} policy");
                    return "saved and refreshed via Group Policy infrastructure";
                }

                ApplyToRegistry(previous);
                return "applied to Registry (no Group Policy service)";
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorSaveFailedFormat, ex.Message);
                LogDebug($"[ERROR] Save failed: {ex.Message}");
                return $"save failed: {ex.Message}";
            }
        }

        public void Reload()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                LoadOrCreatePolFile();
                LogDebug("Reloaded POL file");
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorReloadFailedFormat, ex.Message);
                LogDebug($"[ERROR] Reload failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a deep copy of the currently loaded policy snapshot.
        /// </summary>
        /// <returns>A duplicate <see cref="PolFile"/> instance.</returns>
        public PolFile GetSnapshot()
        {
            EnsureInitialized();
            return _polFile!.Duplicate();
        }

        /// <summary>
        /// Replaces the in-memory policy snapshot with the supplied <see cref="PolFile"/>.
        /// </summary>
        /// <param name="snapshot">The policy snapshot to persist on the next <see cref="Save"/>.</param>
        public void ReplaceSnapshot(PolFile snapshot)
        {
            EnsureInitialized();
            ArgumentNullException.ThrowIfNull(snapshot);
            _polFile = snapshot.Duplicate();
        }

        private void LoadOrCreatePolFile()
        {
            if (File.Exists(_polFilePath))
            {
                _polFile = PolFile.Load(_polFilePath);
            }
            else
            {
                _polFile = new PolFile();

                // Try to create the directory and file
                var dirPath = Path.GetDirectoryName(_polFilePath);
                if (!string.IsNullOrEmpty(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
            }
        }

        private void TestWriteAccess()
        {
            try
            {
                var dirPath = Path.GetDirectoryName(_polFilePath);
                if (!string.IsNullOrEmpty(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                if (File.Exists(_polFilePath))
                {
                    using var fs = new FileStream(_polFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
                else if (!string.IsNullOrEmpty(dirPath))
                {
                    var tempFilePath = Path.Combine(dirPath, Path.GetRandomFileName());
                    using var fs = new FileStream(
                        tempFilePath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        FileOptions.DeleteOnClose);
                }

                IsWritable = true;
            }
            catch
            {
                IsWritable = false;
            }
        }

        private PolFile? TryLoadPrevious()
        {
            if (!File.Exists(_polFilePath))
            {
                return null;
            }

            try
            {
                return PolFile.Load(_polFilePath);
            }
            catch (Exception ex)
            {
                // Without a readable previous file every entry is applied as new, which is still correct.
                LogDebug($"Could not read the previous POL file for diffing: {ex.Message}");
                return null;
            }
        }

        private void ApplyToRegistry(PolFile? previous)
        {
            var targetHive = IsUserPolicy
                ? Microsoft.Win32.RegistryHive.CurrentUser
                : Microsoft.Win32.RegistryHive.LocalMachine;

            var targetProxy = RegistryPolicyProxy.EncapsulateKey(targetHive);

            _polFile!.ApplyDifference(previous, targetProxy);

            // Flush registry changes
            using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(targetHive, Microsoft.Win32.RegistryView.Default);
            PInvoke.RegFlushKey(key.Handle.DangerousGetHandle());

            // Broadcast setting change
            PInvoke.BroadcastSettingChange();

            LogDebug("Applied diff to Registry and broadcast setting change");
        }

        private void UpdateGptIni()
        {
            if (string.IsNullOrEmpty(_gptIniPath)) return;

            try
            {
                GptIniFile.IncrementVersion(_gptIniPath, IsUserPolicy);
                LogDebug($"Updated gpt.ini at: {_gptIniPath}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to update gpt.ini: {ex.Message}");
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
            _logger.LogDebug($"[PolFilePolicyService] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _polFile = null;
            _disposed = true;
        }
    }
}


