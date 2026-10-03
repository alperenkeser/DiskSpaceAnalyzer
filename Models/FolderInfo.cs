using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskAlanAnaliz.Models
{
    public class FolderInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public DateTime LastModified { get; set; }
        public string SizeDisplay => FormatSize(Size);
        public string Category
        {
            get
            {
                var path = Path.TrimEnd('\\');
                var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (IsWithin(path, windowsPath) ||
                    HasPathSegment(path, "System Volume Information") ||
                    HasPathSegment(path, "$Recycle.Bin") ||
                    HasPathSegment(path, "Recovery"))
                    return "Windows / Sistem";

                if (ContainsHardwareVendor(path))
                    return "Donanım";

                if (ContainsAny(path, "steamapps", "SteamLibrary", "Epic Games", "XboxGames", "GOG Galaxy", "Battle.net", "Riot Games"))
                    return "Oyunlar";

                if (HasPathSegment(path, "Program Files") ||
                    HasPathSegment(path, "Program Files (x86)") ||
                    HasPathSegment(path, "WindowsApps"))
                    return "Uygulamalar";

                if (IsWithin(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                    return "Kullanıcı verileri";

                return "Diğer";
            }
        }

        public string Importance => Category switch
        {
            "Windows / Sistem" => "Kritik",
            "Donanım" => "Önemli",
            "Oyunlar" => "Oyun",
            "Uygulamalar" => "Uygulama",
            "Kullanıcı verileri" => "Kişisel",
            _ => "Genel"
        };

        public string Tags
        {
            get
            {
                var path = Path;
                return Category switch
                {
                    "Windows / Sistem" => "Windows · Kritik",
                    "Donanım" when HasHardwareMarker(path, "NVIDIA", "GeForce") => "NVIDIA · Donanım",
                    "Donanım" when HasHardwareMarker(path, "AMD", "Ryzen", "Radeon") => "AMD / Ryzen · Donanım",
                    "Donanım" when HasHardwareMarker(path, "Intel") => "Intel · Donanım",
                    "Donanım" => "Sürücü / Donanım",
                    "Oyunlar" => "Oyun",
                    "Uygulamalar" => "Uygulama",
                    "Kullanıcı verileri" => "Kişisel veri",
                    _ => "Genel"
                };
            }
        }

        public int CategoryPriority => Category switch
        {
            "Windows / Sistem" => 0,
            "Donanım" => 1,
            "Oyunlar" => 2,
            "Uygulamalar" => 3,
            "Kullanıcı verileri" => 4,
            _ => 5
        };

        private static bool IsWithin(string path, string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return false;

            var normalizedBase = basePath.TrimEnd('\\');
            return path.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(normalizedBase + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPathSegment(string path, string segment) =>
            path.Split('\\').Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));

        private static bool HasHardwareMarker(string path, params string[] markers) =>
            path.Split('\\').Any(segment => markers.Any(marker =>
                segment.Equals(marker, StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith(marker + " ", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith(marker + "-", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith(marker + "_", StringComparison.OrdinalIgnoreCase)));

        private static bool ContainsAny(string path, params string[] values) =>
            values.Any(value => path.Contains(value, StringComparison.OrdinalIgnoreCase));

        private static bool ContainsHardwareVendor(string path) =>
            HasHardwareMarker(path, "AMD", "Ryzen", "Radeon", "NVIDIA", "GeForce", "Intel");

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