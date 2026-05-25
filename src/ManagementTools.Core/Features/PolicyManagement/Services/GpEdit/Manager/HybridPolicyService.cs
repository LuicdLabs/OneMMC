using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Native;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Utilities;
using ManagementTools.Core.Localization;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;
using ManagementTools.Core.Infrastructure.PolicyStorage;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Manager
{
    /// <summary>
    /// Provides policy management that writes to both Registry and POL files.
    /// This ensures changes are visible in both the system and gpedit.msc.
    /// </summary>
    public sealed class HybridPolicyService : IPolicyService
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
                LastError = "Service has been disposed";
                return false;
            }

            try
            {
                // Initialize registry access
                var hive = IsUserPolicy ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                _rootKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                _registrySource = RegistryPolicyProxy.EncapsulateKey(_rootKey);

                // Initialize POL file
                LoadOrCreatePolFile();

                // Test write access
                IsWritable = TestWriteAccess();

                LogDebug($"Initialized hybrid policy service, User: {IsUserPolicy}, Writable: {IsWritable}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Failed to initialize: {ex.Message}";
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
                LastError = $"Failed to set policy state: {ex.Message}";
                LogDebug($"[ERROR] SetPolicyState failed: {ex.Message}");
                return false;
            }
        }

        public string Save()
        {
            EnsureInitialized();

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
                        LastError = $"Failed to save POL file: {ex.Message}";
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
                LastError = $"Save failed: {ex.Message}";
                LogDebug($"[ERROR] Save failed: {ex.Message}");
                return $"save failed: {ex.Message}";
            }
        }

        public void Reload()
        {
            if (_disposed) return;

            try
            {
                LoadOrCreatePolFile();
                LogDebug("Reloaded POL file");
            }
            catch (Exception ex)
            {
                LastError = $"Reload failed: {ex.Message}";
                LogDebug($"[ERROR] Reload failed: {ex.Message}");
            }
        }

        private void LoadOrCreatePolFile()
        {
            if (File.Exists(_polFilePath))
            {
                try
                {
                    _polFile = PolFile.Load(_polFilePath);
                    LogDebug($"Loaded existing POL file: {_polFilePath}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to load POL file, creating new: {ex.Message}");
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

        private void UpdateGptIni()
        {
            const string machExtensionsLine = "gPCMachineExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";
            const string userExtensionsLine = "gPCUserExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]";

            var gptDir = Path.GetDirectoryName(_gptIniPath);
            if (!string.IsNullOrEmpty(gptDir) && !Directory.Exists(gptDir))
            {
                Directory.CreateDirectory(gptDir);
            }

            if (File.Exists(_gptIniPath))
            {
                var lines = File.ReadAllLines(_gptIniPath).ToList();
                bool seenMachExts = false, seenUserExts = false, seenVersion = false;

                using var writer = new StreamWriter(_gptIniPath, false);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int curVersion))
                        {
                            curVersion += IsUserPolicy ? 0x10000 : 1;
                            writer.WriteLine($"Version={curVersion}");
                        }
                        else
                        {
                            writer.WriteLine("Version=" + 0x10001);
                        }
                        seenVersion = true;
                    }
                    else
                    {
                        writer.WriteLine(line);
                        if (line.StartsWith("gPCMachineExtensionNames=", StringComparison.OrdinalIgnoreCase))
                            seenMachExts = true;
                        if (line.StartsWith("gPCUserExtensionNames=", StringComparison.OrdinalIgnoreCase))
                            seenUserExts = true;
                    }
                }

                if (!seenVersion) writer.WriteLine("Version=" + 0x10001);
                if (!seenMachExts) writer.WriteLine(machExtensionsLine);
                if (!seenUserExts) writer.WriteLine(userExtensionsLine);
            }
            else
            {
                using var writer = new StreamWriter(_gptIniPath);
                writer.WriteLine("[General]");
                writer.WriteLine(machExtensionsLine);
                writer.WriteLine(userExtensionsLine);
                writer.WriteLine("Version=" + 0x10001);
            }
        }

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


