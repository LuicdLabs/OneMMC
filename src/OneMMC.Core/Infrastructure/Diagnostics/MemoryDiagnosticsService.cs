using System;
using System.Globalization;
using System.Threading;
using OneMMC.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.System.ProcessStatus;

namespace OneMMC.Core.Infrastructure.Diagnostics;

/// <summary>
/// Default <see cref="IMemoryDiagnostics"/> implementation. Registered as a singleton so the
/// finalizer probe counters accumulate across the whole session.
/// </summary>
public sealed partial class MemoryDiagnosticsService : IMemoryDiagnostics
{
    /// <summary>Gen2 collections that must pass with no finalizer run before declaring a stall.</summary>
    private const int StallGen2Threshold = 3;

    /// <summary>Armed-but-unfinalized probes tolerated before declaring a stall.</summary>
    private const long StallBacklogThreshold = 3;

    private static long _probesArmed;
    private static long _probesRun;

    /// <summary>
    /// Written and immediately cleared so the allocation cannot be treated as dead code, leaving the
    /// probe unreachable and eligible for finalization straight away.
    /// </summary>
    private static FinalizerProbe? _pendingProbe;

    private readonly ILogger<MemoryDiagnosticsService> _logger;

    private long _lastRunCountObserved;
    private int _gen2AtLastRun;
    private bool _stallReported;

    public MemoryDiagnosticsService(ILogger<MemoryDiagnosticsService> logger)
    {
        _logger = logger;
        _gen2AtLastRun = GC.CollectionCount(2);
    }

    /// <inheritdoc />
    public bool FinalizersResponsive { get; private set; } = true;

    /// <inheritdoc />
    public MemorySnapshot Capture()
    {
        ArmProbe();

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        int gen2 = GC.CollectionCount(2);
        long armed = Interlocked.Read(ref _probesArmed);
        long run = Interlocked.Read(ref _probesRun);

        UpdateFinalizerHealth(gen2, armed, run);

        return new MemorySnapshot(
            ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            CommittedBytes: info.TotalCommittedBytes,
            PrivateBytes: GetPrivateBytes(),
            WorkingSetBytes: Environment.WorkingSet,
            TotalAllocatedBytes: GC.GetTotalAllocatedBytes(precise: false),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: gen2,
            FinalizerProbesArmed: armed,
            FinalizerProbesRun: run);
    }

    /// <inheritdoc />
    public MemorySnapshot LogSettledSnapshot(string context, string? detail = null)
    {
        // Two passes: the first frees unreachable objects and queues their finalizers, the wait lets those
        // finalizers run, and the second frees what finalization released. Without this the reading is
        // dominated by garbage that simply has not been collected yet.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

        return LogSnapshot(context, detail is null ? "settled" : $"{detail} settled");
    }

    /// <summary>
    /// Reads private committed bytes for this process.
    /// </summary>
    /// <returns>Private bytes, or 0 if the query fails.</returns>
    private static unsafe long GetPrivateBytes()
    {
        var counters = default(PROCESS_MEMORY_COUNTERS_EX);
        counters.cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX);

        // K32GetProcessMemoryInfo takes the base PROCESS_MEMORY_COUNTERS; passing the EX layout with its
        // own cb is the documented way to get PrivateUsage back.
        if (!PInvoke.K32GetProcessMemoryInfo(
                PInvoke.GetCurrentProcess(),
                (PROCESS_MEMORY_COUNTERS*)&counters,
                counters.cb))
        {
            return 0;
        }

        return (long)counters.PrivateUsage;
    }

    /// <inheritdoc />
    public MemorySnapshot LogSnapshot(string context, string? detail = null)
    {
        MemorySnapshot snapshot = Capture();

        _logger.LogInformation(
            "[Memory] {Context} | private={PrivateMB}MB heap={HeapMB}MB committed={CommittedMB}MB " +
            "workingSet={WorkingSetMB}MB allocated={AllocatedMB}MB gc={Gen0}/{Gen1}/{Gen2} " +
            "finalizers={ProbesRun}/{ProbesArmed} {Detail}",
            context,
            ToMegabytes(snapshot.PrivateBytes),
            ToMegabytes(snapshot.ManagedHeapBytes),
            ToMegabytes(snapshot.CommittedBytes),
            ToMegabytes(snapshot.WorkingSetBytes),
            ToMegabytes(snapshot.TotalAllocatedBytes),
            snapshot.Gen0Collections,
            snapshot.Gen1Collections,
            snapshot.Gen2Collections,
            snapshot.FinalizerProbesRun,
            snapshot.FinalizerProbesArmed,
            detail ?? string.Empty);

        return snapshot;
    }

    private void UpdateFinalizerHealth(int gen2, long armed, long run)
    {
        if (run > _lastRunCountObserved)
        {
            _lastRunCountObserved = run;
            _gen2AtLastRun = gen2;
            FinalizersResponsive = true;
            return;
        }

        // A backlog on its own is normal — probes armed since the last gen2 collection have simply
        // not been collected yet. Only the combination of a backlog and several intervening gen2
        // collections indicates the finalizer thread is no longer draining the queue.
        bool stalled = armed - run > StallBacklogThreshold
                       && gen2 - _gen2AtLastRun >= StallGen2Threshold;

        if (!stalled)
        {
            return;
        }

        FinalizersResponsive = false;

        if (!_stallReported)
        {
            _stallReported = true;
            _logger.LogError(
                "[Memory] Finalization appears stalled: {ProbesArmed} probes armed, {ProbesRun} finalized, " +
                "{Gen2Since} gen2 collections since the last one ran. Native and COM resources that rely on " +
                "finalizers will not be reclaimed.",
                armed,
                run,
                gen2 - _gen2AtLastRun);
        }
    }

    private static void ArmProbe()
    {
        Interlocked.Increment(ref _probesArmed);
        _pendingProbe = new FinalizerProbe();
        Volatile.Write(ref _pendingProbe, null);
    }

    /// <summary>
    /// Formats bytes as megabytes with one decimal. Whole megabytes were too coarse: the managed heap sits
    /// in the single-digit-MB range, where a per-navigation leak of a few hundred KB would be invisible for
    /// dozens of navigations.
    /// </summary>
    private static string ToMegabytes(long bytes) =>
        (bytes / (double)(1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    /// Short-lived sentinel whose finalizer proves the finalizer thread is still draining its queue.
    /// </summary>
    private sealed class FinalizerProbe
    {
        ~FinalizerProbe() => Interlocked.Increment(ref _probesRun);
    }
}
