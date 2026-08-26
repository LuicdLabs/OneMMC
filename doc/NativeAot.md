# Native AOT

## Table of Contents

- [Overview](#overview)
- [Document Scope](#document-scope)
- [Related Files and Entry Points](#related-files-and-entry-points)
- [Current Shipped Model](#current-shipped-model)
- [Why Native AOT Changes the Design](#why-native-aot-changes-the-design)
- [Core Engineering Principles](#core-engineering-principles)
- [Developer Guidelines](#developer-guidelines)
- [Deep Dive](#deep-dive)
  - [COM Interop Model](#com-interop-model)
  - [WMI and CIM Model](#wmi-and-cim-model)
  - [Directory and Account Access Model](#directory-and-account-access-model)
  - [Performance Counter Model](#performance-counter-model)
  - [XAML and MVVM Constraints](#xaml-and-mvvm-constraints)
  - [JSON and Serialization Constraints](#json-and-serialization-constraints)
- [Verification Workflow](#verification-workflow)
- [Known Upstream Risks](#known-upstream-risks)
- [References](#references)
- [Sanctioned Replacements Summary](#sanctioned-replacements-summary)

## Overview

Native AOT is OneMMC's shipped deployment model. `PublishAot` is enabled unconditionally in
`src/OneMMC/OneMMC.csproj`, so every configuration is developed under the same AOT/trim analyzer
rules even though native code generation itself still happens only on `dotnet publish`.

This document is the repository's single Native AOT reference. It is meant to work as both:

- a developer guide for day-to-day implementation choices
- an engineering note that explains why those choices exist

## Document Scope

Read this document when you are:

- adding or modifying COM interop
- adding or modifying WMI/CIM access
- touching ADSI, NetAPI32, or PDH-backed functionality
- changing XAML binding patterns, WinRT ABI-facing classes, or MVVM Toolkit properties
- adding JSON serialization paths
- changing build, publish, trim, or analyzer configuration

For implementation work, start with **Developer Guidelines** and the relevant **Deep Dive**
section.

## Related Files and Entry Points

| Item | Path | Purpose |
|---|---|---|
| App publish settings | `src/OneMMC/OneMMC.csproj` | Enables `PublishAot` and configures AOT publish behavior |
| Analyzer defaults | `Directory.Build.props` | Enables AOT, trim, and single-file analyzers for every build |
| COM activator | `src/OneMMC.Core/Infrastructure/Interop/ComActivator.cs` | Standard COM coclass activation path |
| Dual-interface base | `src/OneMMC.Core/Infrastructure/Interop/IDispatch.cs` | Source-generated base for dual COM vtable layout |
| ADSI layer | `src/OneMMC.Core/Infrastructure/Interop/Adsi/` | AOT-safe replacement for `System.DirectoryServices*` consumers |
| Shared WMI helpers | `src/OneMMC.Core/Infrastructure/Wmi/` | WmiLight helper extensions and utilities |
| Windows Firewall WMI wrapper | `src/OneMMC.Core/Features/SystemManagement/Infrastructure/WF/Wbem/` | Marshal-free `IWbemServices` wrapper for advanced WMI instance CRUD |
| AzMan COM example | `src/OneMMC.Core/Features/UserSecurity/Services/AzMan/Native/AzRolesNative.cs` | Large dual-interface COM port |
| COM+ example | `src/OneMMC.Core/Features/SystemManagement/Services/ComExp/Native/ComAdminNative.cs` | Typed COM automation port |
| Task Scheduler example | `src/OneMMC.Core/Features/PCManagement/Services/TaskSchd/Native/TaskSchedulerNative.cs` | Dual-interface COM port with explicit ABI rules |
| Group Policy example | `src/OneMMC.Core/Features/PolicyManagement/Services/GpEdit/Native/NativeMethods.cs` | Non-dual COM interface port |
| PerfMon example | `src/OneMMC.Core/Features/PCManagement/Services/PerfMon/` | PDH-backed counter implementation |

## Current Shipped Model

The repository is intentionally built around compile-time metadata and explicit ABI contracts
instead of runtime discovery:

- COM interop is source-generated with `[GeneratedComInterface]` and `ComWrappers`.
- WMI/CIM work uses **WmiLight** or the in-house marshal-free `IWbemServices` wrapper.
- Directory and account access uses ADSI and NetAPI32 via CsWin32 instead of
  `System.DirectoryServices*`.
- Performance counters use PDH via CsWin32 instead of
  `System.Diagnostics.PerformanceCounter`.
- New XAML uses `{x:Bind}`, not `{Binding}`.
- JSON serialization uses source-generated `JsonSerializerContext`.
- AOT, trim, and single-file analyzers are enabled for every build in `Directory.Build.props`.

The practical rule is straightforward: if a pattern depends on runtime code generation, built-in
COM marshalling, reflection-only metadata lookup, or runtime XAML property-path discovery, it is
usually the wrong fit for this repository.

## Why Native AOT Changes the Design

Native AOT is not just a publish option. It changes which implementation strategies are dependable:

- the old RCW/CCW-style COM convenience model is not available for ad-hoc `dynamic`,
  `[ComImport]`, or `Activator.CreateInstance(Type.GetTypeFromProgID(...))` patterns
- trimmed metadata makes reflection-based binding and serializer discovery fragile
- source-generated code and explicit ABI declarations are more trustworthy than runtime marshalling
- analyzer cleanliness matters because many AOT failures are design issues caught before runtime

That is why OneMMC standardizes on explicit, source-generated, marshal-free paths even where the
old desktop .NET idioms would have been shorter.

## Core Engineering Principles

1. Prefer compile-time knowledge over runtime discovery.
2. Prefer source-generated interop over built-in runtime marshalling.
3. Prefer narrow, explicit native wrappers over broad convenience APIs that hide ABI details.
4. Treat analyzer warnings as design feedback, not publish-time cleanup.
5. Keep comments in interop-heavy files aligned with the actual ABI reasoning.

## Developer Guidelines

These rules are normative for all new and modified code.

| Do not add | Use instead |
|---|---|
| `dynamic` over COM | Typed `[GeneratedComInterface]` interfaces and `ComWrappers` |
| `Type.GetTypeFromProgID` / `GetTypeFromCLSID` + `Activator.CreateInstance` | `CoCreateInstance` through `ComActivator` |
| New `[ComImport]` interfaces | `[GeneratedComInterface]` |
| `System.Management` | WmiLight, or the marshal-free `IWbemServices` wrapper when instance CRUD is required |
| `Microsoft.Management.Infrastructure` | Same as above |
| `System.DirectoryServices*` | NetAPI32 and ADSI via CsWin32 |
| `System.Diagnostics.PerformanceCounter` | PDH via CsWin32 |
| New `{Binding}` in XAML | `{x:Bind}` |
| Reflection-based `JsonSerializer` overloads | Source-generated `JsonSerializerContext` |
| `[ObservableProperty]` on fields | `[ObservableProperty]` on partial properties |
| Non-`partial` WinRT ABI-facing classes | `partial` classes |
| Reflection-dependent activation or type synthesis | Explicit registrations and compile-time known types |

Quick review checklist:

- Is this code relying on runtime marshalling?
- Is this code relying on reflection to discover types, members, or bindings?
- Is this code introducing a managed wrapper that already has an approved native replacement?
- Is there already a repository pattern for this feature area that we should copy instead?

## Deep Dive

### COM Interop Model

#### Overview

Use this model whenever a feature talks to a COM automation server, shell component, or Win32 COM
API.

#### Standard pattern

1. Use CsWin32 for metadata-backed Win32 COM types whenever possible.
2. For custom or dual interfaces, declare `[GeneratedComInterface]` interfaces with exact member
   order.
3. Derive from `Core/Infrastructure/Interop/IDispatch.cs` only when the interface is actually
   dual and needs the `IUnknown[3] + IDispatch[4] + members` layout.
4. Activate coclasses through `Core/Infrastructure/Interop/ComActivator.cs`.
5. Keep ABI details explicit: `get_`/`put_` accessors, raw `short` for `VARIANT_BOOL` when
   needed, explicit optional `VARIANT` parameters, and placeholder members to preserve slot order.

#### Why this model exists

Native AOT has no fallback RCW behavior to correct layout or activation mistakes at runtime. If
the interface shape is wrong, the failure is immediate and can be catastrophic.

#### Code-level examples

- `src/OneMMC.Core/Infrastructure/Interop/ComActivator.cs`
- `src/OneMMC.Core/Infrastructure/Interop/IDispatch.cs`
- `src/OneMMC.Core/Features/UserSecurity/Services/AzMan/Native/AzRolesNative.cs`
- `src/OneMMC.Core/Features/SystemManagement/Services/ComExp/Native/ComAdminNative.cs`
- `src/OneMMC.Core/Features/PCManagement/Services/TaskSchd/Native/TaskSchedulerNative.cs`
- `src/OneMMC.Core/Features/PolicyManagement/Services/GpEdit/Native/NativeMethods.cs`

#### Design notes

- Dual interfaces must preserve exact vtable order.
- Optional COM arguments that late binding once filled automatically now need explicit values.
- Type-library-derived conventions belong in comments because they are part of the contract.

### WMI and CIM Model

#### Overview

OneMMC uses two sanctioned paths, chosen by capability:

- **WmiLight** for query, method, and event-subscription scenarios
- the in-house marshal-free **`IWbemServices` wrapper** for WMI instance create/modify/delete
  scenarios that WmiLight cannot represent

#### Standard pattern

1. Start with WmiLight for ordinary query-style work.
2. Add shared helper code under `Core/Infrastructure/Wmi/` when multiple features need the same
   disposal or conversion rules.
3. Use `Features/SystemManagement/Infrastructure/WF/Wbem/` as the reference design when classic
   WMI COM is required.
4. Preserve type fidelity. WMI-facing VARTYPE choices, CIM type expectations, and handle lifetime
   matter.

#### Why this model exists

`System.Management` and `Microsoft.Management.Infrastructure` were major AOT blockers in the
original assessment. Replacing them was required to ship the AOT build, not just to clean up code.

#### Code-level examples

- `src/OneMMC.Core/Infrastructure/Wmi/`
- `src/OneMMC.Core/Features/SystemManagement/Infrastructure/WF/Wbem/`

#### Design notes

- WmiLight is the default, not the fallback.
- The classic WMI COM wrapper exists because some write paths need instance CRUD support that
  WmiLight does not expose.
- Dispose behavior is part of correctness because the underlying resources are native.

### Directory and Account Access Model

#### Overview

Do not add new `System.DirectoryServices` or `System.DirectoryServices.AccountManagement` usage.

#### Standard pattern

- Use NetAPI32 for local users and groups.
- Use ADSI via `Core/Infrastructure/Interop/Adsi/` when LDAP or ADSI object access is required.

#### Why this model exists

These managed wrappers depend on runtime marshalling patterns that do not fit the repository's AOT
model. ADSI and NetAPI32 keep the ABI explicit and under repository control.

#### Code-level examples

- `src/OneMMC.Core/Infrastructure/Interop/Adsi/`
- `src/OneMMC.Core/Features/PCManagement/Services/LusrMgr/`

### Performance Counter Model

#### Overview

Use PDH through CsWin32 for performance counter work.

#### Standard pattern

- model per-counter query ownership clearly
- handle counter paths explicitly
- use `PdhAddEnglishCounterW` fallback where localized names differ

#### Why this model exists

`System.Diagnostics.PerformanceCounter` is not part of the approved AOT-compatible dependency set,
and PDH gives the repository exact control over lifetime, localization behavior, and failure
handling.

#### Code-level examples

- `src/OneMMC.Core/Features/PCManagement/Services/PerfMon/`

### XAML and MVVM Constraints

#### Overview

All new XAML must use `{x:Bind}`. When touching nearby existing UI, convert remaining `{Binding}`
paths if that work is naturally part of the same change.

#### Standard pattern

- use `x:DataType` for strongly typed templates
- avoid `DisplayMemberPath` and `SelectedValuePath`; prefer `ItemTemplate` or `ToString()`
- keep WinRT ABI-facing classes `partial`
- apply `[ObservableProperty]` to partial properties, not fields

#### Why this model exists

The original AOT startup crash was caused by trimmed XAML metadata combined with non-`partial`
WinRT classes. The solution was structural and remains a standing repository rule.

### JSON and Serialization Constraints

#### Overview

Every serialization path must use a source-generated `JsonSerializerContext`.

#### Why this model exists

Reflection-based serializer overloads are fragile under trimming and unnecessary in a codebase
whose payload types are known at compile time.

#### Code-level examples

- `src/OneMMC/Models/AppSettings.cs`
- `src/OneMMC.Core/Features/UserSecurity/ViewModels/AzMan/AuthorizationManagerViewModel.cs`
- `src/OneMMC.Core/Features/PCManagement/Services/PerfMon/PerformanceMonitorService.cs`

## Verification Workflow

Run these after interop, project configuration, or dependency changes:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -c Debug -p:Platform=x64
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64 -p:Platform=x64
dotnet publish src/OneMMC/OneMMC.csproj -c Debug -r win-x64 -p:Platform=x64
```

Expected outcomes:

- no new `IL2xxx`, `IL3xxx`, `CsWinRT1xxx`, or `MVVMTK0045` warnings in first-party code
- publish succeeds with the MSVC toolchain available for the ILC link step
- the published executable boots and the log under `%LOCALAPPDATA%\OneMMC\Logs\` stays free of
  `[ERR]`, `[FTL]`, and unexpected exception entries

Notes:

- `dotnet build` and VS F5 still run the normal CoreCLR inner loop
- `dotnet publish` is the step that actually produces native code
- analyzers are enabled in normal builds on purpose so AOT regressions are caught while coding

## Known Upstream Risks

- WinUI/XAML AOT support is still evolving upstream. OneMMC avoids the unstable paths by staying
  `{x:Bind}`-only.
- The .NET runtime AOT navigation hang tracked at `dotnet/runtime#104582` has not reproduced in
  this repository, but it should be re-checked on SDK or Windows App SDK upgrades.
- WmiLight is a small external dependency. The fallback plan remains vendoring or forking it, or
  using the classic WMI COM wrapper where feature scope allows.

## References

- `AGENTS.md`
- `.github/copilot-instructions.md`
- `Directory.Build.props`
- `src/OneMMC/OneMMC.csproj`
- `src/OneMMC.Core/Infrastructure/Interop/ComActivator.cs`
- `src/OneMMC.Core/Infrastructure/Interop/IDispatch.cs`
- `src/OneMMC.Core/Infrastructure/Interop/Adsi/Adsi.cs`
- `src/OneMMC.Core/Infrastructure/Wmi/`
- `src/OneMMC.Core/Features/SystemManagement/Infrastructure/WF/Wbem/`
- `src/OneMMC.Core/Features/PCManagement/Services/PerfMon/`
- `https://github.com/dotnet/runtime/issues/104582`
- `https://github.com/PowerShell/MMI/issues/54`

## Sanctioned Replacements Summary

For quick review, this is the shortest form of the repository rule set:

- COM: `[GeneratedComInterface]` + `ComActivator` + `ComWrappers`
- WMI/CIM: WmiLight, or `IWbemServices` wrapper for advanced write paths
- Directory/account APIs: NetAPI32 + ADSI
- Counters: PDH
- XAML: `{x:Bind}`
- JSON: source-generated contexts
- WinRT ABI: `partial` classes
- MVVM Toolkit: partial properties for `[ObservableProperty]`

`IsAotCompatible` and `IsTrimmable` remain intentionally unset on `OneMMC.Core`. The analyzer
properties in `Directory.Build.props` produce the needed warning stream without changing the
library's packaging semantics.
