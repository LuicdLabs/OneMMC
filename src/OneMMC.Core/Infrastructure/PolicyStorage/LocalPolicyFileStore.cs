using System.IO;
using System.Text;
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
    /// <summary>
    /// gpt.ini extension group registering the EFS recovery client-side extension together with the
    /// Public Key Policies snap-in extension. Required whenever EFS recovery agent policy is written.
    /// </summary>
    public const string EfsRecoveryExtensionGroup = "[{B1BE8D72-6EAC-11D2-A4EA-00C04F79F83A}{53D6AB1D-2488-11D1-A28C-00C04FB94F17}]";

    private const int WmSettingChange = 0x001A;
    private const int HwndBroadcast = 0xFFFF;
    private const uint RefreshPolicyForce = 1;
    private const string MachineRegistryExtensionGroup = "[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";
    private const string UserRegistryExtensionGroup = "[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]";
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
    /// <param name="additionalExtensionGroups">
    /// Extra gpt.ini extension groups (formatted as <c>[{CSE-GUID}{TOOL-GUID}]</c>) merged into the
    /// scope's extension-names line in addition to the always-registered Registry extension pair.
    /// </param>
    /// <returns>A short save result description.</returns>
    public string SaveSnapshot(bool isUser, PolFile snapshot, params string[] additionalExtensionGroups)
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

        // Key deletions are applied synchronously and never persisted: a standing **DeleteKeys instruction
        // in Registry.pol would be re-executed by the Registry CSE on every refresh, silently destroying
        // keys recreated later by other tools (e.g. secpol.msc).
        List<string> deletedKeyPaths = snapshot.TakeDeleteKeyInstructions();

        snapshot.Save(policyFilePath);
        UpdateGptIni(isUser, gptIniPath, additionalExtensionGroups);
        ApplyKeyDeletionsToRegistry(isUser, deletedKeyPaths);

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

    private static void ApplyKeyDeletionsToRegistry(bool isUser, List<string> keyPaths)
    {
        if (keyPaths.Count == 0)
        {
            return;
        }

        RegistryHive targetHive = isUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        RegistryPolicyProxy targetProxy = RegistryPolicyProxy.EncapsulateKey(targetHive);
        foreach (string keyPath in keyPaths)
        {
            targetProxy.DeleteKeyTree(keyPath);
        }
    }

    private static void UpdateGptIni(bool isUser, string gptIniPath, string[] additionalExtensionGroups)
    {
        string[] machineGroups = isUser ? [] : additionalExtensionGroups;
        string[] userGroups = isUser ? additionalExtensionGroups : [];
        string machineExtensionsValue = MergeExtensionGroups(null, MachineRegistryExtensionGroup, machineGroups);
        string userExtensionsValue = MergeExtensionGroups(null, UserRegistryExtensionGroup, userGroups);

        string? gptDirectory = Path.GetDirectoryName(gptIniPath);
        if (!string.IsNullOrEmpty(gptDirectory))
        {
            Directory.CreateDirectory(gptDirectory);
        }

        if (File.Exists(gptIniPath))
        {
            UpdateExistingGptIni(isUser, gptIniPath, additionalExtensionGroups);
            return;
        }

        using StreamWriter writer = new(gptIniPath);
        writer.WriteLine("[General]");
        writer.WriteLine($"gPCMachineExtensionNames={machineExtensionsValue}");
        writer.WriteLine($"gPCUserExtensionNames={userExtensionsValue}");
        writer.WriteLine("Version=" + 0x10001);
    }

    private static void UpdateExistingGptIni(bool isUser, string gptIniPath, string[] additionalExtensionGroups)
    {
        string[] machineGroups = isUser ? [] : additionalExtensionGroups;
        string[] userGroups = isUser ? additionalExtensionGroups : [];
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

            if (line.StartsWith("gPCMachineExtensionNames=", StringComparison.OrdinalIgnoreCase))
            {
                seenMachineExtensions = true;
                writer.WriteLine("gPCMachineExtensionNames=" + MergeExtensionGroups(
                    line["gPCMachineExtensionNames=".Length..],
                    MachineRegistryExtensionGroup,
                    machineGroups));
                continue;
            }

            if (line.StartsWith("gPCUserExtensionNames=", StringComparison.OrdinalIgnoreCase))
            {
                seenUserExtensions = true;
                writer.WriteLine("gPCUserExtensionNames=" + MergeExtensionGroups(
                    line["gPCUserExtensionNames=".Length..],
                    UserRegistryExtensionGroup,
                    userGroups));
                continue;
            }

            writer.WriteLine(line);
        }

        if (!seenVersion)
        {
            writer.WriteLine("Version=" + 0x10001);
        }

        if (!seenMachineExtensions)
        {
            writer.WriteLine("gPCMachineExtensionNames=" + MergeExtensionGroups(null, MachineRegistryExtensionGroup, machineGroups));
        }

        if (!seenUserExtensions)
        {
            writer.WriteLine("gPCUserExtensionNames=" + MergeExtensionGroups(null, UserRegistryExtensionGroup, userGroups));
        }
    }

    /// <summary>
    /// Merges required extension groups into an existing gPC*ExtensionNames value. Each group is a
    /// client-side extension GUID followed by its administrative tool GUIDs; groups are keyed by the CSE
    /// GUID and the result is emitted in the ascending GUID order the Group Policy service expects.
    /// </summary>
    /// <param name="existingValue">The current attribute value, or null when the line does not exist yet.</param>
    /// <param name="registryExtensionGroup">The always-required Registry extension group.</param>
    /// <param name="additionalGroups">Extra groups to register, formatted as <c>[{CSE}{TOOL}...]</c>.</param>
    /// <returns>The merged attribute value.</returns>
    private static string MergeExtensionGroups(string? existingValue, string registryExtensionGroup, string[] additionalGroups)
    {
        SortedDictionary<string, SortedSet<string>> groups = new(StringComparer.OrdinalIgnoreCase);

        void AddGroup(string groupText)
        {
            List<string> guids = ParseGuidList(groupText);
            if (guids.Count == 0)
            {
                return;
            }

            string cseGuid = guids[0];
            if (!groups.TryGetValue(cseGuid, out SortedSet<string>? toolGuids))
            {
                toolGuids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                groups[cseGuid] = toolGuids;
            }

            foreach (string toolGuid in guids.Skip(1))
            {
                _ = toolGuids.Add(toolGuid);
            }
        }

        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            foreach (string groupText in existingValue.Split(']', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddGroup(groupText);
            }
        }

        AddGroup(registryExtensionGroup);
        foreach (string additionalGroup in additionalGroups)
        {
            AddGroup(additionalGroup);
        }

        StringBuilder result = new();
        foreach ((string cseGuid, SortedSet<string> toolGuids) in groups)
        {
            _ = result.Append('[').Append(cseGuid);
            foreach (string toolGuid in toolGuids)
            {
                _ = result.Append(toolGuid);
            }

            _ = result.Append(']');
        }

        return result.ToString();
    }

    private static List<string> ParseGuidList(string groupText)
    {
        List<string> guids = [];
        int position = 0;
        while (true)
        {
            int start = groupText.IndexOf('{', position);
            if (start < 0)
            {
                break;
            }

            int end = groupText.IndexOf('}', start);
            if (end < 0)
            {
                break;
            }

            string candidate = groupText[start..(end + 1)];
            if (Guid.TryParse(candidate, out Guid parsed))
            {
                guids.Add($"{{{parsed.ToString("D").ToUpperInvariant()}}}");
            }

            position = end + 1;
        }

        return guids;
    }
}
