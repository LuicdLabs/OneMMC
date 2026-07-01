namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Common formatting utilities for disk management
    /// </summary>
    public static class FormatHelper
    {
        /// <summary>
        /// Format byte size to human-readable string
        /// </summary>
        public static string FormatSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}


