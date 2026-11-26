# Multi-Instance and Multi-Window Support

## Table of Contents

- [Overview](#overview)
- [Current State: One Window](#current-state-one-window)
- [Threading Model](#threading-model)
- [Technical Obstacles](#technical-obstacles)
  - [1. Machine-global state with snapshot-based writers](#1-machine-global-state-with-snapshot-based-writers)
  - [2. Window singletons in the shell](#2-window-singletons-in-the-shell)
  - [3. Change notification](#3-change-notification)
  - [4. Shared per-user files](#4-shared-per-user-files)
  - [5. The elevation boundary](#5-the-elevation-boundary)
- [Roadmap](#roadmap)
- [Guidance for New Code](#guidance-for-new-code)
- [References](#references)

## Overview

OneMMC is planned to support multiple windows — for example opening a snap-in in its own window, or
comparing two event logs side by side. It does not yet, and until it does, the application is limited
to **one window**: a second launch re-activates the existing window instead of opening another.

The limit exists because multiple windows cannot be supported correctly by simply allowing them. The
shell is built on single-window assumptions, and OneMMC edits live machine-wide state rather than
independent documents. Several of those single-window assumptions were also correctness problems for
the single-window app, because other tools (gpedit.msc, secpol.msc, taskschd.msc, diskpart, PowerShell,
an elevated second OneMMC process) edit the same state. Phase 0 of the roadmap fixed those first; the
remaining phases are about the shell itself.

## Current State: One Window

The restriction is implemented entirely in `src/OneMMC/Program.cs`, which replaces the
XAML-generated `Main` (`DISABLE_XAML_GENERATED_MAIN` in `OneMMC.csproj`):

1. Before `Application.Start`, the process claims the key `OneMMC.MainWindow` with the Windows App SDK
   `AppInstance.FindOrRegisterForKey`.
2. If another process already holds the key, the new process calls `AllowSetForegroundWindow` for it,
   redirects its activation with `RedirectActivationToAsync` (on a worker thread while the STA waits
   with `CoWaitForMultipleObjects`, per Microsoft's guidance), and exits without creating any XAML,
   DI container, or log file.
3. The running process receives `AppInstance.Activated`, restores its window if minimized, and
   activates it.
4. Any failure (registry unavailable, redirect error, 5-second timeout) fails open: the process starts
   normally rather than doing nothing.

"Run as administrator" (`AdminDialogHelper.RunasAdmin`, called from the navigation pane):

1. resolves the current page's unsaved edits first (`IUnsavedChangesGuard`), because the window closes
   once the elevated process starts;
2. releases the key, so the elevated process does not redirect back to the exiting one;
3. starts the elevated process (`IAdminService.RestartAsAdmin`, which returns `false` when UAC is
   declined or the launch fails);
4. on success, closes the window through the normal close path (window placement saved, services
   disposed) instead of `Application.Exit`;
5. on failure, re-claims the key and logs a warning if another process registered it meanwhile.

Inside the process there is one `MainWindow`; secondary UI is a `ContentDialog` or an owned
`ModalDialogWindow`.

## Threading Model

**Decision:** when multiple windows arrive, they all live in one process, on the single XAML UI thread
created in `App.OnLaunched`. No window gets its own thread.

This keeps a large amount of existing process-wide state correct without migration:

- `App.CurrentTheme` and the static `App.ThemeChanged` (subscribed from ~51 files);
- the `PerformanceCounterInfo` dispatcher, captured in `App.OnLaunched` before any window exists;
- static brushes such as those in `LevelToColorConverter` (DependencyObjects with thread affinity);
- the `SettingsViewModel` singleton and view models that capture `SynchronizationContext.Current`;
- the process-wide post-navigation GC debounce in `NavigationService`
  (`doc/MemoryManagement.md`), which correctly coalesces requests from all windows.

Phase 1 asserts the invariant when a window is registered. A thread per window would turn every item
above into a blocker.

## Technical Obstacles

### 1. Machine-global state with snapshot-based writers

Multi-window editors usually give each window its own document. OneMMC has no documents: every
snap-in is a view over one live machine state (Group Policy `Registry.pol` + `gpt.ini`, Windows
Firewall policy stores, Task Scheduler, Service Control Manager, SAM accounts, disk layout,
certificate stores, AzMan, printers).

Other tools (gpedit.msc, secpol.msc, taskschd.msc, diskpart, PowerShell, another OneMMC process) write
the same stores, so the rules for writers apply to the single-window app as much as to multiple
windows. OneMMC follows the semantics of the built-in MMC snap-ins:

1. **The same object: the last writer wins.** Saving a policy, a task, or a rule overwrites a change
   another tool made to that same object while it was open. The built-in tools behave the same way,
   and the conflict is rare enough that detecting it is not worth the extra reads, APIs, and prompts.
   Keeping views current (section 3) is what reduces it further.
2. **Never lose unrelated data.** A writer that rewrites a container holding other objects (a whole
   file such as `Registry.pol`) starts from the stored content, not from the snapshot loaded when the
   page opened, and fails rather than writing when the stored content cannot be read.
3. **Never act on the wrong target.** An irreversible operation addressed by a number that Windows can
   reassign checks a stable identity immediately before acting.
4. **Never leave a damaged file.** Files that other tools or processes read are replaced atomically
   (`AtomicFile`).

Phase 0 applied these rules where they were broken:

| Store | Before | Now |
|---|---|---|
| Group Policy editor (`HybridPolicyService` on Pro/Enterprise, `RegistryPolicyService` on Home) | Rewrote `Registry.pol` from the copy loaded when the page opened, discarding changes made since to **other** policies by gpedit.msc, secpol.msc, LGPO, or the Security Settings editors. If `Registry.pol` could not be parsed, an empty buffer was used and the next save erased every policy. Save errors were only logged. | Each save re-reads `Registry.pol` strictly (an unreadable file fails the save, and nothing is written). The editor dialog re-reads before it opens, so it shows the stored values. `Registry.pol` and `gpt.ini` are replaced atomically. Save errors are reported. (rules 2, 4) |
| Disk Management: Delete Volume, Clean, Mark Active | Addressed the disk and partition by number. A USB disk being added or removed, or diskpart deleting a partition, could renumber them, so the operation could act on a different disk or partition than the one shown. | Checks `MSFT_Disk.UniqueId` and the partition's starting offset immediately before the call (`DiskIdentityCheck`), and refuses with "the layout changed" on a mismatch. (rule 3) |
| `Settings.json` | `File.WriteAllText`; a damaged file stopped startup. | Replaced atomically; a damaged file loads as defaults. (rule 4) |

Still to audit against rules 2 and 3:

- Firewall rules and connection-security rules, SAM accounts, certificate stores, AzMan, printers, and
  services: check whether any writer rewrites a container from a snapshot.
- Disk Management operations addressed by drive letter (format, extend, shrink, drive-letter changes).
  Initialize is safe as it is: the provider refuses it on a disk that is already initialized.
- The Security Settings editors (Software Restriction, Public Key, and Network List Manager policies)
  already follow rule 2: they reopen the local GPO through `IGroupPolicyObject` for every save. Their
  pages still show stale values, which is a section 3 concern.

The COM+ catalog is not on this list: OneMMC never commits catalog changes (no `SaveChanges` callers).

### 2. Window singletons in the shell

| Global | Location | What breaks with a second window |
|---|---|---|
| `App.MainWindowInstance` | `App.xaml.cs` | Referenced from 46 files. It is the owner HWND in 49 `GetWindowHandle(App.MainWindowInstance)` expressions: file pickers, the object picker, the ACL editor, CryptUI and the certificate-authority picker, `PickIconDlg`, and printer property sheets. A dialog opened from window B would be owned by — and disable — window A. `ModalDialogWindow` is not affected: all 41 `ModalDialogOptions` already pass `OwnerXamlRoot`, and `MainWindowInstance` is only its last-resort fallback. `PCManagement` also uses it to navigate. |
| DI root lifetime | `App.MainWindowInstance_Closed` | Closing the main window disposes the root `ServiceProvider` and shuts down logging. With a second window open, every singleton (firewall watcher, AzMan, Task Scheduler) would be disposed under it. Disposal has to move to "last window closed". |
| `BreadcrumbNavigationService` | `Services/BreadcrumbNavigationService.cs` | Fully static and bound by `Init(...)` to one `NavigationView`, `BreadcrumbBar`, and `Frame`; a second window would take over the first window's breadcrumbs and history stacks. Its `CurrentState` enum is written but never read outside the class (only logged), so it can be deleted rather than preserved. The real per-window state is the breadcrumb list, the three history stacks, and the three control references. |
| `App.SetAppTheme` | `App.xaml.cs` | Re-themes only `MainWindowInstance.Content`. With one UI thread the process-wide `App.CurrentTheme` and `App.ThemeChanged` can stay; the fix is to apply the theme to every registered window, apply it when a window is created, and unsubscribe `ThemeChanged` when a window closes. (`IThemeService.ThemeChanged` already exists and has no subscribers.) |
| `App.ShowErrorDialog` | `App.xaml.cs` | Always shows unhandled-exception dialogs in the main window's `XamlRoot`, whichever window failed. |
| `AdminDialogHelper._isDialogOpen`, `App._isErrorDialogShowing`, the welcome-dialog check | UI helpers | Static "one dialog at a time" guards: a dialog in window A suppresses the dialog in window B. Whether WinUI's one-`ContentDialog`-at-a-time limit is per `XamlRoot` or per thread is not yet verified; if it is per thread, per-window guards alone do not help. |
| Synchronous native modals | Object picker `InvokeDialog`, ACL editor `EditSecurity`, `PickIconDlg`, CryptUI | They run nested message loops on the shared UI thread. Window B stays interactive while A's picker is open and can start its own nested modal; because nested loops unwind last-in-first-out, A's call cannot return until B's closes. These calls need a gate that disables the other windows while one is open. |
| `TaskSchedulerService` connection | DI singleton | Holds one mutable connection target. Connecting to another computer in window A would switch window B too. |
| `AzManService` open stores | DI singleton | The open-store list is shared (two getters read it without the lock). Sharing it across windows can be deliberate, as in an MMC console, but then windows need a `StoresChanged` event. |

### 3. Change notification

A view stays current only if it reloads when its store changes. Today:

| Source | Lifetime | Delivery |
|---|---|---|
| `WindowsFirewallRuleChangeService` | DI singleton; ref-counted `Subscribe()` returning `IDisposable`; the first subscriber arms `RegNotifyChangeKeyValue` on a watcher thread, the last disarms it | 750 ms debounce on a thread-pool timer; each page marshals to its own `DispatcherQueue` |
| Page `DispatcherQueueTimer`s (Shared Folders, DTC statistics and transactions) | Page-owned, stopped at page teardown | Polling on the page's UI thread |
| `PerformanceCounterInfo` | Static dispatcher; two `System.Timers.Timer`s | PDH samples marshaled to the UI thread |

`EventViewerService` has an `EventLogWatcher` API, but nothing calls it.

Earlier versions of this plan proposed an eight-state listener state machine (arming, faulted, draining,
epochs, write tokens). With every window on one UI thread, most of that is unnecessary:

- The race between subscribe and draining is already prevented: subscribe and unsubscribe both run
  under one lock in `WindowsFirewallRuleChangeService`.
- Echo suppression does not need write tokens. Pages already ignore reloads caused by their own
  commits, and a change hub that skips the publishing origin covers commits from another window.
- Use-after-dispose only needs a per-subscription `IsActive` flag checked when the callback runs on
  the UI thread.

What is still needed:

- **A change hub.** Stores with no OS notification (Task Scheduler, SAM, AzMan, `Registry.pol`,
  certificate stores) need an in-process `IChangeHub` so a commit in one window invalidates the
  others. The hub also owns OS triggers such as the firewall registry watch: armed while its
  reference count is above zero, stopped without blocking the UI thread.
- **Lifecycle discipline.** Subscribe in `Loaded`, dispose on `Unloaded`. Some firewall pages
  subscribe in their constructor today. Leaving the last firewall page joins the watcher thread on
  the UI thread (`StopCore`).
- **Refresh on activation.** A window that was in the background refreshes once when it is activated,
  instead of polling while hidden.

Notification keeps the display current; the write rules in section 1 are what keep data safe when it
is not.

### 4. Shared per-user files

| File | Writer | State |
|---|---|---|
| `%LOCALAPPDATA%\OneMMC\Settings.json` | `AppSettings.Load()` → mutate → `Save()` (theme, welcome flag, window placement) | `Save` replaces the file atomically and `Load` falls back to defaults on a damaged file, so a crash can no longer break startup. Writers still use read-modify-write, so the last writer wins; that is acceptable for these settings. Per-window placement entries are deferred to Phase 3. |
| `%LOCALAPPDATA%\OneMMC\Logs\OneMMC-<date>.log` | Serilog file sink, `shared: true` | Works, but interleaves records from several processes (the elevated restart already produces two). |

This is one reason the roadmap keeps multiple windows **inside one process** rather than allowing
multiple processes.

### 5. The elevation boundary

OneMMC can run elevated or not (`doc/AdminDetectionSystem.md`). An elevated and a non-elevated window
side by side show different subsets of the same data and run at different integrity levels; users
would edit in the wrong one. Elevation is therefore a *restart* of the window, not a second window.
Cases not handled yet:

- launching with "Run as administrator" from the shell while a non-elevated window is open redirects
  to the non-elevated window, and the elevation request is dropped;
- a non-elevated launch while an elevated window is open crosses an integrity boundary. Whether the
  Windows App SDK instance registry allows that redirect is not documented. If it does not, the
  fail-open path opens a second, non-elevated window. Not verified yet;
- elevating with a *different user's* credentials starts a process with a different
  `%LOCALAPPDATA%` and a different instance registration, so two processes are unavoidable. The write
  rules in section 1 are what keep their edits from destroying each other's unrelated data;
- when the redirect times out (5 seconds), the new process opens a second window while the first
  one may still be alive.

## Roadmap

Each phase can ship on its own. The one-window limit is not lifted all at once: Phase 3 opens it per
snap-in, and Phase 4 extends it to the rest.

### Phase 0 — Write safety and lifecycle (single-window correctness)

Done:

- The write rules in section 1, applied to the Group Policy editor (re-read `Registry.pol` before
  every save; never write over an unreadable file) and to destructive Disk Management operations
  (identity checks).
- `AtomicFile` for `Registry.pol`, `gpt.ini`, and `Settings.json`; `Settings.json` load tolerates a
  damaged file.
- Run as administrator resolves unsaved edits, handles a declined UAC prompt, closes the window
  normally, and checks the re-claimed instance key (see
  [Current State](#current-state-one-window)).

Remaining:

- Move page polling timers (Shared Folders, both DTC pages) onto a shared polling-subscription helper,
  move firewall subscriptions from constructors to `Loaded`, make the firewall watcher stop
  non-blocking, and delete the unused `EventViewerService` watcher API.
- Audit the writers listed under "Still to audit" in section 1, starting with firewall rules.
- Add the process ID to the log output template.

Exit criterion: no editor loses another tool's change to a *different* object, no irreversible
operation can act on a renumbered target, and no page creates a timer or watcher directly.

### Phase 1 — Window context instead of window globals

- **1a. Owner HWND from the element.** An extension `element.GetOwnerHwnd()` resolves the owner from
  `XamlRoot.ContentIslandEnvironment.AppWindowId` (the logic `ModalDialogWindow` already uses).
  Migrate the 49 `GetWindowHandle(App.MainWindowInstance)` expressions, turn the static picker
  helpers into ones that take an owner, and give `SecurityPolicyExport` an owner parameter. Resolve
  the owner in event handlers, never in constructors.
- **1b. Breadcrumb navigator per window.** An instance `BreadcrumbNavigator` owned by the window,
  found through the window context. Delete `CurrentState` and the unused static API, and update
  `doc/Breadcrumb.md`.
- **1c. Window registry and lifetime.** A `WindowRegistry` holding one plain `ShellWindowContext` per
  window (window, navigation service, breadcrumb navigator, dialog guard, unsaved-changes check) —
  not a DI scope: there are no scoped registrations, Microsoft.Extensions.DependencyInjection scopes
  are flat, and `PageServiceScope` creates its scope from the root, so a window scope would be
  invisible to page scopes. The registry asserts the threading model, tracks the last active
  window, resolves a context from a `XamlRoot` (walking the Win32 owner chain for
  `ModalDialogWindow`), offers `ConfirmCloseAllAsync`, and raises `LastWindowClosed`, where `App`
  disposes the service provider and logging. Apply theme changes to every registered window. Make
  the dialog guards and the unhandled-exception dialog per window. Delete `App.MainWindowInstance`.
  Add a Debug-only "New window" command to exercise all of this before any user sees it.

Exit criterion: `App.MainWindowInstance` no longer exists; `Program` and `App` reach windows only
through `WindowRegistry`; in Debug, two windows close in any order with no use-after-dispose.

### Phase 2 — Shared singleton state

- Task Scheduler: a session per connection, owned by the page, instead of one mutable connection on
  the singleton.
- AzMan: take the lock in the open-store getters and raise `StoresChanged`; keep the open-store list
  shared deliberately (pending a product decision).
- `INativeModalGate`: the services that open synchronous native modals disable the other shell
  windows while the modal is open and re-enable them in `finally`.

Exit criterion: connecting Task Scheduler to another computer in one window does not affect another;
a native modal disables every other window.

### Phase 3 — Multi-window UX behind an allowlist

- Each snap-in declares a `WindowAffinity`: `MainWindowOnly` (the default) or `AnyWindow`.
- "Open in new window" for `AnyWindow` snap-ins: a second window in the same process with its own
  `ShellWindowContext`. Opening a `MainWindowOnly` snap-in again focuses the existing window.
- The second-launch redirect in `Program.cs` stays; it changes from "activate the window" to
  "activate or open the window for the requested snap-in", and future command-line or protocol
  activations naming a snap-in are routed the same way.
- Windows refresh their current page when activated. Per-window placement entries in
  `Settings.json`; optional restore of open windows on startup.

Exit criterion: two `AnyWindow` snap-ins work independently in two windows; every snap-in declares an
affinity.

### Phase 4 — Change hub

- `IChangeHub`: each successful commit publishes a typed topic (store + scope) with its origin;
  subscriptions are tied to a page's `Loaded`/`Unloaded` and skip their own origin; OS triggers such
  as the firewall registry watch are registered as topic sources.
- Snap-ins move from `MainWindowOnly` to `AnyWindow` as each one publishes its commits and its
  writers follow the section 1 rules.

Exit criterion: `MainWindowOnly` contains only deliberate entries; a commit in one window refreshes
every other view of that store, and no view stays stale after activation.

### Phase 5 — Elevation-aware instancing

- When an elevated launch finds a non-elevated instance, send a *handoff request* rather than a plain
  activation: the running instance runs `ConfirmCloseAllAsync`, releases the key, and exits, and the
  elevated process takes over. This needs a custom activation payload (for example a protocol
  activation such as `onemmc://handoff`), because plain launch arguments cannot express the intent.
- Compare elevation using the key owner's process ID; verify cross-integrity redirection and settle
  the policy for the reverse case and for elevation as a different user.

Exit criterion: an elevated launch never ends with zero windows, or with two windows for the same user.

## Guidance for New Code

Until the roadmap is done:

- Do not create additional top-level application windows.
- Code that starts a replacement OneMMC process must resolve unsaved edits first, call
  `Program.ReleaseInstanceKey()`, and call `Program.ReclaimInstanceKey()` if the launch did not happen
  (see `AdminDialogHelper.RunasAdmin`).
- Follow the write rules in section 1: a writer that rewrites a container (a whole file) starts from
  the stored content and fails rather than writing when it cannot read it; an irreversible operation
  addressed by a number that Windows can reassign checks a stable identity first. Do not add
  same-object conflict detection; the last writer wins, as in the built-in snap-ins.
- Write files that other tools or processes read with `AtomicFile`, never `File.WriteAllText` or a
  `StreamWriter` on the destination.
- Write new listeners so they could serve several consumers: return an `IDisposable` subscription,
  capture the consumer's `DispatcherQueue` at subscribe time, and never call UI code from the OS
  notification thread (follow `WindowsFirewallRuleChangeService`). Subscribe in `Loaded`, not in a
  constructor.
- Resolve an owner HWND or `XamlRoot` from the calling element, in an event handler, rather than adding
  new `App.MainWindowInstance` references.

## References

- [Create a single-instanced WinUI app with C#](https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)
- [App instancing with the app lifecycle API](https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing)
- [Multi-instance apps with Windows App SDK](https://learn.microsoft.com/windows/apps/develop/launch/multi-instance-apps)
- [`AllowSetForegroundWindow`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow)
- [`ReplaceFileW`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew)
- `doc/AdminDetectionSystem.md`, `doc/Breadcrumb.md`, `doc/MemoryManagement.md`
