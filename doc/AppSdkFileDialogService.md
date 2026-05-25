# App SDK File Dialog Service

## Overview

`AppSdkFileDialogService` is the DI-backed implementation of `IFileDialogService`.
It lives in Core and wraps Windows App SDK 2.0 storage pickers from
`Microsoft.Windows.Storage.Pickers`.

The service accepts Win32-style filter strings such as
`"Text Files\0*.txt\0All Files\0*.*\0"` and converts them to picker extension
lists. Prefix globs such as `report_*.csv` are reduced to `.csv` because the
Windows App SDK picker APIs are extension-based.

## File Location

| Item | Value |
|---|---|
| Service class | `src/ManagementTools.Core/Infrastructure/WindowsCapabilities/AppSdkFileDialogService.cs` |
| Namespace | `ManagementTools.Core.Infrastructure.WindowsCapabilities` |
| Interface | `ManagementTools.Core.Abstractions.Services.IFileDialogService` |
| DI mapping | `IFileDialogService -> AppSdkFileDialogService` via `AddManagementToolsCore(...)` |

## Windows App SDK 2.0 Behavior

This service targets Windows App SDK 2.0.1 picker APIs:

- `FileOpenPicker.FileTypeChoices` is used for grouped open filters with labels.
- `FileOpenPicker.Title`, `FileSavePicker.Title`, and `FolderPicker.Title` are used for dialog titles.
- `SettingsIdentifier` is available on file and folder pickers for picker-specific persisted state.
- `SuggestedFolder` and `SuggestedStartFolder` are used for exact folder hints where supported.
- `FileSavePicker.ShowOverwritePrompt` is exposed through `SaveFileAsync`.
- `FolderPicker(WindowId)` replaces the legacy `Windows.Storage.Pickers.FolderPicker` plus `InitializeWithWindow`.

Reference: [Windows App SDK 2.0.1 storage picker updates](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0#version-20-stable-ga-201).

## Public API

```csharp
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
```

The UI layer still owns the app window. Callers pass the HWND obtained from the
active WinUI window, usually through `WindowNative.GetWindowHandle`.

## Filter Conversion

| Win32 pattern | Picker extension |
|---|---|
| `*.txt` | `.txt` |
| `*.cer;*.crt;*.pfx` | `.cer`, `.crt`, `.pfx` |
| `*.*` or `*` | `*` for open pickers; omitted from save choices |
| `report_*.csv` | `.csv` |
| `.log` | `.log` |
| `log` | `.log` |

Open pickers preserve filter grouping through `FileTypeChoices`. Save pickers
also preserve grouped labels but skip wildcard-only choices because save
pickers require concrete extensions.

## Usage

```csharp
var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
var path = await App.GetRequiredService<IFileDialogService>().OpenFileAsync(
    hwnd,
    "Script Files\0*.vbs;*.js;*.txt\0All Files\0*.*\0",
    title: LocalizedStrings.AuthorizationRuleDialog_SelectScriptFile_Title,
    settingsIdentifier: "AuthorizationRuleScriptPicker");
```

```csharp
var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
var path = await App.GetRequiredService<IFileDialogService>().SaveFileAsync(
    hwnd,
    "Event Log Files (*.evtx)\0*.evtx\0All Files\0*.*\0",
    title: "Export Event Log",
    defaultExtension: "evtx",
    suggestedFileName: "EventLog",
    showOverwritePrompt: true);
```

## Placeholder Cleanup

`CleanupPlaceholderFile` deletes a selected save path only when the file exists
and is exactly 0 bytes. Use it only for create workflows where downstream APIs
require a non-existing path, such as VHD creation or creating a new AzMan store.
Do not use it for export or backup flows.

## Error Handling

Picker methods catch exceptions, log through `ILogger<AppSdkFileDialogService>`,
and return `null` or an empty list. Callers handle cancellation and failures the
same way by checking the returned value.

No new `Debug.WriteLine`, `Console.WriteLine`, or `Trace.WriteLine` diagnostics
should be added to this service.
