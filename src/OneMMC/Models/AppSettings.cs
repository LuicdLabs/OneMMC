using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// Loads settings from the persistent Settings.json file.
    /// If the file does not exist, returns default settings.
    /// </summary>
    public static AppSettings Load()
    {
        AppSettings? settings = null;
        if (File.Exists(SettingsFilePath))
        {
            string json = File.ReadAllText(SettingsFilePath);
            settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        }

        return settings ?? new AppSettings();
    }

    /// <summary>
    /// Saves the current settings to the persistent settings.json file.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
        File.WriteAllText(SettingsFilePath, json);
    }
}

/// <summary>
/// Source-generated JSON context so settings serialization works without reflection (Native AOT compatible).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
