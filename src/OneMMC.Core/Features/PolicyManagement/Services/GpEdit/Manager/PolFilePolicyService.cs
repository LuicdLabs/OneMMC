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
    public sealed class PolFilePolicyService : IPolicyService
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
                LastError = "Service has been disposed";
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
                LastError = $"Failed to initialize POL file policy service: {ex.Message}";
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
                // Save the POL file
                _polFile!.Save(_polFilePath);
                LogDebug($"Saved POL file to: {_polFilePath}");

                // Update gpt.ini version
                if (!string.IsNullOrEmpty(_gptIniPath))
                {
                    UpdateGptIni();
                    LogDebug($"Updated gpt.ini at: {_gptIniPath}");
                }

                // Trigger policy refresh if infrastructure exists
                if (SystemInfo.HasGroupPolicyInfrastructure())
                {
                    PInvoke.RefreshPolicyEx(!IsUserPolicy, PInvoke.RP_FORCE);
                    LogDebug($"Triggered policy refresh for {(IsUserPolicy ? "User" : "Machine")} policy");
                    return "saved and refreshed via Group Policy infrastructure";
                }
                else
                {
                    // Apply directly to registry for Home editions
                    ApplyToRegistry();
                    return "applied to Registry (no Group Policy service)";
                }
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

            try
            {
                LoadOrCreatePolFile();
                LogDebug("Reloaded POL file");
            }
            catch (Exception ex)
            {
                LastError = $"Failed to reload: {ex.Message}";
                LogDebug($"[ERROR] Reload failed: {ex.Message}");
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

        private void ApplyToRegistry()
        {
            var targetHive = IsUserPolicy
                ? Microsoft.Win32.RegistryHive.CurrentUser
                : Microsoft.Win32.RegistryHive.LocalMachine;

            var targetProxy = RegistryPolicyProxy.EncapsulateKey(targetHive);

            // Load the previous version to calculate diff
            PolFile? oldPol = null;
            if (File.Exists(_polFilePath))
            {
                try
                {
                    oldPol = PolFile.Load(_polFilePath);
                }
                catch
                {
                    // Ignore errors loading old file
                }
            }

            _polFile!.ApplyDifference(oldPol, targetProxy);

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

            const string machExtensionsLine = "gPCMachineExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";
            const string userExtensionsLine = "gPCUserExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]";

            try
            {
                var gptDir = Path.GetDirectoryName(_gptIniPath);
                if (!string.IsNullOrEmpty(gptDir) && !Directory.Exists(gptDir))
                {
                    Directory.CreateDirectory(gptDir);
                }

                if (File.Exists(_gptIniPath))
                {
                    UpdateExistingGptIni(machExtensionsLine, userExtensionsLine);
                }
                else
                {
                    CreateNewGptIni(machExtensionsLine, userExtensionsLine);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to update gpt.ini: {ex.Message}");
            }
        }

        private void UpdateExistingGptIni(string machExtensionsLine, string userExtensionsLine)
        {
            var lines = File.ReadAllLines(_gptIniPath);
            using var writer = new StreamWriter(_gptIniPath, false);

            bool seenMachExts = false, seenUserExts = false, seenVersion = false;

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

        private void CreateNewGptIni(string machExtensionsLine, string userExtensionsLine)
        {
            using var writer = new StreamWriter(_gptIniPath);
            writer.WriteLine("[General]");
            writer.WriteLine(machExtensionsLine);
            writer.WriteLine(userExtensionsLine);
            writer.WriteLine("Version=" + 0x10001);
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


