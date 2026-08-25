# Memory Management

## Memory Model

OneMMC is a WinUI 3 desktop process. Its memory includes managed heaps, native heaps, XAML and composition
objects, loaded code, COM state, private commit, and resident pages. These values answer different questions:

- Managed reachability identifies whether a departed page or service is still owned.
- Private bytes approximate process-private committed memory. They do not fall merely because a page is no
  longer visible.
- Working set is the subset currently resident in physical memory. Task Manager primarily makes this number
  visible, and it can fall without releasing ownership or commit.

UWP applications participate in OS-managed lifecycle and suspension. A normal WinUI 3 desktop process does not
receive the same Process Lifetime Manager behavior. OneMMC reproduces the required user-visible effect whenever a
tracked Frame successfully replaces an existing Page: after the departed visual tree has unloaded, it collects
unreachable managed wrappers and then calls `EmptyWorkingSet(GetCurrentProcess())`. Collection addresses ownership;
trimming evicts eligible resident pages. Neither operation guarantees that native allocator commit returns to the
OS.

Do not compare launch memory with the first visit to a feature page and label the difference a leak. Framework,
XAML, interop, and feature initialization establish reusable high-water marks. Diagnose repeated late-cycle growth,
resource counts, and reachability instead.

## Production Rules

### Own and dispose resources explicitly

Dispose objects that own native handles, COM references, timers, watchers, cancellation sources, subscriptions,
or disposable services. Page teardown must stop callbacks, unsubscribe events, cancel page-owned work, and dispose
page-owned scopes or services.

Finalizers must never wait for another thread. A blocked finalizer thread prevents finalization process-wide.
Apartment-aware cleanup may coordinate during explicit disposal only when completion is guaranteed; finalization
must remain non-blocking.

Do not clear ordinary managed collections during unload. Once the page graph is unreachable, its collections are
unreachable as well.

### Scope transient disposable graphs

The DI container tracks every container-created `IDisposable` in the resolving scope, including a disposable nested
under a non-disposable outer service. Resolve a transient graph containing a disposable once from
`PageServiceScope`. Register the page's cancellation and event cleanup first, then attach the scope to the page so
scope disposal always follows page cleanup:

```csharp
private readonly PageServiceScope _serviceScope = new();

public EventViewerPage()
{
    ViewModel = _serviceScope.GetRequiredService<EventViewerViewModel>();
    InitializeComponent();
    Unloaded += OnUnloaded;
    _serviceScope.Attach(this);
}

private void OnUnloaded(object sender, RoutedEventArgs e)
{
    // Cancel work and detach page-owned handlers here. PageServiceScope disposes afterward.
}
```

`PageServiceScope.Attach` is idempotent for the same owner and rejects a second owner. Its unload handler detaches
itself before disposing the DI scope. Do not manually dispose the scope from the page's unload handler, do not
separately dispose a service owned by that scope, and never dispose an injected singleton. Attachment is one-shot:
do not attach a scope to a cached page or a page expected to load again after unloading unless that page recreates
the scope for each lifetime.

### Keep navigation state lightweight

`Frame.BackStack` and breadcrumb history retain navigation parameters. Pass identifiers, names, paths, or small
self-contained DTOs. Do not pass live services, view models, COM objects, or feature models containing large
collections. Do not cap valid navigation history merely to lower memory metrics.

Per-visit subscriptions must be balanced in `OnNavigatedTo`/`OnNavigatedFrom` or `Loaded`/`Unloaded`. Keep normal
page navigation uncached unless preserving a specific page instance is a product requirement. Caching a page hides
recreation cost by retaining its complete visual and data graph.

### Preserve collection identity

Mutate an `ObservableCollection<T>` already bound to an items control instead of assigning a replacement instance.
Use `ObservableCollectionExtensions.ReplaceAll` when replacing its contents. Replacing the collection causes the
control to discard and recreate its item containers, raising native XAML allocator high-water marks.

### Bound element realization

- Use `ListView` or `GridView` for growing collections and place it in a height-constrained row. Do not wrap it in
  another vertical `ScrollViewer`, place it in an unconstrained `Auto` row, or replace its virtualizing panel with a
  plain `StackPanel`.
- `ItemsRepeater` has no scroll host. Put it in a `ScrollViewer` and use a virtualizing layout.
- Do not use `ItemsControl` for a growing collection. It is acceptable for a fixed handful of cheap elements.
- `SettingsExpander.IsExpanded` is presentation state only. Collapsing it does not release data, reclaim process
  memory, or correct a layout cycle.
- Use `StableSettingsExpander` only for fixed or tightly bounded direct items. Do not use it for certificates,
  devices, users, shares, AzMan objects, or other growing catalogs because it realizes every direct item.
- Populate `TreeView` branches from `Expanding` with `HasUnrealizedChildren`, and remove realized children from
  `Collapsed`. XAML-wired handlers must be instance methods.

The certificate store pages use height-constrained `ListView` controls for their outer store collections. Their
previous `ScrollViewer` plus `ItemsControl` plus `StackPanel` arrangement realized every store container.

### Reclaim after every tracked Frame page replacement

`NavigationService` subscribes to both `Frame.Navigating` and `Frame.Navigated`. When navigation starts with a
`Page` in `Frame.Content` and completes successfully, the service schedules reclamation regardless of navigation
mode. This covers NavigationView selection, SettingsCard drill-in, Breadcrumb, GoBack, Forward, and direct
`Frame.Navigate` calls that target the shell Frame. The first navigation into an empty Frame does not reclaim.

Nested Frames must be registered with `NavigationService.TrackFrame` and their registration disposed with the
owning page. Event Viewer's `DetailContentFrame` is the current nested Frame case. The shared coordinator coalesces
requests from every tracked Frame and waits 750 ms so page unload, cancellation, and destination layout can finish.

The worker performs a blocking full compacting collection, waits for pending finalizers, performs a second full
compacting collection, and then calls the CsWin32-generated `EmptyWorkingSet(GetCurrentProcess())`. It runs away
from the Frame event handler, so navigation does not synchronously freeze before rendering the destination.
The first collection finds unreachable page wrappers; waiting permits their finalizers to release native peers; the
second collection removes objects made unreachable by finalization. The trim then produces the Task Manager Working
Set reduction that GC alone did not consistently produce.

Window minimize and a Frame's initial navigation do not trigger reclamation. Do not add periodic trims,
`SetProcessWorkingSetSizeEx`, memory-pressure callbacks, or forced working-set timers. Any change to this policy
requires New, Back, Forward, NavigationView, and Breadcrumb measurements of Working Set, Private Bytes, page
reachability, and interaction latency.

### Bound genuinely unbounded caches

Bound process-wide caches whose key space can grow indefinitely when values are cheap to reconstruct.
`SmbClientNameResolver.MaximumCacheEntries` is the reference case. A deliberately bounded cache such as the shared
single-culture `AdmxBundleProvider` does not need timers, weak layers, or memory-pressure callbacks.

## References

- [Windows App SDK app lifecycle](https://learn.microsoft.com/windows/apps/develop/launch/app-lifecycle)
- [UWP app lifecycle](https://learn.microsoft.com/windows/uwp/launch-resume/app-lifecycle)
- [`EmptyWorkingSet`](https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-emptyworkingset)
- [Manage memory usage in Windows App SDK desktop apps](https://learn.microsoft.com/windows/apps/develop/launch/reduce-memory-usage)
- [ItemsRepeater guidance](https://learn.microsoft.com/windows/apps/develop/ui/controls/items-repeater)
- [.NET dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines)
