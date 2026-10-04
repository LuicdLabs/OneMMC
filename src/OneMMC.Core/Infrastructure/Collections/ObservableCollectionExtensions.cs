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

    /// <summary>
    /// Synchronises <paramref name="target"/> to <paramref name="desired"/> in place by applying only
    /// the differences: unchanged items keep their existing instance (no change notification is
    /// raised), reordered items are moved, items whose values changed are replaced, new items are
    /// inserted, and items that disappeared are removed.
    /// </summary>
    /// <remarks>
    /// Use this instead of <see cref="ReplaceAll{T}"/> for lists that are re-read on a live-monitoring
    /// timer. Clearing and refilling the collection makes the items control discard and rebuild every
    /// container on every poll — even when nothing changed — which drops selection and scroll position
    /// and churns memory (see <c>doc/MemoryManagement.md</c>). Reconciling means the UI changes only
    /// where the underlying Windows state actually changed.
    /// </remarks>
    /// <typeparam name="T">The collection's element type.</typeparam>
    /// <param name="target">The bound collection to update in place.</param>
    /// <param name="desired">The desired final contents, in order.</param>
    /// <param name="identityEquals">Returns whether two items represent the same entity.</param>
    /// <param name="valueEquals">Returns whether two same-identity items are visually identical.</param>
    public static void Reconcile<T>(
        this ObservableCollection<T> target,
        IReadOnlyList<T> desired,
        Func<T, T, bool> identityEquals,
        Func<T, T, bool> valueEquals)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(identityEquals);
        ArgumentNullException.ThrowIfNull(valueEquals);

        // Pass 1: remove items no longer present, so a removal is a single Remove rather than a
        // cascade of Move events pulling the survivors forward.
        for (int index = target.Count - 1; index >= 0; index--)
        {
            T current = target[index];
            if (!ContainsIdentity(desired, current, identityEquals))
            {
                target.RemoveAt(index);
            }
        }

        // Pass 2: insert new items, move any that are genuinely out of order, and replace items whose
        // values changed. Unchanged items keep their instance and raise no notification.
        for (int index = 0; index < desired.Count; index++)
        {
            T desiredItem = desired[index];

            int existingIndex = -1;
            for (int search = index; search < target.Count; search++)
            {
                if (identityEquals(target[search], desiredItem))
                {
                    existingIndex = search;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                target.Insert(index, desiredItem);
            }
            else
            {
                if (existingIndex != index)
                {
                    target.Move(existingIndex, index);
                }

                if (!valueEquals(target[index], desiredItem))
                {
                    target[index] = desiredItem;
                }
            }
        }
    }

    private static bool ContainsIdentity<T>(IReadOnlyList<T> items, T candidate, Func<T, T, bool> identityEquals)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (identityEquals(items[index], candidate))
            {
                return true;
            }
        }

        return false;
    }
}
