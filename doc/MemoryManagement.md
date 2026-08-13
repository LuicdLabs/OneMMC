# Resource Lifetime and Large Collections

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
- Start data-bound `SettingsExpander` controls collapsed when expansion avoids significant initial realization.
  This is a responsiveness choice, not proof of reduced process memory.
- Materialize expensive `TreeView` branches on `Expanding` with `HasUnrealizedChildren`, and remove realized
  child nodes on `Collapsed`. XAML-wired handlers must be instance methods.

These rules prevent UI element count and layout work from scaling with all available data. They are not attempts
to force the CLR or Windows to return committed or resident pages immediately.

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
- [TreeView incremental population](https://learn.microsoft.com/windows/apps/develop/ui/controls/tree-view#interacting-with-a-tree-view)
