using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Services;

/// <summary>
/// Coalesces lightweight GC maintenance after navigation releases large page data.
/// </summary>
public sealed class NavigationMemoryCleanupService
{
    private const long MinimumManagedHeapGrowthBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumCollectionInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger<NavigationMemoryCleanupService> _logger;
    private int _scheduled;
    private int _collecting;
    private long _lastManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
    private DateTimeOffset _lastCollectionTime = DateTimeOffset.MinValue;

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
                RunOptimizedCollectionIfUseful();
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

    private void RunOptimizedCollectionIfUseful()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastCollectionTime < MinimumCollectionInterval)
        {
            return;
        }

        long currentManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        if (currentManagedHeapBytes - _lastManagedHeapBytes < MinimumManagedHeapGrowthBytes)
        {
            _lastManagedHeapBytes = Math.Min(_lastManagedHeapBytes, currentManagedHeapBytes);
            return;
        }

        GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
        _lastCollectionTime = now;
        _lastManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        _logger.LogDebug(
            "Requested optimized navigation memory maintenance. Managed heap before request: {ManagedHeapBytes} bytes.",
            currentManagedHeapBytes);
    }
}
