using System;
using System.ComponentModel;
using System.Diagnostics;
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
    private static readonly TimeSpan SettledSnapshotTimeout = TimeSpan.FromSeconds(5);

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
    private readonly object _healthGate = new();
    private readonly object _settleGate = new();

    private long _lastRunCountObserved;
    private long _lastTotalAllocatedBytes;
    private int _gen2AtLastRun;
    private bool _finalizersResponsive = true;
    private bool _stallReported;
    private Task<MemorySnapshot>? _activeSettleTask;
    private bool _activeSettleTimedOut;

    public MemoryDiagnosticsService(ILogger<MemoryDiagnosticsService> logger)
    {
        _logger = logger;
        _gen2AtLastRun = GC.CollectionCount(2);
    }

    /// <inheritdoc />
    public bool FinalizersResponsive => Volatile.Read(ref _finalizersResponsive);

    /// <inheritdoc />
    public MemorySnapshot Capture()
    {
        ArmProbe();

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        int gen2 = GC.CollectionCount(2);
        long armed = Interlocked.Read(ref _probesArmed);
        long run = Interlocked.Read(ref _probesRun);
        long totalAllocated = GC.GetTotalAllocatedBytes(precise: false);
        long allocatedDelta = CalculateAllocatedDelta(totalAllocated);
        (int handleCount, int threadCount) = GetProcessResourceCounts();

        UpdateFinalizerHealth(gen2, armed, run);

        return new MemorySnapshot(
            ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            HeapSizeBytes: info.HeapSizeBytes,
            FragmentedBytes: info.FragmentedBytes,
            CommittedBytes: info.TotalCommittedBytes,
            PrivateBytes: GetPrivateBytes(),
            WorkingSetBytes: Environment.WorkingSet,
            TotalAllocatedBytes: totalAllocated,
            AllocatedDeltaBytes: allocatedDelta,
            GcIndex: info.Index,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: gen2,
            FinalizerProbesArmed: armed,
            FinalizerProbesRun: run,
            ProcessHandleCount: handleCount,
            ProcessThreadCount: threadCount);
    }

    /// <inheritdoc />
    public async Task<MemorySnapshot?> LogSettledSnapshotAsync(string context, string? detail = null)
    {
        Task<MemorySnapshot>? settleTask = null;

        lock (_settleGate)
        {
            if (_activeSettleTask is null || _activeSettleTask.IsCompleted)
            {
                settleTask = Task.Factory.StartNew(
                    () => CollectAndLogSettledSnapshot(context, detail),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);

                _activeSettleTask = settleTask;
                _activeSettleTimedOut = false;
            }
        }

        if (settleTask is null)
        {
            LogSnapshot(context, AppendDetail(detail, "settle-in-progress"));
            return null;
        }

        _ = settleTask.ContinueWith(
            completedTask => OnSettleWorkerCompleted(completedTask),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Task completed = await Task.WhenAny(settleTask, Task.Delay(SettledSnapshotTimeout)).ConfigureAwait(false);
        if (completed == settleTask)
        {
            try
            {
                return await settleTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The completion callback logs the worker exception with its original stack and context.
                return null;
            }
        }

        bool timedOut;
        lock (_settleGate)
        {
            timedOut = ReferenceEquals(_activeSettleTask, settleTask) && !settleTask.IsCompleted;
            if (timedOut)
            {
                _activeSettleTimedOut = true;

                // Keep timeout ownership and health publication under the same gate. Otherwise the
                // worker can complete and publish recovery between these two state changes, after
                // which this path would incorrectly overwrite recovery with a permanent stall.
                lock (_healthGate)
                {
                    Volatile.Write(ref _finalizersResponsive, false);
                    _stallReported = true;
                }
            }
        }

        // Task.Delay can win the race while the worker is completing. In that case observe the finished
        // task instead of reporting a false finalizer stall.
        if (!timedOut)
        {
            try
            {
                return await settleTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The completion callback logs the worker exception with its original stack and context.
                return null;
            }
        }

        _logger.LogError(
            "[Memory] Settled snapshot timed out after {TimeoutSeconds}s for {Context}. " +
            "The UI remains responsive; no additional settle worker will start until this one exits.",
            SettledSnapshotTimeout.TotalSeconds,
            context);

        LogSnapshot(context, AppendDetail(detail, "settle-timeout"));
        return null;
    }

    private MemorySnapshot CollectAndLogSettledSnapshot(string context, string? detail)
    {
        // Two passes: the first frees unreachable objects and queues their finalizers, the wait lets those
        // finalizers run, and the second frees what finalization released. This deliberately blocking work
        // runs only on the dedicated LongRunning worker created by LogSettledSnapshotAsync.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

        return LogSnapshot(context, AppendDetail(detail, "settled"));
    }

    private void OnSettleWorkerCompleted(Task<MemorySnapshot> completedTask)
    {
        bool recoveredFromTimeout = false;

        lock (_settleGate)
        {
            if (ReferenceEquals(_activeSettleTask, completedTask))
            {
                recoveredFromTimeout = _activeSettleTimedOut;
                _activeSettleTask = null;
                _activeSettleTimedOut = false;
            }
        }

        if (completedTask.IsFaulted)
        {
            Exception error = completedTask.Exception?.GetBaseException()
                ?? new InvalidOperationException("The memory settle worker failed without an exception.");
            _logger.LogError(error, "[Memory] Settled snapshot worker failed.");
            return;
        }

        if (!recoveredFromTimeout)
        {
            return;
        }

        lock (_healthGate)
        {
            Volatile.Write(ref _finalizersResponsive, true);
            _stallReported = false;
        }

        _logger.LogWarning(
            "[Memory] The previously timed-out settle worker completed; finalizer diagnostics are active again.");
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

    private (int HandleCount, int ThreadCount) GetProcessResourceCounts()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return (process.HandleCount, process.Threads.Count);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug(ex, "[Memory] Process handle/thread counts are unavailable for this snapshot.");
            return (0, 0);
        }
    }

    private long CalculateAllocatedDelta(long totalAllocated)
    {
        long previousAllocated = Volatile.Read(ref _lastTotalAllocatedBytes);

        while (totalAllocated > previousAllocated)
        {
            long observed = Interlocked.CompareExchange(
                ref _lastTotalAllocatedBytes,
                totalAllocated,
                previousAllocated);
            if (observed == previousAllocated)
            {
                return previousAllocated == 0 ? 0 : totalAllocated - previousAllocated;
            }

            previousAllocated = observed;
        }

        // Capture can run concurrently on the settle worker and the UI thread. An older reading must
        // not move the shared watermark backwards or inflate the following allocation delta.
        return 0;
    }

    /// <inheritdoc />
    public MemorySnapshot LogSnapshot(string context, string? detail = null)
    {
        MemorySnapshot snapshot = Capture();

        _logger.LogInformation(
            "[Memory] {Context} | private={PrivateMB}MB heap={HeapMB}MB gcHeap={GcHeapMB}MB " +
            "committed={CommittedMB}MB fragmented={FragmentedMB}MB workingSet={WorkingSetMB}MB " +
            "allocated={AllocatedMB}MB delta={AllocatedDeltaMB}MB gcIndex={GcIndex} " +
            "gc={Gen0}/{Gen1}/{Gen2} finalizers={ProbesRun}/{ProbesArmed} " +
            "handles={HandleCount} threads={ThreadCount} {Detail}",
            context,
            ToMegabytes(snapshot.PrivateBytes),
            ToMegabytes(snapshot.ManagedHeapBytes),
            ToMegabytes(snapshot.HeapSizeBytes),
            ToMegabytes(snapshot.CommittedBytes),
            ToMegabytes(snapshot.FragmentedBytes),
            ToMegabytes(snapshot.WorkingSetBytes),
            ToMegabytes(snapshot.TotalAllocatedBytes),
            ToMegabytes(snapshot.AllocatedDeltaBytes),
            snapshot.GcIndex,
            snapshot.Gen0Collections,
            snapshot.Gen1Collections,
            snapshot.Gen2Collections,
            snapshot.FinalizerProbesRun,
            snapshot.FinalizerProbesArmed,
            snapshot.ProcessHandleCount,
            snapshot.ProcessThreadCount,
            detail ?? string.Empty);

        return snapshot;
    }

    private void UpdateFinalizerHealth(int gen2, long armed, long run)
    {
        lock (_healthGate)
        {
            if (run > _lastRunCountObserved)
            {
                _lastRunCountObserved = run;
                _gen2AtLastRun = gen2;
                Volatile.Write(ref _finalizersResponsive, true);
                _stallReported = false;
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

            Volatile.Write(ref _finalizersResponsive, false);

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
    }

    private static string AppendDetail(string? detail, string suffix) =>
        string.IsNullOrWhiteSpace(detail) ? suffix : $"{detail} {suffix}";

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
