using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.PolicyStorage;

/// <summary>
/// Provides raw local Group Policy file persistence for machine and user Registry.pol snapshots.
/// </summary>
public sealed class LocalPolicyFileStore
{
    private const int WmSettingChange = 0x001A;
    private const int HwndBroadcast = 0xFFFF;
    private const uint RefreshPolicyForce = 1;
    private static readonly HashSet<int> HomeEditions =
    [
        2, 3, 5, 11, 26, 42, 64, 98, 99, 100, 101
    ];

    private readonly ILogger<LocalPolicyFileStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalPolicyFileStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LocalPolicyFileStore(ILogger<LocalPolicyFileStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the local Registry.pol file path for the requested policy scope.
    /// </summary>
    /// <param name="isUser">True for user policy; false for machine policy.</param>
    /// <returns>The absolute Registry.pol file path.</returns>
    public string GetPolicyFilePath(bool isUser)
    {
        return GetPaths(isUser).polFilePath;
    }

    /// <summary>
    /// Loads the local Registry.pol snapshot for the requested policy scope.
    /// </summary>
    /// <param name="isUser">True for user policy; false for machine policy.</param>
    /// <returns>The loaded snapshot, or an empty snapshot when the file does not exist.</returns>
    public PolFile LoadSnapshot(bool isUser)
    {
        string policyFilePath = GetPolicyFilePath(isUser);
        if (!File.Exists(policyFilePath))
        {
            return new PolFile();
        }

        return PolFile.Load(policyFilePath);
    }

    /// <summary>
    /// Checks whether the local Registry.pol file is writable for the requested scope.
    /// </summary>
    /// <param name="isUser">True for user policy; false for machine policy.</param>
    /// <param name="error">Receives the write-access diagnostic when access is denied.</param>
    /// <returns><see langword="true"/> when the file can be written.</returns>
    public bool IsWritable(bool isUser, out string? error)
    {
        string policyFilePath = GetPolicyFilePath(isUser);

        try
        {
            string? directoryPath = Path.GetDirectoryName(policyFilePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            if (File.Exists(policyFilePath))
            {
                using FileStream _ = new(policyFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            else if (!string.IsNullOrEmpty(directoryPath))
            {
                string tempFilePath = Path.Combine(directoryPath, Path.GetRandomFileName());
                using FileStream _ = new(
                    tempFilePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose);
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify write access for {PolicyScope} Registry.pol.", isUser ? "user" : "machine");
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Saves a Registry.pol snapshot and applies the appropriate local refresh flow.
    /// </summary>
    /// <param name="isUser">True for user policy; false for machine policy.</param>
    /// <param name="snapshot">The snapshot to persist.</param>
    /// <returns>A short save result description.</returns>
    public string SaveSnapshot(bool isUser, PolFile snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        (string policyFilePath, string gptIniPath) = GetPaths(isUser);
        bool hasGroupPolicyInfrastructure = HasGroupPolicyInfrastructure();
        PolFile? previousSnapshot = null;

        if (!hasGroupPolicyInfrastructure && File.Exists(policyFilePath))
        {
            try
            {
                previousSnapshot = PolFile.Load(policyFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load previous Registry.pol snapshot from {PolicyFilePath}.", policyFilePath);
            }
        }

        EnsurePolicyDirectoryExists(policyFilePath);
        snapshot.Save(policyFilePath);
        UpdateGptIni(isUser, gptIniPath);

        if (hasGroupPolicyInfrastructure)
        {
            _ = Win32PInvoke.RefreshPolicyEx(!isUser, RefreshPolicyForce);
            return "saved and refreshed via Group Policy infrastructure";
        }

        ApplySnapshotToRegistry(isUser, snapshot, previousSnapshot);
        return "applied to Registry (no Group Policy service)";
    }

    private static (string polFilePath, string gptIniPath) GetPaths(bool isUser)
    {
        string section = isUser ? "User" : "Machine";
        string policyFilePath = Environment.ExpandEnvironmentVariables($@"%SYSTEMROOT%\System32\GroupPolicy\{section}\Registry.pol");
        string gptIniPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\System32\GroupPolicy\gpt.ini");
        return (policyFilePath, gptIniPath);
    }

    private static bool HasGroupPolicyInfrastructure()
    {
        _ = Win32PInvoke.GetProductInfo(6, 0, 0, 0, out var productType);
        return !HomeEditions.Contains((int)productType);
    }

    private static void EnsurePolicyDirectoryExists(string policyFilePath)
    {
        string? directoryPath = Path.GetDirectoryName(policyFilePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private static void ApplySnapshotToRegistry(bool isUser, PolFile snapshot, PolFile? previousSnapshot)
    {
        RegistryHive targetHive = isUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        RegistryPolicyProxy targetProxy = RegistryPolicyProxy.EncapsulateKey(targetHive);

        snapshot.ApplyDifference(previousSnapshot, targetProxy);

        using RegistryKey key = RegistryKey.OpenBaseKey(targetHive, RegistryView.Default);
        _ = Win32PInvoke.RegFlushKey((HKEY)key.Handle.DangerousGetHandle());
        _ = Win32PInvoke.SendNotifyMessage(new HWND(new IntPtr(HwndBroadcast)), WmSettingChange, default, default);
    }

    private static void UpdateGptIni(bool isUser, string gptIniPath)
    {
        const string machineExtensionsLine = "gPCMachineExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";
        const string userExtensionsLine = "gPCUserExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]";

        string? gptDirectory = Path.GetDirectoryName(gptIniPath);
        if (!string.IsNullOrEmpty(gptDirectory))
        {
            Directory.CreateDirectory(gptDirectory);
        }

        if (File.Exists(gptIniPath))
        {
            UpdateExistingGptIni(isUser, gptIniPath, machineExtensionsLine, userExtensionsLine);
            return;
        }

        using StreamWriter writer = new(gptIniPath);
        writer.WriteLine("[General]");
        writer.WriteLine(machineExtensionsLine);
        writer.WriteLine(userExtensionsLine);
        writer.WriteLine("Version=" + 0x10001);
    }

    private static void UpdateExistingGptIni(bool isUser, string gptIniPath, string machineExtensionsLine, string userExtensionsLine)
    {
        string[] lines = File.ReadAllLines(gptIniPath);
        using StreamWriter writer = new(gptIniPath, false);

        bool seenMachineExtensions = false;
        bool seenUserExtensions = false;
        bool seenVersion = false;

        foreach (string line in lines)
        {
            if (line.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split('=');
                if (parts.Length == 2 && int.TryParse(parts[1], out int currentVersion))
                {
                    currentVersion += isUser ? 0x10000 : 1;
                    writer.WriteLine($"Version={currentVersion}");
                }
                else
                {
                    writer.WriteLine("Version=" + 0x10001);
                }

                seenVersion = true;
                continue;
            }

            writer.WriteLine(line);
            if (line.StartsWith("gPCMachineExtensionNames=", StringComparison.OrdinalIgnoreCase))
            {
                seenMachineExtensions = true;
            }

            if (line.StartsWith("gPCUserExtensionNames=", StringComparison.OrdinalIgnoreCase))
            {
                seenUserExtensions = true;
            }
        }

        if (!seenVersion)
        {
            writer.WriteLine("Version=" + 0x10001);
        }

        if (!seenMachineExtensions)
        {
            writer.WriteLine(machineExtensionsLine);
        }

        if (!seenUserExtensions)
        {
            writer.WriteLine(userExtensionsLine);
        }
    }
}
