using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DiskAlanAnaliz.Models;
using Microsoft.Win32;

namespace DiskAlanAnaliz.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private string _statusMessage = "Taramaya başlamak için bir sürücü seçin.";
        private bool _isScanning;
        private bool _isCleaning;
        private double _progressValue;
        private ObservableCollection<FolderInfo> _largestFolders = new();
        private ObservableCollection<ApplicationInfo> _installedApplications = new();
        private ObservableCollection<CleanupSuggestion> _cleanupSuggestions = new();
        private DriveInfoModel _driveInfo = new();
        private ObservableCollection<System.IO.DriveInfo> _drives = new();
        private System.IO.DriveInfo? _selectedDrive;
        private FolderInfo? _selectedFolder;
        private CleanupSuggestion? _selectedCleanupSuggestion;
        private ApplicationInfo? _selectedApplication;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly string _scanCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiskAlanAnaliz",
            "scan-cache.json");
        private readonly Dictionary<string, ScanSnapshot> _scanCache;
        private string _lastScanDisplay = "Henüz kayıtlı tarama yok";

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private static long? TryGetScannedFolderSize(
            string installLocation,
            IReadOnlyDictionary<string, long> folderSizes)
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return null;

            try
            {
                return folderSizes.TryGetValue(GetNormalizedPath(installLocation), out var size)
                    ? size
                    : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                return null;
            }
        }

        private static long? ReadEstimatedSize(RegistryKey subKey)
        {
            var estimatedSize = subKey.GetValue("EstimatedSize");
            if (estimatedSize is null ||
                !long.TryParse(
                    Convert.ToString(estimatedSize, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sizeInKilobytes) ||
                sizeInKilobytes < 0)
                return null;

            return sizeInKilobytes > long.MaxValue / 1024
                ? long.MaxValue
                : sizeInKilobytes * 1024;
        }

        private static long? CalculateInstalledFolderSize(
            string installLocation,
            IDictionary<string, long> calculatedSizes)
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return null;

            try
            {
                var normalizedPath = GetNormalizedPath(installLocation);
                if (calculatedSizes.TryGetValue(normalizedPath, out var cachedSize))
                    return cachedSize > 0 ? cachedSize : null;
                if (!Directory.Exists(normalizedPath))
                    return null;

                var size = CalculateDirectorySize(normalizedPath);
                calculatedSizes[normalizedPath] = size;
                return size > 0 ? size : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string GetNormalizedPath(string path) =>
            Path.GetFullPath(path).TrimEnd('\\');

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanCancelScan));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsCleaning
        {
            get => _isCleaning;
            private set
            {
                _isCleaning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanStartScan));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsBusy => IsScanning || IsCleaning;

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public string ScanButtonText => IsCleaning ? "Temizleniyor..." : IsScanning ? "Taranıyor..." : "Taramayı Başlat";

        public string LastScanDisplay
        {
            get => _lastScanDisplay;
            private set { _lastScanDisplay = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FolderInfo> LargestFolders
        {
            get => _largestFolders;
            set { _largestFolders = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ApplicationInfo> InstalledApplications
        {
            get => _installedApplications;
            set { _installedApplications = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CleanupSuggestion> CleanupSuggestions
        {
            get => _cleanupSuggestions;
            set { _cleanupSuggestions = value; OnPropertyChanged(); }
        }

        public DriveInfoModel DriveInfo
        {
            get => _driveInfo;
            set { _driveInfo = value; OnPropertyChanged(); }
        }

        public ObservableCollection<System.IO.DriveInfo> Drives
        {
            get => _drives;
            private set { _drives = value; OnPropertyChanged(); }
        }

        public System.IO.DriveInfo? SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                _selectedDrive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartScan));
                CommandManager.InvalidateRequerySuggested();
                ShowSavedScan(value);
            }
        }

        public FolderInfo? SelectedFolder
        {
            get => _selectedFolder;
            set { _selectedFolder = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public CleanupSuggestion? SelectedCleanupSuggestion
        {
            get => _selectedCleanupSuggestion;
            set { _selectedCleanupSuggestion = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public ApplicationInfo? SelectedApplication
        {
            get => _selectedApplication;
            set { _selectedApplication = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool CanStartScan => !IsScanning && !IsCleaning && SelectedDrive is not null;
        public bool CanCancelScan => IsScanning;

        public ICommand StartScanCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand OpenSelectedFolderCommand { get; }
        public ICommand OpenApplicationFolderCommand { get; }
        public ICommand OpenWindowsAppsSettingsCommand { get; }
        public ICommand CleanSuggestionCommand { get; }

        public MainViewModel()
        {
            _scanCache = LoadScanCache(out var cacheWarning);
            Drives = new ObservableCollection<System.IO.DriveInfo>(
                System.IO.DriveInfo.GetDrives().Where(drive => drive.IsReady));
            SelectedDrive = Drives.FirstOrDefault(drive =>
                string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                ?? Drives.FirstOrDefault();
            if (cacheWarning is not null)
                StatusMessage = cacheWarning;

            StartScanCommand = new RelayCommand(StartScan, () => CanStartScan);
            CancelScanCommand = new RelayCommand(CancelScan, () => CanCancelScan);
            OpenInExplorerCommand = new RelayCommand<string>(OpenInExplorer, path => !string.IsNullOrWhiteSpace(path));
            OpenSelectedFolderCommand = new RelayCommand(
                () => OpenInExplorer(SelectedFolder?.Path),
                () => SelectedFolder is not null);
            OpenApplicationFolderCommand = new RelayCommand(
                OpenSelectedApplicationFolder,
                () => SelectedApplication is not null);
            OpenWindowsAppsSettingsCommand = new RelayCommand(
                OpenWindowsAppsSettings,
                () => SelectedApplication is not null);
            CleanSuggestionCommand = new RelayCommand(CleanSelectedSuggestion, () => SelectedCleanupSuggestion is not null && !IsScanning && !IsCleaning);
        }

        private async void StartScan()
        {
            if (SelectedDrive is null || IsScanning)
                return;

            var selectedDrive = SelectedDrive;
            IsScanning = true;
            OnPropertyChanged(nameof(ScanButtonText));
            ProgressValue = 0;
            LargestFolders.Clear();
            InstalledApplications.Clear();
            CleanupSuggestions.Clear();
            StatusMessage = $"{selectedDrive.Name} sürücüsü taranıyor...";
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                UpdateDriveInfo(selectedDrive);
                var progress = new Progress<string>(message => StatusMessage = message);
                var scanResult = await Task.Run(
                    () => ScanDrive(selectedDrive.RootDirectory.FullName, progress, token));

                SetLargestFolders(scanResult.Folders
                    .OrderByDescending(folder => folder.Size)
                    .Take(100));

                if (!scanResult.Cancelled)
                {
                    StatusMessage = "Yüklü uygulamalar okunuyor ve boyutları hesaplanıyor...";
                    InstalledApplications = new ObservableCollection<ApplicationInfo>(
                        (await Task.Run(() => ReadInstalledApplications(scanResult.Folders)))
                        .OrderByDescending(application => application.Size));
                    CleanupSuggestions = new ObservableCollection<CleanupSuggestion>(
                        await Task.Run(GetCleanupSuggestions));

                    var snapshot = new ScanSnapshot
                    {
                        ScannedAt = DateTimeOffset.Now,
                        Folders = LargestFolders.ToList(),
                        Applications = InstalledApplications.ToList(),
                        CleanupSuggestions = CleanupSuggestions.ToList()
                    };
                    var cacheKey = GetDriveKey(selectedDrive);
                    var hadPreviousSnapshot = _scanCache.TryGetValue(cacheKey, out var previousSnapshot);
                    _scanCache[cacheKey] = snapshot;
                    try
                    {
                        SaveScanCache();
                        LastScanDisplay = FormatLastScan(snapshot.ScannedAt);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        if (hadPreviousSnapshot && previousSnapshot is not null)
                            _scanCache[cacheKey] = previousSnapshot;
                        else
                            _scanCache.Remove(cacheKey);

                        LastScanDisplay = hadPreviousSnapshot && previousSnapshot is not null
                            ? FormatLastScan(previousSnapshot.ScannedAt)
                            : "Bu tarama kaydedilemedi";
                        StatusMessage = $"Tarama tamamlandı ancak sonuçlar kaydedilemedi: {ex.Message}";
                        ProgressValue = 100;
                        return;
                    }
                }

                StatusMessage = scanResult.Cancelled
                    ? $"Tarama iptal edildi. Kısmi sonuçlar gösteriliyor ({scanResult.SkippedItems} öğe atlandı)."
                    : $"Tarama tamamlandı. {LargestFolders.Count} büyük klasör listelendi; {scanResult.SkippedItems} öğe atlandı.";
                ProgressValue = scanResult.Cancelled ? 0 : 100;
                if (scanResult.Cancelled)
                    LastScanDisplay = "Kısmi tarama — kaydedilmedi";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Tarama sırasında hata oluştu: {ex.Message}";
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsScanning = false;
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }

        private void CancelScan()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Tarama durduruluyor...";
        }

        private void UpdateDriveInfo(System.IO.DriveInfo drive)
        {
            DriveInfo = new DriveInfoModel
            {
                DriveLetter = drive.Name.TrimEnd('\\'),
                VolumeName = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Yerel Disk" : drive.VolumeLabel,
                TotalSize = drive.TotalSize,
                UsedSpace = drive.TotalSize - drive.AvailableFreeSpace,
                FreeSpace = drive.AvailableFreeSpace,
                UsagePercentage = drive.TotalSize == 0
                    ? 0
                    : Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100, 1)
            };
        }

        private void SetLargestFolders(IEnumerable<FolderInfo> folders)
        {
            LargestFolders = new ObservableCollection<FolderInfo>(folders);
            var view = CollectionViewSource.GetDefaultView(LargestFolders);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FolderInfo.Category)));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(FolderInfo.CategoryPriority),
                System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(FolderInfo.Size),
                System.ComponentModel.ListSortDirection.Descending));
        }

        private void ShowSavedScan(System.IO.DriveInfo? drive)
        {
            if (drive is null)
                return;

            try
            {
                if (drive.IsReady)
                    UpdateDriveInfo(drive);
            }
            catch (IOException)
            {
                StatusMessage = $"{drive.Name} sürücüsüne erişilemiyor.";
            }

            if (_scanCache.TryGetValue(GetDriveKey(drive), out var snapshot))
            {
                SetLargestFolders(snapshot.Folders);
                InstalledApplications = new ObservableCollection<ApplicationInfo>(
                    snapshot.Applications.OrderByDescending(application => application.Size));
                CleanupSuggestions = new ObservableCollection<CleanupSuggestion>(snapshot.CleanupSuggestions);
                LastScanDisplay = FormatLastScan(snapshot.ScannedAt);
                StatusMessage = $"{drive.Name} için kayıtlı tarama yüklendi. Güncellemek için yeniden taratın.";
            }
            else
            {
                LargestFolders.Clear();
                InstalledApplications.Clear();
                CleanupSuggestions.Clear();
                LastScanDisplay = "Henüz kayıtlı tarama yok";
                StatusMessage = $"{drive.Name} için kayıtlı tarama bulunamadı. Yeni tarama başlatabilirsiniz.";
            }

            SelectedFolder = null;
            SelectedCleanupSuggestion = null;
        }

        private static string GetDriveKey(System.IO.DriveInfo drive) =>
            Path.GetFullPath(drive.RootDirectory.FullName).TrimEnd('\\').ToUpperInvariant();

        private static string FormatLastScan(DateTimeOffset scannedAt) =>
            $"Son tarama: {scannedAt.ToLocalTime():dd.MM.yyyy HH:mm}";

        private Dictionary<string, ScanSnapshot> LoadScanCache(out string? warning)
        {
            warning = null;
            if (!File.Exists(_scanCachePath))
                return new Dictionary<string, ScanSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var document = JsonSerializer.Deserialize<ScanCacheDocument>(File.ReadAllText(_scanCachePath));
                return document?.Scans ?? new Dictionary<string, ScanSnapshot>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warning = $"Önceki tarama kayıtları okunamadı: {ex.Message}";
                return new Dictionary<string, ScanSnapshot>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveScanCache()
        {
            var directory = Path.GetDirectoryName(_scanCachePath)
                ?? throw new IOException("Tarama önbelleği klasörü belirlenemedi.");
            Directory.CreateDirectory(directory);

            var temporaryPath = _scanCachePath + ".tmp";
            var json = JsonSerializer.Serialize(new ScanCacheDocument { Scans = _scanCache });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _scanCachePath, true);
        }

        private static DriveScanResult ScanDrive(string rootPath, IProgress<string> progress, CancellationToken token)
        {
            var folders = new Dictionary<string, FolderInfo>(StringComparer.OrdinalIgnoreCase);
            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var traversalOrder = new List<string>();
            var pending = new Stack<(string Path, string? Parent)>();
            pending.Push((rootPath, null));
            var skipped = 0;
            var visited = 0;

            while (pending.Count > 0 && !token.IsCancellationRequested)
            {
                var (path, parentPath) = pending.Pop();
                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(path);
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    skipped++;
                    continue;
                }

                var folder = new FolderInfo
                {
                    Name = directory.Name,
                    Path = directory.FullName,
                    LastModified = directory.LastWriteTime
                };
                folders[path] = folder;
                traversalOrder.Add(path);
                if (parentPath is not null)
                    parents[path] = parentPath;

                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        try
                        {
                            var file = new System.IO.FileInfo(filePath);
                            folder.Size += file.Length;
                            folder.FileCount++;
                            if (file.LastWriteTime > folder.LastModified)
                                folder.LastModified = file.LastWriteTime;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            skipped++;
                        }
                    }

                    foreach (var childPath in Directory.EnumerateDirectories(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        try
                        {
                            if ((File.GetAttributes(childPath) & FileAttributes.ReparsePoint) == 0)
                                pending.Push((childPath, path));
                            else
                                skipped++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            skipped++;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    skipped++;
                }

                visited++;
                if (visited % 100 == 0)
                    progress.Report($"Taranıyor: {visited:N0} klasör incelendi...");
            }

            for (var index = traversalOrder.Count - 1; index >= 0; index--)
            {
                var path = traversalOrder[index];
                if (!parents.TryGetValue(path, out var parentPath) ||
                    !folders.TryGetValue(parentPath, out var parent) ||
                    !folders.TryGetValue(path, out var child))
                    continue;

                parent.Size += child.Size;
                parent.FileCount += child.FileCount;
                parent.FolderCount += child.FolderCount + 1;
                if (child.LastModified > parent.LastModified)
                    parent.LastModified = child.LastModified;
            }

            return new DriveScanResult(
                folders.Values.Where(folder => !string.Equals(folder.Path, rootPath, StringComparison.OrdinalIgnoreCase)).ToList(),
                token.IsCancellationRequested,
                skipped);
        }

        private static List<ApplicationInfo> ReadInstalledApplications(IEnumerable<FolderInfo> scannedFolders)
        {
            var folderSizes = scannedFolders.ToDictionary(
                folder => GetNormalizedPath(folder.Path),
                folder => folder.Size,
                StringComparer.OrdinalIgnoreCase);
            var calculatedInstallSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var applications = new Dictionary<string, ApplicationInfo>(StringComparer.OrdinalIgnoreCase);
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var registryPath in registryPaths)
            {
                ReadApplicationsFromRegistry(Registry.LocalMachine, registryPath, applications, folderSizes, calculatedInstallSizes);
                ReadApplicationsFromRegistry(Registry.CurrentUser, registryPath, applications, folderSizes, calculatedInstallSizes);
            }

            return applications.Values.OrderBy(application => application.Name).ToList();
        }

        private static void ReadApplicationsFromRegistry(
            RegistryKey hive,
            string registryPath,
            IDictionary<string, ApplicationInfo> applications,
            IReadOnlyDictionary<string, long> folderSizes,
            IDictionary<string, long> calculatedInstallSizes)
        {
            try
            {
                using var key = hive.OpenSubKey(registryPath);
                if (key is null)
                    return;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName")?.ToString();
                        if (subKey is null || string.IsNullOrWhiteSpace(displayName) ||
                            string.Equals(subKey.GetValue("SystemComponent")?.ToString(), "1", StringComparison.Ordinal))
                            continue;

                        var installLocation = subKey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
                        var appSize = TryGetScannedFolderSize(installLocation, folderSizes)
                                      ?? ReadEstimatedSize(subKey)
                                      ?? CalculateInstalledFolderSize(installLocation, calculatedInstallSizes)
                                      ?? 0;

                        var application = new ApplicationInfo
                        {
                            Name = displayName,
                            Publisher = subKey.GetValue("Publisher")?.ToString() ?? "Bilinmiyor",
                            Version = subKey.GetValue("DisplayVersion")?.ToString() ?? "Bilinmiyor",
                            InstallLocation = installLocation,
                            Size = appSize,
                            InstallDate = ParseInstallDate(subKey.GetValue("InstallDate")?.ToString())
                        };

                        if (!applications.TryGetValue(displayName, out var existing) ||
                            (existing.Size == 0 && application.Size > 0))
                            applications[displayName] = application;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        // Ignore individual registry entries that cannot be read.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Continue with the other registry hives and paths.
            }
        }

        private static DateTime ParseInstallDate(string? installDate)
        {
            if (string.IsNullOrWhiteSpace(installDate))
                return DateTime.MinValue;

            var formats = new[] { "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy" };
            return DateTime.TryParseExact(
                installDate,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result)
                ? result
                : DateTime.MinValue;
        }

        private static List<CleanupSuggestion> GetCleanupSuggestions()
        {
            var paths = new List<(string Path, string Reason)>
            {
                (Path.GetTempPath(), "Kullanıcı geçici dosyaları"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), "Windows geçici dosyaları"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome önbelleği"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache"), "Edge önbelleği")
            };

            var suggestions = new List<CleanupSuggestion>();
            foreach (var (path, reason) in paths.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(path))
                    continue;

                try
                {
                    suggestions.Add(new CleanupSuggestion
                    {
                        Path = Path.GetFullPath(path),
                        Reason = reason,
                        Size = CalculateDirectorySize(path)
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Omit directories whose size cannot be inspected.
                }
            }

            return suggestions.OrderByDescending(suggestion => suggestion.Size).ToList();
        }

        private static long CalculateDirectorySize(string path)
        {
            long totalSize = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(current))
                    {
                        try { totalSize += new System.IO.FileInfo(filePath).Length; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                    }

                    foreach (var child in Directory.EnumerateDirectories(current))
                    {
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                                pending.Push(child);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }

            return totalSize;
        }

        private void OpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                StatusMessage = "Klasör bulunamadı veya artık erişilemiyor.";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Klasör açılamadı: {ex.Message}";
            }
        }

        private void OpenSelectedApplicationFolder()
        {
            var application = SelectedApplication;
            if (application is null)
                return;

            if (string.IsNullOrWhiteSpace(application.InstallLocation) ||
                !Directory.Exists(application.InstallLocation))
            {
                StatusMessage = "Bu uygulama için geçerli bir kurulum klasörü bulunamadı.";
                return;
            }

            OpenInExplorer(application.InstallLocation);
        }

        private void OpenWindowsAppsSettings()
        {
            if (SelectedApplication is null)
                return;

            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Windows uygulama ayarları açılamadı: {ex.Message}";
            }
        }

        private async void CleanSelectedSuggestion()
        {
            var suggestion = SelectedCleanupSuggestion;
            if (suggestion is null)
                return;

            var isSuggestedPath = CleanupSuggestions.Any(item =>
                string.Equals(item.Path, suggestion.Path, StringComparison.OrdinalIgnoreCase));
            if (!isSuggestedPath || !Directory.Exists(suggestion.Path))
            {
                StatusMessage = "Temizlenecek klasör bulunamadı veya öneriler güncelliğini yitirdi.";
                return;
            }

            var confirmation = MessageBox.Show(
                $"Aşağıdaki klasörün içeriği silinecek:\n\n{suggestion.Path}\n\nTahmini boyut: {suggestion.SizeDisplay}\n\nDevam etmek istiyor musunuz?",
                "Temizliği Onayla",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            IsCleaning = true;
            OnPropertyChanged(nameof(ScanButtonText));
            StatusMessage = "Seçilen geçici dosyalar temizleniyor...";
            try
            {
                var failures = await Task.Run(() => DeleteDirectoryContents(suggestion.Path));
                CleanupSuggestions = new ObservableCollection<CleanupSuggestion>(await Task.Run(GetCleanupSuggestions));
                SelectedCleanupSuggestion = null;
                StatusMessage = failures == 0
                    ? "Seçilen geçici dosyalar temizlendi."
                    : $"Temizlik tamamlandı ancak {failures} öğe silinemedi (ör. kullanımda veya erişim izni yok).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Temizlik tamamlanamadı: {ex.Message}";
            }
            finally
            {
                IsCleaning = false;
                OnPropertyChanged(nameof(ScanButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static int DeleteDirectoryContents(string rootPath)
        {
            var directories = new List<string>();
            var pending = new Stack<string>();
            var failures = 0;

            try
            {
                if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
                    return 1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return 1;
            }

            pending.Push(rootPath);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                directories.Add(current);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { failures++; }
                    }

                    foreach (var child in Directory.EnumerateDirectories(current))
                    {
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                                pending.Push(child);
                            else
                                failures++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { failures++; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    failures++;
                }
            }

            for (var index = directories.Count - 1; index >= 0; index--)
            {
                if (string.Equals(directories[index], rootPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(directories[index]); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { failures++; }
            }

            return failures;
        }

        private sealed class ScanCacheDocument
        {
            public Dictionary<string, ScanSnapshot> Scans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ScanSnapshot
        {
            public DateTimeOffset ScannedAt { get; set; }
            public List<FolderInfo> Folders { get; set; } = new();
            public List<ApplicationInfo> Applications { get; set; } = new();
            public List<CleanupSuggestion> CleanupSuggestions { get; set; } = new();
        }

        private sealed record DriveScanResult(List<FolderInfo> Folders, bool Cancelled, int SkippedItems);
    }
}
