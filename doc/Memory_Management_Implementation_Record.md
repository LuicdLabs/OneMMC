# OneMMC Memory Management Investigation and Implementation Record

## 1. Document Purpose

This document preserves the complete memory investigation that was previously described in
`doc/MemoryManagement.md`. It records the historical measurements, the in process probe design, the Git
history review, the conclusions reached during the investigation, and the production code changes made on
August 13, 2026.

The project is located at `C:\Users\User\Desktop\OneMMC`. The investigation covered the current working
tree, the complete Git history, WinUI navigation behavior, Microsoft.Extensions.DependencyInjection
lifetime behavior, managed and native resource ownership, COM apartment rules, UI virtualization, process
caches, and the diagnostic code that had been added to the shipped application.

The final policy is simple. Permanent production code must address a verified owner, a correctness defect,
or a data structure that can grow without a bound. Code that only changes a memory counter, records a
counter, or attempts to influence Task Manager without correcting ownership does not meet that standard.

## 2. Incident That Triggered the Final Review

The final review started when navigation to Group Policy Editor failed with this exception:

```text
System.Runtime.InteropServices.COMException (0xD0000225)
   at WinRT.ExceptionHelpers.ThrowExceptionForHR(Int32 hr)
   at ABI.WinRT.Interop.EventSource`1.Subscribe(TDelegate handler)
   at Windows.System.MemoryManager.add_AppMemoryUsageIncreased(EventHandler`1 value)
   at OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers.AdmxBundleProvider..ctor(...)
```

The constructor contained this subscription:

```csharp
MemoryManager.AppMemoryUsageIncreased += OnAppMemoryUsageIncreased;
```

OneMMC is an unpackaged WinUI 3 desktop application. The active process context rejected this WinRT event
subscription. The exception escaped the singleton constructor, dependency injection could not construct
`AdmxBundleProvider`, and XAML page activation failed.

The first correction made the subscription optional by catching `COMException`. That prevented the crash,
but it also exposed a more important design issue. The subscription was not required for application
correctness. Its callback only removed a managed strong reference and requested an optimized generation 2
collection. Neither operation guaranteed a reduction in private commit or working set. The optional fallback
still retained a timer, a weak reference cache, exception handling, state tracking, disposal logic, and a
warning that appeared during normal use.

This incident led to a broader question. The repository contained several permanent mechanisms that had
originated as memory experiments. Their maintenance cost was clear, but their production benefit was not.
The scope of work therefore expanded from one exception to a complete historical audit.

## 3. Git History Reviewed

The investigation searched all commits for `memory`, `leak`, `retention`, `cache`, `virtualization`,
`dispose`, `lifetime`, `working set`, and `GC`.

The main commits were:

1. Commit `2010b01`, dated July 13, 2026, introduced the Native AOT migration and some resource service
   foundations.
2. Commit `6773349`, dated July 26, 2026, was titled `Memory Management (#3)` and introduced the original
   broad memory management effort.
3. Commit `1709dd1`, dated August 12, 2026, clarified memory documentation and updated dependencies.
4. Commit `fc38277`, dated August 12, 2026, changed memory behavior, UI responsiveness, and tree
   virtualization.
5. Commit `4cf6021`, dated August 13, 2026, added resource management fixes, COM interoperability fixes,
   UI safety work, page lifetime diagnostics, and ADMX cache expiration behavior.

The commits could not be reverted as units. Each one mixed multiple categories of work. Some changes fixed
real handle, COM, thread, and dependency injection lifetime defects. Some prevented UI object counts from
scaling with all available data. Other changes were diagnostic probes or unmeasured attempts to influence
memory counters. The audit therefore classified individual mechanisms rather than classifying whole commits.

## 4. Classification Standard

### 4.1 Production correctness

A mechanism was retained when removing it would reintroduce at least one of the following conditions:

1. A native handle would remain open longer than its owner.
2. A COM reference would not receive deterministic release from the correct apartment.
3. A timer, watcher, callback, or worker thread would continue after the owning page left.
4. The root dependency injection scope would retain a transient disposable graph until application exit.
5. A finalizer could wait on another thread and block process wide finalization.
6. A navigation journal entry would retain a service, view model, open COM object, or large feature graph.
7. A process cache could acquire an unlimited number of keys.
8. A large data set would cause the UI to create an element for every record at once.

### 4.2 Production scalability

A mechanism was also retained when framework behavior provided a direct scaling reason. Examples include a
height constrained `ListView`, an `ItemsRepeater` with a scroll host, incremental `TreeView` population, and
a cache bound for client addresses that can change indefinitely. These mechanisms may not reduce an
immediate Task Manager reading. Their value is that layout work, element count, or cache size does not grow
without control as the input data grows.

### 4.3 Diagnostic or speculative code

A mechanism was removed when it met the following criteria:

1. It measured memory but did not correct an owner.
2. It ran during normal application navigation only to support an investigation.
3. It forced collection or waited for finalizers without correcting a retention path.
4. It cleared ordinary managed collections without an identified retaining owner.
5. It used a timer or weak reference to discard an already bounded cache.
6. It attempted to improve working set presentation rather than resource ownership.
7. It had no repeatable Release measurement showing a worthwhile benefit.
8. The documentation itself stated that the benefit had not been measured.
9. Its navigation, lifetime, exception, or disposal complexity exceeded its demonstrated value.

## 5. Memory Metrics Used During the Investigation

The investigation distinguished several metrics that had previously been discussed as if they were
interchangeable.

### 5.1 Managed heap

`GC.GetTotalMemory(false)` reports an estimate of live managed heap data. `GC.GetGCMemoryInfo()` reports
information from the most recently completed collection, including heap size, committed bytes, and
fragmentation. These values do not include native heaps, XAML composition allocations, loaded images, COM
state, or shared code pages.

### 5.2 Private commit

The probe called `K32GetProcessMemoryInfo` and read
`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`. The log called this value `private`. It represented process private
commit, not a count of live application objects. It combined managed heap segments, native heaps, XAML and
composition state, COM allocations, interop buffers, and other process private memory.

### 5.3 Working set

`Environment.WorkingSet` reported resident pages at one point in time. Working set included private and
shared resident pages. It changed with system memory pressure, OS trimming, page faults, and recent access.
It was not a reliable ownership metric.

### 5.4 Handles and threads

`Process.HandleCount` and `Process.Threads.Count` were collected because native lifetime problems may not be
obvious from managed heap size. A repeated increase in handles or threads during an identical route was a
stronger resource ownership lead than a single high working set value.

### 5.5 Allocated bytes and collection counts

The probe recorded `GC.GetTotalAllocatedBytes(false)`, allocation deltas, and generation 0, generation 1,
and generation 2 collection counts. These values described churn and collection activity. They did not prove
retention by themselves.

## 6. Historical Measurements

### 6.1 Historical Debug probe run from July 26, 2026

The original historical baseline used an x64 Debug build with probe mode enabled. It covered 25 navigations
through PC Management, Event Viewer, Task Scheduler, System Management, Windows Firewall, Disk Management,
and Settings.

The first managed heap reading was approximately 0.5 MB. The peak was approximately 2.5 MB. The final reading
was approximately 1.6 MB.

Private commit started at approximately 53.2 MB, peaked at approximately 126.0 MB, and ended at approximately
114.1 MB.

Working set started at approximately 129.0 MB, peaked at approximately 258.6 MB, and ended at approximately
248.7 MB.

The settled managed heap returned to roughly 1.5 MB for the routes covered by that run. Private commit moved
up and down instead of increasing on every navigation. One example moved from 109.4 MB to 92.5 MB after
leaving Disk Management. Another moved from 126.0 MB to 114.1 MB near the end of the route.

The run did not establish a per navigation managed leak for the covered pages. It also did not prove that
all features or native allocations were leak free. The 53.2 MB to 114.1 MB endpoint change could not be
assigned to XAML style caches without allocation or commit stack evidence.

The same navigation count in a separate run without probe mode ended near 343 MB working set. The two runs
had different collection and residency states. That difference could not be used as proof of retention or
proof of successful cleanup.

### 6.2 Later broad non probe route

A later route moved from approximately 33.8 MB to 199.6 MB private commit. Managed heap moved from
approximately 3.3 MB to 11.9 MB. Handle count moved from approximately 673 to 1200. Thread count moved from
approximately 21 to 27.

That route mixed first visits, different features, background work, and unsynchronized collection points.
Its endpoint change was not a leak rate.

Windows Firewall drill down was a useful lead. The first editor, information, and Back cycle moved from
approximately 127 MB and 952 handles to 171.5 MB and 1110 handles. A second cycle reached approximately
190 MB and 1127 handles. The much smaller handle increase during the second cycle was compatible with first
use initialization or a high water mark, but only repeated identical settled cycles could establish a
plateau.

### 6.3 ADMX bundle observation

One non probe run entered and left Group Policy Editor. Managed heap rose to approximately 33.3 MB and later
remained near 20 MB to 22 MB. This was consistent with the singleton ADMX provider intentionally retaining
the parsed policy bundle.

The observation identified a bounded cache cost. It did not prove that a ten minute expiration policy would
improve private commit, working set, or user experience. A current Release comparison was never completed.

### 6.4 First visit cost investigation

One varied first visit session moved from approximately 50.8 MB to 130.1 MB private commit while the last
managed heap reading was approximately 7.2 MB. This showed that a three digit Task Manager value could coexist
with a comparatively small managed heap.

The investigation reviewed WinUI source at commit
`6112d936461edb6d81ce7db983c74cc60ea2bc28`. The review found long lived framework mechanisms such as the
default style cache and XBF node stream storage. These mechanisms made a bounded first visit cost plausible.
They did not prove that every departed `Page` instance was retained, and they did not quantify the source of
the observed private commit increase.

The defensible conclusion was that a bounded first visit cost was plausible, but the measured delta had not
been attributed to a specific framework cache. Page instance lifetime and process lifetime framework caches
were separate questions.

## 7. Original Memory Diagnostics Service

### 7.1 Public abstraction

`IMemoryDiagnostics` exposed `Capture`, `LogSnapshot`, and `LogSettledSnapshotAsync`. It returned a
`MemorySnapshot` containing managed heap size, GC heap size, committed GC bytes, fragmentation, private
commit, working set, allocated bytes, collection counts, finalizer probe counts, process handles, and process
threads.

The service was registered as a singleton so allocation deltas and finalizer counters persisted for the
application session.

### 7.2 Normal navigation behavior

When `MemoryProbeMode` was false, every navigation still performed a normal snapshot. The operation queried
GC information, private commit, working set, handle count, thread count, and allocation counters. It also
created a finalizable sentinel and wrote an Information log entry.

A representative log was:

```text
[Memory] post-nav:EventViewerPage | private=114.2MB heap=6.2MB gcHeap=7.1MB
committed=14.0MB fragmented=0.4MB workingSet=210.4MB allocated=57.3MB
delta=0.8MB gcIndex=6 gc=22/15/6 finalizers=11/12 handles=742 threads=31
backStack=4 breadcrumbs=2 mode=New sequence=7 initiator=NavigationView settled
```

This instrumentation ran in the normal shipped process even though it did not reduce memory or correct
resource ownership.

### 7.3 Settled snapshot behavior

When `MemoryProbeMode` was true, navigation requested a settled snapshot on a dedicated long running worker.
The worker executed:

```csharp
GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
GC.WaitForPendingFinalizers();
GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
```

The worker existed so the UI thread did not block in `GC.WaitForPendingFinalizers`. A five second timeout
produced a `settle-timeout` diagnostic. The service refused to start another settle worker while the first
worker was still active. Completion after a timeout produced a recovery log.

This design was careful, but it was still a diagnostic tool embedded in production code. It changed
collection timing and added a long running thread during the measurement.

### 7.4 Finalizer health sentinel

Each snapshot armed a `FinalizerProbe` instance and then allowed it to become unreachable. Its finalizer
incremented a process counter. The service compared armed and completed counts with generation 2 progress.
If generation 2 collections continued while the finalizer backlog increased, the service reported a possible
stalled finalizer thread.

This mechanism helped identify finalizer health as an investigation area. It did not repair finalization. It
also added finalizable objects to every normal navigation snapshot.

### 7.5 Native metadata used only by diagnostics

`NativeMethods.txt` contained `K32GetProcessMemoryInfo` and `PROCESS_MEMORY_COUNTERS_EX` only for the memory
diagnostics service. `GetCurrentProcess` had other security and process handle users, so it was not removed.

## 8. Original Page Lifetime Probe

### 8.1 Purpose

Task Manager could not show whether a departed WinUI `Page` remained reachable. The page lifetime probe was
designed to answer that narrower question directly.

### 8.2 Navigation event coverage

When probe mode was enabled, `MainWindow` subscribed to `Frame.Navigating`, `Frame.NavigationFailed`, and
`Frame.NavigationStopped`. It always subscribed to `Frame.Navigated` because that handler also maintained
normal shell state.

`RunNavigation` assigned a diagnostic initiator around navigation actions. Initiators included Startup,
GoBack, Breadcrumb, NavigationView, and Other.

### 8.3 Capturing the outgoing page

On `Navigating`, the probe created a `PendingNavigationAttempt`. The attempt contained a `DepartedPageProbe`
with a `WeakReference<Page>`, the outgoing page type, the destination page type, the navigation initiator,
and a sequence number.

No strong page reference was intentionally retained.

### 8.4 Navigation completion and failure

`NavigationFailed` and `NavigationStopped` invalidated the active attempt so failed navigation did not create
a false departed page record.

`Navigated` did not pass `NavigationEventArgs` into the asynchronous diagnostic path. The `Content` property
strongly referenced the destination page. Carrying the event args across an asynchronous boundary could have
made the probe itself retain a page after a later navigation.

The handler copied only scalar metadata and queued a DispatcherQueue callback. The callback ran after the
Frame navigation callback stack unwound, committed the weak probe, requested a settled snapshot, and then
evaluated page reachability.

### 8.5 Probe storage and reporting

The probe list was capped at 128 entries. Collected pages were removed. A live page received another settled
GC survival count. A page that survived at least two settled passes produced one warning.

A representative report was:

```text
[Memory][PageLifetime] settledThrough=7 tracked=0 collected=1 alive=0
dropped=0 survivors=none
```

One survival was treated as a lead rather than proof of a leak because transition animation, asynchronous
work, or temporary framework ownership could keep a page alive briefly. Repeated survival suggested a path
to root investigation.

### 8.6 Original interpretation rules

The original procedure interpreted page lifetime results as follows:

1. `collected=1 alive=0` meant the departed instance became unreachable by that settled pass.
2. One live observation was recorded but was not called a leak.
3. Survival across two settled passes produced a warning and requested a managed path to root investigation.
4. Growth in old live pages across repetitions indicated navigation release failure even if private commit
   appeared flat.
5. Collection of old pages combined with linear private commit growth shifted the investigation toward
   native allocations, process caches, handles, or threads.

## 9. Original Probe Procedure

The historical procedure required these steps:

1. Use a Release build with no debugger attached. Debugger services and Debug behavior changed absolute
   memory values and lifetime timing.
2. Set `"MemoryProbeMode": true` in `%LOCALAPPDATA%\OneMMC\Settings.json`.
3. Wait for each feature to finish loading before leaving it. `Frame.Navigated` only indicated that the
   destination content was available.
4. Repeat the same route with the same data. Visit 2 was compared with visits 5 and 10 rather than comparing
   launch with an unrelated first visit.
5. Exercise GoBack, breadcrumb navigation, and NavigationView departure separately.
6. Disable probe mode after the investigation.

The original named probes were:

1. Probe A opened Authorization Manager, entered a store, returned to the manager, and repeated the route
   five times. It checked thread and handle growth and duplicate STA ownership.
2. Probe B entered and left Group Policy Editor five times. It checked duplicate ADMX bundle parsing and
   view model capture.
3. Probe B idle left Group Policy Editor, waited more than ten minutes, and requested a settled sample. It
   tested the strong ADMX cache demotion policy.
4. Probe C rotated through six main navigation pages ten times. It checked general per navigation retention.
5. Probe D expanded and collapsed the same large certificate store ten times. It checked flattened row and
   repeater growth.
6. Probe E left a drill down page through breadcrumb navigation five times and NavigationView navigation five
   times. It checked departure path differences.

Probe A expected handles and threads to reach a plateau. Probe B allowed the first parsed ADMX bundle to stay
resident because it was an intentional shared cache. Another bundle sized increase on every visit was the
regression signal. Probe B idle attempted to test the ten minute policy, but no Release comparison established
that the policy provided a worthwhile process memory benefit.

## 10. Working Set Trimming Experiment

An earlier experiment called `EmptyWorkingSet`, or the equivalent
`SetProcessWorkingSetSizeEx(handle, -1, -1)`, after the window had been minimized or inactive for a short
period. It also used an aggressive blocking collection.

One x64 Debug measurement produced these values:

1. Working set while running was approximately 163.4 MB.
2. Working set after minimization and trimming was approximately 17.4 MB.
3. Working set approximately five seconds after restoration was approximately 43.5 MB.
4. The application became responsive approximately 78 milliseconds after the restore request.
5. Private commit changed only slightly in one run, from approximately 60.0 MB to 59.1 MB.
6. Another run changed from approximately 63.5 MB to 44.2 MB when the aggressive collection happened to
   return retained managed heap memory.

The experiment was reverted for four reasons.

1. Working set trimming changed residency, not ownership or private commit. Reaccess could fault the pages
   back into memory, and Windows already managed working sets under pressure.
2. `EmptyWorkingSet` was documented primarily for testing and tuning. It was not an application memory
   ownership API.
3. The aggressive blocking collection introduced pauses and did not follow the limited pressure only sample
   in the Windows App SDK guidance.
4. The five second and thirty second inactivity delays were much shorter than the guidance for releasing
   reconstructable data after sustained inactivity.

The experiment demonstrated that Task Manager working set could be changed dramatically without proving
that the application owned less memory. It was correctly removed before the final historical commit.

## 11. Process GC Configuration Investigation

The historical document also investigated runtime GC settings under Native AOT. A prior .NET runtime issue
had reported that `RuntimeHostConfigurationOption` values for `System.GC.*` were ignored under Native AOT.
The SDK used by OneMMC no longer had that issue.

An AOT probe confirmed values similar to:

```text
ServerGC       False
LatencyMode    Batch
ConcurrentGC   False
RetainVM       False
GCConserveMem  5
```

Possible settings included `DOTNET_GCConserveMemory=5`, disabling concurrent collection, and a Segment Heap
manifest option. None was enabled in the project because no Release measurement established a worthwhile
memory reduction with acceptable CPU and pause behavior.

That decision remains unchanged. The current simplification removed documentation that could encourage an
untested setting to become permanent configuration.

## 12. Confirmed Production Resource Fixes

### 12.1 Page scoped dependency injection

`App.GetRequiredService<T>()` resolves from the root service provider. Microsoft.Extensions.DependencyInjection
tracks each container created `IDisposable` in the resolving scope. A non disposable outer service can still
cause root capture when a nested dependency is disposable.

`TaskHistoryService` is the reference case:

```text
TaskHistoryService
    EventViewerService : IDisposable
```

Root resolution of `TaskHistoryService` would cause the root scope to retain `EventViewerService` until
application exit. `PageServiceScope` fixes the actual owner relationship. The page resolves the graph once,
and `Unloaded` disposes the scope.

This mechanism was retained for Event Viewer, Performance Monitor, Task Scheduler history, Group Policy
Editor, RSoP, and Authorization Manager where the dependency graph requires it.

### 12.2 Deterministic native and COM disposal

The audit retained deterministic disposal for Event Log records and watchers, PDH queries and counters,
Task Scheduler COM services, RSoP and GPO resources, Firewall COM wrappers, AzMan COM objects, timers,
cancellation sources, and event subscriptions.

These objects own resources outside the managed object graph. Removing their disposal would reintroduce real
handle, thread, COM, or callback lifetime defects.

### 12.3 STA shutdown and finalizer behavior

AzMan and Task Scheduler use dedicated apartment threads for COM ownership. The retained shutdown protocol
stops accepting new work, completes accepted work, performs terminal cleanup on the owning STA, and allows
explicit disposal to join when completion is guaranteed.

The AzMan finalizer requests cleanup without waiting. A finalizer must not call `Join`, `Wait`, or
`GetResult()` on another thread because one blocked finalizer blocks finalization for the entire process.

### 12.4 Identifier based navigation parameters

`Frame.BackStack` and breadcrumb history retain navigation parameters. The audit retained routing DTOs that
carry store paths, application names, scope names, task paths, rule lookup names, and instance identifiers.
It did not restore parameters that carried services, view models, open COM objects, or large feature models.

Navigation history remained uncapped. A count based cap had previously removed valid Back behavior and added
synchronization complexity between Frame history and breadcrumb history. Small identifier parameters did not
justify that behavior loss.

### 12.5 UI virtualization and incremental realization

The audit retained height constrained `ListView` and `GridView` layouts. It retained `ItemsRepeater` inside an
appropriate scroll host. It retained incremental `TreeView` population through `HasUnrealizedChildren`,
`Expanding`, and `Collapsed`. It also retained certificate display row materialization based on expansion
state.

These changes prevent UI element count and layout work from scaling with every available item. Reverting them
would produce a direct scalability regression even if a small test data set showed an acceptable Task Manager
value.

### 12.6 Bounded SMB client name cache

`SmbClientNameResolver` caches results by client address. A busy server can produce new addresses
indefinitely. TTL alone does not remove an expired entry that is never queried again. The 512 entry bound,
expired entry sweep, and clear fallback were retained because they bound a genuinely open key space and
cache misses are safe to recompute.

## 13. Speculative Mechanisms Removed

### 13.1 Memory diagnostics subsystem

The following files were deleted:

```text
src/OneMMC.Core/Abstractions/Services/IMemoryDiagnostics.cs
src/OneMMC.Core/Infrastructure/Diagnostics/MemoryDiagnosticsService.cs
```

The corresponding singleton registration, diagnostics namespace import, `MemoryProbeMode` setting,
`K32GetProcessMemoryInfo` metadata, and `PROCESS_MEMORY_COUNTERS_EX` metadata were removed.

The shipped application no longer performs memory snapshots, finalizer sentinel allocation, process resource
enumeration, or forced collection during navigation.

### 13.2 MainWindow page lifetime probe

Approximately 380 lines of diagnostic navigation code were removed from `MainWindow.xaml.cs`. This included
weak page probes, pending attempts, initiator state, navigation sequence state, failure invalidation, deferred
diagnostic callbacks, settled logging, survivor reports, and `RunNavigation` wrappers.

Normal shell behavior remained in `ContentFrame_Navigated`. It still updates the Back button, restores
breadcrumb state after GoBack, and synchronizes the selected NavigationView item.

### 13.3 Unload collection clearing

Eight `ClearCachedData()` methods and nine unload call sites were removed from Certificates, Device Manager,
Disk Management, Shared Folders, Local Users and Groups, Services, Print Management, and Firewall Rules.

An unreachable page and its collections are collected as one object graph. Clearing the collections first
does not make that graph unreachable and does not guarantee that the runtime returns memory to Windows.

Shared Folders required one behavior preserving adjustment. Its old cleanup method also set polling state.
The unload handler now calls `ApplyLiveMonitoring(false)`, then detaches and releases the timer. Firewall Rules
retained cancellation, timer shutdown, change subscription disposal, admin event detachment, and cancellation
source disposal.

### 13.4 ADMX pressure and expiration layers

The shared `AdmxBundleProvider` remains because Group Policy Editor and RSoP would otherwise parse duplicate
copies of approximately 250 to 300 ADMX and ADML pairs.

The provider now keeps one strong bundle for one culture. `Invalidate()` clears it when definitions are known
to have changed.

The following layers were removed:

1. `MemoryManager.AppMemoryUsageIncreased` subscription.
2. Memory pressure callback.
3. Pressure triggered `GC.Collect`.
4. Ten minute idle timer.
5. Weak reference cache.
6. Weak cache promotion.
7. Timer disposal.
8. `IDisposable` implementation that existed only for the timer and event subscription.

The prior ten minute value was an arbitrary policy without a completed Release comparison. Dropping a strong
reference did not guarantee lower private commit or working set, and later use could require expensive policy
file parsing again.

### 13.5 Transition object pooling

`MainWindow` previously shared one `SlideNavigationTransitionInfo` instance because a journal entry could
retain its transition object. No measurement showed that this small object mattered compared with XAML page
trees and feature data. The shared field and its retention explanation were removed. Navigation now creates a
normal transition object and preserves the animation behavior.

### 13.6 Explicit Frame cache size

`CacheSize="0"` was removed from the main Frame. No page enables `NavigationCacheMode`, so the explicit value
did not change current behavior. Keeping it could incorrectly imply that it was responsible for releasing
departed page instances.

## 14. Documentation Changes

The previous `doc/MemoryManagement.md` contained approximately 565 lines. It mixed production ownership
rules, local measurements, historical Debug baselines, probe implementation details, rejected experiments,
upstream issue notes, and untested GC configuration ideas.

The production document was replaced with a shorter resource lifetime policy. It now contains only the
rules needed for current development:

1. Scope transient disposable graphs correctly.
2. Dispose owned native and COM resources.
3. Keep finalizer paths nonblocking.
4. Keep navigation parameters small and identifier based.
5. Use controls with bounded realization for large data sets.
6. Bound caches whose key space can grow indefinitely.
7. Require profiler evidence and an A/B result before adding memory specific machinery.

This desktop record now contains the historical measurements, probe implementation, rejected experiments,
and removal rationale. The production document no longer requires developers to maintain an investigation
tool inside the application.

`AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, and `doc/Logging.md` were updated to match the
new policy.

## 15. Files Changed

### 15.1 Deleted files

```text
src/OneMMC.Core/Abstractions/Services/IMemoryDiagnostics.cs
src/OneMMC.Core/Infrastructure/Diagnostics/MemoryDiagnosticsService.cs
```

### 15.2 Core and shell files simplified

```text
src/OneMMC/MainWindow.xaml
src/OneMMC/MainWindow.xaml.cs
src/OneMMC/Models/AppSettings.cs
src/OneMMC.Core/DependencyInjection/ServiceCollectionExtensions.cs
src/OneMMC.Core/NativeMethods.txt
src/OneMMC.Core/Features/PolicyManagement/Services/GpEdit/Parsers/AdmxBundleProvider.cs
```

### 15.3 View models with collection cleanup removed

```text
src/OneMMC.Core/Features/Certificates/ViewModels/CertificateStoresViewModelBase.cs
src/OneMMC.Core/Features/PCManagement/ViewModels/DevMgmt/DeviceManagerViewModel.cs
src/OneMMC.Core/Features/PCManagement/ViewModels/DiskMgmt/DiskManagementViewModel.cs
src/OneMMC.Core/Features/PCManagement/ViewModels/FsMgmt/SharedFoldersViewModel.cs
src/OneMMC.Core/Features/PCManagement/ViewModels/LusrMgr/LocalUsersGroupsViewModel.cs
src/OneMMC.Core/Features/PCManagement/ViewModels/Services/ServicesViewModel.cs
src/OneMMC.Core/Features/PrintManagement/ViewModels/PrintManagement/PrintManagementViewModel.cs
src/OneMMC.Core/Features/SystemManagement/ViewModels/WF/Rules/FirewallRuleViewModel.cs
```

### 15.4 Page cleanup call sites simplified

```text
src/OneMMC/Views/CertificatesCredential/CertLM/LocalComputerCertificatesPage.xaml.cs
src/OneMMC/Views/CertificatesCredential/CertMgr/CurrentUserCertificatesPage.xaml.cs
src/OneMMC/Views/PCManagement/DevMgmt/DeviceManagerPage.xaml.cs
src/OneMMC/Views/PCManagement/DiskMgmt/DiskManagementPage.xaml.cs
src/OneMMC/Views/PCManagement/FsMgmt/SharedFoldersPage.xaml.cs
src/OneMMC/Views/PCManagement/LusrMgr/LocalUsersGroupsPage.xaml.cs
src/OneMMC/Views/PCManagement/Services/ServicesPage.xaml.cs
src/OneMMC/Views/PrintManagement/PrintManagement.xaml.cs
src/OneMMC/Views/SystemManagement/WF/FirewallRuleEditorPage.xaml.cs
```

### 15.5 Documentation and repository guidance

```text
doc/MemoryManagement.md
doc/Logging.md
AGENTS.md
CLAUDE.md
.github/copilot-instructions.md
```

## 16. Change Size

The working tree comparison reported approximately 30 changed files, 135 inserted lines, and 1,670 deleted
lines. Most inserted lines were the replacement production policy. The shipped execution path became
substantially smaller.

The largest reductions were approximately 387 lines from `MemoryDiagnosticsService.cs`, 98 lines from
`IMemoryDiagnostics.cs`, 379 lines from `MainWindow.xaml.cs`, and 112 lines from `AdmxBundleProvider.cs`.

## 17. Verification

### 17.1 Release build

The following command was executed:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -c Release -p:Platform=x64
```

The result was:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

This covered C# compilation, XAML compilation, dependency injection references, Native AOT and trim
analyzers, CsWinRT diagnostics, and CsWin32 generated references.

### 17.2 Diff validation

`git diff --check` completed without a whitespace error. Git produced only normal line ending notices about
future LF to CRLF conversion in the working tree.

### 17.3 Removed symbol scan

The source tree was searched for the following removed symbols and behaviors:

```text
MemoryProbeMode
IMemoryDiagnostics
MemoryDiagnosticsService
MemorySnapshot
FinalizerProbe
post-nav:
[Memory][PageLifetime]
ClearCachedData
StrongCacheIdleTimeout
WeakReference<AdmxBundle>
AppMemoryUsageIncreased
K32GetProcessMemoryInfo
PROCESS_MEMORY_COUNTERS_EX
GC.Collect
GC.WaitForPendingFinalizers
Environment.WorkingSet
WeakReference<Page>
```

No source references remained.

### 17.4 Retained lifetime checks

The final review confirmed that `PageServiceScope`, page scope disposal, Event Viewer resource disposal,
Performance Monitor PDH disposal, Task Scheduler history scoping, Group Policy and RSoP disposal,
Authorization Manager STA ownership, Shared Folders polling shutdown, Firewall cancellation, timer disposal,
event detachment, tree realization, list virtualization, and the bounded SMB resolver cache remained in place.

### 17.5 Independent diff review

A final read only review checked navigation equivalence, breadcrumb and Back behavior, unsaved changes guards,
Shared Folders polling shutdown, Firewall unload cancellation, ADMX provider locking, singleton registration,
deleted diagnostic references, and documentation consistency. The review reported no blocking finding.

## 18. Manual Verification Still Required

The repository has no automated test project. The build and static review passed, but the following runtime
checks remain appropriate:

1. Start OneMMC and navigate among all main sections.
2. Test NavigationView Back behavior.
3. Test breadcrumb navigation to a parent page.
4. Test Settings navigation.
5. Test cancel and confirm behavior on a page with unsaved changes.
6. Enter and leave Group Policy Editor repeatedly.
7. Enter RSoP and confirm shared ADMX loading.
8. Enable Shared Folders live monitoring, leave the page, and confirm polling stops.
9. Leave Firewall Rules while enumeration is active and confirm there is no callback after unload or
   unhandled exception.
10. Repeat Event Viewer, Performance Monitor, Task Scheduler, and Authorization Manager routes and confirm
    handle and thread counts reach a stable range with an external profiler.
11. Test large certificate stores, event channel trees, and policy trees to confirm incremental UI behavior.

These checks should use external tools when memory data is required. They should not restore a permanent
in process navigation probe.

## 19. Final Conclusions

The investigation established five conclusions.

1. A memory counter is not a resource owner. Working set, private commit, and managed heap describe different
   parts of process behavior. A change in one value does not identify the code that owns it.
2. Permanent code is justified when it fixes ownership, releases an external resource, or bounds growth that
   can continue with input size or session length.
3. Forced collection, working set trimming, weak cache layers, idle expiration, and unload collection clearing
   do not substitute for finding the owner.
4. Diagnostic probes can be useful during an investigation, but a probe that changes navigation, collection
   timing, finalization, logging, and process enumeration should not remain in the shipped application after
   the investigation ends.
5. The valuable parts of the earlier memory work were dependency injection scoping, deterministic native and
   COM disposal, STA shutdown correctness, event and cancellation lifetime management, identifier based
   navigation, UI virtualization, and bounds on genuinely open cache key spaces.

The final implementation removed approximately 1,670 lines of diagnostic and speculative code while
retaining the mechanisms that prevent actual resource leaks and data dependent scaling problems. The Release
x64 build completed with no warnings and no errors.

## 20. Policy for Future Investigations

Future memory investigations should follow this sequence:

1. Reproduce the behavior in a Release build with the same route and the same data.
2. Determine whether the process reaches a plateau or grows with every identical repetition.
3. Check handle and thread trends in addition to byte totals.
4. Use managed path to root analysis, native heap snapshots, or WPR and WPA commit stack analysis to identify
   the owner.
5. Apply the smallest ownership, disposal, scope, cache bound, or virtualization correction.
6. Compare the same reproduction before and after the change.
7. Remove the experiment if the benefit is not repeatable or does not justify its runtime and maintenance
   cost.
8. Keep investigation tools in an external profiler or a temporary branch unless they provide an ongoing
   operational requirement.

This record is an archive of the completed investigation. It is not a requirement to restore the removed
probe or any other memory optimization. The governing practice is to measure first, identify the owner, make
the smallest correct change, and verify the result.
