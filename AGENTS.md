# AGENTS.md

This file provides project orientation to Codex (or other AI coding agents) working in this
repository: what OneMMC is, how to build it, and where things live.

**The normative rules live in [`.github/copilot-instructions.md`](.github/copilot-instructions.md)** —
coding conventions, Native AOT compatibility, architecture boundaries, WinUI 3 specifics,
administrator handling, and shell usage. Read it before starting any task. If anything here conflicts
with that file, follow `copilot-instructions.md`.

## Project Overview

OneMMC is a **WinUI 3 desktop application** that serves as a modern alternative to Windows MMC
(Microsoft Management Console) snap-ins. It provides native UI for system management tasks like
Device Manager, Disk Management, Local Users & Groups, Group Policy Editor, Performance Monitor,
Certificate Management, and more.

**Native AOT is the project's shipped deployment model** — `PublishAot` is enabled unconditionally
for every configuration, and the AOT/trim analyzers run on every build (defaults in
`Directory.Build.props`). First-party code builds warning-clean and must stay that way. The mandatory
rules are in `.github/copilot-instructions.md` (§Native AOT Compatibility); the full technical
reference is [`doc/NativeAot.md`](doc/NativeAot.md).

## Build & Run

```powershell
# Restore and build (default platform is x64)
dotnet build src/OneMMC/OneMMC.csproj

# Build specific platform
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64

# Publish (Release, Native AOT — requires the MSVC toolchain for the ILC link step)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64
```

**Prerequisites**: .NET 10.0 SDK, Windows App SDK (version pinned in `Directory.Packages.props`),
Windows 10 SDK (19041)+. Recommended dev/test OS: Windows 11 Pro or Windows Server 2025 Standard.

**Solution file**: `OneMMC.slnx` (modern VS 2022+ format). Supported platforms: x64, ARM64.

No test projects exist in this repository, so every change needs a successful build plus manual
verification notes.

> Package and SDK versions are pinned centrally in `Directory.Packages.props`. Treat that file as the
> source of truth and verify against it rather than trusting a version quoted in prose.

## Architecture

### Two-Project Structure

| Project | Type | Responsibility |
|---|---|---|
| **OneMMC.Core** | Class library | ViewModels, Services, Models, domain logic, COM/WMI/Win32 interop |
| **OneMMC** | WinUI 3 `WinExe` | XAML Views, converters, helpers, localization resources, app shell |

Dependency direction is one-way: **UI → Core**. The boundary rules (what Core may reference, what
must stay in the UI project, feature isolation, DI-only infrastructure access) are defined in
`.github/copilot-instructions.md` §Architecture Boundaries. Each project's README covers its own
internals in depth:

- [`src/OneMMC.Core/README.md`](src/OneMMC.Core/README.md)
- [`src/OneMMC/README.md`](src/OneMMC/README.md)

### Feature Organization

New functionality goes under `Core/Features/<FeatureName>/` (`Models`, `Services`, `ViewModels`,
plus `Infrastructure`/`Interop`/`Utilities` as needed), with a `<FeatureName>Module.cs` for DI
registration and Views under `Views/<FeatureName>/` in the UI project.

Feature areas: **PCManagement** (DevMgmt, DiskMgmt, Eventvwr, FsMgmt, LusrMgr, PerfMon, TaskSchd,
Services), **PolicyManagement** (GpEdit, RSoP), **UserSecurity** (AzMan, SecPol),
**SystemManagement** (ComExp, WF, TPM), **Certificates** (CertMgr, CertLM), **PrintManagement**.

### Dependency Injection

DI is bootstrapped in `LoggingBootstrapper.BuildServiceProvider()` (UI project). **All registrations
are explicit** — there is no convention-based auto-registration, which is a hard AOT requirement:

- `LoggingBootstrapper.BuildServiceProvider()` → `AddOneMMCApplicationServices()` (UI) →
  `AddOneMMCCore()` (Core) → each `Core/Features/<Feature>/<Feature>Module.cs`
- Most services and every ViewModel are `Transient`. Singletons are the exception and are registered
  explicitly (`AdminService`, `ITaskSchedulerService`, `AzManService`, `AdmxBundleProvider`, the WF
  services, `SecurityPolicyService`, …)
- `ValidateScopes`/`ValidateOnBuild` are enabled in Debug builds only

Resolve services in page code-behind via `App.GetRequiredService<T>()` — **except** transient
resolution graphs containing any container-created `IDisposable`. Those use `PageServiceScope`; see
[`doc/MemoryManagement.md`](doc/MemoryManagement.md).

### Navigation

Top-level navigation uses `NavigationView` in `MainWindow.xaml`. Sub-page tab navigation uses
**`SelectorBar`** (not `Pivot`). Page routing goes through `NavigationService`; breadcrumb tracking
via `BreadcrumbNavigationService`. Details: [`doc/Breadcrumb.md`](doc/Breadcrumb.md).

## Subsystem References

Each subsystem has one authoritative document. Read it before changing that area, and update it
there rather than restating rules elsewhere.

| Area | Reference |
|---|---|
| Native AOT (COM/WMI/ADSI/PDH/XAML/JSON constraints) | [`doc/NativeAot.md`](doc/NativeAot.md) |
| Administrator detection & elevation UX | [`doc/AdminDetectionSystem.md`](doc/AdminDetectionSystem.md) |
| Memory management & page teardown | [`doc/MemoryManagement.md`](doc/MemoryManagement.md) |
| Logging pipeline | [`doc/Logging.md`](doc/Logging.md) |
| Localization (`.resw`, `LocalizedStrings`, `ResourceKeys`) | [`doc/Localization.md`](doc/Localization.md) |
| Breadcrumb navigation | [`doc/Breadcrumb.md`](doc/Breadcrumb.md) |
| File/folder pickers | [`doc/AppSdkFileDialogService.md`](doc/AppSdkFileDialogService.md) |
| Directory object picker | [`doc/ObjectPickerService.md`](doc/ObjectPickerService.md) |
| Contribution workflow & PR checklist | [`.github/CONTRIBUTING.md`](.github/CONTRIBUTING.md) |
