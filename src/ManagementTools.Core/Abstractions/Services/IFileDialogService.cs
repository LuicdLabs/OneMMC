using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagementTools.Core.Abstractions.Services;

public interface IFileDialogService
{
    Task<string?> OpenFileAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null);

    Task<IReadOnlyList<string>> OpenFilesAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null);

    Task<string?> SaveFileAsync(
        nint ownerWindowHandle,
        string filter,
        string? title = null,
        string? initialDirectory = null,
        string? defaultExtension = null,
        string? suggestedFileName = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null,
        bool showOverwritePrompt = true);

    Task<string?> PickFolderAsync(
        nint ownerWindowHandle,
        string? title = null,
        string? initialDirectory = null,
        string? commitButtonText = null,
        string? settingsIdentifier = null);

    void CleanupPlaceholderFile(string? filePath);
}
