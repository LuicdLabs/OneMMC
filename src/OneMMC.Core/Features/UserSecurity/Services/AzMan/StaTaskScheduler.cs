// ============================================================================
// StaTaskScheduler.cs
//
// Single-threaded STA task scheduler for COM interop safety
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed partial class StaTaskScheduler : TaskScheduler, IDisposable
{
    // Unique marker enqueued by Shutdown to wake the consumer thread. Shutting down via
    // BlockingCollection.CompleteAdding() would wake the blocked Take() by cancelling
    // BlockingCollection's internal SemaphoreSlim wait, which raises two first-chance
    // OperationCanceledExceptions. They are caught inside BlockingCollection, but a debugger still
    // reports them and they surface as shutdown noise. Enqueuing an ordinary sentinel item wakes the
    // consumer through the normal path instead, with no exception thrown. The sentinel task is never
    // started or executed — it is only compared by reference.
    private static readonly Task StopSentinel = new(static () => { });

    private readonly BlockingCollection<Task> _tasks = new();
    private readonly object _stateLock = new();
    private readonly Thread _thread;
    private bool _accepting = true;
    private Action? _terminalCleanup;
    private Exception? _terminalException;

    public StaTaskScheduler(string name)
    {
        _thread = new Thread(RunOnCurrentThread)
        {
            IsBackground = true,
            Name = name
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunOnCurrentThread()
    {
        try
        {
            while (true)
            {
                Task task = _tasks.Take();
                if (ReferenceEquals(task, StopSentinel))
                {
                    break;
                }

                TryExecuteTask(task);
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

            _tasks.Dispose();
        }
    }

    protected override IEnumerable<Task> GetScheduledTasks()
    {
        try
        {
            return _tasks.ToArray();
        }
        catch (ObjectDisposedException)
        {
            return Enumerable.Empty<Task>();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the scheduler has been shut down and no longer accepts work.
    /// </summary>
    public bool IsShutdown
    {
        get
        {
            lock (_stateLock)
            {
                return !_accepting;
            }
        }
    }

    protected override void QueueTask(Task task)
    {
        lock (_stateLock)
        {
            // Never accept a task we cannot run. Shutdown takes this same lock before it enqueues the
            // stop sentinel, so every task added here is queued ahead of the sentinel and executes
            // before cleanup.
            if (!_accepting)
            {
                throw new ObjectDisposedException(nameof(StaTaskScheduler));
            }

            _tasks.Add(task);
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        if (Thread.CurrentThread != _thread)
        {
            return false;
        }

        return TryExecuteTask(task);
    }

    /// <summary>
    /// Stops accepting tasks, drains accepted work, and runs terminal cleanup on the STA thread.
    /// </summary>
    /// <param name="terminalCleanup">Cleanup that must run after the final accepted task.</param>
    /// <param name="waitForThread">
    /// Whether to join the STA thread. Finalizers must pass <see langword="false"/>.
    /// </param>
    public void Shutdown(Action? terminalCleanup, bool waitForThread)
    {
        lock (_stateLock)
        {
            if (_accepting)
            {
                _accepting = false;
                _terminalCleanup = terminalCleanup;

                // Wake the consumer with an ordinary queued item rather than CompleteAdding(), which
                // would raise (internally handled) first-chance OperationCanceledExceptions at shutdown.
                _tasks.Add(StopSentinel);
            }
        }

        if (!waitForThread || Thread.CurrentThread == _thread)
        {
            return;
        }

        _thread.Join();
        if (_terminalException is not null)
        {
            throw new AggregateException("The AzMan STA terminal cleanup failed.", _terminalException);
        }
    }

    public void Dispose() => Shutdown(terminalCleanup: null, waitForThread: true);
}


