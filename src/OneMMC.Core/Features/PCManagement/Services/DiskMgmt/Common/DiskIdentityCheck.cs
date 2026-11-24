using OneMMC.Core.Infrastructure.Wmi;
using WmiLight;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Confirms that a disk or partition found by number is the one the user saw when choosing an operation.
    /// </summary>
    /// <remarks>
    /// Destructive operations address disks by <c>MSFT_Disk.Number</c> and partitions by
    /// <c>PartitionNumber</c>. Both can be reassigned between the refresh that produced the view and the
    /// operation: a USB disk is plugged in or removed, or another tool (diskpart, PowerShell, diskmgmt.msc)
    /// deletes or creates a partition. Checking the disk's <c>UniqueId</c> and the partition's starting
    /// offset immediately before the call keeps a stale view from deleting or wiping the wrong data.
    /// A missing expected value skips that check.
    /// </remarks>
    internal static class DiskIdentityCheck
    {
        /// <summary>Returns true when <paramref name="msftDisk"/> has the expected <c>UniqueId</c>.</summary>
        public static bool DiskMatches(WmiObject msftDisk, string? expectedUniqueId) =>
            string.IsNullOrEmpty(expectedUniqueId)
            || string.Equals(msftDisk.GetPropertySafe<string>("UniqueId"), expectedUniqueId, StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true when <paramref name="msftPartition"/> starts at the expected byte offset.</summary>
        public static bool PartitionMatches(WmiObject msftPartition, ulong? expectedOffset) =>
            expectedOffset is null || msftPartition.GetPropertySafe<ulong>("Offset") == expectedOffset.Value;
    }
}
