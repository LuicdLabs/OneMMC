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
    private readonly BlockingCollection<Task> _tasks = new();
    private readonly Thread _thread;

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
        foreach (var task in _tasks.GetConsumingEnumerable())
        {
            TryExecuteTask(task);
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
    public bool IsShutdown => _tasks.IsAddingCompleted;

    protected override void QueueTask(Task task)
    {
        // Never accept a task we cannot run: the scheduler contract gives us no way to complete a
        // Task we decline, so silently dropping it leaves the caller's await pending forever. Throwing
        // instead surfaces the shutdown as a faulted task, which callers can observe and handle.
        bool queued;
        try
        {
            queued = !_tasks.IsAddingCompleted && _tasks.TryAdd(task);
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding() raced with this add.
            queued = false;
        }

        if (!queued)
        {
            throw new ObjectDisposedException(nameof(StaTaskScheduler));
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

    public void Dispose()
    {
        _tasks.CompleteAdding();
    }
}


