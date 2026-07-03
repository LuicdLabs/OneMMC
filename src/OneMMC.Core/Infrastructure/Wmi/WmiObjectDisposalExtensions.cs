using System.Collections.Generic;
using WmiLight;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Helpers for enumerating WmiLight objects while releasing native WMI handles promptly.
/// Mirrors the disposal helper that existed for <c>System.Management</c> before the
/// WmiLight migration (doc/NativeAotMigration.md, M2).
/// </summary>
internal static class WmiObjectDisposalExtensions
{
    /// <summary>
    /// Enumerates a WMI query result and disposes each object after its loop iteration.
    /// </summary>
    public static IEnumerable<WmiObject> DisposeItems(this WmiQuery query)
    {
        foreach (WmiObject item in query)
        {
            using (item)
            {
                yield return item;
            }
        }
    }
}
