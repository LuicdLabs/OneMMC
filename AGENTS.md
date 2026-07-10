# AGENTS.md

This file provides guidance to Codex (or some AI coding agents) when working with code in this repository.

## Copilot Instructions

Before starting any task, read `.github/copilot-instructions.md` first. It contains additional project-specific guidance that supplements this file. If any guidance in that file conflicts with this file, follow the guidance in `copilot-instructions.md`.

## Project Overview

OneMMC is a **WinUI 3 desktop application** that serves as a modern alternative to Windows MMC (Microsoft Management Console) snap-ins. It provides native UI for system management tasks like Device Manager, Disk Management, Local Users & Groups, Group Policy Editor, Performance Monitor, Certificate Management, and more.

**Native AOT is the project's shipped deployment model** (the M0–M4 migration is complete). `PublishAot` is enabled unconditionally for every configuration (Debug and Release); all COM interop is source-generated (`[GeneratedComInterface]`/`ComWrappers`/CsWin32 marshal-free structs), WMI/CIM runs on WmiLight plus a marshal-free `IWbemServices` wrapper, and directory/account/counter access runs on ADSI/NetAPI32/PDH via CsWin32. All new and modified code must follow the mandatory AOT compatibility rules in `.github/copilot-instructions.md` (§Native AOT Compatibility); the single Native AOT reference (verified state, mandatory rules, measured baseline, migration record) is `doc/NativeAot.md`. Do not propose abandoning or scaling back AOT support because of a current limitation — propose the AOT-compatible alternative instead. The AOT/trim analyzers run on every build (defaults in `Directory.Build.props`); first-party code builds warning-clean and must stay that way.

## Build & Run

```bash
# Restore and build (default platform is x64)
dotnet build src/OneMMC/OneMMC.csproj

# Build specific platform
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64

# Publish (Release, Native AOT — requires the MSVC toolchain for the ILC link step)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64
```

**Prerequisites**: .NET 10.0 SDK, Windows App SDK 2.2.0+, Windows 10 SDK (19041)+. Recommended dev/test OS: Windows 11 Pro or Windows Server 2025 Standard.

**Solution file**: `OneMMC.slnx` (modern VS 2022+ format). Supported platforms: x86, x64, ARM64.

No test projects exist in this repository.

## Important: WinUI 3 vs WPF vs UWP

**This is a WinUI 3 application, NOT WPF or UWP.** Do not apply WPF or UWP patterns without verification:

### WinUI 3 vs WPF
- WinUI 3 uses `Microsoft.UI.Xaml` namespace, not `System.Windows`
- No `SetResourceReference`, `DependencyProperty.Register`, `RoutedCommand`, or WPF-specific APIs
- Threading: `DispatcherQueue.TryEnqueue`, not WPF's `Dispatcher.Invoke`
- Commands: `CommunityToolkit.Mvvm` `[RelayCommand]`, not WPF's `ICommand` from `System.Windows.Input`
- Resource updates: Define styles with `{ThemeResource}` in XAML and apply via `Style` property; no dynamic resource binding like WPF
- Controls: `SelectorBar` for tabs (not `TabControl`), `NavigationView` for navigation (not WPF's `Frame`)
- **ThemeResource in Code-Behind**: When dynamically creating UI elements in code-behind that need theme-aware brushes, define a named `Style` with `{ThemeResource ...}` in the page's XAML `ResourceDictionary` and apply it via `Style = (Style)Resources["StyleKey"]` in code-behind. Never use `Application.Current.Resources["ResourceKey"]` directly — it is a one-time static fetch that will not update when the user switches between Light and Dark mode.

### WinUI 3 vs UWP
- **Namespace migration**: `Windows.UI.Xaml` (UWP) → `Microsoft.UI.Xaml` (WinUI 3)
- **App model**: WinUI 3 is desktop Win32 (full system access), UWP is sandboxed (app container restrictions)
- **Deployment**: WinUI 3 supports unpackaged deployment; UWP requires MSIX packaging
- **Window management**: `Microsoft.UI.Windowing.AppWindow` (WinUI 3) vs `ApplicationView` (UWP)
- **API access**: WinUI 3 has full Win32, P/Invoke, and COM interop; UWP has limited Win32 API surface
- Some `Windows.*` APIs still work in WinUI 3 (e.g., `Windows.Storage`), but prefer Windows App SDK equivalents when available

When in doubt, consult [WinUI 3 documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/) and Windows App SDK samples, not WPF or UWP documentation.

## Architecture

### Two-Project Structure
- **OneMMC.Core** (class library) — ViewModels, Services, Models, domain logic, COM/WMI interop
- **OneMMC** (WinUI 3 WinExe) — XAML Views, converters, helpers, localization resources, app shell

### Architecture Boundaries
- **Core may reference Windows App SDK platform APIs**: `OneMMC.Core` may reference `Microsoft.WindowsAppSDK` and `Microsoft.UI.*` only for reusable Windows-native services such as file/folder pickers, native OS dialogs, interop helpers, and image conversion helpers. Dependency still flows one way: UI → Core only.
- **ViewModel must not touch UI elements**: ViewModels in Core must not create or manipulate `ContentDialog`, `FrameworkElement`, `XamlRoot`, `DispatcherQueue`, `ElementTheme`, pages, windows, controls, or any presentation state. Expose state via observable properties; let the View decide how to present it.
- **Features must not cross-reference each other**: A Feature (e.g. `PCManagement`) must not directly reference types from another Feature (e.g. `SystemManagement`). Share only through `Abstractions`.
- **No direct `new` on Infrastructure classes from Features**: Features must depend on `Abstractions` interfaces only. Infrastructure implementations (e.g. `AdminService`) are resolved via DI — never instantiated directly with `new`.
- **No hardcoded user-facing strings in ViewModels or Views**: All user-visible strings must come from resource keys defined in `Core/Localization/ResourceKeys.cs` and loaded via `ILocalizationProvider`. Never inline string literals that will be shown to the user.
- **Async relay commands must return `Task`**: Methods decorated with `[RelayCommand]` that are async must return `Task`, not use `async void`. See [MVVMTK0039](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0039).
- **New functionality placement**: New features go under `Core/Features/<FeatureName>/` (Models, Services, ViewModels) with a corresponding `<FeatureName>Module.cs` for DI registration, and Views under `Views/<FeatureName>/` in the UI project.
- **UI-specific code stays in UI**: App window ownership, XAML dialogs/pages, theme mapping (`WinUIThemeService`), visual presentation, and UI composition belong in the UI project. Reusable Windows-native platform services may live in Core when they keep HWND ownership at the call boundary and do not own XAML presentation.

### Native Interop
- **CsWin32 is the default**: When a Win32 API exists in CsWin32 metadata, add it to the project-level `NativeMethods.txt` and call the generated `Windows.Win32.PInvoke` API instead of adding a new handwritten `[DllImport]` or `[LibraryImport]`.
- **Handwritten interop requires a documented exception**: Only keep handwritten imports for unsupported exports, APIs that CsWin32 cannot emit for the active target configuration or architecture, unavoidable BCL/COM marshalling gaps, or mixed native workflows where partial CsWin32 adoption would create a second unsafe marshalling model.
- **Prefer `NativeLibrary` for one-off metadata gaps**: If only one export is missing from CsWin32 (for example a DLL entry point not projected by metadata), prefer `NativeLibrary` + delegate binding over introducing a new static import.
- **Keep exceptions centralized**: Any remaining handwritten interop must live in a dedicated native wrapper/helper file and include a comment or XML summary explaining why CsWin32 could not be used directly.

### MVVM Pattern
Uses `CommunityToolkit.Mvvm` (8.4.2). ViewModels use `ObservableObject` base, `[ObservableProperty]` for bindable properties, `[RelayCommand]` for commands. Keep code-behind minimal — prefer data binding and `DataTemplate`.

### Dependency Injection
DI is bootstrapped in `LoggingBootstrapper.BuildServiceProvider()` (UI project). Classes are **auto-registered** by convention in `ServiceCollectionExtensions.AddOneMMCModules()`:
- Any concrete class whose name ends in `Service`, `Manager`, or `ViewModel` is registered automatically
- Default lifetime is `Transient`; override with `[ServiceRegistration(ServiceLifetime)]` attribute
- `IAdminService` is explicitly mapped as a singleton

Resolve services in page code-behind via `App.GetRequiredService<T>()`.

### Navigation
Top-level navigation uses `NavigationView` in `MainWindow.xaml`. Sub-page tab navigation uses **`SelectorBar`** (not `Pivot`). Page routing is index-based through `NavigationService`. Breadcrumb tracking via `BreadcrumbNavigationService`.

### Feature Organization
Each Windows management feature (DevMgmt, DiskMgmt, LusrMgr, GpEdit, PerfMon, SecPol, AzMan, ComExp, RSoP, etc.) has a parallel folder structure:
- `Core/Services/{Feature}/` — business logic, COM/WMI interop
- `Core/ViewModels/{Feature}/` — observable view models
- `Core/Models/{Feature}/` — domain models
- `Views/{Category}/{Feature}/` — XAML pages and dialogs

### Localization
Two supported locales: **en-US**, **zh-TW**. Resources live in `Strings/{locale}/{Feature}.resw` files. Access patterns:
- XAML: use `x:Uid` attributes
- Code: `LocalizationProvider.Current.GetString()` with `ResourceKeys` constants
- `LocalizedStrings` is a partial class split per feature (e.g., `LocalizedStrings.PerfMon.cs`)

### Logging
Serilog + `Microsoft.Extensions.Logging`. Configured in `LoggingBootstrapper.cs`.
- File sink: daily rolling logs at `%LOCALAPPDATA%/OneMMC/Logs/`
- Debug sink: custom `DebugOutputSink` using `OutputDebugString` (avoids trace loops)
- **Never use** `Debug.WriteLine`, `Console.WriteLine`, or `Trace.WriteLine` directly
- Services/ViewModels: Constructor injection with `ILogger<T>`
- Page classes: Use `App.GetRequiredService<T>()` to obtain instances (never `new`)
- Static/Factory classes: Provide `ConfigureLogger(...)` or `SetLogger(...)` methods

### Admin Permission Handling
Three patterns documented in `doc/AdminDetectionSystem.md`:
1. **Pre-flight**: Check `IAdminService.IsRunningAsAdmin` before operations that always require admin rights
2. **Event-driven**: Catch permission errors in ViewModel, trigger `AdminPermissionRequired` event, handle in View
3. **OperationResult**: Return `OperationResult.AccessDenied()` for disk management operations

Always use `AdminDialogHelper` for admin-related dialogs and InfoBars. Never create custom admin permission dialogs. Use `IAdminService.IsPermissionError(ex)` to detect permission exceptions. Use `LocalizationProvider.Current.GetString()` with `ResourceKeys` constants for all admin-related messages.

## Code Conventions

- Follow [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) and [.NET Runtime Coding Guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)
- **PascalCase** for public members, types, and namespaces; **_camelCase** for private fields
- Avoid the `I` prefix for non-interface classes
- **Null checks**: use pattern matching (`is null`, `is not null`)
- **String interpolation** over `String.Format` or concatenation
- **ImplicitUsings** is enabled — don't add redundant common namespace imports
- **LangVersion**: `preview` (latest C# features available)
- **Nullable**: enabled in both projects
- Prefer native WinUI 3 / Windows App SDK APIs; avoid shelling out with `System.Diagnostics.Process` unless no API alternative exists
- Avoid hardcoding values (strings, magic numbers, colors); use `Constants` classes for compile-time constants
- XML documentation comments (`///`) on all public APIs, classes, methods, and properties
- Write clear and concise comments to explain complex logic or non-obvious behavior; keep them up-to-date
- Do not write comments that simply restate what the code does
- Use TODO comments sparingly and include context about what needs to be done and why
- Document any workarounds or non-standard implementations with clear explanations of why they exist

## API Usage

- **Do Not Use Non-Existent APIs**: Never suggest or use APIs that do not exist in the target framework or SDK. If you are unsure whether an API exists, say so explicitly rather than guessing or fabricating method/property names.

## Known WinUI 3 Pitfalls

- **Layout Panel IsEnabled**: `Grid`, `StackPanel`, `Border`, and other layout panels do **not** have an `IsEnabled` property in WinUI 3 — they do not inherit from `Control`. Placing `IsEnabled` on them in XAML causes `WMC0011` compiler errors. The correct pattern is: omit `IsEnabled` from the panel in XAML entirely, then call a helper method (e.g. `SetMode(bool enabled)`) from the constructor and from event handlers to iterate children and set `IsEnabled` on each `Control` individually.
- **ContentDialog `await ShowAsync()`**: `ContentDialog.ShowAsync()` returns `IAsyncOperation<ContentDialogResult>`. If you see `CS4036 'IAsyncOperation<T>' does not contain a definition for 'GetAwaiter'`, add `using System;` explicitly to the file. Alternatively use `.AsTask()` to convert to a standard `Task<T>`.
