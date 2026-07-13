using System.Collections.Generic;
using WmiLight;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Helpers for enumerating WmiLight objects while releasing native WMI handles promptly.
/// Mirrors the disposal helper that existed for <c>System.Management</c> before the
/// repository standardized on WmiLight for Native AOT-safe WMI access
/// (see <c>doc/NativeAot.md</c>, "WMI and CIM").
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
