namespace OneMMC.Core.Abstractions.Services;

/// <summary>
/// A point-in-time reading of the process's memory state.
/// </summary>
/// <param name="ManagedHeapBytes">Live managed heap size right now (<see cref="GC.GetTotalMemory(bool)"/>).</param>
/// <param name="HeapSizeBytes">Total managed heap size reported for the most recently completed GC.</param>
/// <param name="FragmentedBytes">Managed heap fragmentation reported for the most recently completed GC.</param>
/// <param name="CommittedBytes">Total memory committed by the GC, in bytes.</param>
/// <param name="PrivateBytes">
/// Private committed bytes — memory this process owns, excluding pages shared with other processes.
/// A sustained increase across repeated settled routes is a useful retention signal, but the value also
/// includes legitimate managed, native, XAML, and runtime caches. Working set is current physical
/// residency and can change independently as Windows trims or faults pages back in.
/// </param>
/// <param name="WorkingSetBytes">Process working set, in bytes.</param>
/// <param name="TotalAllocatedBytes">Cumulative managed bytes allocated since process start.</param>
/// <param name="AllocatedDeltaBytes">Managed bytes allocated since the previous snapshot.</param>
/// <param name="GcIndex">Index of the most recently completed GC, or zero when no GC has completed.</param>
/// <param name="Gen0Collections">Number of gen0 collections since process start.</param>
/// <param name="Gen1Collections">Number of gen1 collections since process start.</param>
/// <param name="Gen2Collections">Number of gen2 collections since process start.</param>
/// <param name="FinalizerProbesArmed">How many finalizer probes have been created since process start.</param>
/// <param name="FinalizerProbesRun">How many finalizer probes have actually been finalized.</param>
/// <param name="ProcessHandleCount">Number of handles currently owned by the process.</param>
/// <param name="ProcessThreadCount">Number of threads currently owned by the process.</param>
public readonly record struct MemorySnapshot(
    long ManagedHeapBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    long CommittedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    long TotalAllocatedBytes,
    long AllocatedDeltaBytes,
    long GcIndex,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long FinalizerProbesArmed,
    long FinalizerProbesRun,
    int ProcessHandleCount,
    int ProcessThreadCount);

/// <summary>
/// Captures process memory readings so navigation-driven growth can be measured over a session.
/// </summary>
/// <remarks>
/// This exists purely as a diagnostic: it never affects behaviour and never surfaces in the UI.
/// Readings are written to the application log so a session's growth curve can be reconstructed
/// from <c>%LOCALAPPDATA%/OneMMC/Logs/</c> without attaching a profiler.
/// </remarks>
public interface IMemoryDiagnostics
{
    /// <summary>
    /// Reads the current memory state without forcing a collection.
    /// </summary>
    /// <returns>The current <see cref="MemorySnapshot"/>.</returns>
    MemorySnapshot Capture();

    /// <summary>
    /// Captures a snapshot and writes it to the log under the supplied context label.
    /// </summary>
    /// <param name="context">Short label identifying the call site, for example a page name.</param>
    /// <param name="detail">Optional extra detail appended to the log entry.</param>
    /// <returns>The snapshot that was logged.</returns>
    MemorySnapshot LogSnapshot(string context, string? detail = null);

    /// <summary>
    /// Asynchronously requests a full collection/finalizer pass, then captures and logs. Use this when
    /// comparing readings across a probe run.
    /// </summary>
    /// <remarks>
    /// An ordinary <see cref="Capture"/> reports whatever the heap happened to look like, which is mostly
    /// uncollected garbage — far too noisy to tell a leak from normal churn. Settling first makes
    /// successive readings comparable: a value that keeps climbing after a full collection is retained,
    /// not merely allocated. The blocking collection runs on a dedicated worker, never on the UI thread.
    /// Only one worker may run at a time; the request returns <see langword="null"/> if that worker does
    /// not finish within the diagnostic timeout.
    /// </remarks>
    /// <param name="context">Short label identifying the call site, for example a page name.</param>
    /// <param name="detail">Optional extra detail appended to the log entry.</param>
    /// <returns>The snapshot that was logged, or <see langword="null"/> when settling timed out or another
    /// settle worker is still active.</returns>
    Task<MemorySnapshot?> LogSettledSnapshotAsync(string context, string? detail = null);

    /// <summary>
    /// Gets a value indicating whether the finalizer thread still appears to be running finalizers.
    /// </summary>
    /// <remarks>
    /// A blocked finalizer thread stops <em>all</em> finalization process-wide, so every native or COM
    /// resource that relies on a finalizer leaks permanently. This probe detects that state: each
    /// <see cref="Capture"/> arms a short-lived sentinel whose finalizer bumps a counter, so a run
    /// count that stops advancing while gen2 collections continue means finalization has stalled.
    /// Returns <see langword="true"/> until enough evidence has accumulated to say otherwise.
    /// </remarks>
    bool FinalizersResponsive { get; }
}
