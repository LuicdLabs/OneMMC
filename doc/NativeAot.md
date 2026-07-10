# Native AOT

**Native AOT is OneMMC's shipped deployment model.** `PublishAot` is set **unconditionally** in
`src/OneMMC/OneMMC.csproj`, so it applies to every configuration — Debug and Release alike. The
AOT/trim analyzers run on every build (defaults in `Directory.Build.props`), first-party code
builds warning-clean, and `dotnet publish` produces a single native executable in either
configuration. Native compilation itself still happens only on `dotnet publish`; a plain
`dotnet build` / VS F5 keeps the normal CoreCLR inner loop.

This is the single reference for OneMMC's Native AOT support: the current verified state, the
mandatory coding rules (sanctioned replacements), the measured baseline the migration started
from, and the M0–M4 migration record with the engineering facts learned along the way. It
consolidates and supersedes the former `doc/NativeAotAssessment.md`, `doc/NativeAotMigration.md`,
and `doc/M4.md` (all preserved in git history).

## Current state (full-enablement verification, 2026-07-10)

| Check | Result |
|---|---|
| `dotnet build` Debug x64, full rebuild, all analyzers on | 0 warnings / 0 errors |
| `dotnet build` Release x64, full rebuild, all analyzers on | 0 warnings / 0 errors |
| `dotnet publish` **Release** win-x64 (ILC + link) | Succeeded, **0 warnings** (including dependencies) |
| `dotnet publish` **Debug** win-x64 (ILC + link) | Succeeded, 0 warnings — `PublishAot` implies self-contained, no Debug-only settings needed |
| Release payload (excl. symbols) | **70.4 MB / 13 files** (`OneMMC.exe` 28.8 MB; remainder is Windows App SDK runtime: onnxruntime/DirectML/WebView2 + resources). Former R2R baseline: 224.4 MB / 276 files |
| WmiLight native shim | Statically linked into the exe (`PublishWmiLightStaticallyLinked`); no `WmiLight.Native.dll` ships |
| UIA navigation smoke on the Release AOT exe | PASS — all 7 top-level pages selected, process alive/responding, 0 errors or exceptions in the Serilog log |
| Debug AOT exe boot smoke | Boots to the main window, responsive, 0 log errors |

Source-level gates (all grep-verified; residual text hits are XML-doc comments describing the
replaced APIs, which is the accepted convention):

- 0 `dynamic` late binding, 0 `[ComImport]`, 0 `Type.GetTypeFromProgID`/`GetTypeFromCLSID` +
  `Activator.CreateInstance` — every COM interop is source-generated.
- 0 `System.Management`, 0 `Microsoft.Management.Infrastructure`, 0 `System.DirectoryServices*`,
  0 `System.Diagnostics.PerformanceCounter` — the packages are gone from the dependency graph.
- 0 `{Binding}` in XAML (all `{x:Bind}`), 0 `DisplayMemberPath`/`SelectedValuePath`.

`IsAotCompatible`/`IsTrimmable` are deliberately **not** set on `OneMMC.Core`: they would stamp
`AssemblyMetadata("IsTrimmable")` and change packaging semantics, while the analyzer properties in
`Directory.Build.props` produce the identical warning stream. The app is the only consumer, and
everything is trimmed under `PublishAot` regardless.

## How to verify

```powershell
# Analyzer-guarded builds (either configuration; must stay 0-warning in first-party code)
dotnet build src/OneMMC/OneMMC.csproj -c Debug -p:Platform=x64
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64

# Native AOT publish (either configuration; requires the MSVC toolchain for the ILC link step)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64 -p:Platform=x64
dotnet publish src/OneMMC/OneMMC.csproj -c Debug -r win-x64 -p:Platform=x64
```

- The link step invokes a bare `vswhere.exe`. In a non-Developer shell, prepend
  `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` first, or the publish fails
  with `MSB3073` (exit 123) after ILC has already compiled successfully.
- Smoke-test the published exe by launching it and navigating the top-level pages, then check the
  Serilog log (`%LOCALAPPDATA%\OneMMC\Logs\`) for `[ERR]`/`[FTL]`/exceptions. A reusable UIA
  driver for this existed as `eng/Drive-AotSmokeNavigation.ps1` (Windows PowerShell 5.1) until the
  evaluation tooling was retired — recover it from git history if automation is needed again.
- Re-run all of the above after every .NET SDK or Windows App SDK upgrade, and re-check the
  [known upstream risks](#known-upstream-risks).

## Terminology: WMI, MI, CIM, MMI (and which is which)

| Name | What it is | AOT status |
|---|---|---|
| **CIM** | The DMTF data-model standard (classes, instances, namespaces) that all of the below implement | n/a (a standard, not an API) |
| **Classic WMI COM API** | `IWbemLocator`/`IWbemServices` (wbemcli.h), the original WMI client API | Usable under AOT only via marshal-free COM (CsWin32 `allowMarshaling: false` or `[GeneratedComInterface]`) — this is what the in-house WF `Wbem` wrapper does |
| **`System.Management`** | The original .NET wrapper over classic WMI COM (`ManagementObjectSearcher`) | **Not AOT/trim compatible** (official); removed in M2 |
| **MI API ("WMI v2")** | The native C API in `mi.h` (`MI_Application_*`, `MI_Session_*`) | Native C API — AOT-neutral, but has no supported managed binding (see next row) |
| **MMI — `Microsoft.Management.Infrastructure`** | The managed .NET binding of the MI API (`CimSession`, `CimInstance`); what PowerShell's CIM cmdlets use | **Confirmed NOT AOT compatible**: crashes with `MissingInteropDataException` in delegate marshalling ([PowerShell/MMI#54](https://github.com/PowerShell/MMI/issues/54)); targets netstandard1.6 (cannot carry annotations); repo archived 2024-06-14 — will never be fixed. Removed in M2 (WF Half B) |
| **WmiLight** | Third-party MIT-licensed WMI client (queries, methods, event subscriptions) over classic WMI with its own native shim | **Native AOT supported since 5.0**; `PublishWmiLightStaticallyLinked` folds the shim into the exe |

## Sanctioned replacements (normative)

All code MUST use the right-hand column. These rules are enforced by the always-on analyzers and
by review; the day-to-day summary lives in
[.github/copilot-instructions.md](../.github/copilot-instructions.md) (§Native AOT Compatibility).

| Forbidden pattern (AOT-incompatible) | Sanctioned AOT-compatible replacement |
|---|---|
| `dynamic` over COM (IDispatch late binding) | Typed COM interfaces via `[GeneratedComInterface]`/`ComWrappers` source generation; the in-house blittable `Variant` (`Core/Infrastructure/Interop`) for VARIANT parameters |
| `Type.GetTypeFromProgID` / `GetTypeFromCLSID` + `Activator.CreateInstance` | `CoCreateInstance` via CsWin32 wrapped by `ComActivator` (`Core/Infrastructure/Interop`) |
| Handwritten `[ComImport]` interfaces | `[GeneratedComInterface]` (source-generated, marshal-free) |
| `System.Management` (`ManagementObjectSearcher`, …) | **WmiLight** (primary), or classic WMI COM (`IWbemServices`) via CsWin32 `allowMarshaling: false` (no-dependency fallback; the WF `Wbem` wrapper is the in-repo example) |
| `Microsoft.Management.Infrastructure` (`CimSession`, …) | Same as above — WmiLight for read/query/method/events; the `Wbem` wrapper for instance CRUD (`PutInstance`) |
| `System.DirectoryServices(.AccountManagement)` | NetAPI32 (`NetUserEnum`, `NetLocalGroup*`, …) and ADSI (`ADsOpenObject`, `IADs*`, `IDirectorySearch`) via CsWin32 |
| `System.Diagnostics.PerformanceCounter` | PDH API (`PdhOpenQuery`, `PdhCollectQueryData`, …) via CsWin32 |
| `{Binding}` in XAML | `{x:Bind}` (compile-time generated); no `DisplayMemberPath`/`SelectedValuePath` (reflection property paths) — use `ItemTemplate` or `ToString()` |
| Reflection-based `JsonSerializer` calls | Source-generated `JsonSerializerContext` |
| `[ObservableProperty]` on fields (MVVMTK0045) | `[ObservableProperty]` on partial properties |
| Non-`partial` classes crossing the WinRT ABI (CsWinRT1028/1029) | Mark the class (and containing types) `partial` |
| Reflection-dependent patterns (`Assembly.Load*`, `Type.GetType(string)`, `MakeGenericType`, `Reflection.Emit`) | Not used anywhere; keep DI registrations explicit |

Engineering notes:

- **CsWin32 for AOT**: `"allowMarshaling": false` is set in `NativeMethods.json` so generated code
  avoids the runtime marshaler. For COM interfaces present in Win32 metadata (IWbem*, IADs*,
  PDH/NetAPI structs), CsWin32 emits marshal-free function-pointer-vtable structs with **vtable
  order taken from Windows metadata** — prefer this over hand-authoring whenever the interface is
  projected, because hand-authoring risks silent vtable-order corruption.
- **Dual (IDispatch-based) automation interfaces** written by hand as `[GeneratedComInterface]`
  derive from the in-house source-gen `IDispatch` base to reproduce the dual vtable
  (IUnknown[3] + IDispatch[4] + members) and declare explicit `get_`/`put_` accessors in exact
  vtable order.
- **WmiLight deployment**: OneMMC.Core is a class library, so the app project carries
  `<TrimmerRootAssembly Include="WmiLight" />` (documented WmiLight requirement for library
  encapsulation under AOT) plus `<PublishWmiLightStaticallyLinked>true</...>`, and references the
  package directly so its build props/targets apply. WmiLight risk: single-maintainer project
  (MIT) — mitigations are vendoring/forking, or falling back to the classic WMI COM wrapper.
- **XAML metadata**: `{Binding}`-free XAML plus `partial` classes is the reliable path; use
  `TrimmerRootDescriptor` only as a last resort for types genuinely reachable only dynamically
  (none needed so far).

## Stage 1 assessment (2026-07-02) — the measured baseline

Recorded from the original feasibility evaluation, run when the default build was ReadyToRun +
self-contained and the analyzers sat behind an opt-in switch (`OneMMCAotAnalysis`, since
dissolved). Environment: .NET SDK 10.0.301, Windows App SDK 2.2.0, VS 18 MSVC toolchain,
Windows 11 Pro 26200, `net10.0-windows10.0.19041.0` Release x64.

| Run | Outcome | Distinct warning sites |
|---|---|---:|
| A — baseline (analyzers off) | 0 warnings (default build proven unchanged) | 0 |
| B — analyzer build (Roslyn) | Succeeded | 1,144 |
| C — trim-only publish (ILLink) | Succeeded (runtime correctness not implied) | 1,604 |
| D — full Native AOT publish (ILC) | **Compiled, but crashed at startup** | 2,381 |
| E — default R2R publish (regression check) | 0 warnings | 0 |

| Publish mode | Size | Files | Boots? |
|---|---:|---:|---|
| R2R self-contained (then-default) | 224.4 MB | 276 | Yes |
| Trimmed (experimental) | 78.7 MB | 118 | Not validated |
| Native AOT (Run D) | 75.7 MB | 16 | **No — `0xC000027B` at startup** |

Key findings that shaped the migration:

- **The boot crash was XAML-side**: stowed exception in `Microsoft.UI.Xaml.dll`
  (`STATUS_STOWED_EXCEPTION`, HRESULT `E_INVALIDARG`) caused by 74 non-`partial` WinRT classes and
  ~925 reflection-based `{Binding}` expressions. Fixing exactly that (M0) made the app boot (M1) —
  no additional metadata rooting was ever needed.
- **1,237 of the 2,381 Run D warning sites sat inside dependency packages**
  (`System.Management`, MMI, `System.DirectoryServices*`, `Microsoft.CSharp` RuntimeBinder, WinRT
  generic marshallers) — fixable only by replacing the packages/patterns, which is what M2–M4 did.
- **Hard blockers by scale**: ~175 `dynamic`-over-COM call sites (AzMan ~150), 13 ProgID/CLSID
  activations, 121 `System.Management` sites, 467 MMI sites (all WF), ~925 `{Binding}`.
  Compile success under AOT does **not** imply runtime success: built-in COM interop and
  `Microsoft.CSharp.RuntimeBinder` simply do not exist under Native AOT, so those sites compile
  and then throw (or, for trimmed `{Binding}`, fail silently — the worst mode for an admin tool).
- **Structurally favorable**: DI fully explicit, no `Reflection.Emit`/`MakeGenericType`/
  `Assembly.Load*`/`XmlSerializer`/`BinaryFormatter` anywhere; CsWin32 and CommunityToolkit.Mvvm
  source generators already AOT-friendly.
- **Decision (2026-07-02)**: Native AOT adopted as the end-state; ReadyToRun kept as the interim
  publish so the app stayed shippable throughout; retired at the M4 cutover.

## Migration record (M0–M4)

Sequencing: M0 (mechanical hygiene) unblocked M1 (boot). M2 ran before M3 because WMI replacements
were self-contained per feature, while M3 (AzMan) was the largest rewrite. M4 replaced the last
packages and flipped the default publish. Every phase gate was re-verified with analyzer builds +
AOT publishes + feature smoke tests; destructive paths were only ever verified against disposable
resources (a scratch VHD, throwaway firewall rules/IPsec sets, throwaway AzMan stores, a
disposable local test user), never production state.

### M0 — hygiene (DONE 2026-07-02)

74 CsWinRT1028 classes made `partial`; 26 MVVMTK0045 sites converted to partial properties;
source-generated `JsonSerializerContext` for all 7 JSON sites; **all ~925 `{Binding}` converted to
`{x:Bind}`** (localized strings bind `{x:Bind prefix:LocalizedStrings.Instance.KEY}`; item
templates use `x:DataType`; item classes hoisted to namespace scope; template→page-VM `Command`
bindings became `Click` + `Tag="{x:Bind}"` handlers). Reflection property paths also removed:
XAML `DisplayMemberPath`/`SelectedValuePath` (WMC1510) and runtime `DisplayMemberPath`
assignments replaced with `ItemTemplate`s or `ToString()`. Gate: 0 CsWinRT1028/1029, 0 MVVMTK0045,
0 WMC1510, `{Binding}` = 0.

### M1 — boot under AOT (DONE 2026-07-03)

M0 alone eliminated the `0xC000027B` startup crash — the AOT-published app booted to the main
window and a UIA smoke run navigated all top-level pages (payload then: 73.8 MB / 14 files). The
GC-race navigation hang (dotnet/runtime#104582) did not manifest. Startup-path audit (DI
bootstrap, `AppSettings` JSON, App/MainWindow) found everything already explicit/source-generated.

### M2 — WMI/CIM migration (DONE 2026-07-09)

**Half A — `System.Management` → WmiLight** (TPM, WindowsServices, DevMgmt, DiskMgmt, ComExp DTC,
WF event watcher). Facts that matter for future WMI work:

- Shared plumbing lives in `Core/Infrastructure/Wmi/`: `WmiObjectDisposalExtensions.DisposeItems`
  (prompt native-handle release), `WmiPropertyExtensions.GetPropertySafe`, `DmtfDateTimeConverter`
  (WmiLight returns CIM datetimes as raw DMTF strings), and `WmiMethodParameterExtensions`.
- **WMI providers demand specific VARTYPEs for method parameters**: VT_I4 for CIM uint16/uint32,
  VT_BSTR for uint64, VT_I2 for char16. WmiLight's native VT_UI2/VT_UI8 encodings are rejected
  with `WBEM_E_TYPE_MISMATCH` (0x80041005) — caught live on `MSFT_Disk.Initialize`.
- Embedded out-parameter objects surface as `WmiObject`/`WmiObject[]`; DiskMgmt's destructive
  paths (Initialize/CreateVolume/Shrink) were verified against a disposable 100 MB VHD.

**Half B — MMI → marshal-free `IWbemServices` wrapper** (Windows Firewall: ~489 sites in 14 files;
WmiLight could not replace MMI here because the WF write path is instance CRUD —
`ModifyInstance`/`CreateInstance`/`DeleteInstance` with instances built from scratch including
**embedded instance arrays** (IKE proposals), and WmiLight has no `PutInstance` surface at all).
The replacement is `Features/SystemManagement/Infrastructure/WF/Wbem/` (`WbemServices`,
`WbemObject`, `WbemCimType`) over CsWin32's marshal-free IWbem structs. Facts:

- `wbemcli.h` interfaces have no type library, so CsWin32 projection (vtable order from Windows
  metadata) was the only trustworthy source; hand-authoring was rejected for that reason.
- **`CoSetProxyBlanket` is mandatory** after `ConnectServer` (MMI did it internally); without the
  DCOM blanket every call fails `WBEM_E_ACCESS_DENIED`. It is one of the few handwritten imports
  (CsWin32 does not project it).
- **Box returned VARIANTs by the property's CIMTYPE, not the VARTYPE**: WMI stores both
  `CIM_UINT16` and `CIM_UINT32` as `VT_I4`, and consumers pattern-match on the exact CLR type.
  Read-parity was proven against MMI: 1,626 properties through both stacks, 0 value and 0
  CLR-type mismatches.
- Property `Get`/`Put` are declared PreserveSig-style (non-throwing) so an absent property
  degrades to `null` like the `CimInstance` indexer — no first-chance `COMException` noise.
- `SpawnInstance` requires a live session (`GetObject` on the class) — unlike `new CimInstance`,
  so the session is threaded into instance builders.
- **`SafeArrayPutElement` on a VT_UNKNOWN SAFEARRAY hard-faults the process** under the
  source-generated/marshal-free interop model. Populate via `SafeArrayAccessData` + direct
  pointer write + `Marshal.AddRef` instead; `FADF_UNKNOWN` still balances releases.
- `WbemObject` carries a finalizer backstop (`IWbemClassObject` is a free-threaded data object,
  safe to release from the finalizer thread) because lazy LINQ enumeration cannot always dispose
  deterministically.
- Write path was round-trip-verified against MMI on a disposable `MSFT_NetIKEMMCryptoSet` and a
  disposable connection-security rule (create → read back through both stacks → modify → delete),
  then all 14 WF files were converted and the MMI package reference removed.

### M3 — COM/`dynamic` rewrite (DONE 2026-07-04)

All late-binding/`[ComImport]` COM became typed `[GeneratedComInterface]` calls. Reusable
foundation in `Core/Infrastructure/Interop/`: `ComActivator` (CsWin32 `CoCreateInstance` +
`StrategyBasedComWrappers`), the source-gen `IDispatch` base, and the blittable 24-byte `Variant`
(chosen over `ComVariant` to avoid assembly-wide `[DisableRuntimeMarshalling]`, which would have
forced converting the remaining runtime-marshalled handwritten P/Invoke files first). Facts:

- **Vtable order is authoritative and must come from the binary**: dual-interface members were
  transcribed from each machine's own type library (azroles.dll, FirewallAPI.dll, COMAdmin)
  with a dumper script (`eng/Dump-TypeLibVtable.ps1`, retired with the eng tooling — in git
  history) that decoded slots and parameter/return types. Dual members begin at vtable slot 7.
- **Coclass `ThreadingModel` matters**: GroupPolicyObject is Apartment (STA-affine) — activation
  must happen on an STA thread; Task Scheduler/COMAdmin/HNetCfg/DsObjectPicker are Both/Free.
- **AzMan specifics** (the flagship rewrite, ~150 `dynamic` sites): AzRoles booleans are LONG
  (I4), not VARIANT_BOOL; nearly every mutator takes a trailing optional `VARIANT varReserved`;
  string-list getters return VARIANT-wrapped SAFEARRAYs (`Variant.ToStringList()`); a parent must
  be `Submit()`ted before children can be created on it (else `0x80072089`); collections are
  **one-based** (`Item(0)` → `E_INVALIDARG`). XML (`msxml://`) stores do not support the
  policy-admin getters (`0x80070032`) — those getters are declared `[PreserveSig] int` so the
  HRESULT degrades to an empty list instead of throwing first-chance exceptions.
- **Source-generator constraints**: BCL `FORMATETC`/`STGMEDIUM` cannot appear in generated
  signatures (SYSLIB1051 — use a blittable mirror struct and a plain `uint tymed`); nested types
  in generated COM signatures must be `internal`+ (SYSLIB1090).
- **WF HNetCfg**: the rule collection only enumerates via `IEnumVARIANT` (source-gen port) and
  `INetFwRule2/3` use interface inheritance so a wrapper IS-A its bases (correct QI on `Add`).
  `Variant.FromStringArray` builds the VT_ARRAY|VT_BSTR `Interfaces` VARIANT.
- **Gate lesson**: "0 `dynamic`" means the late-binding *keyword*, not the word — SecPol's hits
  were an identifier/comments (renamed for grep hygiene); domain prose like "dynamic disk" stays.

### M4 — package replacements + cutover (DONE 2026-07-10)

- **LusrMgr → NetAPI32** (drop-in backend swap, public surface unchanged): `NetUserEnum` level 3,
  `NetLocalGroup*`, `NetUserGetLocalGroups` (flags 0 = direct membership, pinned to old WinNT
  semantics); flags via level-1008 RMW; `password_expired` via level-4 RMW (no 10xx level exists
  for it); silent no-op/success mappings pinned to the old FindByIdentity/Contains behavior
  (NERR_UserNotFound/GroupNotFound, ERROR_MEMBER_IN_ALIAS/NOT_IN_ALIAS); other statuses throw
  `Win32Exception(status)` so `IsPermissionError` keeps recognizing access-denied. Verified with a
  disposable local test user round-trip, elevated and non-elevated.
- **PerfMon → PDH**: per-counter query topology (one `PDH_HQUERY`+`PDH_HCOUNTER` per cache entry)
  for exact `NextValue()` semantic parity and fault isolation; `PdhAddCounterW` with
  `PdhAddEnglishCounterW` fallback (fixes the latent hardcoded-English-counter bug on
  non-English OSes); enumeration via `PdhEnumObjectsW`/`PdhEnumObjectItemsW` (PDH has no
  category/counter help text — documented behavior change).
- **ADSI wrapper** (`Core/Infrastructure/Interop/Adsi/`) over CsWin32 `IADs*`/`IDirectorySearch`
  replaced the three `System.DirectoryServices` consumers (deployed-printer lookup, GPO printer
  deployment, AzMan AD store schema). The in-house `Variant` casts directly to CsWin32's
  `VARIANT*`. **Honest caveat**: this machine is not domain-joined — off-domain degradation
  paths are verified live; the domain-joined happy paths are structurally verified (metadata
  vtables + docs transcription), lab pass pending.
- **Packages dropped**: `System.DirectoryServices`, `System.DirectoryServices.AccountManagement`,
  `System.Diagnostics.PerformanceCounter` (with `System.Management` and MMI already gone in M2);
  `ThirdPartyNotices.txt` refreshed (WmiLight added, stale versions fixed).
- **Cutover**: default publish flipped to `PublishAot` — set **unconditionally** so the analyzers
  and CsWinRT AOT codegen guard every build; Debug F5 still runs CoreCLR. The `OneMMCAotAnalysis`
  opt-in switch (`eng/AotAnalysis.props`) was dissolved into `Directory.Build.props` defaults:
  `EnableAotAnalyzer`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`,
  `CsWinRTAotWarningLevel=2`, `TrimmerSingleWarn=false`, `SuppressTrimAnalysisWarnings=false`.
- **Full enablement verified 2026-07-10** on both configurations — see
  [Current state](#current-state-full-enablement-verification-2026-07-10). With that, the
  evaluation-era tooling (`eng/` scripts and `eng/aot-logs/`) was retired; recover from git
  history if needed.

## Known upstream risks (track, do not block on)

- WinUI/XAML `{Binding}` and metadata rooting under AOT are still maturing upstream (WASDK
  release notes: "Later releases will enhance both C#/WinRT and the XAML Compiler to automate
  rooting") — irrelevant while the codebase stays `{x:Bind}`-only, but re-check on upgrades.
- .NET runtime GC-race navigation hang under AOT:
  [dotnet/runtime#104582](https://github.com/dotnet/runtime/issues/104582) — has never
  manifested here; re-test at each SDK/WASDK upgrade.
