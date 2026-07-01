# OneMMC.Core

`OneMMC.Core` is the Windows-native application layer for OneMMC.
It is not a cross-platform Clean Architecture sample. The goal here is practical,
maintainable WinUI 3 support code with clear UI/Core boundaries and predictable
feature structure.

## Top-Level Rules

Only these top-level folders are valid:

- `Abstractions`
- `DependencyInjection`
- `Features`
- `Infrastructure`
- `Localization`

Do not reintroduce a top-level `Services` folder.

## DI Rules

- The only Core DI entrypoint is `AddOneMMCCore(this IServiceCollection services)`.
- Core registration is explicit. Do not add reflection-based auto scanning back.
- Feature modules stay internal and are called by `AddOneMMCCore(...)`.
- Do not add runtime service locators, parameterless fallback constructors, or
  direct service instantiation bypasses for normal application flow.

## Feature Layout

Use feature-first folders under `Features/<FeatureName>/`.

Allowed first-level buckets:

- `Models`
- `Services`
- `ViewModels`
- `Infrastructure`
- `Interop`
- `Utilities`

Only create buckets that a feature actually needs.

Do not create feature first-level buckets such as:

- `Common`
- `Native`
- `Support`
- `Helpers`

### Bucket Responsibilities

- `Models`: feature DTOs, state objects, and bindable data models.
- `Services`: feature orchestration, queries, operations, and business logic.
- `ViewModels`: application-facing state and command orchestration for the UI layer.
- `Infrastructure`: feature-local storage, filesystem, registry, or OS integration details.
- `Interop`: COM interfaces, P/Invoke structs, constants, and native wrappers.
- `Utilities`: stateless helpers, extension methods, formatters, and pure conversions.

`Utilities` must stay stateless. Do not place orchestration, persistence, DI, or
native ownership there.

## Shared Infrastructure

Cross-feature sharing goes only through:

- `Abstractions`
- `Infrastructure`
- explicitly documented shared contracts

Current shared infrastructure areas:

- `Infrastructure/WindowsCapabilities`
  - `AppSdkFileDialogService`
  - `AclEditorService`
  - `DirectoryObjectPickerService`
  - `CertificateAuthorityPickerService`
  - `IconPickerService`
- `Infrastructure/PolicyStorage`
  - raw `Registry.pol` snapshot types
  - registry-backed policy proxy
  - local policy file persistence helpers used outside `PolicyManagement`

Keep Windows-native capability code here instead of scattering it across features.

## Namespace Rule

Namespace must always match the real folder path.

Do not create or keep:

- `OneMMC.Core.ViewModels.*`
- `OneMMC.Core.Models.*`
- `OneMMC.Core.Services.*`
- mirror namespaces
- compatibility namespaces
- wrapper namespaces
- type-alias transition layers

## UI/Core Boundary

Core may use Windows-native APIs such as Win32, COM, WinRT, P/Invoke, Windows SDK,
and WinAppSDK capability APIs when they are part of OS integration.

Core must not depend on WinUI presentation types such as:

- `Page`
- `UserControl`
- `Window`
- `FrameworkElement`
- `ContentDialog`
- `NavigationViewItem`
- visual-tree manipulation

If a workflow needs HWND ownership or `BitmapImage`, keep that boundary in the UI
project and feed Core with UI-neutral data.

## Static Exceptions

The only normal static access point allowed at the architecture level is
`LocalizationProvider.Current`.

Low-level native metadata/delegate caches are acceptable when required by interop,
but do not expand that into new service-locator patterns.

## Adding or Changing Features

When adding functionality:

1. Put the code under the owning feature first.
2. Move it to shared `Infrastructure` only when two features genuinely need it.
3. Register it explicitly in the owning feature module.
4. Keep namespace and folder path aligned from the start.
5. Update the feature README if the boundary or folder intent changes.

## Anti-Patterns

Avoid these in Core:

- interface explosion
- artificial platform abstraction
- mediator/event-bus/CQRS/repository scaffolding
- direct feature-to-feature implementation references
- fallback constructors that hide missing DI wiring
- reintroducing reflection-driven registration

