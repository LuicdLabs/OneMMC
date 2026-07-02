# Contributing to OneMMC

Thank you for helping improve OneMMC. This project is a WinUI 3 desktop application for Windows system management, so contributions must be careful about platform behavior, administrator permissions, localization, and native interop.

OneMMC is in an early dogfooding stage and can affect critical system components such as disks, services, users, certificates, and group policies. Develop and verify changes in an isolated test environment or virtual machine.

## Before You Start

- Read the [README](../README.md) for the current project overview, prerequisites, and debugging flow.
- Use the existing GitHub issue templates when reporting bugs or proposing features.
- Review relevant documentation under [doc](../doc), especially:
  - [Logging](../doc/Logging.md)
  - [AdminDetectionSystem](../doc/AdminDetectionSystem.md)
- If you are using an AI coding assistant, also follow [.github/copilot-instructions.md](copilot-instructions.md).

## Development Environment

Recommended environment:

- Windows 11 Pro, Windows Server 2025, or a supported Windows 10/11 build for WinUI 3 development.
- .NET 10 SDK.
- Latest Windows App SDK version
- Windows 10 SDK 10.0.19041.0 or newer.
- Visual Studio 2026 (recommended) or 2022 17.8+ with WinUI, .NET desktop, and C++ desktop workloads.

Open `OneMMC.slnx` in Visual Studio, set `OneMMC` as the startup project, and use the unpackaged launch profile for local debugging.

## Build and Verification

Build the main app before submitting changes:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64
```

Release publish uses self-contained ReadyToRun (interim while the Native AOT migration is in progress):

```powershell
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64
```

**Native AOT is the project's end-state goal.** Legacy code still relies on COM interop, reflection, dynamic activation, WMI, and Windows management APIs that require the full .NET runtime, so the default publish stays ReadyToRun until the migration milestones in [doc/NativeAotMigration.md](../doc/NativeAotMigration.md) are met (measured baseline: [doc/NativeAotAssessment.md](../doc/NativeAotAssessment.md)). New and modified code must follow the mandatory AOT compatibility rules in [.github/copilot-instructions.md](copilot-instructions.md) (§Native AOT Compatibility). When touching interop, serialization, or XAML, build with the opt-in analyzer switch and introduce no new AOT/trim warnings:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64 -p:OneMMCAotAnalysis=true
```

The switch (wired in `eng/AotAnalysis.props`) must never be enabled by default; default builds are unaffected.

There are currently no test projects in this repository. For now, every change should include:

- A successful build.
- Manual verification notes for the affected feature.
- Any administrator/elevation scenario tested, if applicable.
- Any VM or OS version constraints that affected validation.

When documentation mentions SDK, package, target framework, runtime, supported platform, package identity, or app version information, verify it against these files first. If one of these files changes, update related documentation in the same pull request.

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

Core dependencies must not flow back into the UI project. Keep presentation concerns in the UI project.

Important boundaries:

- ViewModels must not create or manipulate XAML UI types such as `ContentDialog`, `FrameworkElement`, `XamlRoot`, `DispatcherQueue`, pages, windows, controls, or presentation state.
- Features must not directly reference types from other features. Share contracts through abstractions.
- Feature code should depend on abstraction interfaces and DI registration, not direct construction of infrastructure classes.
- UI-specific ownership such as windows, dialogs, XAML pages, and theme mapping belongs in the UI project.
- New feature code should follow the established feature/category layout. Place reusable domain code in Core and corresponding views under the appropriate `Views` category.

## WinUI 3 Rules

- Use `DispatcherQueue.TryEnqueue` for UI thread marshaling.
- Use `SelectorBar` for tab-like navigation.
- Do not assume UWP APIs or app-container behavior unless verified for WinUI 3 desktop.
- When dynamically creating controls that need theme-aware brushes, define a named XAML style using `{ThemeResource}` and apply that style in code-behind.

Keep code-behind minimal. Prefer binding, `DataTemplate`, ViewModels, and existing helpers.

## MVVM and Commands

The project uses `CommunityToolkit.Mvvm`.

- Use `ObservableObject`, `[ObservableProperty]`, and `[RelayCommand]` consistently with existing code.
- Async methods decorated with `[RelayCommand]` must return `Task`, not `async void`.
- Expose state and events from ViewModels; let Views decide how to present dialogs, InfoBars, and visual state.

## Dependency Injection

Use the existing DI registration pattern for the area you are modifying.

- Resolve services in page code-behind with `App.GetRequiredService<T>()`.
- Services and ViewModels should receive dependencies through constructors.
- Do not add parameterless fallback constructors just to bypass DI.
- Do not instantiate Core services or ViewModels directly from pages.

## Localization

Do not hardcode user-facing strings in ViewModels or Views.

- Define resource keys in `src/OneMMC.Core/Localization/ResourceKeys.cs`.
- Load strings through `ILocalizationProvider` or `LocalizationProvider.Current.GetString()` where appropriate.
- Use `x:Uid` in XAML.
- Add or update resources for both supported locales: `en-US` and `zh-TW`.

## Logging

OneMMC uses `Microsoft.Extensions.Logging` with Serilog. Follow [doc/Logging.md](../doc/Logging.md).

- Inject `ILogger<T>` into services and ViewModels.
- Use structured logging with named properties.
- Do not use `Debug.WriteLine`, `Console.WriteLine`, or `Trace.WriteLine`.
- Static or low-level native helpers should expose `ConfigureLogger(...)` or `SetLogger(...)` only when constructor injection is not practical.

Useful check:

```powershell
rg "Debug.WriteLine|Console.WriteLine|Trace.WriteLine" src/OneMMC src/OneMMC.Core
```

## Administrator Permissions

Features that require elevation must follow [doc/AdminDetectionSystem.md](../doc/AdminDetectionSystem.md).

- Use `IAdminService.IsRunningAsAdmin` for pre-flight checks.
- Use `IAdminService.IsPermissionError(ex)` to detect permission failures.
- Use `AdminDialogHelper` for administrator-related dialogs and InfoBars.
- Do not create custom administrator permission dialogs.
- Use localized resource keys for administrator messages.
- Disk management operations should use the existing `OperationResult.AccessDenied(...)` pattern where applicable.

## Native Interop

Use CsWin32 by default for Win32 APIs.

- Add supported APIs to the project-level `NativeMethods.txt`.
- Call generated `Windows.Win32.PInvoke` members where possible.
- Handwritten `[DllImport]` or `[LibraryImport]` requires a documented exception.
- Prefer `NativeLibrary` plus delegate binding for isolated metadata gaps.
- Keep handwritten interop centralized in native helper/wrapper files.

## Code Style

- Follow official C# coding conventions and .NET runtime style guidance.
- Use PascalCase for public members, types, and namespaces.
- Use `_camelCase` for private fields.
- Use pattern matching for null checks, such as `is null` and `is not null`.
- Prefer string interpolation over concatenation or `String.Format`.
- Avoid redundant common namespace imports because implicit usings are enabled.
- Add XML documentation comments for public APIs, classes, methods, and properties.
- Keep comments concise and focused on non-obvious behavior.

## Pull Request Checklist

Before requesting review, confirm:

- `dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64` succeeds.
- The change is scoped to the issue or feature being addressed.
- ViewModels do not manipulate UI elements.
- WinUI 3 APIs and patterns are used instead of WPF or unverified UWP patterns.
- Logging uses `ILogger<T>` and does not introduce direct debug, console, or trace writes.
- Administrator scenarios use `IAdminService` and `AdminDialogHelper`.
- Native interop uses CsWin32 unless a documented exception is necessary.
- New/modified code follows the Native AOT compatibility rules (no `dynamic`, no ProgID/CLSID + `Activator` COM activation, no new `System.Management`/`Microsoft.Management.Infrastructure` usage, `{x:Bind}` in new XAML), and an `-p:OneMMCAotAnalysis=true` build introduces no new AOT/trim warnings for touched interop, serialization, or XAML code.
- Manual verification notes are included because there are no automated test projects yet.
