# Memory Management

## Resource Lifetime and Large Collections

This document contains only production rules backed by concrete ownership or scalability behavior. It does
not prescribe forced garbage collection, working-set trimming, navigation-time memory logging, weak-reference
probes, idle cache timers, or unload-time clearing of ordinary managed collections.

Task Manager totals are not sufficient evidence for a leak. A Windows App SDK process includes managed heaps,
native heaps, XAML and composition state, loaded code, COM objects, and shared resident pages. Add memory-specific
code only after a repeatable Release profile identifies an owner or allocation path and an A/B measurement shows
that the proposed change is worth its runtime and maintenance cost.

## Production Rules

### Scope transient disposable graphs

`App.GetRequiredService<T>()` resolves from the root `ServiceProvider`. Microsoft.Extensions.DependencyInjection
tracks every container-created `IDisposable` in the resolving scope, including disposable dependencies nested
under a non-disposable outer service. Root resolution can therefore retain that graph until application exit.

Use `PageServiceScope` when a page owns a transient graph containing a disposable:

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
    _serviceScope.Dispose();
}
```

`TaskHistoryService` is the reference case: it is not disposable, but it depends on the disposable
`EventViewerService`. Resolve the graph once from the page scope and let the scope dispose it. Do not also call
`Dispose()` on the resolved service. Never dispose an injected singleton.

### Release owned resources

`Dispose()` is required when an object owns a native handle, COM reference, timer, watcher, cancellation source,
subscription, or other disposable service. Page unload handlers must stop callbacks, unsubscribe events, cancel
page-owned work, and dispose page-owned scopes or services.

Do not clear ordinary managed collections merely because a page unloads. Once the page graph is unreachable, its
collections are unreachable too and the GC collects the graph as a unit. Collection clearing does not make an
otherwise unreachable graph collect sooner.

Finalizers must never wait for another thread. In particular, a finalizer must not call `Join`, `Wait`, or
`GetResult()` to marshal cleanup to an STA. A blocked finalizer thread stalls all finalization in the process.
Explicit disposal may coordinate with an owning apartment when its shutdown protocol guarantees completion;
the finalizer path must remain non-blocking.

### Keep navigation parameters small

`Frame.BackStack` and breadcrumb history retain navigation parameters. Pass identifiers such as paths, names,
instance IDs, and small routing DTOs. Do not pass services, view models, open COM objects, or feature models with
nested collections. The destination resolves its dependencies and reloads current state.

Do not cap navigation or breadcrumb history solely to improve memory metrics. A count-based cap removes valid
navigation behavior, and small identifier parameters do not justify that tradeoff without measured growth.

### Use controls with bounded realization

For large or unbounded collections:

- Put `ListView` and `GridView` in a height-constrained layout. Do not wrap them in another vertical
  `ScrollViewer`, place them in an unconstrained `Auto` row, or replace their virtualizing panel with a plain
  `StackPanel`.
- `ItemsRepeater` has no built-in scroll host. Place it in a `ScrollViewer` and use a virtualizing layout.
- Do not use `ItemsControl` for a growing collection. It is acceptable for a fixed handful of items.
- Treat `SettingsExpander.IsExpanded` strictly as presentation state. Starting collapsed may defer initial
  rendering, but it does not clear `Items`/`ItemsSource`, release the backing view model, dispose resources,
  guarantee that previously realized XAML elements are reclaimed, reduce process RAM, change the scroll extent
  algorithm, or fix `LayoutCycleException`. Never present `IsExpanded="False"` as a memory or layout-cycle fix.
- CommunityToolkit `SettingsExpander` contains an internal `ItemsRepeater` with virtualizing `StackLayout`.
  On long pages with nested, widely varying expanded heights, unrealized heights make the scroll extent an
  estimate. Near the estimated end, realization can revise the extent and make the thumb/offset jump; repeated
  viewport correction can visibly move the content. This is the behavior described by microsoft-ui-xaml
  issues 9308 and 1829.
- Use `StableSettingsExpander` only for a fixed or tightly bounded, cheap direct item collection. It replaces the
  inner layout with exact non-virtualizing measurement. Do not use it for growing sources such as certificate
  entries, devices, users/groups, shares/sessions/files, AzMan objects, COM+ applications, or similar catalogs;
  doing so realizes every item and can increase startup work and UI memory. Preserve virtualization for those
  sources and solve any measured instability with paging, deterministic heights, flattening, or a custom stable
  virtualizing layout.
- Materialize expensive `TreeView` branches on `Expanding` with `HasUnrealizedChildren`, and remove realized
  child nodes on `Collapsed`. XAML-wired handlers must be instance methods.

These rules prevent UI element count and layout work from scaling with all available data. They are not attempts
to force the CLR or Windows to return committed or resident pages immediately.

### Update bound collections in place

Never assign a new `ObservableCollection<T>` to a property an items control is already bound to. Mutate the
bound instance instead, via `ObservableCollectionExtensions.ReplaceAll`.

Handing an items control a different collection instance makes it discard every item container and build new
element trees. XAML does not release the native side of the discarded trees, and the loss is not recoverable:
a forced blocking gen2 collection followed by finalizers reclaimed 2.9 MB of a 95 MB process, and the
discarded containers have no managed GC roots at all (`gcroot` reports none), so this is not deferred
finalization. The managed heap stays small — 6.3 MB inside a 109 MB process — while committed private bytes
grow without bound; roughly 88% of the growth lands in the NT process heap, not the GC heap.

Measured on Device Manager with pinned window geometry, 20 consecutive list rebuilds driven by the filter box
(pure managed work, no interop):

| Bound collection updated by | Private bytes |
| --- | --- |
| assigning a new collection | +3.59 MB per rebuild, never returned |
| `ReplaceAll` on the bound instance | −0.75 MB per rebuild (flat) |

The cost scales with the number of elements realized per container, not with the number of items in the
source: a rebuild that produces an empty result set is free, and swapping the same list under a bare
`TextBlock` item template costs 0.54 MB per rebuild against 3.59 MB for a `SettingsExpander` template. It is
therefore a property of container realization, not of the data or of any one control.

### Cache pages that rebuild expensive item containers

A page whose item containers are expensive to realize should set `NavigationCacheMode="Enabled"` so revisiting
it reuses the page instance instead of realizing a fresh element tree that will never be released. Device
Manager round trips measured 9.04 MB per visit uncached against roughly 1.4 MB per visit cached, with handle
count flat instead of climbing about 20 per visit.

Caching changes page lifetime, so two things must be adjusted together:

- Move per-visit event subscriptions to `OnNavigatedTo`/`OnNavigatedFrom`. Subscribing in the constructor and
  unsubscribing in `Unloaded` detaches the handler permanently after the first navigation away; removing the
  `Loaded` handler there also silently stops the page from ever reloading again.
- Guard the initial load so it only runs when there is nothing to show (`if (Collection.Count == 0)`), and
  leave explicit refresh to the toolbar and pull-to-refresh. `ServicesPage` and `DeviceManagerPage` are the
  reference cases.

Do **not** enable caching on a page that disposes a `PageServiceScope` in `Unloaded` — `EventViewerPage`,
`PerformanceMonitorPage`, `TaskSchedulerPage`, `TaskPropertiesPage`, `AuthorizationManagerPage`. A cached page
would come back with a disposed scope. Either leave those uncached or first move scope ownership to
`OnNavigatedTo`/`OnNavigatedFrom`.

### Bound genuinely unbounded caches

A process-wide cache whose keys can grow indefinitely needs a bound when entries are cheap to reconstruct.
`SmbClientNameResolver.MaximumCacheEntries` is the reference case: client addresses can keep changing on a busy
server, and TTL alone does not remove an expired entry that is never queried again.

The shared `AdmxBundleProvider` is intentionally different. It stores at most one culture's immutable policy
bundle and prevents GpEdit and RSoP from parsing duplicate copies. It has no idle timer, weak-cache layer,
memory-pressure subscription, or forced collection. `Invalidate()` exists for a known on-disk definitions change.

## Practices Not Used

Do not introduce the following without a repeatable production problem, profiler evidence identifying the owner,
and a measured A/B benefit:

- `GC.Collect` or `GC.WaitForPendingFinalizers` in normal application flows
- working-set trimming with `EmptyWorkingSet` or `SetProcessWorkingSetSizeEx`
- timers that discard bounded caches merely to influence Task Manager
- memory-pressure subscriptions whose callbacks only drop managed references or force GC
- weak-reference cache layers without a demonstrated latency/memory tradeoff
- per-navigation memory snapshots, finalizer sentinels, or departed-page probes in the shipped application
- unload-time clearing of collections as generic retention insurance
- navigation-history caps or transition-object pooling for negligible object counts
- process-wide GC configuration changes without CPU, pause-time, and memory measurements

Working-set trimming changes residency, not ownership or private commit, and Windows already manages process
working sets. Forced collection can add pauses and still does not guarantee that the runtime returns memory to
the operating system. Diagnostic machinery also changes the application being measured and should live in an
external profiler or a disposable investigation branch rather than permanent production code.

## Investigating a Regression

1. Reproduce with a Release x64 build and no debugger attached.
2. Repeat the same route with the same data. Compare later repetitions rather than first visit versus launch.
3. Check whether process handles or threads rise with each repetition; that points to resource ownership rather
   than ordinary managed heap high-water behavior.
4. Use Visual Studio Memory Usage or another profiler to inspect managed paths to root and native allocation
   diffs. Take snapshots around repeated identical operations.
5. For commit growth not represented by heap snapshots, use WPR/WPA virtual-allocation tracing and group by
   process, commit type, and stack.
6. Fix the identified owner with the smallest change: dispose the resource, unsubscribe the event, cancel the
   work, correct the DI scope, bound an unbounded cache, or restore control virtualization.
7. Keep the change only when the same reproduction verifies the benefit and normal behavior remains correct.

Useful references:

- [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines#example-anti-patterns)
- [Manage memory usage in Windows App SDK desktop apps](https://learn.microsoft.com/windows/apps/develop/launch/reduce-memory-usage)
- [Visual Studio Memory Usage](https://learn.microsoft.com/visualstudio/profiling/memory-usage-without-debugging2)
- [Windows Performance Toolkit memory tracing](https://learn.microsoft.com/windows/apps/develop/performance/disk-memory)
- [ItemsRepeater guidance](https://learn.microsoft.com/windows/apps/develop/ui/controls/items-repeater)
- [ItemsRepeater nested variable-height extent issue #9308](https://github.com/microsoft/microsoft-ui-xaml/issues/9308)
- [ItemsRepeater variable-height layout issue #1829](https://github.com/microsoft/microsoft-ui-xaml/issues/1829)
- [TreeView incremental population](https://learn.microsoft.com/windows/apps/develop/ui/controls/tree-view#interacting-with-a-tree-view)
