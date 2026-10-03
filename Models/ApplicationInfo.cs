using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskAlanAnaliz.Models
{
    public class ApplicationInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public long Size { get; set; } // Size in bytes, -1 if not available
        public string InstallLocation { get; set; } = string.Empty;
        public string InstallDrive => GetInstallDrive(InstallLocation);
        public DateTime InstallDate { get; set; }
        public string SizeDisplay => Size > 0 ? FormatSize(Size) : "Bilinmiyor";

        private static string GetInstallDrive(string installLocation)
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return "?";

            try
            {
                var root = System.IO.Path.GetPathRoot(installLocation)?.TrimEnd('\\');
                return string.IsNullOrWhiteSpace(root) ? "?" : root;
            }
            catch (ArgumentException)
            {
                return "?";
            }
        }

        private static string FormatSize(long size)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = size;
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