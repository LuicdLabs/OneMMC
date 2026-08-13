using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd.Native;

/// <summary>
/// Runs delegates on a single dedicated STA thread and serializes them through a queue. Task
/// Scheduler COM objects are apartment-affine, so every COM call (and the lifetime of the cached
/// <see cref="ITaskService"/>) must stay on one STA thread. This mirrors the STA discipline used by
/// the AzMan service, kept local to the PCManagement feature to respect the no-cross-feature rule.
/// </summary>
internal sealed partial class StaComExecutor : IDisposable
{
    // Unique marker enqueued by Shutdown to wake the consumer thread. Shutting down via
    // BlockingCollection.CompleteAdding() would wake the blocked GetConsumingEnumerable()/Take() by
    // cancelling BlockingCollection's internal SemaphoreSlim wait, which raises two first-chance
    // OperationCanceledExceptions. They are caught inside BlockingCollection, but a debugger still
    // reports them and they surface as shutdown noise. Enqueuing an ordinary sentinel item wakes the
    // consumer through the normal path instead, with no exception thrown.
    private static readonly Action StopSentinel = static () => { };

    private readonly BlockingCollection<Action> _queue = new();
    private readonly object _stateLock = new();
    private readonly Thread _thread;
    private bool _accepting = true;
    private Action? _terminalCleanup;
    private Exception? _terminalException;

    public StaComExecutor(string name)
    {
        _thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ProcessQueue()
    {
        try
        {
            while (true)
            {
                Action work = _queue.Take();
                if (ReferenceEquals(work, StopSentinel))
                {
                    break;
                }

                work();
            }
        }
        finally
        {
            try
            {
                _terminalCleanup?.Invoke();
            }
            catch (Exception ex)
            {
                _terminalException = ex;
            }

            _queue.Dispose();
        }
    }

    /// <summary>Runs <paramref name="func"/> on the STA thread and returns its result.</summary>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
        {
            if (!_accepting)
            {
                tcs.SetException(new ObjectDisposedException(nameof(StaComExecutor)));
                return tcs.Task;
            }

            // Shutdown uses the same lock before adding the stop sentinel, so any work accepted here is
            // guaranteed to be queued ahead of the sentinel and drained before the terminal COM cleanup.
            _queue.Add(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }

        return tcs.Task;
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread.</summary>
    public Task RunAsync(Action action) => RunAsync<object?>(() =>
    {
        action();
        return null;
    });

    /// <summary>
    /// Atomically stops accepting work, drains every accepted delegate, performs the optional
    /// cleanup as the final action on the owning STA thread, and waits for that thread to exit.
    /// </summary>
    public void Shutdown(Action? terminalCleanup)
    {
        lock (_stateLock)
        {
            if (_accepting)
            {
                _accepting = false;
                _terminalCleanup = terminalCleanup;

                // Wake the consumer with an ordinary queued item rather than CompleteAdding(), which
                // would raise (internally handled) first-chance OperationCanceledExceptions at shutdown.
                _queue.Add(StopSentinel);
            }
        }

        if (Thread.CurrentThread == _thread)
        {
            return;
        }

        _thread.Join();
        if (_terminalException is not null)
        {
            throw new AggregateException("The STA terminal cleanup failed.", _terminalException);
        }
    }

    public void Dispose() => Shutdown(terminalCleanup: null);
}
