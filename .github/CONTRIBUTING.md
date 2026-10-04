# Contributing to OneMMC

Thank you for helping improve OneMMC. This project is a WinUI 3 desktop application for Windows system management, so contributions must be careful about platform behavior, administrator permissions, localization, and native interop.

OneMMC is in an early dogfooding stage and can affect critical system components such as disks, services, users, certificates, and group policies. Develop and verify changes in an isolated test environment or virtual machine.

## Before You Start

- Read the [README](../README.md) for the current project overview, prerequisites, and debugging flow.
- Use the existing GitHub issue templates when reporting bugs or proposing features.
- Review the relevant reference document under [doc](../doc) before changing that area. Each subsystem
  has exactly one authoritative document — update it there rather than restating rules elsewhere:
  - [Native AOT](../doc/NativeAot.md) — COM/WMI/ADSI/PDH/XAML/JSON constraints
  - [Administrator detection](../doc/AdminDetectionSystem.md) — elevation checks and UX
  - [Memory management](../doc/MemoryManagement.md) — page teardown, DI scopes, collections
  - [Logging](../doc/Logging.md)
  - [Localization](../doc/Localization.md)
  - [Breadcrumb navigation](../doc/Breadcrumb.md)
- If you are using an AI coding assistant, also follow [.github/copilot-instructions.md](copilot-instructions.md),
  which is the normative rule set for coding conventions, architecture boundaries, and AOT compatibility.

## Development Environment

Recommended environment:

- Windows Pro edition, Windows Server series.  Home edition may lacks some MMC snap-ins features
- .NET 10 SDK
- Windows App SDK (the version pinned in [Directory.Packages.props](../Directory.Packages.props))
- Windows 10 SDK 10.0.19041.0 or newer
- Visual Studio 2026 (or newer) with WinUI, .NET desktop, and C++ desktop workloads

Open `OneMMC.slnx` in Visual Studio, set `OneMMC` as the startup project, and use the unpackaged launch profile for local debugging.

## Build and Verification

Build the main app before submitting changes:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64
```

Release publish is Native AOT (requires the MSVC toolchain for the ILC link step):

```powershell
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64
```

**Native AOT is the project's shipped deployment model.** `PublishAot` applies to every configuration (Debug and Release). New and modified code must follow the AOT compatibility rules in [.github/copilot-instructions.md](copilot-instructions.md) (§Native AOT Compatibility); the full technical reference is [doc/NativeAot.md](../doc/NativeAot.md). The AOT/trim analyzers run on every build — first-party code builds warning-clean, and changes must introduce no new AOT/trim warnings.

There are currently no test projects in this repository. For now, every change should include:

- A successful build.
- Manual verification notes for the affected feature.
- Any administrator/elevation scenario tested, if applicable.
- Any VM or OS version constraints that affected validation.

SDK, package, target framework, runtime, supported platform, package identity, and app version information are pinned in [Directory.Packages.props](../Directory.Packages.props), [Directory.Build.props](../Directory.Build.props), and the project files. Verify documentation against those files first, and if one of them changes, update related documentation in the same pull request.

## Contribution Workflow

1. Open or reference an issue for bug fixes, feature work, or behavior changes.
2. Keep changes focused. Avoid broad refactors unless they are required for the issue.
3. Match the existing project structure and naming style.
4. Run the build command above.
5. In your pull request description, include the problem, the approach, verification performed, and any known limitations.

## Project Architecture

OneMMC has two main projects:

- `src/OneMMC.Core`: ViewModels, services, models, domain logic, COM/WMI interop, and reusable Windows-native services.
- `src/OneMMC`: WinUI 3 app shell, XAML views, converters, helpers, localization resources, and UI composition.

Dependency direction is one-way: **UI → Core**. Keep presentation concerns in the UI project.

The full boundary rules — what Core may reference, ViewModel/UI separation, feature isolation,
DI-only infrastructure access, and where new code belongs — are in
[.github/copilot-instructions.md](copilot-instructions.md) (§Architecture Boundaries). Each project's
README covers its own internals: [Core](../src/OneMMC.Core/README.md), [UI](../src/OneMMC/README.md).

## Coding Rules

The normative conventions live in [.github/copilot-instructions.md](copilot-instructions.md) and apply
to human contributors too. The highlights most often missed in review:

- **WinUI 3, not WPF or UWP.** Use `DispatcherQueue.TryEnqueue` for UI marshaling and `SelectorBar`
  for tab-like navigation. Do not assume UWP APIs or app-container behavior without verifying.
- **MVVM.** `CommunityToolkit.Mvvm` with `ObservableObject`, `[ObservableProperty]` on partial
  properties, and `[RelayCommand]`. Async relay commands return `Task`, never `async void`.
  Keep code-behind minimal.
- **Dependency injection.** Resolve services in page code-behind with `App.GetRequiredService<T>()`;
  inject through constructors elsewhere. No parameterless fallback constructors, and never `new` a
  Core service or ViewModel from a page.
- **Localization.** No hardcoded user-facing strings. Bind XAML through
  `{x:Bind LocalizedStrings.<Key>}` and resolve Core strings via `ResourceKeys` +
  `ILocalizationProvider`. Update both `en-US` and `zh-TW`. See [doc/Localization.md](../doc/Localization.md).
- **Logging.** Inject `ILogger<T>`, use structured properties, and never call `Debug.WriteLine`,
  `Console.WriteLine`, or `Trace.WriteLine`. See [doc/Logging.md](../doc/Logging.md).
- **Administrator permissions.** Use `IAdminService` for checks/detection and `AdminDialogHelper` for
  all admin dialogs and InfoBars — never a custom admin dialog. See
  [doc/AdminDetectionSystem.md](../doc/AdminDetectionSystem.md).
- **Native interop.** CsWin32 is the default: add APIs to the project-level `NativeMethods.txt` and
  call the generated `Windows.Win32.PInvoke` members. Handwritten `[LibraryImport]` requires a
  documented exception in a dedicated native wrapper file.
- **Code style.** Official C#/.NET conventions, PascalCase public members, `_camelCase` private
  fields, pattern matching for null checks, string interpolation, and XML docs on public APIs.

Useful check for the logging ban:

```powershell
rg "Debug.WriteLine|Console.WriteLine|Trace.WriteLine" src/OneMMC src/OneMMC.Core
```

## Pull Request Checklist

Before requesting review, confirm:

- `dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64` succeeds.
- The change is scoped to the issue or feature being addressed.
- ViewModels do not manipulate UI elements.
- WinUI 3 APIs and patterns are used instead of WPF or unverified UWP patterns.
- Logging uses `ILogger<T>` and does not introduce direct debug, console, or trace writes.
- Administrator scenarios use `IAdminService` and `AdminDialogHelper`.
- Native interop uses CsWin32 unless a documented exception is necessary.
- New/modified code follows the Native AOT compatibility rules (no `dynamic`, no ProgID/CLSID + `Activator` COM activation, no new `System.Management`/`Microsoft.Management.Infrastructure` usage, `{x:Bind}` in new XAML), and a build (analyzers are on by default) introduces no new AOT/trim warnings for touched interop, serialization, or XAML code.
- Manual verification notes are included because there are no automated test projects yet.

