using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;
using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using Windows.Foundation;

namespace OneMMC.Core.Features.PCManagement.ViewModels.EventViewer;

/// <summary>
/// Observable collection of <see cref="EventLogEntry"/> items that participates in
/// ListView data virtualization through <see cref="ISupportIncrementalLoading"/>.
/// The list control pulls additional batches by itself whenever its viewport is not
/// filled — on initial display, when scrolling near the end, or when a client-side
/// text filter leaves too few visible rows — which replaces manual ScrollViewer
/// offset tracking in the view. Pagination state lives in the owning view model;
/// this collection only forwards pull requests through the supplied delegates.
/// </summary>
public sealed partial class IncrementalEventLogCollection : ObservableCollection<EventLogEntry>, ISupportIncrementalLoading
{
    private readonly Func<bool> _hasMoreItems;
    private readonly Func<CancellationToken, Task<int>> _fetchMoreAsync;

    /// <param name="hasMoreItems">Returns whether another batch can be requested.</param>
    /// <param name="fetchMoreAsync">
    /// Fetches the next batch and returns how many items were appended to this collection.
    /// </param>
    public IncrementalEventLogCollection(Func<bool> hasMoreItems, Func<CancellationToken, Task<int>> fetchMoreAsync)
    {
        _hasMoreItems = hasMoreItems;
        _fetchMoreAsync = fetchMoreAsync;
    }

    /// <inheritdoc />
    public bool HasMoreItems => _hasMoreItems();

    /// <inheritdoc />
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        // The count argument is a viewport-based hint; batch size is fixed by the
        // pagination cursor in the view model, so the hint is intentionally ignored.
        return AsyncInfo.Run(async ct =>
        {
            var appended = await _fetchMoreAsync(ct);
            return new LoadMoreItemsResult { Count = (uint)appended };
        });
    }

    /// <summary>
    /// Appends items one by one so the ListView extends in place, preserving the
    /// current scroll position and selection.
    /// </summary>
    public void AppendRange(IEnumerable<EventLogEntry> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    /// <summary>
    /// Replaces the entire content with a single reset notification. Intended for
    /// filter or log changes, where returning the view to the top is the desired outcome.
    /// </summary>
    public void ResetWith(IEnumerable<EventLogEntry> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
