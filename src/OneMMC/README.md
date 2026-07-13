# OneMMC (UI)

`OneMMC` is the **WinUI 3 presentation layer** and application shell — the executable that ships.
It owns everything visual: XAML views and dialogs, value converters, custom controls, navigation,
theming, localization resources, the app lifecycle, and the composition root that wires up
dependency injection and logging.

All logic lives in [`OneMMC.Core`](../OneMMC.Core/README.md); this project binds to it. The
dependency is strictly one-way — **UI → Core**.

> This is a **WinUI 3 desktop (Win32)** app — **not WPF, not UWP**. It uses the
> `Microsoft.UI.Xaml` namespace, `DispatcherQueue` threading, `{x:Bind}` binding, and
> `Microsoft.UI.Windowing.AppWindow`. Do not apply WPF/UWP patterns without verification.

---

## Table of Contents

- [Role in the Solution](#role-in-the-solution)
- [Technology Stack](#technology-stack)
- [Project Layout](#project-layout)
- [Application Lifecycle](#application-lifecycle)
- [Composition Root (DI + Logging)](#composition-root-di--logging)
- [Navigation](#navigation)
- [MVVM & Data Binding](#mvvm--data-binding)
- [Theming](#theming)
- [Localization](#localization)
- [Converters, Controls & Helpers](#converters-controls--helpers)
- [Admin Permission Handling](#admin-permission-handling)
- [Native AOT Rules for XAML](#native-aot-rules-for-xaml)
- [Build, Run & Debug](#build-run--debug)
- [Common WinUI 3 Pitfalls](#common-winui-3-pitfalls)

---

## Role in the Solution

| Project | Type | Responsibility |
|---|---|---|
| **OneMMC** *(this project)* | WinUI 3 `WinExe` | XAML views/dialogs, converters, controls, navigation, theming, localization, app shell, DI/logging bootstrap |
| **OneMMC.Core** | Class library | ViewModels, services, models, and all Windows-native interop |

The UI project keeps code-behind minimal and pushes all state and commands into Core ViewModels.
Its job is to *present* — turn observable state into Fluent UI, and route user intent back to
commands.

> **~207 C# files**, **~145 XAML files**, across 6 feature view areas plus settings and commons.

---

## Technology Stack

| Concern | Technology |
|---|---|
| UI framework | **WinUI 3** via Windows App SDK `2.2.0` (`Microsoft.UI.Xaml`) |
| Output | `WinExe`, target `net10.0-windows10.0.19041.0`, platforms `x64` / `ARM64` |
| Deployment | **Unpackaged** (`WindowsPackageType=None`), self-contained in Release, **Native AOT** |
| MVVM | `CommunityToolkit.Mvvm` 8.4.2 |
| Fluent controls | `CommunityToolkit.WinUI.Controls.SettingsControls`, `.Controls.Sizers` |
| Windowing | `Microsoft.UI.Windowing.AppWindow` (tall title bar, custom chrome) |
| Navigation | `NavigationView` (top-level) + `SelectorBar` (sub-page tabs) + `Frame` routing |
| Data binding | **`{x:Bind}` only** (compile-time, AOT-safe) — never `{Binding}` |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Logging | `Microsoft.Extensions.Logging` + Serilog (file + debug sinks) |
| Win32 interop | CsWin32 (`NativeMethods.txt`), plus `Interop/WindowLongNativeMethods.cs` |

---

## Project Layout

```
OneMMC/
├── App.xaml / App.xaml.cs         Application entry point, DI init, global exception handling, theme
├── MainWindow.xaml / .xaml.cs     Shell: NavigationView, title bar, Frame host, breadcrumb
├── Views/                         XAML pages & dialogs, grouped by feature domain
│   ├── PCManagement/              DevMgmt, DiskMgmt, Eventvwr, FsMgmt, LusrMgr, PerfMon, Services, TaskSchd
│   ├── PolicyManagement/          GpEdit, RSoP
│   ├── UserSecurity/              AzMan, SecPol
│   ├── SystemManagement/          ComExp, TPM, WF (Windows Firewall)
│   ├── CertificatesCredential/    CertMgr, CertLM
│   ├── PrintManagement/
│   ├── Settings/                  Settings page
│   └── Commons/                   Shared shell pieces & dialogs
├── ViewModels/                    UI-only VMs: MainWindowViewModel, SettingsViewModel
├── Services/                      UI-only services (see Composition Root & Navigation below)
│   ├── DependencyInjection/       AddOneMMCApplicationServices() — composition root
│   ├── Logging/                   LoggingBootstrapper, UiLogger
│   ├── NavigationService.cs       Frame-based page routing
│   ├── BreadcrumbNavigationService.cs
│   └── WinUIThemeService.cs / IThemeService.cs
├── Controls/                      AotGridSplitter (AOT-safe toolkit GridSplitter subclass)
├── Converters/                    IValueConverters for XAML bindings
├── Helpers/                       AdminDialogHelper, ModalDialogWindow, DPI, unsaved-changes guards
├── Localization/                  LocalizedStrings.*.cs (partial, per feature) + UILocalizationProvider
├── Strings/                       en-US / zh-TW .resw resource files (40 files)
├── Interop/                       WindowLongNativeMethods (window subclassing)
├── Models/                        AppSettings (source-gen JSON) and UI-local models
├── Assets/                        App icons, logos, splash
├── LegalDocs/                     License / third-party / privacy text (copied to output)
├── app.manifest                  Win32 manifest (DPI awareness, etc.)
└── Package.appxmanifest           MSIX manifest (packaged-mode tooling)
```

---

## Application Lifecycle

Startup flow lives in `App.xaml.cs`:

1. **`App()` ctor** — `LoggingBootstrapper.BuildServiceProvider()` builds the DI container and
   Serilog; `UiLogger` is configured; global exception handlers are attached
   (`UnhandledException`, `AppDomain.CurrentDomain.UnhandledException`,
   `TaskScheduler.UnobservedTaskException`). Unhandled UI exceptions are surfaced through a single
   guarded `ContentDialog` and marked handled to keep the app alive.
2. **`OnLaunched`** — initializes Core localization, wires the `DispatcherQueue` marshaller for
   `PerformanceCounterInfo` (so Core can post to the UI thread *without* referencing WinUI types),
   creates and activates `MainWindow`, then applies the saved theme.
3. **`MainWindow.Closed`** — `LoggingBootstrapper.Shutdown()` flushes Serilog.

Services are resolved anywhere via the static accessor:

```csharp
var vm = App.GetRequiredService<DiskManagementViewModel>();
```

Page classes use `App.GetRequiredService<T>()`; **never** `new` a service or ViewModel.

---

## Composition Root (DI + Logging)

`LoggingBootstrapper.BuildServiceProvider()` (in `Services/Logging/`) is the composition root:

- Configures Serilog: daily rolling file sink at `%LOCALAPPDATA%\OneMMC\Logs\` (14-day retention)
  plus a custom `DebugOutputSink` that uses `OutputDebugString` — avoiding a `Trace` feedback loop.
- `Microsoft`/`System` log categories are throttled to Warning.
- Registers `Microsoft.Extensions.Logging` over Serilog, then calls
  `AddOneMMCApplicationServices()`.

`AddOneMMCApplicationServices()` (in `Services/DependencyInjection/`) chains Core and adds the
UI-only singletons:

```csharp
public static IServiceCollection AddOneMMCApplicationServices(this IServiceCollection services)
{
    services.AddOneMMCCore();                    // ← all Core services + ViewModels
    services.AddSingleton<WinUIThemeService>();
    services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<WinUIThemeService>());
    services.AddSingleton<BreadcrumbNavigationService>();
    services.AddSingleton<LocalizationService>();
    services.AddSingleton<NavigationService>();
    services.AddSingleton<SettingsViewModel>();
    return services;
}
```

Registration is **explicit** (no reflection scanning) — a hard Native AOT requirement.

---

## Navigation

Two levels:

- **Top-level** — `NavigationView` in `MainWindow.xaml`. Selecting an item routes a hosted `Frame`
  through `NavigationService`, which maps string keys to page types:

  ```csharp
  { "PCManagement",  typeof(Views.PCManagement.PCManagement) },
  { "PolicyPolicies", typeof(Views.PolicyManagement.PolicyManagement) },
  { "SystemManagement", typeof(Views.SystemManagement) },
  // ...
  ```

- **Sub-page tabs** — **`SelectorBar`** (not `Pivot`, not WPF `TabControl`) for tool tabs inside a
  feature page.

Breadcrumb state is tracked by `BreadcrumbNavigationService`. Page routing is index/key-based
through `NavigationService`; `GoBack()` honors the `Frame` back stack.

---

## MVVM & Data Binding

- ViewModels come from Core (`CommunityToolkit.Mvvm`: `ObservableObject`, `[ObservableProperty]`
  on **partial properties**, `[RelayCommand]`). This project adds only UI-specific VMs
  (`MainWindowViewModel`, `SettingsViewModel`).
- **Binding is `{x:Bind}` only.** Use `x:DataType` for strongly-typed `DataTemplate`s; prefer
  `ItemTemplate`/`ToString()` over `DisplayMemberPath`/`SelectedValuePath`.
- Keep code-behind minimal — favor data binding and `DataTemplate`. A page's code-behind should
  mostly resolve its ViewModel and forward events, not hold logic.
- Marshal to the UI thread with `DispatcherQueue.TryEnqueue`; never block with `.Result`/`.Wait()`.

---

## Theming

- `App.SetAppTheme(ElementTheme)` sets `RequestedTheme` on the root element, re-applies the tall
  title bar, persists the choice to `AppSettings`, and raises `App.ThemeChanged`.
- The saved theme (`Light` / `Dark` / `Default`) is read on launch and re-applied.
- **Theme-aware brushes in code-behind:** define a named `Style` with `{ThemeResource …}` in the
  page's XAML `ResourceDictionary` and apply it via `Style = (Style)Resources["StyleKey"]`. **Never**
  use `Application.Current.Resources["Key"]` directly — it is a one-time static fetch that will not
  update on Light↔Dark switches. (`SetResourceReference` is WPF-only and does not exist here.)

---

## Localization

- Two locales: **en-US** and **zh-TW**, backed by `Strings/{locale}/*.resw` (40 resource files).
- XAML uses `x:Uid`; code uses `LocalizationProvider.Current.GetString()` with `ResourceKeys`
  constants.
- `LocalizedStrings` is a `partial` class split per feature
  (`LocalizedStrings.PerfMon.cs`, `LocalizedStrings.WF.cs`, …). `UILocalizationProvider` bridges
  the UI resource system into Core's `ILocalizationProvider`.
- **No hardcoded user-facing strings** — everything visible comes from a resource key.

---

## Converters, Controls & Helpers

- **Converters** (`Converters/`) — `IValueConverter`s for XAML: bool/visibility/negation, null
  checks, level→glyph/color (Event Viewer), and per-feature converters (PerfMon, Print, TPM,
  properties). `DiskItemTemplateSelector` picks templates by disk item type.
- **Controls** (`Controls/`) — `AotGridSplitter`: a subclass of the toolkit `GridSplitter` that
  works around a CsWinRT AOT struct-unbox crash (`RhUnbox2` `InvalidCastException` when reading
  `Width`/`Height` dependency properties as boxed structs). Use it instead of the raw toolkit
  splitter.
- **Helpers** (`Helpers/`) — `AdminDialogHelper` (unified admin dialogs/InfoBars),
  `ModalDialogWindow`, `DpiScaleHelper`, `EventLogPickerController`, and the unsaved-changes guard
  (`IUnsavedChangesGuard` / `UnsavedChangesPrompt`).

---

## Admin Permission Handling

Many features require elevation. Use the **unified** system (never a custom admin dialog) —
documented in [`doc/AdminDetectionSystem.md`](../../doc/AdminDetectionSystem.md):

1. **Pre-flight** — check `IAdminService.IsRunningAsAdmin` before always-elevated operations.
2. **Event-driven** — catch permission errors in the ViewModel, raise `AdminPermissionRequired`,
   handle in the View.
3. **OperationResult** — return `OperationResult.AccessDenied()` for disk operations.

Always route admin UI through `AdminDialogHelper`, detect permission exceptions with
`IAdminService.IsPermissionError(ex)`, and localize every message via `ResourceKeys`.

---

## Native AOT Rules for XAML

The app ships as **Native AOT** (`PublishAot` is unconditional; `SelfContained` in Release; the
WmiLight native shim is statically linked into the exe). The original AOT startup crash was a
XAML-metadata problem, so these rules are standing:

- **`{x:Bind}` only** — never add new `{Binding}`; convert nearby `{Binding}` when you touch it.
- **WinRT ABI-facing classes must be `partial`** — fix CsWinRT1028/1029 in files you touch.
- **`[ObservableProperty]` on partial properties**, not fields (MVVMTK0045).
- **JSON via source-generated `JsonSerializerContext`** (e.g. `AppSettings`) — no reflection
  serializer overloads.
- WmiLight is rooted as a `TrimmerRootAssembly` because it is consumed through the Core library.

Full rationale and the verified baseline: [`doc/NativeAot.md`](../../doc/NativeAot.md).

---

## Build, Run & Debug

**Prerequisites:** .NET 10 SDK, Windows App SDK 2.2.0+, Windows 10 SDK (19041)+, and — for the
`publish` ILC link step — the MSVC toolchain (Desktop C++ workload).

```bash
# Build (normal CoreCLR inner loop; F5 in Visual Studio uses the same)
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64

# Verify AOT-clean (analyzers run on every build — expect 0 new IL/CsWinRT/MVVMTK warnings)
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64

# Publish the native single-exe (actual ILC codegen; needs MSVC)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64
```

**In Visual Studio (2026+):** open `OneMMC.slnx`, set **OneMMC** as the single startup project, and
switch the run profile from **OneMMC (Package)** to **OneMMC (Unpackaged)** before F5.

**Logs:** `%LOCALAPPDATA%\OneMMC\Logs\OneMMC-<date>.log`. Global handlers in `App.xaml.cs` capture
UI, AppDomain, and unobserved-task exceptions. When the debugger is attached, the `DebugOutputSink`
also emits to the VS Output window. Never call `Debug.WriteLine`/`Console.WriteLine`/
`Trace.WriteLine` directly — go through `ILogger<T>` / `UiLogger`.

> Because this is unpackaged Win32, you get full system access (P/Invoke, COM, WMI) — but many
> features **do nothing useful without elevation**. Run elevated when testing management operations,
> and only in an isolated VM (features can modify disks, users, services, and policy directly).

---

## Common WinUI 3 Pitfalls

- **Layout panels have no `IsEnabled`.** `Grid`/`StackPanel`/`Border` don't derive from `Control`,
  so `IsEnabled` in XAML causes `WMC0011`. Omit it and instead iterate children in a
  `SetMode(bool)` helper, setting `IsEnabled` on each `Control`.
- **`await ContentDialog.ShowAsync()`** returns `IAsyncOperation<ContentDialogResult>`. On `CS4036`
  add `using System;`, or call `.AsTask()`.
- **Only one `ContentDialog` at a time** — showing a second throws `COMException 0x80000019`. The
  global error dialog in `App.xaml.cs` already guards against this.
- **Don't apply WPF/UWP habits** — no `SetResourceReference`, `DependencyProperty.Register`,
  `RoutedCommand`, WPF `Dispatcher`, or UWP `ApplicationView`. Use the WinUI 3 equivalents.
