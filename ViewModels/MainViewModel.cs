using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using FileTinder.Models;
using FileTinder.Services;

namespace FileTinder.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ── Internal state ─────────────────────────────────────────────────────────
    private readonly List<FileItem> _allFiles    = [];
    private readonly List<FileItem> _pendingFiles = [];
    private readonly object         _pendingLock  = new();
    private int                     _currentIndex;
    private int                     _bucketCounter = 1;
    private long                    _filteredTotalSize;   // decremented on each confirmed delete
    private CancellationTokenSource? _scanCts;
    private DispatcherTimer?         _scanTimer;
    private DispatcherTimer?         _bannerTimer;

    // MTP state
    private string? _mtpDeviceId;
    private string  _mtpDeviceName = string.Empty;
    private bool    _mtpPermanentDeleteWarningShown;

    // ── Bindable properties ────────────────────────────────────────────────────

    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => _folderPath;
        set { _folderPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFolder)); }
    }

    private bool _includeSubfolders;
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set { _includeSubfolders = value; OnPropertyChanged(); if (HasFolder) LoadFiles(); }
    }

    private FileTypeCategory _selectedTypeFilter = FileTypeCategory.All;
    public FileTypeCategory SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set { _selectedTypeFilter = value; OnPropertyChanged(); if (HasFolder) LoadFiles(); }
    }

    private DateFilter _selectedDateFilter = DateFilter.Any;
    public DateFilter SelectedDateFilter
    {
        get => _selectedDateFilter;
        set { _selectedDateFilter = value; OnPropertyChanged(); if (HasFolder) LoadFiles(); }
    }

    private FileItem? _currentFile;
    public FileItem? CurrentFile
    {
        get => _currentFile;
        private set
        {
            _currentFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentFile));
            OnPropertyChanged(nameof(QueueInfo));
        }
    }

    private FileItem? _nextFile1;
    public FileItem? NextFile1
    {
        get => _nextFile1;
        private set { _nextFile1 = value; OnPropertyChanged(); }
    }

    private FileItem? _nextFile2;
    public FileItem? NextFile2
    {
        get => _nextFile2;
        private set { _nextFile2 = value; OnPropertyChanged(); }
    }

    // ── Session banner ─────────────────────────────────────────────────────────

    private string _sessionBanner = string.Empty;
    public string SessionBanner
    {
        get => _sessionBanner;
        private set { _sessionBanner = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSessionBanner)); }
    }
    public bool HasSessionBanner => !string.IsNullOrEmpty(_sessionBanner);

    // ── Scanning state ─────────────────────────────────────────────────────────

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScanStatusText));
            OnPropertyChanged(nameof(QueueInfo));
        }
    }

    private int _scanCount;
    public int ScanCount
    {
        get => _scanCount;
        private set { _scanCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScanStatusText)); OnPropertyChanged(nameof(QueueInfo)); }
    }

    public string ScanStatusText => IsScanning
        ? $"Scanning… {ScanCount} found"
        : string.Empty;

    // ── Stats ──────────────────────────────────────────────────────────────────

    private int _filesReviewed;
    public int FilesReviewed
    {
        get => _filesReviewed;
        private set { _filesReviewed = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(QueueInfo)); }
    }

    private int _filesKept;
    public int FilesKept
    {
        get => _filesKept;
        private set { _filesKept = value; OnPropertyChanged(); }
    }

    private int _filesDeleted;
    public int FilesDeleted
    {
        get => _filesDeleted;
        private set { _filesDeleted = value; OnPropertyChanged(); }
    }

    private long _spaceFreed;
    public long SpaceFreed
    {
        get => _spaceFreed;
        private set { _spaceFreed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpaceFreedFormatted)); }
    }

    private int _filesBucketed;
    public int FilesBucketed
    {
        get => _filesBucketed;
        private set { _filesBucketed = value; OnPropertyChanged(); }
    }

    public string SpaceFreedFormatted => FormatBytes(_spaceFreed);

    // Total size of files matching current filters, decremented on confirmed deletes
    public string FilteredTotalSizeFormatted => FormatBytes(_filteredTotalSize);

    public double Progress => _allFiles.Count == 0 ? 0 : (double)_filesReviewed / _allFiles.Count;

    public string QueueInfo
    {
        get
        {
            if (_allFiles.Count == 0)
                return IsScanning ? $"Scanning… {_scanCount} files found" : "No files loaded";
            var remaining = _allFiles.Count - _filesReviewed;
            return $"{_filesReviewed} / {_allFiles.Count} reviewed · {remaining} left";
        }
    }

    public bool HasCurrentFile => CurrentFile != null;
    public bool HasFolder      => IsMtpSource
        || (!string.IsNullOrWhiteSpace(FolderPath) && Directory.Exists(FolderPath));
    public bool IsComplete     => _allFiles.Count > 0 && _currentIndex >= _allFiles.Count;

    // ── MTP properties ─────────────────────────────────────────────────────────

    private bool _isMtpSource;
    public bool IsMtpSource
    {
        get => _isMtpSource;
        private set { _isMtpSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFolder)); }
    }

    // ── Buckets ────────────────────────────────────────────────────────────────

    public ObservableCollection<ReviewBucket> Buckets { get; } = [];

    public bool HasBuckets => Buckets.Count > 0;

    // ── Filter option lists ────────────────────────────────────────────────────

    public IReadOnlyList<(FileTypeCategory Value, string Label)> TypeFilters { get; } =
    [
        (FileTypeCategory.All,      "All Files"),
        (FileTypeCategory.Image,    "🖼 Images"),
        (FileTypeCategory.Video,    "🎬 Videos"),
        (FileTypeCategory.Document, "📄 Documents"),
        (FileTypeCategory.Audio,    "🎵 Audio"),
        (FileTypeCategory.Archive,  "📦 Archives"),
        (FileTypeCategory.Other,    "📁 Other"),
    ];

    public IReadOnlyList<(DateFilter Value, string Label)> DateFilters { get; } =
    [
        (DateFilter.Any,         "Any time"),
        (DateFilter.Last7Days,   "Last 7 days"),
        (DateFilter.Last30Days,  "Last 30 days"),
        (DateFilter.Last6Months, "Last 6 months"),
        (DateFilter.LastYear,    "Last year"),
    ];

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand BrowseFolderCommand   { get; }
    public ICommand KeepCommand           { get; }
    public ICommand DeleteCommand         { get; }
    public ICommand ReloadCommand         { get; }
    public ICommand ResetStatsCommand     { get; }
    public ICommand StartFreshCommand     { get; }
    public ICommand AddToNewBucketCommand { get; }
    public ICommand AddToBucketCommand    { get; }

    public MainViewModel()
    {
        BrowseFolderCommand   = new RelayCommand(BrowseFolder);
        KeepCommand           = new RelayCommand(Keep,   () => HasCurrentFile);
        DeleteCommand         = new RelayCommand(Delete, () => HasCurrentFile);
        ReloadCommand         = new RelayCommand(LoadFiles, () => HasFolder);
        ResetStatsCommand     = new RelayCommand(ResetStats);
        StartFreshCommand     = new RelayCommand(StartFresh, () => HasFolder);
        AddToNewBucketCommand = new RelayCommand(AddToNewBucket, () => HasCurrentFile);
        AddToBucketCommand    = new RelayCommand<ReviewBucket>(AddToExistingBucket, b => b != null && HasCurrentFile);

        Buckets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuckets));
    }

    // ── Actions ────────────────────────────────────────────────────────────────

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description            = "Select a folder to clean up",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            FolderPath = dialog.SelectedPath;
            LoadFiles();
        }
    }

    public async void LoadFiles()
    {
        if (!HasFolder) return;

        // Cancel any in-progress scan
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        lock (_pendingLock) _pendingFiles.Clear();
        _allFiles.Clear();
        _currentIndex = 0;
        ScanCount     = 0;
        IsScanning    = true;
        ResetStats();
        RefreshCurrentCards();
        OnPropertyChanged(nameof(IsComplete));

        // Flush discovered files to the sorted list every 250ms
        _scanTimer?.Stop();
        _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(250) };
        _scanTimer.Tick += (_, _) => FlushPendingFiles();
        _scanTimer.Start();

        try
        {
            if (IsMtpSource && _mtpDeviceId != null)
            {
                await MtpDeviceService.ScanAsync(
                    _mtpDeviceId, FolderPath,
                    IncludeSubfolders, SelectedTypeFilter, SelectedDateFilter,
                    item =>
                    {
                        lock (_pendingLock) _pendingFiles.Add(item);
                    },
                    ct);
            }
            else
            {
                await FileScanner.ScanStreamingAsync(
                    FolderPath, SelectedTypeFilter, SelectedDateFilter, IncludeSubfolders,
                    item =>
                    {
                        lock (_pendingLock)
                            _pendingFiles.Add(item);
                    },
                    ct);
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            _scanTimer.Stop();
        }

        FlushPendingFiles(); // final flush
        IsScanning = false;

        // Restore session (only for local folders)
        if (!IsMtpSource) TryRestoreSession();

        // Run duplicate detection in background without blocking swiping
        _ = Task.Run(() =>
        {
            try
            {
                DuplicateDetector.MarkDuplicates(_allFiles, ct);

                // Force current card to re-bind its duplicate badge
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var tmp = _currentFile;
                    _currentFile = null;
                    OnPropertyChanged(nameof(CurrentFile));
                    OnPropertyChanged(nameof(HasCurrentFile));
                    _currentFile = tmp;
                    OnPropertyChanged(nameof(CurrentFile));
                    OnPropertyChanged(nameof(HasCurrentFile));
                });
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    /// <summary>Load files from a connected MTP device.</summary>
    public void LoadMtpFiles(string deviceId, string folderPath, string deviceName)
    {
        _mtpDeviceId   = deviceId;
        _mtpDeviceName = deviceName;
        IsMtpSource    = true;
        FolderPath     = folderPath;
        LoadFiles();
    }

    private void FlushPendingFiles()
    {
        List<FileItem> batch;
        lock (_pendingLock)
        {
            if (_pendingFiles.Count == 0) return;
            batch = new List<FileItem>(_pendingFiles);
            _pendingFiles.Clear();
        }

        _allFiles.AddRange(batch);
        ScanCount = _allFiles.Count;

        // Sort descending by size — fast even for 100k files
        _allFiles.Sort((a, b) => b.Size.CompareTo(a.Size));

        _filteredTotalSize = _allFiles.Sum(f => f.Size);
        OnPropertyChanged(nameof(QueueInfo));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(FilteredTotalSizeFormatted));
        RefreshCurrentCards();
    }

    public void Keep()
    {
        if (CurrentFile == null) return;
        FilesKept++;
        Advance();
        SaveSession();
    }

    public void Delete()
    {
        if (CurrentFile == null) return;
        var file = CurrentFile;

        if (file.IsMtp && file.MtpDeviceId != null)
        {
            // MTP: permanent delete — warn user first
            if (!_mtpPermanentDeleteWarningShown)
            {
                var result = System.Windows.MessageBox.Show(
                    $"⚠️  Files on \"{_mtpDeviceName}\" will be permanently deleted.\n" +
                    "There is no Recycle Bin for portable devices.\n\n" +
                    "Do you want to continue?",
                    "Permanent Delete Warning",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;  // don't advance, let user reconsider

                _mtpPermanentDeleteWarningShown = true;
            }

            if (MtpDeviceService.DeleteFile(file.MtpDeviceId, file.FullPath))
            {
                FilesDeleted++;
                SpaceFreed += file.Size;
                _filteredTotalSize = Math.Max(0, _filteredTotalSize - file.Size);
                OnPropertyChanged(nameof(FilteredTotalSizeFormatted));
            }
        }
        else
        {
            // Local: send to Recycle Bin
            if (RecycleBinService.SendToRecycleBin(file.FullPath))
            {
                FilesDeleted++;
                SpaceFreed += file.Size;
                _filteredTotalSize = Math.Max(0, _filteredTotalSize - file.Size);
                OnPropertyChanged(nameof(FilteredTotalSizeFormatted));
            }
        }
        Advance();
        SaveSession();
    }

    public void AddToNewBucket(string name)
    {
        if (CurrentFile == null) return;
        var bucket = CreateBucket(name);
        AddToExistingBucket(bucket);
        bucket.IsExpanded = true;
    }

    public void AddToNewBucket()
    {
        if (CurrentFile == null) return;
        var bucket = CreateBucket($"Bucket {_bucketCounter++}");
        AddToExistingBucket(bucket);
        bucket.IsExpanded = true;
    }

    public ReviewBucket CreateBucket(string name)
    {
        var bucket = new ReviewBucket(name);

        bucket.OnDeleteFile = (b, f) =>
        {
            if (DeleteFileItem(f))
            {
                FilesDeleted++;
                SpaceFreed += f.Size;
            }
            b.Files.Remove(f);
            if (b.Files.Count == 0)
                Buckets.Remove(b);
        };

        bucket.OnKeepFile = (b, f) =>
        {
            FilesKept++;
            b.Files.Remove(f);
            if (b.Files.Count == 0)
                Buckets.Remove(b);
        };

        bucket.OnDeleteBucket = b =>
        {
            foreach (var f in b.Files.ToList())
            {
                if (DeleteFileItem(f))
                {
                    FilesDeleted++;
                    SpaceFreed += f.Size;
                }
            }
            Buckets.Remove(b);
        };

        bucket.OnConvertToSubfolder = b =>
        {
            if (string.IsNullOrEmpty(FolderPath)) return;
            var safeName = SanitizeFolderName(b.Name);
            if (string.IsNullOrEmpty(safeName)) return;
            var destDir = Path.Combine(FolderPath, safeName);
            try
            {
                Directory.CreateDirectory(destDir);
                foreach (var f in b.Files.ToList())
                {
                    var destFile = GetUniqueFilePath(Path.Combine(destDir, Path.GetFileName(f.FullPath)));
                    File.Move(f.FullPath, destFile);
                    b.Files.Remove(f);
                }
                Buckets.Remove(b);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not convert bucket to subfolder:\n{ex.Message}",
                    "FileTinder", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        };

        Buckets.Add(bucket);
        return bucket;
    }

    public void AddToExistingBucket(ReviewBucket? bucket)
    {
        if (CurrentFile == null || bucket == null) return;
        var file = CurrentFile;
        if (!bucket.Files.Contains(file))
            bucket.Files.Add(file);
        FilesBucketed++;
        Advance();
    }

    private void TryRestoreSession()
    {
        var session = SessionService.Load(FolderPath);
        if (session == null) return;

        // Only restore if the filters still match
        if (session.TypeFilter        != SelectedTypeFilter.ToString() ||
            session.DateFilter        != SelectedDateFilter.ToString() ||
            session.IncludeSubfolders != IncludeSubfolders)
            return;

        var restoredIndex = Math.Min(session.CurrentIndex, _allFiles.Count);
        if (restoredIndex == 0) return; // nothing to restore

        _currentIndex = restoredIndex;
        _filesReviewed = session.FilesReviewed;
        _filesKept     = session.FilesKept;
        _filesDeleted  = session.FilesDeleted;
        _spaceFreed    = session.SpaceFreed;

        // Fire property changed for all stats
        OnPropertyChanged(nameof(FilesReviewed));
        OnPropertyChanged(nameof(FilesKept));
        OnPropertyChanged(nameof(FilesDeleted));
        OnPropertyChanged(nameof(SpaceFreed));
        OnPropertyChanged(nameof(SpaceFreedFormatted));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(QueueInfo));
        OnPropertyChanged(nameof(IsComplete));

        RefreshCurrentCards();

        ShowBanner($"▶  Resumed — {restoredIndex} of {_allFiles.Count} already reviewed");
    }

    private void SaveSession()
    {
        if (IsMtpSource) return;  // MTP sessions aren't persistent
        if (!HasFolder) return;
        SessionService.Save(new SessionState(
            FolderPath:        FolderPath,
            CurrentIndex:      _currentIndex,
            TypeFilter:        SelectedTypeFilter.ToString(),
            DateFilter:        SelectedDateFilter.ToString(),
            IncludeSubfolders: IncludeSubfolders,
            FilesReviewed:     _filesReviewed,
            FilesKept:         _filesKept,
            FilesDeleted:      _filesDeleted,
            SpaceFreed:        _spaceFreed
        ));
    }

    private void ShowBanner(string message)
    {
        _bannerTimer?.Stop();
        SessionBanner = message;
        _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bannerTimer.Tick += (_, _) =>
        {
            _bannerTimer.Stop();
            SessionBanner = string.Empty;
        };
        _bannerTimer.Start();
    }

    private void Advance()
    {
        FilesReviewed++;
        _currentIndex++;
        RefreshCurrentCards();
        OnPropertyChanged(nameof(IsComplete));
    }

    private void RefreshCurrentCards()
    {
        CurrentFile = _currentIndex     < _allFiles.Count ? _allFiles[_currentIndex]     : null;
        NextFile1   = _currentIndex + 1 < _allFiles.Count ? _allFiles[_currentIndex + 1] : null;
        NextFile2   = _currentIndex + 2 < _allFiles.Count ? _allFiles[_currentIndex + 2] : null;
    }

    private void ResetStats()
    {
        FilesReviewed = 0;
        FilesKept     = 0;
        FilesDeleted  = 0;
        SpaceFreed    = 0;
        FilesBucketed = 0;
    }

    private void StartFresh()
    {
        if (!HasFolder) return;
        SessionService.Clear(FolderPath);
        LoadFiles();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return sanitized.Length == 0 ? "Bucket" : sanitized;
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir  = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext  = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Deletes a file, routing to MTP permanent delete or local Recycle Bin as appropriate.
    /// Returns true if the delete succeeded.
    /// </summary>
    private bool DeleteFileItem(FileItem f)
    {
        if (f.IsMtp && f.MtpDeviceId != null)
            return MtpDeviceService.DeleteFile(f.MtpDeviceId, f.FullPath);
        return RecycleBinService.SendToRecycleBin(f.FullPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
