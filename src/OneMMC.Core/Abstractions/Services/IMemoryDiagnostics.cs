namespace OneMMC.Core.Abstractions.Services;

/// <summary>
/// A point-in-time reading of the process's memory state.
/// </summary>
/// <param name="ManagedHeapBytes">Live managed heap size right now (<see cref="GC.GetTotalMemory(bool)"/>).</param>
/// <param name="CommittedBytes">Total memory committed by the GC, in bytes.</param>
/// <param name="PrivateBytes">
/// Private committed bytes — memory this process owns, excluding pages shared with other processes.
/// This is the metric to watch for a leak; working set includes shared DLL pages and is trimmed by the OS
/// on its own schedule, so it behaves like a high-water mark rather than current usage.
/// </param>
/// <param name="WorkingSetBytes">Process working set, in bytes.</param>
/// <param name="TotalAllocatedBytes">Cumulative bytes allocated by the process since start.</param>
/// <param name="Gen0Collections">Number of gen0 collections since process start.</param>
/// <param name="Gen1Collections">Number of gen1 collections since process start.</param>
/// <param name="Gen2Collections">Number of gen2 collections since process start.</param>
/// <param name="FinalizerProbesArmed">How many finalizer probes have been created since process start.</param>
/// <param name="FinalizerProbesRun">How many finalizer probes have actually been finalized.</param>
public readonly record struct MemorySnapshot(
    long ManagedHeapBytes,
    long CommittedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    long TotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long FinalizerProbesArmed,
    long FinalizerProbesRun);

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
    /// Forces a full collection, waits for finalizers, then captures and logs. Use this when comparing
    /// readings across a probe run.
    /// </summary>
    /// <remarks>
    /// An ordinary <see cref="Capture"/> reports whatever the heap happened to look like, which is mostly
    /// uncollected garbage — far too noisy to tell a leak from normal churn. Settling first makes
    /// successive readings comparable: a value that keeps climbing after a full collection is retained,
    /// not merely allocated. This pauses the app, so it is a deliberate diagnostic, not a default.
    /// </remarks>
    /// <param name="context">Short label identifying the call site, for example a page name.</param>
    /// <param name="detail">Optional extra detail appended to the log entry.</param>
    /// <returns>The snapshot that was logged.</returns>
    MemorySnapshot LogSettledSnapshot(string context, string? detail = null);

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
