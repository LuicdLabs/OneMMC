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

### 3. `Dispose()` must release the graph, not just the handles

Disposing an owned service is not enough. Clear the collections too, so memory returns at unload instead
of at some later GC:

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

`CertificateStoresViewModelBase.ClearCachedData()` is the reference shape for non-disposable view models.

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

### 8. Bound anything that grows per navigation

`Frame.BackStack` (`MainWindow.MaximumBackStackDepth`), the breadcrumb history stacks
(`BreadcrumbNavigationService.MaximumHistoryDepth`), and any process-wide cache
(`SmbClientNameResolver.MaximumCacheEntries`) all need an explicit cap. None of them have one by default.

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
