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
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

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
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    /// <summary>Runs <paramref name="func"/> on the STA thread and returns its result.</summary>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_queue.IsAddingCompleted)
        {
            tcs.SetException(new ObjectDisposedException(nameof(StaComExecutor)));
            return tcs.Task;
        }

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
        return tcs.Task;
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread.</summary>
    public Task RunAsync(Action action) => RunAsync<object?>(() =>
    {
        action();
        return null;
    });

    public void Dispose() => _queue.CompleteAdding();
}
