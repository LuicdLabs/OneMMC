# Memory Management

How OneMMC keeps its memory footprint flat across a long session, the rules new code must follow, and how
to measure a regression.

The original investigation found memory growing with the *number of pages visited*, not just with the data
currently on screen. Several confirmed retention paths have been fixed, but this is not a claim that every
future build is leak-free or that the process must return to its launch footprint. The rules and repeatable
measurements below remain load-bearing.

## Rules

### 1. Resolve transient graphs containing disposables from a page scope

`App.GetRequiredService<T>()` resolves from the **root** `ServiceProvider`. `Microsoft.Extensions.DependencyInjection`
adds every `IDisposable` it creates anywhere in the requested graph to the resolving scope's disposal list.
OneMMC disposes its root provider when the main window closes, so a page that root-resolves such a transient
graph pins the disposable — and everything it references — for the rest of the interactive process lifetime.
This is a documented anti-pattern
([Dependency injection guidelines → "Disposable transient services captured by container"](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines#example-anti-patterns)).

The requested outer type does **not** have to implement `IDisposable`. `TaskHistoryService` is not disposable,
but its constructor receives a transient `EventViewerService`, which is. Root-resolving a fresh history service
therefore captured one event-viewer service per call.

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

An outer service that is not disposable may use `App.GetRequiredService<T>()` only when its complete transient
dependency graph contains no container-created disposable. Explicit singletons are already owned once by the
root provider and are not a reason to put every consumer in a page scope.

`EventViewerPage.xaml.cs` is the reference implementation for full page cleanup.

### 2. Navigation parameters carry identifiers, not objects

`Frame.BackStack` retains every `PageStackEntry`, and each entry holds the parameter it was navigated
with. The breadcrumb trail holds them too. Passing a live service or view model therefore pins it for the
rest of the session.

Pass a store path, an application name, a rule lookup name, an instance id. Let the target page resolve
services from DI and re-query current state. See `StoreNavigationParameter`,
`FirewallRuleNavigationParameter`, `ApplicationNavigationParameter`, and `ScopeNavigationParameter`.

A small DTO containing identifiers and primitive routing data is acceptable. If the target can be renamed,
update the identifier held by the existing journal parameter after a successful rename. Do not call a DTO
"lightweight" while it still holds a feature model with nested collections.

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

**The collection clearing is not what frees the memory.** Once the page graph is actually unreachable, its
view model and every collection hanging off it are unreachable too, and the GC frees the whole graph in one
pass whether or not the collections were emptied first. `Unloaded` is a lifecycle notification, not proof
of reachability: an event publisher, async operation, navigation parameter, cache, or other owner may still
hold the page. `Microsoft.Extensions.DependencyInjection` tracks the disposables it creates, not every
non-disposable outer object. A non-disposable transient is safe from container capture only when rule 1's
complete-graph check also passes.

The existing `ClearCachedData()` calls in `Unloaded` handlers are retained as insurance if some unrelated
owner keeps a page reachable. `CertificateStoresViewModelBase.ClearCachedData()` is the reference shape. Do
not add new ones expecting a measurable saving, and do not repeat the old claim that they make "memory return
at unload instead of at some later GC" — that was wrong.

### 4. Give each list the scrolling host its virtualization model expects

The correct host depends on the control. `ListView` and `GridView` provide their own scrolling and viewport
management; wrapping either in an outer `ScrollViewer` (or placing it in a `StackPanel` or unconstrained
`Auto` row) can give it unbounded height and defeat its virtualization. `ItemsRepeater`, by contrast, has
no built-in scrolling. Putting an `ItemsRepeater` with a virtualizing layout inside a `ScrollViewer` is the
[documented pattern](https://learn.microsoft.com/windows/apps/develop/ui/controls/items-repeater); the
scroll host supplies the viewport information used for realization.

- Never use `ItemsControl` for a large or unbounded collection. It does not virtualize; a fixed handful of
  static items is fine.
- Never override `ItemsPanel` with a plain `StackPanel`. That disables `ItemsStackPanel`.
- Use `ItemsRepeater` + a virtualizing layout inside a `ScrollViewer` for custom or variable-height
  collections. See `ConnectionSecurityRulesPage.xaml`, or the two certificate pages.
- A plain `ListView` in a `*`-sized `Grid` row is correct for uniform rows. See `ServicesPage.xaml`.
- Nested repeaters, variable-size items and content that resizes during scrolling remain higher-risk layout
  paths. Measure those pages rather than assuming that the presence of a `ScrollViewer` disables
  virtualization.

Tuning knobs when a list is unavoidably large: `ItemsStackPanel.CacheLength` (default **4.0** = two
viewports either side) and `ItemsRepeater.VerticalCacheLength` (default **2.0**).

**Known limitation — ItemsRepeater and variable-height items.** ItemsRepeater has a long-standing open bug
([microsoft-ui-xaml#1829](https://github.com/microsoft/microsoft-ui-xaml/issues/1829)): variable-height
items in a scroll host can mis-render — blank or overlapping bands — during fast scrolling. The harder
"Layout cycle detected" reports include nested ItemsRepeater in a scroll container
([#9345](https://github.com/microsoft/microsoft-ui-xaml/issues/9345),
[#3989](https://github.com/microsoft/microsoft-ui-xaml/issues/3989), and
[#6218](https://github.com/microsoft/microsoft-ui-xaml/issues/6218)). #3989 and #6218 were tracked for the
Windows App SDK 1.5 milestone. #9345 is also closed, but its issue metadata does not establish that it was
fixed in 1.5. This project is on 2.3.1 (`Directory.Packages.props`), but the still-open #1829 remains
relevant.
The two certificate pages are the most exposed case: `ScrollViewer` → outer `ItemsRepeater` →
`SettingsExpander` (whose `Items` live in its own internal `ItemsRepeater`) → inner `ItemsRepeater` is
nested *and* variable-height. They reduce initial layout work by starting expanders collapsed (rule 5), and
the outer repeater sets `VerticalCacheLength="1.0"`. If a store with thousands of certificates shows blank
bands on fast scroll, this is a likely upstream layout path to investigate — reduce layout complexity or
the realized-row count rather than abandoning virtualization, which would guarantee linear element growth.

### 5. Start data-bound `SettingsExpander` controls collapsed for responsiveness

Default expanders over growing collections to collapsed, matching the native MMC snap-ins, to reduce
initial measure, layout and visual-realization work. Treat this as a responsiveness rule, not as a promise
of lower process memory.

The CommunityToolkit template creates the `Expander`, `SettingsCard`, presenters, grid, internal
`ItemsRepeater`, layout and other chrome as part of the control template. Collapsing changes the content's
`Visibility`; it does not use [`x:Load`](https://learn.microsoft.com/windows/apps/develop/performance/optimize-xaml-loading),
so those object instances remain allocated. The backing view-model
collection and its models, strings and metadata also remain loaded. A collapsed subtree is skipped during
normal measure and can avoid or reduce row realization, but repeater recycling and framework caches make
the resulting private-byte difference page-dependent. Do not claim that `IsExpanded="False"` alone
materially reduces RAM without a Release measurement for that page.

The certificate pages now do more than collapse the Toolkit control: `CertificateStoreNode.Rows` is empty
while collapsed, is flattened from `Sections` on expansion, and is reset to an empty array on collapse. That
releases the flattened row objects and removes them from the inner repeater's item source when nothing else
holds them. It does **not** release the store's `Sections`, certificate models, or the expander template
chrome, so it limits realized/list overhead rather than unloading the whole store.

Expanders over a fixed handful of static rows (property sheets) may stay expanded — the content template is
inflated regardless and there is nothing to virtualize.

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
relies on a finalizer then backs up. A finalizer must never call `Join`, `.Wait()` or `GetResult()` on an
apartment thread.

`AzManService` atomically stops accepting work and requests terminal cleanup from its owning STA. Explicit
`Dispose()` drains accepted tasks and joins; the finalizer uses the same terminal-cleanup request but returns
without waiting. The Task Scheduler COM service is a root-owned singleton and has no finalizer: root-provider
disposal at process close drains accepted work and releases the cached service as the last STA action.
`GroupPolicyObjectWrapper`
does not explicitly `FinalRelease` its apartment-threaded pointer from the finalizer; deterministic release
is restricted to explicit disposal from the creating COM apartment. A scheduler must also reject work after
shutdown rather than silently dropping it — a dropped task leaves the caller's `await` pending forever.

### 8. Cap process-wide caches — but not the navigation journals

A cache keyed by something the user can keep producing needs an explicit bound.
`SmbClientNameResolver.MaximumCacheEntries` is the example: entries are cheap to recompute, and on a busy
file server the key space keeps changing, so expired entries used to accumulate for the process lifetime.

The navigation journals do **not** currently have evidence justifying a cap. `Frame.BackStack` and the
breadcrumb history stacks were capped at ten entries in the original fix; both caps have since been removed:

- Once rule 2 is followed, journal entries retain routing metadata and identifier DTOs rather than pages,
  services, view models, or feature models. Their actual cost has not been byte-sized here, so do not invent
  an exact per-entry number.
- The cap removed valid back-navigation based only on entry count, not measured retained bytes.
- Breadcrumb trimming rebuilt its tracking stacks and introduced another depth that had to remain exactly
  synchronized with `Frame.BackStack`; a mismatch could restore the wrong navigation branch.

`MainWindow` now shares one `SlideNavigationTransitionInfo` across its navigations. This statement applies
only to `MainWindow`; child frames still create transition objects at navigation sites. Those short-lived
objects are negligible beside XAML trees and native framework allocations, so app-wide transition pooling
is not a useful memory optimization without contrary measurements.

### Note: not every change in the original fix reduced memory

`DeviceManagerViewModel._allCategories` was added in the same commit and deliberately keeps a second, full
copy of the device list. It is a correctness fix — filtering used to narrow `DeviceCategories` in place, so
deleting characters from the search box could not bring previously excluded devices back — and the extra
copy is the price. Do not "optimise" it away.

## Why Task Manager can show 100 MB+

A three-digit Task Manager value is not by itself proof of a leak. A
[working set](https://learn.microsoft.com/windows/win32/memory/working-set) is the set of process pages
currently resident in physical memory and includes shared as well as private pages. It is a momentary value
affected by RAM size, system activity and OS trimming policy.
[`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`](https://learn.microsoft.com/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex),
logged here as `private`, is instead the process's private **commit charge**. It is more stable for comparing
the same route, but it still combines managed heaps, native heaps, XAML/composition allocations, COM/WMI
state and other process-private runtime data.

Windows App SDK desktop apps are not automatically suspended like UWP apps. Minimizing OneMMC therefore
does not imply a prompt PLM suspension or deterministic trim. That does **not** mean its working set can
never shrink: Windows can age, trim and page out process pages as system conditions change. Compare repeated
steady-state routes and allocation traces, not the visual behavior of an app with a different lifecycle.

### What the WinUI "first-visit tax" does — and does not — prove

The earlier shorthand that the whole **53 → 114 MB** change was a permanent XAML type/template cache was too
strong. The evidence supports a narrower conclusion:

| Layer | Verified behavior | What it means here |
|---|---|---|
| `Frame` page cache | Microsoft documents that every `Navigate` and `GoBack` creates a new `Page` by default. Reuse requires `Page.NavigationCacheMode`; OneMMC sets `CacheSize="0"` and enables it nowhere. | The back stack is not an explanation for keeping an old OneMMC `Page` visual tree alive. Its parameter still matters. |
| WinUI default styles | WinUI source has a core-owned `StyleCache::m_stylesMap`; a control-library resource dictionary is inserted on first lookup and the built-in style cache is cleared during `DXamlCore::ClearCaches()` at core/application shutdown. | Introducing a new control library or style namespace can create a real core-lifetime first-use cost. |
| Compiled XAML/XBF | WinUI's `XamlNodeStreamCacheManager` deliberately keeps newly seen XBF resources and their mapped buffers in "long term storage" for the manager lifetime, even across its ordinary `Flush()`. | First parsing of a XAML resource can leave bounded framework/parser state after the `Page` instance dies. |
| Page/control instances | No source or public contract says every realized control tree is retained for the process. Many rendering, text, theme-walk and element caches have explicit clear/release paths. | A departed `Page` that survives repeated full collections is an app or framework retention lead, not an acceptable consequence of the style/XBF caches above. |
| Private bytes | `PrivateUsage` combines GC, native heaps, XAML, composition, loaded code, COM and feature data. | A one-time jump or plateau is compatible with first-use caches, but cannot identify them or quantify their share. |

Sources: the public [`Frame` remarks](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.frame)
and [navigation tutorial](https://learn.microsoft.com/windows/apps/develop/ui/navigation/navigate-between-two-pages)
define the page-cache behavior. The implementation evidence is from the WinUI source at commit
[`6112d936`](https://github.com/microsoft/microsoft-ui-xaml/tree/6112d936461edb6d81ce7db983c74cc60ea2bc28):
[`StyleCache`](https://github.com/microsoft/microsoft-ui-xaml/blob/6112d936461edb6d81ce7db983c74cc60ea2bc28/dxaml/xcp/dxaml/lib/DefaultStyles.h#L95-L108),
its [lookup/insertion](https://github.com/microsoft/microsoft-ui-xaml/blob/6112d936461edb6d81ce7db983c74cc60ea2bc28/dxaml/xcp/dxaml/lib/DefaultStyles.cpp#L511-L626),
[`DXamlCore::ClearCaches`](https://github.com/microsoft/microsoft-ui-xaml/blob/6112d936461edb6d81ce7db983c74cc60ea2bc28/dxaml/xcp/dxaml/lib/DXamlCore.cpp#L1174-L1207),
and the XBF [long-term resource storage](https://github.com/microsoft/microsoft-ui-xaml/blob/6112d936461edb6d81ce7db983c74cc60ea2bc28/dxaml/xcp/core/Parser/NodeStreamCache.cpp#L199-L206).
Those internal details corroborate a mechanism; they are not a supported API or a promise that every WinUI
version retains the same objects. OneMMC must not call test hooks or internal cache-clearing entry points.

So the defensible verdict is: **a bounded first-use tax is real and plausible, but the measured 61 MB delta
has not been attributed to it.** Nor is it necessary to "give up WinUI rendering caches" to release pages:
page-instance lifetime and process/core caches are separate questions. The weak-reference probe below tests
the former; native/managed snapshot diffs test the latter.

### Local evidence before the settled Page probe (2026-08-13)

The installed setting was `"MemoryProbeMode": false`. In one varied first-visit session the navigation log
went from approximately **50.8 MB to 130.1 MB private**, while `GC.GetTotalMemory(false)` at the last reading
was about **7.2 MB**. Because the run did not settle the heap and visited different pages, it cannot prove or
disprove a leak. It does show why 100 MB+ can coexist with a comparatively small managed heap and why the
absolute Task Manager number is insufficient evidence.

A later non-probe route was broader and reached **33.8 → 199.6 MB private**, **3.3 → 11.9 MB managed
heap**, **673 → 1200 process handles**, and **21 → 27 threads**. It mixed first visits, different features,
background work and unsynchronized collection points, so its endpoint delta is not a leak rate. The Windows
Firewall drill-down is nevertheless a useful lead: the first editor/info/back cycle moved from roughly
127 MB / 952 handles to 171.5 MB / 1110 handles, while a second cycle reached 190 MB / 1127 handles. The
much smaller second handle increase is compatible with first-use/high-water behavior, but only repeated
settled identical cycles can establish a plateau. This is why the current probe records handles and threads
alongside Page reachability instead of explaining the whole increase as XAML caching.

An earlier non-probe run exposed a specific managed retention candidate: after entering and leaving GpEdit,
the reported managed heap rose to 33.3 MB and later remained around **20–22 MB**. That was consistent with
the singleton ADMX provider intentionally holding the parsed policy bundle strongly. It motivated the
pressure-aware cache below; it is not a settled before/after benchmark.

### Actions aligned with the official desktop guidance

[Manage memory usage in Windows App SDK desktop apps](https://learn.microsoft.com/windows/apps/develop/launch/reduce-memory-usage)
is the on-point guidance for this situation. It recommends releasing data that can be reconstructed and
responding to `MemoryManager.AppMemoryUsageIncreased` when usage reaches `High` or `OverLimit`. OneMMC now:

- `contentFrame` is declared `CacheSize="0"` (`MainWindow.xaml`), and no page opts into `NavigationCacheMode`.
- Probe mode tracks each successfully departed `Page` through a bounded weak-reference list and reports
  whether GoBack, breadcrumb or NavigationView departures remain alive after settled collections. It delays
  committing the record until the complete Frame navigation callback stack unwinds, and drops stopped or
  failed attempts. The probe cannot itself keep a page alive.
- Pages that own transient disposable graphs use `PageServiceScope` to dispose those graphs in `Unloaded`
  (rule 1).
- Navigation entries carry lightweight identifiers; the journal and breadcrumb history intentionally remain
  uncapped, while process-wide caches with an unbounded key space are capped where recomputation is cheap
  (rule 8).
- `AdmxBundleProvider` keeps one strong and one weak cache reference. It drops the strong reference after
  ten idle minutes, or immediately at `High`/`OverLimit` memory pressure. Only the pressure path issues the
  guidance's non-blocking optimized gen2 collection hint; a live GpEdit/RSoP consumer can still keep the
  bundle alive until that consumer leaves.

There is no blanket hide-time collection or navigation-history purge. Add one only for a measured,
reloadable resource; hiding a window is not evidence that every page-owned object is retained.

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

- **It changes residency, not private commit.** Active pages can move to the standby/available lists and are
  then reclaimable, so the working-set number falls; the process's private commit does not fall merely
  because those pages were trimmed. Re-access can fault the pages back in, and Windows already manages
  working sets under pressure.
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

The singleton ADMX bundle was the measured reloadable candidate. It now uses the ten-minute idle and
memory-pressure behavior described above; a settled Release before/after run is still required to quantify
the private-byte benefit.

## Measuring

`IMemoryDiagnostics` / `MemoryDiagnosticsService` writes a reading to the log on every navigation:

```
[Memory] post-nav:EventViewerPage | private=114.2MB heap=6.2MB gcHeap=7.1MB
         committed=14.0MB fragmented=0.4MB workingSet=210.4MB
         allocated=57.3MB delta=0.8MB gcIndex=6 gc=22/15/6 finalizers=11/12
         handles=742 threads=31 backStack=4 breadcrumbs=2 mode=New
         sequence=7 initiator=NavigationView settled
[Memory][PageLifetime] settledThrough=7 tracked=0 collected=1 alive=0 dropped=0 survivors=none
```

**Read `private` as the process commit curve, not as a count of live objects.** `workingSet` is current
residency and includes shared pages. `heap` is `GC.GetTotalMemory(false)`; `gcHeap`, `fragmented` and
`committed` are managed-GC values from the most recently completed collection. None of those managed
metrics explains native allocations on its own. `delta` is managed allocation churn since the previous
reading, while handle and thread counts catch native lifetime regressions that byte totals can hide.

`finalizers=run/armed` is the finalizer-health probe: a run count that stops advancing while gen2
collections continue means finalization has stalled, and the service logs an error when it detects that.

`PageLifetime` is the direct navigation-release check. On `Frame.Navigating`, probe mode stores only a
`WeakReference<Page>` plus scalar metadata. `Navigated` copies page name, mode, depths, sequence and
initiator, but does not carry `NavigationEventArgs` into an async state machine: its `Content` property would
otherwise keep the destination Page alive. WinUI raises `Navigated` before the old/new Page navigation
callbacks have all returned, so commit and collection are queued for the next `DispatcherQueue` turn. A
`NavigationStopped` or `NavigationFailed` in the meantime invalidates the attempt, including a failure that
restores the old content after `Navigated`. See the corresponding
[`Frame::ChangeContent` ordering](https://github.com/microsoft/microsoft-ui-xaml/blob/6112d936461edb6d81ce7db983c74cc60ea2bc28/dxaml/xcp/dxaml/lib/Frame_Partial.cpp#L645-L680).
Collected entries are removed, the metadata list is capped at 128, and `dropped` reports any diagnostic
records evicted at that bound.

- `collected=1 alive=0` means the departed instance became unreachable by that settled pass.
- One `alive` observation can be a transition animation, an in-flight async operation or another temporary
  owner. It is recorded but is not called a leak.
- Survival across two settled passes emits one warning with page type, destination, `GoBack` / `Breadcrumb`
  / `NavigationView` initiator, GC-survival count and navigation age. It is a concrete retention lead; use a
  managed Paths-to-Root snapshot before deciding which owner is wrong.
- If the number of old live pages grows with repetitions, navigation release is failing even if private
  bytes happen to plateau. If old pages collect but private bytes still rise linearly, investigate native
  allocations, process caches, handles and threads instead.

Logs are at `%LOCALAPPDATA%/OneMMC/Logs/`. The probe logs at `Information`, so it is visible at the default
level.

### Measure correctly, or the numbers mean nothing

1. **Release build, no debugger attached.** Debugger services and Debug-only lifetime/timing differences
   inflate and perturb the measurement, so do not compare that absolute total with a Release run.
2. **Turn on probe mode.** Set `"MemoryProbeMode": true` in `%LOCALAPPDATA%/OneMMC/Settings.json`. Each
   navigation requests a full collection/finalizer/full-collection pass on one dedicated `LongRunning`
   worker, so the UI thread does not wait inside `GC.WaitForPendingFinalizers()`. The await times out after
   five seconds, logs `settle-timeout`, and refuses to start a second settle worker until the first exits;
   completion later logs recovery. A timeout is a diagnostic failure, not a comparable settled sample.
   Turn probe mode back off afterwards.
3. **Let the feature finish loading before leaving it.** [`Frame.Navigated`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.frame.navigated)
   only means the destination content is available; Microsoft explicitly says it may not have completed
   loading. The shell therefore cannot generically label a snapshot "data loaded". Wait for the page's
   spinner/progress state to finish, then invoke GoBack, a breadcrumb, or a NavigationView item. The settled
   sample after that departure tests whether the fully exercised outgoing page was released.
4. **Repeat the identical route with identical data.** The first visit can load default style dictionaries,
   XBF resources, composition devices, interop state and intentional feature caches. Compare visit 2 with
   visits 5 and 10; comparing unrelated page types cannot distinguish a leak from first-use work.
5. **Exercise every exit path.** For the same page, repeat GoBack, breadcrumb and NavigationView departures.
   The `initiator=` and `PageLifetime` fields keep those results separate.

Interpret the shape before optimizing the absolute number:

- If private bytes rise to a high-water mark and then oscillate without increasing with navigation count,
  and departed pages collect, treat that as a steady-state candidate. It is compatible with core-lifetime
  style/XBF caches and allocator high-water behavior, but does not prove either one. Do not optimize Task
  Manager's working-set display with forced GC or working-set trimming.
- If departed pages survive repeatedly, use Visual Studio Memory Usage in managed or mixed mode and inspect
  `Page`, ViewModel and generated binding instances with Paths to Root / Event Handler Leak insights.
- If private bytes continue to rise approximately with every repeated visit, capture native heap snapshots
  in a Release run: take snapshot A after visit 2, repeat the same away/back sequence 10–20 times, then take
  snapshot B. The [Visual Studio Memory Usage profiler](https://learn.microsoft.com/visualstudio/profiling/memory-usage-without-debugging2)
  supports managed, native and mixed snapshots and diff reports. For commit not represented by that heap,
  record `wpr -start VirtualAllocation -filemode` as documented in Microsoft's
  [memory trace workflow](https://learn.microsoft.com/windows/apps/develop/performance/disk-memory), then group
  WPA Total Commit by process, commit type and commit stack. WPR
  [heap snapshots](https://learn.microsoft.com/windows-hardware/test/wpt/record-heap-snapshot) can compare
  outstanding native heap allocation stacks. The WinUI
  [XAML activity profile](https://learn.microsoft.com/windows/apps/develop/performance/winui-perf) aligns
  `Frame::Navigating` / `Frame::Navigated`, layout and graphics-device events with that interval. Repeated
  growth under `Microsoft.UI.Xaml`, composition, WinRT or Toolkit frames is evidence to investigate; a large
  but flat total is not.

For a page dominated by many `SettingsExpander` controls, the remaining useful UI experiment is a scoped
A/B test, not a project-wide rewrite. Compare the Toolkit template with a minimal expander-like template
(`Grid` + toggle + `ItemsRepeater`) using the same data, viewport and Release profiling procedure. Keep the
custom version only if a preselected private-byte and responsiveness threshold justifies its maintenance
cost; element-count reduction is plausible, but a meaningful process-memory reduction is not assumed.

### Historical measured baseline (2026-07-26, x64 Debug, probe mode on)

This is a historical x64 Debug baseline from an older implementation. It is useful only for relative shape
inside that run; Debug inflates absolute memory, the probe/log format has since changed, and the route did
not cover the current COM/cache fixes. Do not compare its values with a current Release run. A new x64
Release baseline, with no debugger attached, should supersede it.

25 navigations across PCManagement, Event Viewer, Task Scheduler, System Management, Windows Firewall
(incl. rule editor and rule info), Component Services, Disk Management and Settings:

| Metric | First reading | Peak | Last reading |
|---|---|---|---|
| Managed heap (settled) | 0.5 MB | 2.5 MB | **1.6 MB** |
| Private bytes | 53.2 MB | 126.0 MB | **114.1 MB** |
| Working set | 129.0 MB | 258.6 MB | 248.7 MB |

The settled managed heap returned to roughly **1.5 MB** across the pages covered by that route. Private
bytes oscillated rather than rising on every navigation (for example 109.4 → 92.5 MB on leaving Disk
Management and 126.0 → 114.1 MB by the end). That run therefore did not show monotonic per-navigation
growth for its covered route; it does not prove that every feature or native allocation was leak-free. In
particular, the **53.2 → 114.1 MB** endpoints cannot be assigned to XAML/style caches without an allocation
or commit-stack diff, and that historical build predates the Page-lifetime probe.

For contrast, a run with the same navigation count but without probe mode ended at 343 MB working set. The
runs had different collection and residency state, so that difference cannot be classified as retention —
or as proof of no retention — from working-set totals alone.

The firewall drill-down exercised a ten-entry back stack. The cap present during this historical run has
since been removed; navigation and breadcrumb histories are now intentionally uncapped (rule 8).

GpEdit and Authorization Manager were not covered by that settled historical route. The later local
non-probe evidence above found the ADMX strong-cache cost, but probes A and B still need a current Release
run after these changes.

### Probes

Run each several times and compare the reading after each pass. Flat = good; monotonically rising =
regression.

| Probe | Steps | What it catches |
|---|---|---|
| A | AzMan: Manager → Store → Back, ×5 | Thread/handle growth; duplicate singleton/STA ownership |
| B | Group Policy Editor: enter and leave ×5 | A new ADMX bundle per visit; view-model capture |
| B-idle | Leave GpEdit, wait >10 minutes, then take a settled sample | Strong ADMX cache did not demote/collect |
| C | Six main nav pages in rotation ×10 | Overall per-navigation retention; lightweight journal entries |
| D | Certificates: expand/collapse the same large store ×10 | Flattened row/repeater growth |
| E | A drill-down page: breadcrumb to parent ×5, then repeat using NavigationView ×5 | Exit-path-specific Page retention |

For A, compare the logged `threads` and `handles`; both must plateau. For B, the first parsed bundle is an
intentional cache, so immediate post-navigation heap size may stay high. The regression signal is another
bundle-sized increase per visit; B-idle separately tests the ten-minute demotion policy.

### Verbose logging

Set `"VerboseLogging": true` in `%LOCALAPPDATA%/OneMMC/Settings.json` to restore `Debug` level. It is off
by default: enabled debug events add formatting, sink, allocation and I/O work; even disabled calls that use
non-source-generated `params object[]` overloads can allocate an argument array or box values. The `Trace`
→ Serilog bridge is likewise installed only when a debugger is attached.

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

[microsoft-ui-xaml#10981](https://github.com/microsoft/microsoft-ui-xaml/issues/10981) is an open .NET 10
report whose minimal reproduction dispatches string/double binding updates from background threads at
**10–20 Hz** and observes growth in `ComWrappers.ManagedObjectWrapperHolder`. It is evidence for that
high-frequency binding/DispatcherQueue shape, not proof that ordinary navigation or every `{x:Bind}` in
OneMMC leaks. Consider it only if a current repeated route is linear and a managed heap diff identifies
the same holder type.

Generated binding tracking in this project uses weak tracking for the page bindings object, but that does
not rule out an upstream ABI-wrapper regression under a different update pattern. Hand-written
`viewModel.PropertyChanged += ...` in code-behind is a direct strong subscription and must always be
unhooked.
