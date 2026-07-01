using System.Collections.Generic;
using System.Management;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Helpers for enumerating WMI objects while releasing unmanaged WMI handles promptly.
/// </summary>
internal static class ManagementObjectDisposalExtensions
{
    /// <summary>
    /// Executes a search and disposes the returned collection and each object after its loop iteration.
    /// </summary>
    public static IEnumerable<ManagementObject> GetAndDispose(this ManagementObjectSearcher searcher)
    {
        using var collection = searcher.Get();
        foreach (ManagementObject item in collection.DisposeItems())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Enumerates an existing WMI collection and disposes each object after its loop iteration.
    /// </summary>
    public static IEnumerable<ManagementObject> DisposeItems(this ManagementObjectCollection collection)
    {
        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                yield return item;
            }
        }
    }
}
