# Native AOT Migration Strategy

**Native AOT is the project's end-state goal.** Every new design and every rewrite must move the
codebase toward full AOT compatibility. This document is the plan of record: it maps each
AOT-incompatible pattern to its sanctioned AOT-compatible replacement and sequences the migration.
The measured baseline this plan starts from is recorded in
[NativeAotAssessment.md](NativeAotAssessment.md).

The default publish remains self-contained ReadyToRun **only as an interim state** until the
milestone gates below are met. It is a transition vehicle, not the destination.

## Terminology: WMI, MI, CIM, MMI (and which is which)

These names are used precisely throughout this document:

| Name | What it is | AOT status |
|---|---|---|
| **CIM** | The DMTF data-model standard (classes, instances, namespaces) that all of the below implement | n/a (a standard, not an API) |
| **Classic WMI COM API** | `IWbemLocator`/`IWbemServices` (wbemcli.h), the original WMI client API | Usable under AOT only via marshal-free COM (CsWin32 `allowMarshaling: false` or `[GeneratedComInterface]`) |
| **`System.Management`** | The original .NET wrapper over classic WMI COM (`ManagementObjectSearcher`) | **Not AOT/trim compatible** (official) |
| **MI API ("WMI v2")** | The native C API in `mi.h` (`MI_Application_*`, `MI_Session_*`), introduced in Windows 8/Server 2012; the latest version of the WMI technologies, CIM-standard based | Native C API — AOT-neutral, but has **no supported managed binding** (see next row) |
| **MMI — `Microsoft.Management.Infrastructure`** | The managed .NET binding of the MI API (`CimSession`, `CimInstance`); what PowerShell's CIM cmdlets use | **Confirmed NOT AOT compatible**: crashes with `MissingInteropDataException` in delegate marshalling (`MI_ApplicationWrapper_Initialize`, [PowerShell/MMI#54](https://github.com/PowerShell/MMI/issues/54)); package targets netstandard1.6 (cannot carry trim/AOT annotations); **repo archived 2024-06-14** — it will never be fixed |
| **WmiLight** | Third-party MIT-licensed WMI client (queries, methods, event subscriptions) over classic WMI with its own native shim | **Native AOT supported since 5.0** (`PublishWmiLightStaticallyLinked` can fold the shim into the exe) |

So: "the MI API" is the native `mi.h` C API, and MMI (`Microsoft.Management.Infrastructure`) is
its managed CIM wrapper — the one this repo already uses in the Windows Firewall feature. The
research verdict overturns the earlier "uncertain" rating: **MMI is a dead end for AOT**, and the
existing 467 MMI call sites must migrate too.

## Sanctioned replacements (normative)

New code MUST use the right-hand column. Existing code migrates per the phase plan.

| Forbidden pattern (AOT-incompatible) | Sanctioned AOT-compatible replacement |
|---|---|
| `dynamic` over COM (IDispatch late binding) | Typed COM interfaces via `[GeneratedComInterface]`/`ComWrappers` source generation; `ComVariant` (`System.Runtime.InteropServices.Marshalling`) for VARIANT parameters |
| `Type.GetTypeFromProgID` / `GetTypeFromCLSID` + `Activator.CreateInstance` | `CLSIDFromProgID` + `CoCreateInstance` P/Invoke (CsWin32) returning a `[GeneratedComInterface]` pointer via `ComWrappers` |
| Handwritten `[ComImport]` interfaces | `[GeneratedComInterface]` (source-generated, marshal-free) |
| `System.Management` (`ManagementObjectSearcher`, …) | **WmiLight** (primary), or classic WMI COM (`IWbemServices`) via CsWin32 `allowMarshaling: false` (fallback, no third-party dependency) |
| `Microsoft.Management.Infrastructure` (`CimSession`, …) | Same as above — WmiLight covers queries, method invocation, and event subscriptions over WQL |
| `System.DirectoryServices(.AccountManagement)` | NetAPI32 (`NetUserEnum`, `NetLocalGroup*`, `NetUserGetInfo`, …) and LSA via CsWin32 |
| `System.Diagnostics.PerformanceCounter` | PDH API (`PdhOpenQuery`, `PdhCollectQueryData`, …) via CsWin32 |
| `{Binding}` in XAML | `{x:Bind}` (compile-time generated) |
| Reflection-based `JsonSerializer` calls | Source-generated `JsonSerializerContext` |
| `[ObservableProperty]` on fields (MVVMTK0045) | `[ObservableProperty]` on partial properties (MVVM Toolkit 8.4+, `LangVersion=preview` already enables this) |
| Non-`partial` classes crossing the WinRT ABI (CsWinRT1028/1029) | Mark the class (and containing types) `partial` |

Engineering notes:

- **CsWin32 for AOT**: set `"allowMarshaling": false` in `NativeMethods.json` so generated code
  avoids the runtime marshaler (documented CsWin32 support for `PublishAot`/`PublishTrimmed`/
  `DisableRuntimeMarshalling` environments).
- **COM automation servers used by this app expose dual interfaces** (AzRoles `IAzAuthorizationStore`,
  COMAdmin catalog, `HNetCfg` firewall, Task Scheduler `ITaskService`), so vtable-based
  `[GeneratedComInterface]` calls replace IDispatch dispatch one-for-one; `dynamic` is never needed.
- **WmiLight deployment**: OneMMC.Core is a class library, so the app project must add
  `<TrimmerRootAssembly Include="WmiLight" />` (documented WmiLight requirement for .NET Standard/
  library encapsulation under AOT) and can set `<PublishWmiLightStaticallyLinked>true</...>` to
  avoid shipping `WmiLight.dll`.
- **WmiLight risk**: single-maintainer project (MIT, 52 releases, actively maintained). Mitigation:
  the MIT license permits forking/vendoring, and the fallback path (classic WMI COM via CsWin32)
  removes the dependency entirely at the cost of more in-house interop code.
- **XAML metadata**: until the XAML compiler/CsWinRT automate rooting, `{Binding}`-free XAML plus
  `partial` classes is the reliable path; use `TrimmerRootDescriptor` only as a last resort for
  types that are genuinely only reachable dynamically.

## Phase plan

Each phase has an acceptance gate measured with the existing evaluation infrastructure
(`/p:OneMMCAotAnalysis=true`; see NativeAotAssessment.md for the exact commands).

| Phase | Scope | Gate |
|---|---|---|
| **M0 — hygiene** (start immediately, no dependencies) | Mark the 74 CsWinRT1028 classes `partial`; convert 26 MVVMTK0045 sites to partial properties; add `JsonSerializerContext` for the 7 JSON sites; convert `{Binding}` → `{x:Bind}` (~925 sites, feature-by-feature starting with WF and AzMan dialogs) | Run B analyzer build: 0 × CsWinRT1028/1029, 0 × MVVMTK0045, 0 × IL2026/IL3050 from JSON; `{Binding}` count trending to 0. **Status 2026-07-02: partial classes, partial properties, and JSON contexts are DONE and gate-verified (analyzer warnings 1,144 → 1,030; CsWinRT1028 = 0, MVVMTK0045 = 0). WF `{x:Bind}` conversion DONE (754 → 0; localized strings bind statically via `{x:Bind prefix:LocalizedStrings.Instance.KEY}`, item templates use `x:DataType`, item classes hoisted to namespace level where needed). Remaining repo-wide: 1,240 `{Binding}` in non-WF views, of which 880 match the proven scriptable localization pattern.** |
| **M1 — boot under AOT** | With M0 done, root remaining XAML metadata; fix startup-path warnings; re-run the Run D publish and launch | AOT-published app boots to the main window and navigates all top-level pages |
| **M2 — WMI/CIM migration** | Replace `System.Management` (121 sites: DiskMgmt, DevMgmt, TPM, WindowsServices, ComExp, WF monitoring) and MMI (467 sites: WF) with WmiLight (or CsWin32 IWbem fallback); remove both package references | Grep: 0 `using System.Management`, 0 `Microsoft.Management.Infrastructure`; affected features pass manual smoke tests on non-AOT build first, then under AOT |
| **M3 — COM/`dynamic` rewrite** | AzMan (~150 `dynamic` + ProgID), ComExp, WF `HNetCfg`, TaskSchd, shared `DirectoryObjectPickerService` → `[GeneratedComInterface]` + `CoCreateInstance`; delete `ComPropertyAccessor` | Grep: 0 `dynamic` in Core/App, 0 `GetTypeFromProgID/CLSID`, 0 `[ComImport]`; features smoke-tested under AOT |
| **M4 — package replacements + cutover** | LusrMgr → NetAPI32; PerfMon → PDH; drop `System.DirectoryServices*`, `System.Diagnostics.PerformanceCounter`; flip the default publish from R2R to `PublishAot` and retire the interim wording in docs | Run D equivalent publish is warning-clean in first-party code, all features smoke-tested under AOT; default publish = AOT |

Sequencing rationale: M0 is mechanical and unblocks M1 (the current startup crash is XAML-side).
M2 before M3 because WMI replacements are self-contained per feature, while M3 (AzMan) is the
single largest rewrite. Features remain shippable throughout because the default publish stays
R2R until the M4 gate.

## Known upstream risks (track, do not block on)

- WinUI/XAML `{Binding}` and metadata rooting under AOT are still maturing upstream (WASDK 1.6
  release notes: "Later releases will enhance both C#/WinRT and the XAML Compiler to automate
  rooting").
- .NET runtime GC-race navigation hang under AOT:
  [dotnet/runtime#104582](https://github.com/dotnet/runtime/issues/104582) — re-test at each
  SDK/WASDK upgrade.
