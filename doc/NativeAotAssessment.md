# Native AOT Feasibility Assessment

## Purpose and Method

This document records the Stage 1 evaluation of Native AOT compatibility for OneMMC: an opt-in
build switch was added to surface every AOT/trimming diagnostic without changing the default
build or publish behavior, the analysis builds and experimental publishes were executed, and the
results are aggregated here to decide whether a full-codebase refactor toward Native AOT is
worthwhile.

**The default build remains ReadyToRun + self-contained. Nothing in this evaluation changes
shipped behavior.** The switch must never be enabled by default.

### Evaluation infrastructure

| Piece | Location |
|---|---|
| Gated MSBuild properties | `eng/AotAnalysis.props` (imported by `Directory.Build.props` only when `OneMMCAotAnalysis=true`) |
| Conditional R2R exclusion | `src/OneMMC/OneMMC.csproj` (R2R and `PublishAot` are mutually exclusive) |
| Conditional `MVVMTK0045` un-suppression | `src/OneMMC.Core/OneMMC.Core.csproj` |
| Warning aggregation script | `eng/Get-AotWarningSummary.ps1` |
| Logs (gitignored) | `eng/aot-logs/` |

The switch enables `EnableAotAnalyzer`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`,
`CsWinRTAotWarningLevel=2`, and `TrimmerSingleWarn=false` for both projects, plus
`PublishAot=true` for the app project. `IsAotCompatible`/`IsTrimmable` are deliberately **not**
set: they would stamp `AssemblyMetadata("IsTrimmable")` into the built assemblies and publicly
claim a compatibility that does not exist, while producing the identical warning stream.

### Commands

```powershell
# Run A - baseline (switch OFF; proves default build is unchanged)
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64 -t:Rebuild `
  "-flp2:LogFile=eng/aot-logs/baseline-warn.log;WarningsOnly"

# Run B - analyzer build (switch ON, no publish; Roslyn-level diagnostics)
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64 -p:OneMMCAotAnalysis=true -t:Rebuild `
  "-flp2:LogFile=eng/aot-logs/aot-build-warn.log;WarningsOnly"

# Run C - trim-only publish (isolates ILLink IL2xxx from AOT codegen IL3xxx)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64 -p:Platform=x64 `
  -p:OneMMCAotAnalysis=true -p:PublishAot=false -p:PublishTrimmed=true `
  "-flp2:LogFile=eng/aot-logs/trim-publish-warn.log;WarningsOnly"

# Run D - full Native AOT publish (ILC)
dotnet publish src/OneMMC/OneMMC.csproj -c Release -r win-x64 -p:Platform=x64 -p:OneMMCAotAnalysis=true `
  "-flp2:LogFile=eng/aot-logs/aot-publish-warn.log;WarningsOnly"

# Aggregation (after each run)
pwsh eng/Get-AotWarningSummary.ps1 -LogFile eng/aot-logs/<run>-warn.log -OutFile eng/aot-logs/<run>-summary.md
```

### Environment

| Item | Value |
|---|---|
| Date | 2026-07-02 |
| .NET SDK | 10.0.301 |
| Windows App SDK | 2.2.0 (CsWinRT provided by the .NET SDK, >= 2.1.1) |
| MSVC toolchain (required by ILC) | Visual Studio 18 Community, VC.Tools.x86.x64 present |
| OS | Windows 11 Pro 26200 |
| Target | `net10.0-windows10.0.19041.0`, Release, x64, `win-x64` |

## Results Summary

| Run | Outcome | Distinct warning sites |
|---|---|---:|
| A - baseline (switch off) | Build succeeded, **0 warnings** (default build proven unchanged) | 0 |
| B - analyzer build | Build succeeded, 2,056 raw warnings | **1,144** |
| C - trim-only publish | Publish succeeded (ILLink completed), 1,663 raw warnings | **1,604** |
| D - full AOT publish | **Compilation succeeded** (ILC + link.exe); **app crashes at startup** (`0xC000027B` stowed exception in `Microsoft.UI.Xaml.dll`) | **2,381** |
| E - default R2R publish (switch off, regression check) | Publish succeeded, **0 warnings** - default publish path unaffected by the new wiring | 0 |

Output size comparison (win-x64, Release, excluding symbol files):

| Publish mode | Size | Files | Boots? |
|---|---:|---:|---|
| R2R self-contained (current default) | 224.4 MB | 276 | Yes (shipping path) |
| Trimmed (Run C, experimental) | 78.7 MB | 118 | Not validated; `{Binding}`/WMI/reflection expected to break |
| Native AOT (Run D, experimental) | 75.7 MB | 16 | **No - crashes at startup** |

## Warning Breakdown

### Run B - analyzer build (Roslyn diagnostics, source-level)

| Code | Count | Meaning |
|---|---:|---|
| IL2026 | 510 | RequiresUnreferencedCode member called (trim-unsafe) |
| IL3050 | 510 | RequiresDynamicCode member called (AOT-unsafe) |
| CsWinRT1028 | 74 | Class implements WinRT interfaces but is not partial |
| MVVMTK0045 | 26 | `[ObservableProperty]` field not AOT-safe for WinRT (use partial property) |
| IL2072 | 14 | Return value does not satisfy DynamicallyAccessedMembers |
| IL2075 | 5 | Unknown reflected type (GetProperty/GetMethod on unannotated Type) |
| IL2050 | 3 | P/Invoke with COM marshalling (trimmer cannot analyze) |
| IL2087 / IL2091 | 2 | Generic parameter annotation mismatches |

The IL2026/IL3050 pairs are dominated by `dynamic` dispatch (each `dynamic` call site emits both
codes via the `Microsoft.CSharp.RuntimeBinder` API surface) plus the reflection-based
`JsonSerializer` sites.

| Feature area | Count |
|---|---:|
| Core/Features/UserSecurity/Services (AzMan, SecPol) | 938 |
| Core/Features/SystemManagement/Services (ComExp, WF, TPM) | 92 |
| App/Converters | 30 |
| Core/Features/PCManagement (Services + ViewModels + Models) | 28 |
| Core ViewModels (SystemManagement/UserSecurity/Policy/Certificates) | 22 |
| Core/Features/PolicyManagement/Services | 9 |
| App (Models/Views/Services) | 17 |
| Core/Infrastructure | 4 |
| other/generated | 4 |

82% of all analyzer warnings sit in `Core/Features/UserSecurity/Services` — almost entirely the
AzMan COM automation layer. This matches the static inventory (~150 `dynamic` sites in AzMan).

### Run C - trim-only publish (ILLink, whole-program including dependencies)

| Code | Count | Meaning |
|---|---:|---|
| IL2026 | 912 | RequiresUnreferencedCode member called (trim-unsafe) |
| IL3050 | 510 | RequiresDynamicCode member called (AOT-unsafe) |
| CsWinRT1028 | 74 | Class implements WinRT interfaces but is not partial |
| IL2081 | 35 | Field value does not satisfy generic annotations (WinRT ABI marshallers) |
| MVVMTK0045 | 26 | `[ObservableProperty]` field not AOT-safe for WinRT |
| IL2072/2075/2070/2067/2077/2087/2091 | 43 | DynamicallyAccessedMembers annotation mismatches |
| IL2050 | 4 | P/Invoke with COM marshalling (trimmer cannot analyze) |

Compared with Run B, ILLink adds ~460 warning sites attributed to dependency assemblies
("(other/generated)" bucket): `System.Management`, `System.DirectoryServices*`, and CsWinRT's own
generic WinRT ABI marshallers (`IVectorViewMethods<T>`, `KeyValuePairMethods<K,V>` - IL2081).
IL2026 nearly doubles (510 → 912) because the trimmer sees trim-unsafe calls *inside* those
packages, which the source-level analyzers of Run B cannot.

The trim-only publish **completing** proves the toolchain runs end-to-end; it says nothing about
runtime correctness - `{Binding}` targets, WMI, and reflection paths in the trimmed output are
expected to fail or silently misbehave at runtime.

### Run D - full Native AOT publish (ILC, whole program)

| Code | Count | Meaning |
|---|---:|---|
| IL3050 | 1,319 | RequiresDynamicCode member called (AOT-unsafe) |
| IL2026 | 912 | RequiresUnreferencedCode member called (trim-unsafe) |
| CsWinRT1028 | 74 | Class implements WinRT interfaces but is not partial |
| MVVMTK0045 | 26 | `[ObservableProperty]` field not AOT-safe for WinRT |
| IL2072/2075/2070/2067/2077/2087/2091 | 43 | DynamicallyAccessedMembers annotation mismatches |
| IL2050 | 4 | P/Invoke with COM marshalling |
| IL3052 | 3 | COM interop with unsupported marshalling under AOT |

IL3050 grows from 510 (Run B) to 1,319: ILC sees the AOT-unsafe code *inside* dependency
assemblies. The "(other/generated)" bucket alone is 1,237 sites - `System.Management`,
`System.DirectoryServices*`, `Microsoft.CSharp` RuntimeBinder machinery, and CsWinRT's generic
WinRT ABI marshallers. **These cannot be fixed in this repository**; they go away only by
replacing the packages/patterns outright.

Notes on the two publish attempts:

- The first attempt failed at the native **link step** with a garbled linker command
  (`Microsoft.NETCore.Native.targets` invokes a bare `vswhere.exe`, which was not on `PATH` in
  the non-Developer shell; MSB3073 exit 123). This was an **environment issue, not a code
  result** - ILC itself had already compiled the entire app to native code. Prepending
  `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` and re-publishing succeeded.
- Output size: publish folder 220 MB, of which 145 MB is symbol files (`OneMMC.pdb` 144 MB).
  Actual app payload ≈ **75.7 MB / 16 files** (single native `OneMMC.exe` 33 MB +
  `onnxruntime.dll`/`DirectML.dll` from Windows App SDK + resources). For comparison the
  trim-only output (Run C) was 78.7 MB / 118 files.

### Runtime smoke test - the decisive result

The AOT-compiled `OneMMC.exe` **crashes immediately at startup**:

- Process exit code `0xC000027B` (`STATUS_STOWED_EXCEPTION` - an unhandled WinRT/XAML failure).
- Windows Error Reporting: faulting module `Microsoft.UI.Xaml.dll` (Windows App Runtime 2.2),
  stowed HRESULT `0x80070057` (`E_INVALIDARG`).

This is exactly the predicted failure mode: with 74 non-`partial` WinRT classes, ~925
reflection-based `{Binding}` expressions, and trimmed XAML metadata, XAML initialization fails
before the first window appears. **Native AOT currently produces a binary that compiles cleanly
enough but cannot even boot.**

## Static Inventory (code sweep)

Independent of the analyzer runs, a full sweep of the codebase (454 `.cs` files, 146 `.xaml`
files) found the following AOT-relevant patterns.

### Hard blockers (unsupported under Native AOT, not merely warned)

| Pattern | Scale | Where |
|---|---|---|
| `dynamic` over COM (IDispatch late binding) | ~175 call sites in 17 files | AzMan (~150 - the entire feature), ComExp, WF, SecPol |
| COM ProgID/CLSID activation (`Type.GetTypeFromProgID`/`FromCLSID` + `Activator.CreateInstance`) | 13 sites | AzMan, ComExp, WF (`HNetCfg.*`), TaskSchd, `DirectoryObjectPickerService` (shared by ~20 dialogs) |
| WMI via `System.Management` (officially not AOT/trim compatible) | 121 sites in 11 files | DiskMgmt (~81), DevMgmt, TPM, WindowsServices, ComExp, WF monitoring |
| Reflection-based `{Binding}` markup | ~925 occurrences in 63 files (vs ~2123 `{x:Bind}`) | Heaviest in WF and AzMan dialogs |

Native AOT has **no built-in COM interop** and **no `Microsoft.CSharp.RuntimeBinder`**: the COM
activation and `dynamic` sites *compile* but throw at runtime under AOT. Compile success in Run D
therefore does not imply the app works; see [Runtime Risks](#runtime-risks-even-where-compilation-succeeds).

### Moderate / mechanical items

| Pattern | Scale | Remediation |
|---|---|---|
| Reflection-based `JsonSerializer` (no `JsonSerializerContext`) | 7 sites in 4 files (`AppSettings`, AzMan persistence, PerfMon config, SecPol definitions) | Source-generated `JsonSerializerContext` - small, contained |
| MI/CIM via `Microsoft.Management.Infrastructure` | 467 sites in 14 files (all WF) | More AOT-tolerant than `System.Management` but unproven; `CimInstance` property bags need runtime validation |
| Hand-written `[ComImport]` interfaces | 114 occurrences in 21 files | Rewrite as `[GeneratedComInterface]`/`ComWrappers` source generation |
| Member reflection | ~12 sites in 6 files | Annotate or replace; small |
| Classes crossing the WinRT ABI not yet `partial` | surfaced as CsWinRT1028/1029 in Run B | Mechanical `partial` additions |

### Structurally favorable findings

- **Dependency injection is fully explicit** - `AddOneMMCCore()` calls per-feature module
  extensions with explicit `AddSingleton`/`AddTransient`. No assembly scanning. No change needed.
- No `Reflection.Emit`, `MakeGenericType`/`MakeGenericMethod`, `Type.GetType(string)`,
  `Assembly.Load*`, `XmlSerializer`, or `BinaryFormatter` anywhere.
- CsWin32-generated P/Invokes and CommunityToolkit.Mvvm source generators are AOT-friendly.
- RSoP is entirely clean; Certificates, PrintManagement, FsMgmt, EventViewer, and
  NetworkListManager are low-risk.

## Package Compatibility

| Package | AOT status |
|---|---|
| `System.Management` 10.0.9 | **Not supported** (official). Alternatives: MI API, WmiLight, CsWin32 `IWbem*` COM |
| `System.DirectoryServices` / `.AccountManagement` 10.0.9 | Expected **not supported** (built on built-in COM interop / ADSI) |
| `System.Diagnostics.PerformanceCounter` 10.0.9 | Expected not supported (registry/reflection marshalling) |
| `Microsoft.Management.Infrastructure` 3.0.0 | Uncertain - evidence from Runs C/D below |
| `System.Drawing.Common` 10.0.9 | Partial (GDI+ interop; some paths warn) |
| `Microsoft.WindowsAppSDK` 2.2.0 | Supported since 1.6 with `partial` classes + `{x:Bind}` |
| `CommunityToolkit.Mvvm` 8.4.2 | Supported (source generators); `MVVMTK0045` items need partial properties |
| `Serilog`, `Microsoft.Extensions.DependencyInjection`/`Logging` | Supported |

## Per-Feature Blocker Table

| Feature | Blocker class | Severity | Remediation type |
|---|---|---|---|
| AzMan (UserSecurity) | COM activation + ~150 `dynamic` | **Hard** | Full typed `ComWrappers` source-gen rewrite of the AzRoles automation surface |
| ComExp (SystemManagement) | COM `dynamic` (COMAdmin) + WMI | **Hard** | ComWrappers rewrite + WMI migration |
| WF / Windows Firewall | `HNetCfg.*` COM activation + 467 MI/CIM + heaviest `{Binding}` | **Hard** | ComWrappers rewrite; validate MI/CIM; x:Bind conversion |
| TaskSchd (PCManagement) | CLSID activation + `[ComImport]` | **Hard** | ComWrappers (`[GeneratedComInterface]`) rewrite |
| SecPol (UserSecurity) | some `dynamic` + JSON | Hard (partial) | ComWrappers + `JsonSerializerContext` |
| DiskMgmt (PCManagement) | ~81 WMI sites | Hard (package) | Migrate to MI API / WmiLight / CsWin32 `IWbem*` |
| DevMgmt, TPM, WindowsServices | WMI | Hard (package) | Same WMI migration |
| PerfMon (PCManagement) | `PerformanceCounter` package + reflection | Hard (package) | PDH via CsWin32 |
| LusrMgr (PCManagement) | `System.DirectoryServices.AccountManagement` | Hard (package) | SAM/NetAPI via CsWin32 or LDAP rewrite |
| Shared `DirectoryObjectPickerService` | CLSID activation + `[ComImport]` | **Hard, cross-cutting** (~20 dialogs) | ComWrappers rewrite |
| GpEdit (PolicyManagement) | `[ComImport]` (`IGroupPolicyObject`) | Medium | ComWrappers rewrite |
| Certificates, PrintManagement, FsMgmt, EventViewer, NetworkListManager | mostly P/Invoke/managed | Low | `partial` + x:Bind hygiene |
| RSoP (PolicyManagement) | none found | **Clean** | - |

## Runtime Risks Even Where Compilation Succeeds

- Built-in COM interop is disabled under Native AOT: every `GetTypeFromProgID`/`CLSID` +
  `Activator.CreateInstance` site throws `PlatformNotSupportedException` at runtime.
- `dynamic` dispatch throws (no `RuntimeBinderException` infrastructure, no IDispatch binder).
- `{Binding}` targets that get trimmed fail silently (blank UI, no exception).
- Known WinUI-on-AOT issue: page navigation can hang due to a GC-thread race
  ([dotnet/runtime#104582](https://github.com/dotnet/runtime/issues/104582)).
- For an administrative tool, silent runtime breakage is the worst failure mode - compile-time
  success in Run D must not be read as viability.

## Phased Roadmap (if AOT is pursued)

| Phase | Work | Nature | Rough effort |
|---|---|---|---|
| P0 - hygiene (valuable even without AOT) | `partial` classes (CsWinRT1028/29), `MVVMTK0045` partial properties, `JsonSerializerContext` (7 sites), `{Binding}`→`{x:Bind}` (~925 sites) | Mechanical, low-risk | Days-weeks; x:Bind conversion is the bulk |
| P1 - WMI migration | `System.Management` (121 sites) → MI API or WmiLight | Per-feature rewrite + retest | Weeks |
| P2 - COM/`dynamic` rewrite | AzMan/ComExp/WF/TaskSchd/ObjectPicker → typed `[GeneratedComInterface]`/`ComWrappers` | Deep interop work; AzMan alone is a full-feature rewrite | Months |
| P3 - package replacements | `DirectoryServices.AccountManagement`, `PerformanceCounter` → CsWin32-based implementations | Rewrite + behavior parity testing | Weeks-months |

## Recommendation

**No-go for full Native AOT at this time. Keep ReadyToRun. Adopt the P0 hygiene items
opportunistically.**

Decision inputs, with measured values:

1. **Blocker breadth** - 2,381 distinct AOT warning sites; **1,237 of them are inside dependency
   packages** and cannot be fixed in this repository (`System.Management`,
   `System.DirectoryServices*`, `Microsoft.CSharp` RuntimeBinder, WinRT generic marshallers).
   Eight-plus features require full interop rewrites: AzMan, ComExp, WF, TaskSchd, DiskMgmt,
   DevMgmt, PerfMon, LusrMgr, plus the shared `DirectoryObjectPickerService` used by ~20 dialogs.
   The refactor is measured in months (P2 alone - the COM/`dynamic` rewrite - is a full rewrite
   of the AzMan automation surface).
2. **Measured benefit** - size drops 224.4 MB → 75.7 MB (-66%) and 276 → 16 files, which is
   attractive but secondary for a locally-installed admin tool; the startup benefit could not be
   measured because the app does not start.
3. **Risk profile** - the decisive result: the AOT binary **crashes at startup** inside
   `Microsoft.UI.Xaml.dll` (`0xC000027B`). Even after fixing boot (74 `partial` classes, ~925
   `{Binding}` → `{x:Bind}`, XAML metadata rooting), every COM-activation, `dynamic`, and WMI
   code path would still fail *at runtime, not compile time* - the worst possible failure mode
   for a system-administration tool where silent breakage can misconfigure machines.

### What is worth doing regardless (P0 hygiene)

These improve trim-readiness and code health without committing to AOT:

- Mark the 74 CsWinRT1028 classes `partial` (mechanical).
- Fix the 26 `MVVMTK0045` sites by moving `[ObservableProperty]` to partial properties.
- Add a `JsonSerializerContext` for the 7 reflection-based JSON sites.
- Prefer `{x:Bind}` in new XAML; convert existing `{Binding}` opportunistically.

### Re-evaluation triggers

Revisit this assessment (re-run the four commands above) when any of these change:

- Windows App SDK / XAML compiler ships full AOT support for `{Binding}` and automatic metadata
  rooting (tracked since WASDK 1.6 release notes: "Later releases will enhance both C#/WinRT and
  the XAML Compiler to automate rooting").
- AOT-compatible replacements are adopted for `System.Management` (e.g. MI API, WmiLight),
  `System.DirectoryServices.AccountManagement`, and `System.Diagnostics.PerformanceCounter`.
- The AzMan/ComExp/WF/TaskSchd COM layers are rewritten onto source-generated `ComWrappers`
  (`[GeneratedComInterface]`), eliminating `dynamic` and ProgID activation.
