# Memory Management

How OneMMC keeps its memory footprint flat across a long session, the rules new code must follow, and how
to measure a regression.

The problem this addresses: memory used to grow with the *number of pages visited*, not with the data
currently on screen, and it never came back down. That is now fixed — but the fixes are easy to undo by
accident, so the rules below are load-bearing.

## Rules

### 1. Resolve disposable view models from a page scope, never the root provider

`App.GetRequiredService<T>()` resolves from the **root** `ServiceProvider`. `Microsoft.Extensions.DependencyInjection`
adds every `IDisposable` it creates to the resolving scope's disposal list, and the root provider's list is
only drained at process exit. A page that resolves a transient `IDisposable` view model there pins one
instance — and its entire object graph — per navigation. This is a documented anti-pattern
([Dependency injection guidelines → "Disposable transient services captured by container"](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines#example-anti-patterns)).

Use `PageServiceScope` (`src/OneMMC/Services/PageServiceScope.cs`):

```csharp
private readonly PageServiceScope _serviceScope = new();

public EventViewerPage()
{
    ViewModel = _serviceScope.GetRequiredService<EventViewerViewModel>();
    InitializeComponent();
    Unloaded += OnUnloaded;
}

private void OnUnloaded(object sender, RoutedEventArgs e)
{
    // ... unhook handlers, null out ItemsSource/Content ...
    _serviceScope.Dispose();   // disposes the VM *and* drops the container's reference
}
```

Do **not** also call `ViewModel.Dispose()` — the scope does it. `PageServiceScope.Dispose()` is idempotent,
because `Unloaded` is not guaranteed to fire exactly once.

Non-disposable view models are not tracked by the container and may still use `App.GetRequiredService<T>()`.

`EventViewerPage.xaml.cs` is the reference implementation for full page cleanup.

### 2. Navigation parameters carry identifiers, not objects

`Frame.BackStack` retains every `PageStackEntry`, and each entry holds the parameter it was navigated
with. The breadcrumb trail holds them too. Passing a live service or view model therefore pins it for the
rest of the session.

Pass a store path, an application name, an instance id. Let the target page resolve services from DI.
See `StoreNavigationParameter` / `ApplicationNavigationParameter` / `ScopeNavigationParameter`.

A small, self-contained DTO with no service or view-model references (for example `DeviceInfo`) is
acceptable.

### 3. `Dispose()` releases owned services — clearing collections is insurance, not a GC win

Disposing an owned service is required: it releases native and COM handles the GC does not manage.

```csharp
public void Dispose()
{
    if (_disposed) return;
    _rsopService.Dispose();
    RootNodes.Clear();
    CurrentPolicies.Clear();
    SelectedNode = null;
    _disposed = true;
}
```

Never dispose a **singleton** you were injected with. `AzManService` and `AdmxBundleProvider` are shared;
consumers drop their reference and nothing more.

**The collection clearing is not what frees the memory.** Once a page is unloaded it is unreachable, so its
view model and every collection hanging off it are unreachable too, and the GC frees the whole graph in one
pass whether or not the collections were emptied first. `Microsoft.Extensions.DependencyInjection` only
tracks `IDisposable` services, so a non-disposable transient view model — which is what every
`ClearCachedData()` caller has — is not retained by the container either. That is the same fact rule 1
depends on.

The eleven `ClearCachedData()` calls in `Unloaded` handlers are kept for a different, honest reason: they
bound the damage if a page *is* retained, which the known upstream `ComWrappers` issue at the end of this
document can cause. `CertificateStoresViewModelBase.ClearCachedData()` is the reference shape. Do not add
new ones expecting a measurable saving, and do not repeat the old claim that they make "memory return at
unload instead of at some later GC" — that was wrong.

### 4. Lists must live in a height-constrained container

A `ScrollViewer` (or a `StackPanel`, or an `Auto` grid row) gives its child unbounded height. A
virtualizing list handed unbounded height realizes **every** item — virtualization is silently off.

- Never use `ItemsControl` for a collection that can grow. It does not virtualize at all.
- Never override `ItemsPanel` with a plain `StackPanel`. That disables `ItemsStackPanel`.
- `ItemsRepeater` + `StackLayout` directly inside a `ScrollViewer` is the correct pattern for
  variable-height cards. See `ConnectionSecurityRulesPage.xaml`, or the two certificate pages.
- A plain `ListView` in a `*`-sized `Grid` row is correct for uniform rows. See `ServicesPage.xaml`.
- `SettingsExpander` hosts an `ItemsRepeater` internally, so it inherits the same rule.

Tuning knobs when a list is unavoidably large: `ItemsStackPanel.CacheLength` (default **4.0** = two
viewports either side) and `ItemsRepeater.VerticalCacheLength` (default **2.0**).

### 5. `SettingsExpander` bound to data starts collapsed

`IsExpanded="True"` on an expander bound to a growing collection realizes the whole collection on load.
Default to collapsed, matching the native MMC snap-ins. Expanders over a fixed handful of static rows
(property sheets) may stay expanded — there is nothing to virtualize.

### 6. `TreeView` fills on expand and releases on collapse

`TreeView` materializes a `TreeViewNode` per node you add, so building the mirror recursively costs the
whole tree up front. Use the documented pattern
([Tree view → "Fill a node when it's expanding"](https://learn.microsoft.com/windows/apps/develop/ui/controls/tree-view#interacting-with-a-tree-view)):

```csharp
private static TreeViewNode CreateTreeNode(Item item) =>
    new() { Content = item, HasUnrealizedChildren = item.HasChildren };

private void Tree_Expanding(TreeView sender, TreeViewExpandingEventArgs args) { /* add children */ }
private void Tree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
{
    args.Node.Children.Clear();
    args.Node.HasUnrealizedChildren = true;
}
```

Handlers wired from XAML must be **instance** methods — the generated code calls `this.Handler`, so
marking them `static` fails to compile with CS0176.

Where the *data* tree is also expensive (GpEdit walks the whole ADMX category graph), defer that too:
see `GroupPolicyTreeItem.EnsureChildrenPopulated()`.

### 7. Finalizers must never wait on another thread

A blocked finalizer thread stops **all** finalization process-wide, so every native and COM resource that
relies on a finalizer leaks permanently. `AzManService.Dispose(bool)` therefore does its STA-marshalled COM
release only on the `disposing` path. Related: a `TaskScheduler` must never silently drop a queued task —
the caller's `await` would never complete (see `StaTaskScheduler.QueueTask`).

### 8. Cap process-wide caches — but not the navigation journals

A cache keyed by something the user can keep producing needs an explicit bound.
`SmbClientNameResolver.MaximumCacheEntries` is the example: entries are cheap to recompute, and on a busy
file server the key space keeps changing, so expired entries used to accumulate for the process lifetime.

The navigation journals do **not** need one. `Frame.BackStack` and the breadcrumb history stacks were
capped at ten entries in the original fix; both caps have since been removed:

- Once rule 2 is followed, a `PageStackEntry` holds a type name, a small identifier and a shared
  `NavigationTransitionInfo` — a few hundred bytes. A hundred navigations is tens of KB, and the cap paid
  for that by losing every back-navigation past the tenth.
- The breadcrumb caps were worse than pointless. `_backStackSourceType` is a `Stack<bool>`, one byte per
  entry, and the trim rebuilt the whole stack (`new Stack<T>(stack.Take(10).Reverse())`) on *every*
  navigation once over the cap — allocating an enumerator, a buffer and a new stack per navigation to save
  a byte.
- The two depth constants lived in different files and had to stay equal, or `_backStackSourceType` would
  drift out of sync with `Frame.BackStack` and back-navigation would take the wrong restore branch.

`MainWindow` now shares one `SlideNavigationTransitionInfo` across navigations. That removes the only
per-entry allocation worth removing — `PageStackEntry` holds the transition info in a `TrackerPtr`, exactly
as it holds the parameter — and costs nothing.

### Note: not every change in the original fix reduced memory

`DeviceManagerViewModel._allCategories` was added in the same commit and deliberately keeps a second, full
copy of the device list. It is a correctness fix — filtering used to narrow `DeviceCategories` in place, so
deleting characters from the search box could not bring previously excluded devices back — and the extra
copy is the price. Do not "optimise" it away.

## Why Task Manager still shows 100 MB+

Task Manager's **Memory** column on the Processes tab is the process's *active private working set*. A UWP
app such as Settings appears to shrink on its own for two reasons, neither of which is the app releasing
memory:

1. Process Lifetime Management **suspends** it a few seconds after it leaves the foreground, and the memory
   manager trims a suspended process's working set — the pages move to the **standby list**, which counts
   as available memory rather than in-use memory.
2. Task Manager then *excludes* the suspended portion from the Memory column, so the number falls further
   than the trim alone accounts for.

A WinUI 3 app is an ordinary Win32 desktop process. **PLM does not apply to it**: minimizing does not
suspend it, so nothing ever trims it, and its working set stays at the session high-water mark until the
whole machine comes under memory pressure. That is a platform difference, not a leak — and it is why the
navigation probe tells you to read `private` rather than `workingSet`.

The other half of the answer is that most of the residual number is not the app's to give back. At the end
of a 25-navigation session the settled managed heap is ~1.6 MB against ~114 MB private: the rest is native
— the XAML framework, the composition/DirectX device, COM and WMI proxies, fonts, and the AOT image
itself. An empty WinUI 3 window starts around 60–80 MB. Managed-side work cannot move that number much.

### The app already does what the official guidance asks

[Manage memory usage in Windows App SDK desktop apps](https://learn.microsoft.com/windows/apps/develop/launch/reduce-memory-usage)
is the on-point guidance for this exact situation. It says to hook `Window.Activated` / `AppWindow.Changed`
and, when the window is hidden, release **cached images and bitmaps, render-target resources, view-model
data, and the navigation cache** — things you can reload. OneMMC already releases all of that, and does it
*earlier*, on every navigation rather than on hide:

- `contentFrame` is declared `CacheSize="0"` (`MainWindow.xaml`), and no page opts into `NavigationCacheMode`.
- `PageServiceScope` disposes the page's view model and its graph in `Unloaded` (rule 1).
- The journal, breadcrumb stacks and process-wide caches are all capped (rule 8).

That is why the settled heap sits at ~1.5 MB. There is no meaningful app-level allocation left to release
when the window is hidden, so the hide-time hook the guidance describes would have nothing to do.

### Rejected: trimming the working set when idle (2026-07-26)

Calling `EmptyWorkingSet` (or the equivalent `SetProcessWorkingSetSizeEx(h, -1, -1)`) on a delay after the
window is minimized or deactivated **works**, and was measured working — but it was implemented, measured,
and then reverted. Do not re-introduce it without new evidence.

Measured, x64 Debug, launch → minimize → 5 s trim → restore:

| | Running | Minimized + trimmed | Restored, 5 s later |
|---|---|---|---|
| Working set | 163.4 MB | **17.4 MB** | 43.5 MB |

Responsive again 78 ms after the restore request. Private bytes barely moved (60.0 → 59.1 MB in one run;
a second run saw 63.5 → 44.2 MB when the aggressive GC happened to return retained heap).

Why it was reverted anyway:

- **It shrinks the number, not the memory.** The pages move to the standby list; the RAM is still resident
  and Windows would have reclaimed it on its own the moment anything actually needed it. The user gains no
  available memory — only a smaller figure in Task Manager.
- **`EmptyWorkingSet` is not a memory-management API.** MS Learn's
  [Working Set Information](https://learn.microsoft.com/windows/win32/psapi/working-set-information) says
  it "is useful primarily for testing and tuning", and the Windows App SDK memory guidance never mentions it.
- **The forced GC contradicts the documented sample.** That sample uses
  `GC.Collect(2, GCCollectionMode.Optimized, blocking: false)` and comments "Only call this in response to
  system memory pressure, **not on every hide**". The reverted code used
  `GCCollectionMode.Aggressive, blocking: true, compacting: true` on every hide.
- **The delays were two orders of magnitude too short.**
  [Improve app performance](https://learn.microsoft.com/windows/apps/develop/performance/disk-memory) puts
  the inactivity threshold for releasing resources at "a handful of minutes to a ½ hour or more"; the
  reverted code used 5 s and 30 s.

The one candidate that *would* fit the guidance is the singleton ADMX bundle behind `AdmxBundleProvider`
(GpEdit): it is reloadable, potentially tens of MB, and already has a clear path. Measure what it actually
holds before deciding — and use a minute-scale delay, not a second-scale one.

## Measuring

`IMemoryDiagnostics` / `MemoryDiagnosticsService` writes a reading to the log on every navigation:

```
[Memory] nav:EventViewerPage | private=214.7MB heap=6.2MB committed=14.0MB workingSet=310.4MB
         allocated=57.3MB gc=22/15/6 finalizers=11/12 backStack=4 breadcrumbs=2 mode=New
```

**Read `private` first.** It is the memory this process owns. `workingSet` includes pages shared with
other processes (every loaded DLL) and Windows only trims it under pressure, so it behaves like a
high-water mark, not current usage — it will look like a leak even when nothing is leaking. `heap` and
`committed` cover only the managed heap, which in this app is a small fraction of the total; most of
OneMMC's memory is native (XAML, COM, WMI).

`finalizers=run/armed` is the finalizer-health probe: a run count that stops advancing while gen2
collections continue means finalization has stalled, and the service logs an error when it detects that.

Logs are at `%LOCALAPPDATA%/OneMMC/Logs/`. The probe logs at `Information`, so it is visible at the default
level.

### Measure correctly, or the numbers mean nothing

1. **Release build, no debugger attached.** A Debug build under the Visual Studio debugger inflates
   memory substantially and suppresses collection — its numbers cannot be compared against anything.
2. **Turn on probe mode.** Set `"MemoryProbeMode": true` in `%LOCALAPPDATA%/OneMMC/Settings.json`. Each
   navigation then forces a full collection and waits for finalizers before reading, so successive
   readings are comparable. Without it, readings are dominated by garbage that simply has not been
   collected yet. It pauses the app per navigation, so turn it back off afterwards.
3. **Repeat the same navigation.** A first visit to any page permanently caches its XAML types and
   templates in the framework — that is a one-time cost, not a leak. Only the *second and subsequent*
   visits to the same page tell you whether something is retained. Compare visit 2 against visit 5.

### Measured baseline (2026-07-26, x64 Debug, probe mode on)

25 navigations across PCManagement, Event Viewer, Task Scheduler, System Management, Windows Firewall
(incl. rule editor and rule info), Component Services, Disk Management and Settings:

| Metric | First reading | Peak | Last reading |
|---|---|---|---|
| Managed heap (settled) | 0.5 MB | 2.5 MB | **1.6 MB** |
| Private bytes | 53.2 MB | 126.0 MB | **114.1 MB** |
| Working set | 129.0 MB | 258.6 MB | 248.7 MB |

The settled managed heap sat at **~1.5 MB for the whole session** — it returns to the same value after
every navigation, so nothing managed is retained per navigation. Private bytes oscillate rather than
climb (they *fell* 109.4 → 92.5 MB on leaving Disk Management, and 126.0 → 114.1 MB by the end), which is
native-allocator behaviour, not a leak. The residual rise tracks *first* visits to new page types — the
XAML framework caches types and templates permanently by design.

For contrast, the same navigation count without probe mode ended at 343 MB working set: that difference
is uncollected garbage and an untrimmed working set, not retention.

The back-stack cap was exercised and held at 10 during the firewall drill-down.

Not yet covered by a measured run: **Group Policy Editor** and **Authorization Manager** — the two pages
with the largest fixes (shared ADMX bundle, singleton `AzManService`). Run probes A and B against them.

### Probes

Run each several times and compare the reading after each pass. Flat = good; monotonically rising =
regression.

| Probe | Steps | What it catches |
|---|---|---|
| A | AzMan: Manager → Store → Back, ×5 | Leaked `AzManService` + STA thread; stalled finalizers |
| B | Group Policy Editor: enter and leave ×5 | ADMX bundle retention, view-model capture |
| C | Six main nav pages in rotation ×10 | Overall per-navigation growth; journal caps |
| D | Certificates (Local Computer), open once | Peak realized-element count |

For A, also watch the thread count in Task Manager — it must not climb.

### Verbose logging

Set `"VerboseLogging": true` in `%LOCALAPPDATA%/OneMMC/Settings.json` to restore `Debug` level. It is off
by default: every `LogDebug` formats a template and allocates property values, which is sustained gen0
pressure for output nobody reads in production. The `Trace` → Serilog bridge is likewise installed only
when a debugger is attached.

## Process-level GC knobs (verified mechanism, benefit not yet measured)

[dotnet/runtime#85961](https://github.com/dotnet/runtime/issues/85961) reported `System.GC.*` settings
supplied via `RuntimeHostConfigurationOption` being ignored under Native AOT. **That is fixed on the SDK
this project uses**, verified by AOT-publishing a probe that prints `GC.GetConfigurationVariables()`:

```
ServerGC     : False
LatencyMode  : Batch
  ServerGC = False
  ConcurrentGC = False
  RetainVM = False
  GCConserveMem = 5
```

So the knobs below would take effect. They are **not** applied, because their benefit to OneMMC has not
been measured yet — measure first, then decide.

Measure with an environment variable (no rebuild, and environment variables win over MSBuild settings
from .NET 9 onward):

```powershell
$env:DOTNET_GCConserveMemory = '5'   # 1-9; higher = smaller heap, more frequent GCs
.\OneMMC.exe
```

If probes C and D show a worthwhile reduction, make it permanent:

```xml
<PropertyGroup>
  <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
</PropertyGroup>
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="5" />
</ItemGroup>
```

Both trade CPU and pause time for a smaller heap. Server GC and DATAS are not relevant here: this is a
desktop app on workstation GC, and DATAS only applies to Server GC.

A Segment Heap opt-in in `app.manifest` is another untested option for native allocations.

## Known upstream issue

[microsoft-ui-xaml#10981](https://github.com/microsoft/microsoft-ui-xaml/issues/10981) — WinUI 3 binding
retention regression (`ComWrappers.ManagedObjectWrapperHolder`) after moving from .NET 8 to .NET 10.
Backlog, no workaround. If the probes still show residual linear growth after the rules above are
followed, check this before assuming the cause is in this repository.

Note that `{x:Bind}` itself is **not** a retention risk: the generated `BindingsTracking` class holds only
a `WeakReference` to the page's bindings object and self-heals via `ReleaseAllListeners()` when the target
is collected. `Bindings.StopTracking()` is therefore unnecessary. Hand-written
`viewModel.PropertyChanged += ...` in code-behind *is* a strong reference and must be unhooked.
