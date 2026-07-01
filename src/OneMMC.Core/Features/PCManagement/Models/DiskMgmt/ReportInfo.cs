using System;

namespace OneMMC.Core.Features.PCManagement.Models.DiskMgmt
{
    public class ReportInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Size { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public ReportInfo()
        {
        }

        public ReportInfo(string name, string path, DateTime date, long size)
        {
            Name = name;
            Path = path;
            Date = date;
            Size = FormatFileSize(size);
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }
    }
}

