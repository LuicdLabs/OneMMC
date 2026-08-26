using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OneMMC.Core.Infrastructure.Collections;

/// <summary>
/// Helpers for updating collections that are already bound to an items control.
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Replaces the contents of <paramref name="target"/> in place, keeping the collection instance
    /// that the UI is already bound to.
    /// </summary>
    /// <remarks>
    /// Always prefer this over assigning a new <see cref="ObservableCollection{T}"/> to a bound
    /// property. Handing an items control a different collection instance makes it discard every item
    /// container and build new element trees, and XAML does not release the native side of the
    /// discarded trees — a forced gen2 collection plus finalizers reclaims almost none of it. Measured
    /// on Device Manager (~22 rows): rebuilding the list by assignment cost ~3.6 MB per rebuild and
    /// never gave it back, while mutating the bound instance measured flat. Mutating lets the control
    /// recycle the containers it already has. See <c>doc/MemoryManagement.md</c>.
    /// </remarks>
    /// <typeparam name="T">The collection's element type.</typeparam>
    /// <param name="target">The bound collection to update.</param>
    /// <param name="items">The new contents. A <see langword="null"/> value clears the collection.</param>
    public static void ReplaceAll<T>(this ObservableCollection<T> target, IEnumerable<T>? items)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Clear();

        if (items is null)
        {
            return;
        }

        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}
