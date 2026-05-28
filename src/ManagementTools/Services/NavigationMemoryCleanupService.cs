using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Win32;

namespace ManagementTools.Services;

/// <summary>
/// Coalesces navigation cleanup work so short page visits release stale managed and native working-set pressure.
/// </summary>
public sealed class NavigationMemoryCleanupService
{
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMilliseconds(700);

    private readonly ILogger<NavigationMemoryCleanupService> _logger;
    private int _scheduled;
    private int _collecting;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationMemoryCleanupService"/> class.
    /// </summary>
    /// <param name="logger">Logger used for cleanup diagnostics.</param>
    public NavigationMemoryCleanupService(ILogger<NavigationMemoryCleanupService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Requests a delayed cleanup pass after navigation settles.
    /// </summary>
    public void RequestCleanup()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1)
        {
            return;
        }

        _ = RunCleanupAsync();
    }

    private async Task RunCleanupAsync()
    {
        try
        {
            await Task.Delay(CleanupDelay).ConfigureAwait(false);
            Interlocked.Exchange(ref _scheduled, 0);

            if (Interlocked.Exchange(ref _collecting, 1) == 1)
            {
                RequestCleanup();
                return;
            }

            try
            {
                CompactManagedMemory();
                TrimWorkingSet();
            }
            finally
            {
                Interlocked.Exchange(ref _collecting, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Navigation memory cleanup failed.");
            Interlocked.Exchange(ref _scheduled, 0);
            Interlocked.Exchange(ref _collecting, 0);
        }
    }

    private static void CompactManagedMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void TrimWorkingSet()
    {
        try
        {
            _ = PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
        }
        catch
        {
            // Working-set trimming is best-effort. Managed cleanup above is the functional release path.
        }
    }
}
