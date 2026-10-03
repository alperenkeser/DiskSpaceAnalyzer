using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskAlanAnaliz.Models
{
    public class DriveInfoModel
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long UsedSpace { get; set; }
        public long FreeSpace { get; set; }
        public double UsagePercentage { get; set; }

        public string TotalSizeDisplay => FormatSize(TotalSize);
        public string UsedSpaceDisplay => FormatSize(UsedSpace);
        public string FreeSpaceDisplay => FormatSize(FreeSpace);

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