using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace OneMMC.Core.Infrastructure.WindowsCapabilities;

/// <summary>
/// Provides file and folder dialogs through Windows App SDK storage pickers.
/// </summary>
/// <remarks>
/// The service accepts Win32-style filter strings and converts pattern entries to the
/// extension lists required by <see cref="FileOpenPicker.FileTypeChoices"/> and
/// <see cref="FileSavePicker.FileTypeChoices"/>.
/// </remarks>
public sealed class AppSdkFileDialogService : IFileDialogService
{
    private const string AllFilesLabel = "All Files";
    private const string AllFilesWildcard = "*";

    private readonly ILogger<AppSdkFileDialogService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSdkFileDialogService"/> class.
    /// </summary>
    /// <param name="logger">The logger used for picker diagnostics.</param>
    public AppSdkFileDialogService(ILogger<AppSdkFileDialogService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> OpenFileAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null)
    {
        try
        {
            var picker = new FileOpenPicker(GetWindowId(ownerWindowHandle));
            ApplyTextOptions(picker, title, commitButtonText, settingsIdentifier);
            ApplySuggestedFolder(picker, initialDirectory);
            AddOpenFileTypeChoices(picker, filter);

            var result = await picker.PickSingleFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppSdkFileDialogService] OpenFileAsync failed.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> OpenFilesAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null)
    {
        try
        {
            var picker = new FileOpenPicker(GetWindowId(ownerWindowHandle));
            ApplyTextOptions(picker, title, commitButtonText, settingsIdentifier);
            ApplySuggestedFolder(picker, initialDirectory);
            AddOpenFileTypeChoices(picker, filter);

            var result = await picker.PickMultipleFilesAsync();
            return result.Select(file => file.Path).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppSdkFileDialogService] OpenFilesAsync failed.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(
        nint ownerWindowHandle,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null)
    {
        try
        {
            var picker = new FolderPicker(GetWindowId(ownerWindowHandle));
            ApplyTextOptions(picker, title, commitButtonText, settingsIdentifier);
            ApplySuggestedFolder(picker, initialDirectory);

            var result = await picker.PickSingleFolderAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppSdkFileDialogService] PickFolderAsync failed.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> SaveFileAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? defaultExtension = null,
        string? suggestedFileName = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null,
        bool showOverwritePrompt = true)
    {
        try
        {
            var picker = new FileSavePicker(GetWindowId(ownerWindowHandle))
            {
                ShowOverwritePrompt = showOverwritePrompt
            };

            ApplyTextOptions(picker, title, commitButtonText, settingsIdentifier);
            ApplySuggestedStartFolder(picker, initialDirectory);

            var choices = BuildSaveFileTypeChoices(filter)
                .Where(choice => choice.Extensions.Count > 0)
                .ToList();

            if (choices.Count == 0 && !string.IsNullOrWhiteSpace(defaultExtension))
            {
                choices.Add(new FileTypeChoice(AllFilesLabel, [NormalizeExtension(defaultExtension)]));
            }

            foreach (var choice in choices)
            {
                var label = MakeUniqueLabel(choice.Label, picker.FileTypeChoices.Keys);
                picker.FileTypeChoices.Add(label, choice.Extensions);
            }

            if (!string.IsNullOrWhiteSpace(defaultExtension))
            {
                picker.DefaultFileExtension = NormalizeExtension(defaultExtension);
            }

            if (!string.IsNullOrWhiteSpace(suggestedFileName))
            {
                picker.SuggestedFileName = suggestedFileName;
            }

            var result = await picker.PickSaveFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppSdkFileDialogService] SaveFileAsync failed.");
            return null;
        }
    }

    /// <inheritdoc />
    public void CleanupPlaceholderFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                info.Delete();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AppSdkFileDialogService] Failed to clean up picker placeholder file at {Path}.", filePath);
        }
    }

    private static void ApplyTextOptions(FileOpenPicker picker, string? title, string? commitButtonText, string? settingsIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            picker.Title = title;
        }

        if (!string.IsNullOrWhiteSpace(commitButtonText))
        {
            picker.CommitButtonText = commitButtonText;
        }

        if (!string.IsNullOrWhiteSpace(settingsIdentifier))
        {
            picker.SettingsIdentifier = settingsIdentifier;
        }
    }

    private static void ApplyTextOptions(FileSavePicker picker, string? title, string? commitButtonText, string? settingsIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            picker.Title = title;
        }

        if (!string.IsNullOrWhiteSpace(commitButtonText))
        {
            picker.CommitButtonText = commitButtonText;
        }

        if (!string.IsNullOrWhiteSpace(settingsIdentifier))
        {
            picker.SettingsIdentifier = settingsIdentifier;
        }
    }

    private static void ApplyTextOptions(FolderPicker picker, string? title, string? commitButtonText, string? settingsIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            picker.Title = title;
        }

        if (!string.IsNullOrWhiteSpace(commitButtonText))
        {
            picker.CommitButtonText = commitButtonText;
        }

        if (!string.IsNullOrWhiteSpace(settingsIdentifier))
        {
            picker.SettingsIdentifier = settingsIdentifier;
        }
    }

    private void ApplySuggestedFolder(FileOpenPicker picker, string? initialDirectory)
    {
        if (TryGetExistingDirectory(initialDirectory, out var folder))
        {
            picker.SuggestedFolder = folder;
            picker.SuggestedStartFolder = folder;
            return;
        }

        if (TryMapSuggestedStartLocation(initialDirectory, out var location))
        {
            picker.SuggestedStartLocation = location;
        }
    }

    private void ApplySuggestedFolder(FolderPicker picker, string? initialDirectory)
    {
        if (TryGetExistingDirectory(initialDirectory, out var folder))
        {
            picker.SuggestedFolder = folder;
            picker.SuggestedStartFolder = folder;
            return;
        }

        if (TryMapSuggestedStartLocation(initialDirectory, out var location))
        {
            picker.SuggestedStartLocation = location;
        }
    }

    private void ApplySuggestedStartFolder(FileSavePicker picker, string? initialDirectory)
    {
        if (TryGetExistingDirectory(initialDirectory, out var folder))
        {
            picker.SuggestedStartFolder = folder;
            return;
        }

        if (TryMapSuggestedStartLocation(initialDirectory, out var location))
        {
            picker.SuggestedStartLocation = location;
        }
    }

    private static void AddOpenFileTypeChoices(FileOpenPicker picker, string filter)
    {
        foreach (var choice in BuildOpenFileTypeChoices(filter))
        {
            var label = MakeUniqueLabel(choice.Label, picker.FileTypeChoices.Keys);
            picker.FileTypeChoices.Add(label, choice.Extensions);
        }
    }

    private static IReadOnlyList<FileTypeChoice> BuildOpenFileTypeChoices(string filter)
    {
        var entries = ParseFilterEntries(filter);
        var result = new List<FileTypeChoice>();

        foreach (var entry in entries)
        {
            var extensions = entry.Extensions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (extensions.Count == 0)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(entry.Label) ? AllFilesLabel : entry.Label;
            result.Add(new FileTypeChoice(label, extensions));
        }

        return result.Count > 0
            ? result
            : [new FileTypeChoice(AllFilesLabel, [AllFilesWildcard])];
    }

    private static IReadOnlyList<FileTypeChoice> BuildSaveFileTypeChoices(string filter)
    {
        var entries = ParseFilterEntries(filter);
        var result = new List<FileTypeChoice>();

        foreach (var entry in entries)
        {
            var saveExtensions = entry.Extensions
                .Where(ext => ext != AllFilesWildcard)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (saveExtensions.Count == 0)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(entry.Label) ? AllFilesLabel : entry.Label;
            result.Add(new FileTypeChoice(label, saveExtensions));
        }

        return result;
    }

    private static IReadOnlyList<FileTypeChoice> ParseFilterEntries(string filter)
    {
        var normalized = NormalizeFilterString(filter);
        var segments = normalized.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        var entries = new List<FileTypeChoice>();
        for (var index = 0; index < segments.Length; index += 2)
        {
            var label = segments[index];
            var pattern = index + 1 < segments.Length ? segments[index + 1] : string.Empty;
            entries.Add(new FileTypeChoice(label, ParseExtensions(pattern)));
        }

        return entries;
    }

    private static List<string> ParseExtensions(string patternGroup)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(patternGroup))
        {
            return result;
        }

        var patterns = patternGroup.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns)
        {
            var normalized = NormalizePatternToExtension(pattern);
            if (normalized is not null && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static string? NormalizePatternToExtension(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var value = pattern.Trim();
        if (value is "*" or "*.*")
        {
            return AllFilesWildcard;
        }

        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            return "." + value[2..].Trim();
        }

        if (value.StartsWith(".", StringComparison.Ordinal))
        {
            return value;
        }

        if (value.StartsWith("*", StringComparison.Ordinal))
        {
            value = value.TrimStart('*');
            if (string.IsNullOrWhiteSpace(value))
            {
                return AllFilesWildcard;
            }
        }

        if (value.Contains('.', StringComparison.Ordinal))
        {
            var extension = Path.GetExtension(value);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension;
            }
        }

        return "." + value.TrimStart('.');
    }

    private static string NormalizeExtension(string extension)
    {
        var ext = extension.Trim();
        if (ext.StartsWith(".", StringComparison.Ordinal))
        {
            return ext;
        }

        if (ext.StartsWith("*.", StringComparison.Ordinal))
        {
            return "." + ext[2..];
        }

        return "." + ext.TrimStart('*').TrimStart('.');
    }

    private static string MakeUniqueLabel(string label, IEnumerable<string> existingLabels)
    {
        var baseLabel = string.IsNullOrWhiteSpace(label) ? AllFilesLabel : label.Trim();
        var set = new HashSet<string>(existingLabels, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseLabel))
        {
            return baseLabel;
        }

        var suffix = 2;
        var current = $"{baseLabel} ({suffix})";
        while (set.Contains(current))
        {
            suffix++;
            current = $"{baseLabel} ({suffix})";
        }

        return current;
    }

    private bool TryGetExistingDirectory(string? initialDirectory, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(initialDirectory))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(initialDirectory);
            if (Directory.Exists(fullPath))
            {
                folder = fullPath;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AppSdkFileDialogService] Failed to normalize suggested folder {Path}.", initialDirectory);
        }

        return false;
    }

    private static WindowId GetWindowId(nint ownerWindowHandle) =>
        Win32Interop.GetWindowIdFromWindow(ownerWindowHandle);

    private bool TryMapSuggestedStartLocation(string? initialDirectory, out PickerLocationId location)
    {
        location = PickerLocationId.Unspecified;
        if (string.IsNullOrWhiteSpace(initialDirectory))
        {
            return false;
        }

        string path;
        try
        {
            path = Path.GetFullPath(initialDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AppSdkFileDialogService] Failed to map suggested start location {Path}.", initialDirectory);
            return false;
        }

        if (PathEquals(path, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)))
        {
            location = PickerLocationId.DocumentsLibrary;
            return true;
        }

        if (PathEquals(path, Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
        {
            location = PickerLocationId.Desktop;
            return true;
        }

        if (PathEquals(path, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)))
        {
            location = PickerLocationId.PicturesLibrary;
            return true;
        }

        if (PathEquals(path, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)))
        {
            location = PickerLocationId.MusicLibrary;
            return true;
        }

        if (PathEquals(path, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)))
        {
            location = PickerLocationId.VideosLibrary;
            return true;
        }

        return false;
    }

    private static bool PathEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var normalizedLeft = left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFilterString(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return "All Files\0*.*";
        }

        return filter.Replace("\\0", "\0").TrimEnd('\0');
    }

    private sealed record FileTypeChoice(string Label, List<string> Extensions);
}
