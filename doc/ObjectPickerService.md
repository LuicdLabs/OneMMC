# Directory Object Picker Service

## Overview

`DirectoryObjectPickerService` wraps the native Windows Directory Object Picker
dialog (`IDsObjectPicker`). This is the standard "Select User or Group" dialog
used by MMC snap-ins such as Local Users and Groups, Services, Security Policy,
Authorization Manager, and Windows Firewall.

The service is a Core Windows-native platform service. WinUI views still own the
active window and pass an HWND to the service.

## File Location

| Item | Value |
|---|---|
| Service class | `src/ManagementTools.Core/Infrastructure/WindowsCapabilities/DirectoryObjectPickerService.cs` |
| Namespace | `ManagementTools.Core.Infrastructure.WindowsCapabilities` |
| Result model | `DirectoryObject` |
| Options type | `DirectoryObjectPickerOptions` |
| Object type flags | `ObjectPickerTypes` |

## Public API

```csharp
public static List<DirectoryObject>? ShowDialog(
    IntPtr ownerHwnd,
    ObjectPickerTypes types = ObjectPickerTypes.UsersAndGroups,
    bool multiSelect = false);

public static List<DirectoryObject>? ShowDialog(
    IntPtr ownerHwnd,
    DirectoryObjectPickerOptions options);
```

`null` means the user canceled, initialization failed, or the native COM dialog
returned a non-success HRESULT.

## Options

`DirectoryObjectPickerOptions` controls:

- object types: users, groups, computers, or combinations
- single-select vs multi-select
- local computer, domain, workgroup, and user-entered scopes
- uplevel and downlevel well-known principals

The default behavior includes users and groups, local and domain scopes, and
well-known principals.

## Result Data

| Property | Description |
|---|---|
| `Name` | Display name returned by the picker |
| `AdsPath` | ADsPath such as `WinNT://MACHINE/User` or `LDAP://CN=User,DC=corp,DC=local` |
| `ObjectClass` | Native object class such as `user`, `group`, or `computer` |
| `Upn` | User principal name when available |
| `Sid` | Resolved SID string, or empty when resolution fails |

SID resolution uses fetched `objectSid` data first, then ADsPath parsing, then
`NTAccount.Translate` when necessary.

## Implementation Notes

- The service creates the native `IDsObjectPicker` COM object from CLSID
  `17D6CCD8-3B7B-11D2-B9E0-00C04FD8DBF7`.
- Scope and filter data is allocated with unmanaged memory and released in
  `finally` blocks.
- Selection data is extracted from `IDataObject` using the
  `CFSTR_DSOP_DS_SELECTION_LIST` clipboard format.
- CsWin32-backed APIs are used for `RegisterClipboardFormat`, `GlobalLock`,
  `GlobalUnlock`, and `ReleaseStgMedium`.
- The service must be called from a UI thread with a valid owner HWND.

## Usage

```csharp
var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
var selections = DirectoryObjectPickerService.ShowDialog(
    hwnd,
    ObjectPickerTypes.UsersAndGroups,
    multiSelect: true);

if (selections is { Count: > 0 })
{
    foreach (var selectedObject in selections)
    {
        // Use selectedObject.Name or selectedObject.Sid.
    }
}
```

## Developer Guidelines

- Use this service instead of custom user/group picker UI.
- Keep calls in WinUI views or dialogs, where the HWND is available.
- Do not call it from Core ViewModels.
- Handle `null` as cancellation or native picker failure.
- Prefer `DirectoryObject.Sid` instead of manually parsing `AdsPath`.
