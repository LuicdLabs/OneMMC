using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OneMMC.Core.Infrastructure.IO;

namespace OneMMC.Models;

/// <summary>
/// Represents the application's persistent settings stored in settings.json.
/// </summary>
public class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneMMC");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "Settings.json");

    /// <summary>
    /// Gets or sets the current application theme.
    /// </summary>
    public string Theme { get; set; } = "Default";

    /// <summary>
    /// Gets or sets the date when the welcome dialog was last dismissed.
    /// Stored as ISO 8601 string (e.g. "2026-01-15").
    /// Legacy from the previous 30-day reminder behavior; no longer written or read.
    /// Kept so existing settings files continue to deserialize.
    /// </summary>
    public string? WelcomeDialogDismissedDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the welcome dialog should be hidden.
    /// </summary>
    public bool WelcomeDialogHidden { get; set; }

    /// <summary>
    /// Gets or sets the main window X position in the restored state.
    /// </summary>
    public int? MainWindowX { get; set; }

    /// <summary>
    /// Gets or sets the main window Y position in the restored state.
    /// </summary>
    public int? MainWindowY { get; set; }

    /// <summary>
    /// Gets or sets the main window width in the restored state.
    /// </summary>
    public int? MainWindowWidth { get; set; }

    /// <summary>
    /// Gets or sets the main window height in the restored state.
    /// </summary>
    public int? MainWindowHeight { get; set; }

    /// <summary>
    /// Gets or sets the main window presenter state.
    /// </summary>
    public string MainWindowState { get; set; } = "Restored";

    /// <summary>
    /// Gets or sets a value indicating whether logging runs at the deeper <c>Verbose</c> (Trace) level.
    /// </summary>
    /// <remarks>
    /// First-party <c>Debug</c> logging is always on so a user can submit a complete diagnostic log from
    /// any build; this switch is the extra step beyond that. When set, OneMMC logs at <c>Verbose</c> and
    /// the <c>Microsoft</c>/<c>System</c> framework categories drop from <c>Warning</c> to <c>Debug</c>.
    /// Off by default because that extra volume is rarely needed. See <c>doc/Logging.md</c>.
    /// </remarks>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Loads settings from the persistent Settings.json file.
    /// If the file does not exist or cannot be read or parsed, returns default settings.
    /// </summary>
    /// <remarks>
    /// Settings are loaded during startup, so a damaged file (for example one left empty by a crash or a
    /// full disk) must not stop the application from starting. The next <see cref="Save"/> replaces it.
    /// </remarks>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    /// <summary>
    /// Saves the current settings to the persistent settings.json file.
    /// </summary>
    /// <remarks>
    /// The file is replaced atomically, so a crash during the write cannot leave a truncated file. The
    /// elevated restart and the process it replaces can both write this file.
    /// </remarks>
    public void Save()
    {
        string json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
        AtomicFile.WriteAllText(SettingsFilePath, json);
    }
}

/// <summary>
/// Source-generated JSON context so settings serialization works without reflection (Native AOT compatible).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
