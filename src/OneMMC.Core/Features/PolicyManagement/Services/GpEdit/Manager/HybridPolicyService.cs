using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Provides policy management that writes to both Registry and POL files.
    /// This ensures changes are visible in both the system and gpedit.msc.
    /// </summary>
    public sealed partial class HybridPolicyService : IPolicyService
    {
        private static ILogger _logger = NullLogger.Instance;
        private readonly string _polFilePath;
        private readonly string _gptIniPath;
        private RegistryKey? _rootKey;
        private RegistryPolicyProxy? _registrySource;
        private PolFile? _polFile;
        private bool _disposed;

        public bool IsUserPolicy { get; }
        public bool IsWritable { get; private set; }
        public bool IsInitialized => _registrySource != null;
        public string? LastError { get; private set; }

        /// <summary>
        /// Creates a new HybridPolicyService.
        /// </summary>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        public HybridPolicyService(bool isUser)
        {
            IsUserPolicy = isUser;
            var section = isUser ? "User" : "Machine";
            _polFilePath = Environment.ExpandEnvironmentVariables($@"%SYSTEMROOT%\System32\GroupPolicy\{section}\Registry.pol");
            _gptIniPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\System32\GroupPolicy\gpt.ini");
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
                // Initialize registry access
                var hive = IsUserPolicy ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                _rootKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                _registrySource = RegistryPolicyProxy.EncapsulateKey(_rootKey);

                // Initialize POL file. Reads come from the registry, so an unreadable file does not block the
                // editor; writes reload strictly first (see Reload) and fail instead of replacing it.
                LoadOrCreatePolFile(strict: false);

                // Test write access
                IsWritable = TestWriteAccess();

                LogDebug($"Initialized hybrid policy service, User: {IsUserPolicy}, Writable: {IsWritable}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorInitializeFormat, ex.Message);
                LogDebug($"[ERROR] {LastError}");
                return false;
            }
        }

        public PolicyState GetPolicyState(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            // Read from registry for current state (reflects what's actually applied)
            return PolicyProcessing.GetPolicyState(_registrySource!, policy);
        }

        public Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy)
        {
            EnsureInitialized();
            return PolicyProcessing.GetPolicyOptionStates(_registrySource!, policy);
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
                // Write to both registry AND POL file
                LogDebug($"Setting policy state: {policy.DisplayName} -> {state}");

                // 1. Write to Registry (for immediate effect)
                PolicyProcessing.SetPolicyState(_registrySource!, policy, state, options);
                LogDebug("Written to registry");

                // 2. Write to POL file (for gpedit.msc visibility)
                PolicyProcessing.SetPolicyState(_polFile!, policy, state, options);
                LogDebug("Written to POL file buffer");

                LastError = null;
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastError = IsUserPolicy
                    ? LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_User)
                    : LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine);
                LogDebug($"[ERROR] Unauthorized: {ex.Message}");
                return false;
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

            var results = new List<string>();

            try
            {
                // 1. Flush registry changes
                if (_rootKey != null)
                {
                    try
                    {
                        PInvoke.RegFlushKey(_rootKey.Handle.DangerousGetHandle());
                        results.Add("registry flushed");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Registry flush failed: {ex.Message}");
                    }
                }

                // 2. Save POL file
                if (_polFile != null)
                {
                    try
                    {
                        var polDir = Path.GetDirectoryName(_polFilePath);
                        if (!string.IsNullOrEmpty(polDir) && !Directory.Exists(polDir))
                        {
                            Directory.CreateDirectory(polDir);
                        }

                        _polFile.Save(_polFilePath);
                        results.Add("POL file saved");
                        LogDebug($"Saved POL file to: {_polFilePath}");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"POL file save failed: {ex.Message}");
                        LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorSavePolFileFormat, ex.Message);
                    }
                }

                // 3. Update gpt.ini version
                try
                {
                    UpdateGptIni();
                    results.Add("gpt.ini updated");
                    LogDebug("Updated gpt.ini");
                }
                catch (Exception ex)
                {
                    LogDebug($"gpt.ini update failed: {ex.Message}");
                }

                // 4. Broadcast setting change
                PInvoke.BroadcastSettingChange();
                results.Add("settings broadcasted");

                // 5. Trigger GP refresh if available
                if (SystemInfo.HasGroupPolicyInfrastructure())
                {
                    try
                    {
                        PInvoke.RefreshPolicyEx(!IsUserPolicy, PInvoke.RP_FORCE);
                        results.Add("GP refreshed");
                        LogDebug($"Triggered GP refresh for {(IsUserPolicy ? "User" : "Machine")}");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"GP refresh failed: {ex.Message}");
                    }
                }

                return string.Join(", ", results);
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
                LoadOrCreatePolFile(strict: true);
                LogDebug("Reloaded POL file");
            }
            catch (Exception ex)
            {
                LastError = LocalizationProvider.Current.GetFormattedString(ResourceFileNames.Policy, PolicyKeys.ErrorReloadFailedFormat, ex.Message);
                LogDebug($"[ERROR] Reload failed: {ex.Message}");
                throw;
            }
        }

        /// <param name="strict">
        /// When true, a POL file that exists but cannot be read is an error. When false it is replaced by an
        /// empty buffer, which is only safe for reading: saving that buffer would erase every policy in the file.
        /// </param>
        private void LoadOrCreatePolFile(bool strict)
        {
            if (File.Exists(_polFilePath))
            {
                try
                {
                    _polFile = PolFile.Load(_polFilePath);
                    LogDebug($"Loaded existing POL file: {_polFilePath}");
                }
                catch (Exception ex) when (!strict)
                {
                    LogDebug($"Failed to load POL file, using an empty buffer until the next reload: {ex.Message}");
                    _polFile = new PolFile();
                }
            }
            else
            {
                _polFile = new PolFile();
                LogDebug("Created new POL file buffer");
            }
        }

        private bool TestWriteAccess()
        {
            // Test registry write access
            bool registryWritable = false;
            if (_registrySource != null)
            {
                try
                {
                    const string testKey = @"Software\Policies";
                    const string testValue = "_PolicyManagerWriteTest";
                    _registrySource.SetValue(testKey, testValue, "test", RegistryValueKind.String);
                    _registrySource.DeleteValue(testKey, testValue);
                    registryWritable = true;
                }
                catch
                {
                    registryWritable = false;
                }
            }

            // Test POL file write access
            bool polWritable = false;
            try
            {
                var polDir = Path.GetDirectoryName(_polFilePath);
                if (!string.IsNullOrEmpty(polDir))
                {
                    Directory.CreateDirectory(polDir);
                }

                if (File.Exists(_polFilePath))
                {
                    using var fs = new FileStream(_polFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    polWritable = true;
                }
                else if (!string.IsNullOrEmpty(polDir))
                {
                    var tempFilePath = Path.Combine(polDir, Path.GetRandomFileName());
                    using var fs = new FileStream(
                        tempFilePath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        FileOptions.DeleteOnClose);
                    polWritable = true;
                }
            }
            catch
            {
                polWritable = false;
            }

            LogDebug($"Write access - Registry: {registryWritable}, POL: {polWritable}");
            return registryWritable && polWritable;
        }

        private void UpdateGptIni() => GptIniFile.IncrementVersion(_gptIniPath, IsUserPolicy);

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
            }
        }

        private static void LogDebug(string message)
        {
            _logger.LogDebug($"[HybridPolicyService] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;

            _rootKey?.Dispose();
            _rootKey = null;
            _registrySource = null;
            _polFile = null;
            _disposed = true;
        }
    }
}


