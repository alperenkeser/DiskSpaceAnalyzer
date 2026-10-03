using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskAlanAnaliz.Models
{
    public class CleanupSuggestion
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeDisplay => FormatSize(Size);

        private static string FormatSize(long size)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = Math.Max(0, size);
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:N1} {units[unitIndex]}";
        }
    }
}