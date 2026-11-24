using System.IO;
using System.Text;
using OneMMC.Core.Infrastructure.IO;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Utilities;

/// <summary>
/// Updates the local GPO's <c>gpt.ini</c> after a <c>Registry.pol</c> change.
/// </summary>
/// <remarks>
/// The Group Policy engine only re-applies a GPO whose version number changed, and only runs the
/// Administrative Templates client-side extension when its GUID pair is listed. The high word of
/// <c>Version</c> counts user changes and the low word counts machine changes.
/// </remarks>
internal static class GptIniFile
{
    private const string MachineExtensionsLine = "gPCMachineExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";
    private const string UserExtensionsLine = "gPCUserExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]";
    private const int InitialVersion = 0x10001;

    /// <summary>
    /// Increments the user or machine version in <paramref name="gptIniPath"/> and makes sure the
    /// Administrative Templates extensions are registered. The file is replaced atomically.
    /// </summary>
    /// <param name="gptIniPath">The full path of <c>gpt.ini</c>.</param>
    /// <param name="isUserPolicy">True to increment the user version, false for the machine version.</param>
    public static void IncrementVersion(string gptIniPath, bool isUserPolicy)
    {
        var builder = new StringBuilder();

        if (File.Exists(gptIniPath))
        {
            bool seenMachineExtensions = false, seenUserExtensions = false, seenVersion = false;

            foreach (var line in File.ReadAllLines(gptIniPath))
            {
                if (line.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('=');
                    var version = parts.Length == 2 && int.TryParse(parts[1], out var current)
                        ? current + (isUserPolicy ? 0x10000 : 1)
                        : InitialVersion;
                    builder.AppendLine($"Version={version}");
                    seenVersion = true;
                    continue;
                }

                builder.AppendLine(line);
                seenMachineExtensions |= line.StartsWith("gPCMachineExtensionNames=", StringComparison.OrdinalIgnoreCase);
                seenUserExtensions |= line.StartsWith("gPCUserExtensionNames=", StringComparison.OrdinalIgnoreCase);
            }

            if (!seenVersion) builder.AppendLine($"Version={InitialVersion}");
            if (!seenMachineExtensions) builder.AppendLine(MachineExtensionsLine);
            if (!seenUserExtensions) builder.AppendLine(UserExtensionsLine);
        }
        else
        {
            builder.AppendLine("[General]");
            builder.AppendLine(MachineExtensionsLine);
            builder.AppendLine(UserExtensionsLine);
            builder.AppendLine($"Version={InitialVersion}");
        }

        AtomicFile.WriteAllText(gptIniPath, builder.ToString());
    }
}
