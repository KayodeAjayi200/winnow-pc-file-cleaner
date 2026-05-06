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

    // Cache / loading-screen state
    private string _currentCacheKey     = string.Empty;
    private bool   _isInitialSourceScan;               // true = first scan of a new source

    // Undo stack (local files only — MTP deletes are permanent)
    private readonly Stack<DeletedEntry> _undoStack = new();
    private const int MaxUndoDepth = 10;

    // MTP state
    private string? _mtpDeviceId;
    private string  _mtpDeviceName = string.Empty;
    private bool    _mtpPermanentDeleteWarningShown;

    // Presets
    private readonly FolderPresetsService _presetsService = new();

    // ── Bindable properties ────────────────────────────────────────────────────

    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => _folderPath;
        set { _folderPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFolder)); }
    }

    private bool _includeSubfolders = true;
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set { _includeSubfolders = value; OnPropertyChanged(); if (HasFolder) LoadFiles(); }
    }

    private SortMode _selectedSortMode = SortMode.LargestFirst;
    public SortMode SelectedSortMode
    {
        get => _selectedSortMode;
        set { _selectedSortMode = value; OnPropertyChanged(); if (HasFolder) LoadFiles(); }
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

    // ── Initial loading state / cache banner ────────────────────────────────────

    /// <summary>True while scanning a new source for the first time (not a filter change).</summary>
    public bool IsLoadingInitial => _isScanning && _isInitialSourceScan;

    private bool _loadedFromCache;
    public bool LoadedFromCache
    {
        get => _loadedFromCache;
        private set { _loadedFromCache = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCacheBanner)); }
    }

    private string _cacheAgeText = string.Empty;
    public string CacheAgeText
    {
        get => _cacheAgeText;
        private set { _cacheAgeText = value; OnPropertyChanged(); }
    }

    /// <summary>True when files came from cache and the overlay is not showing.</summary>
    public bool ShowCacheBanner => _loadedFromCache && !IsLoadingInitial;

    /// <summary>Friendly name of the current source shown on the loading overlay.</summary>
    public string ScanSourceText => IsMtpSource
        ? $"{_mtpDeviceName} · {(string.IsNullOrEmpty(FolderPath) ? "All files" : System.IO.Path.GetFileName(FolderPath))}"
        : string.IsNullOrEmpty(FolderPath) ? "Selected folder"
            : System.IO.Path.GetFileName(FolderPath) is { Length: > 0 } n ? n : FolderPath;

    // ── Scanning state ─────────────────────────────────────────────────────────

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoadingInitial));
            OnPropertyChanged(nameof(ShowCacheBanner));
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
        ? _filteredTotalSize > 0
            ? $"Scanning… {ScanCount} found · {FormatBytes(_filteredTotalSize)}"
            : $"Scanning… {ScanCount} found"
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

    // ── Undo ───────────────────────────────────────────────────────────────────

    public bool CanUndo => _undoStack.Count > 0 && !IsMtpSource;

    // ── Recently deleted ────────────────────────────────────────────────────────

    public ObservableCollection<DeletedEntry> DeletedEntries { get; } = [];
    public bool HasDeletedEntries => DeletedEntries.Count > 0;

    private bool _showDeletedPanel;
    public bool ShowDeletedPanel
    {
        get => _showDeletedPanel;
        set { _showDeletedPanel = value; OnPropertyChanged(); }
    }

    // ── Recycle Bin size ───────────────────────────────────────────────────────

    private long _recycleBinSize;
    public long RecycleBinSize
    {
        get => _recycleBinSize;
        private set { _recycleBinSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecycleBinSizeFormatted)); OnPropertyChanged(nameof(HasBinContent)); }
    }

    public string RecycleBinSizeFormatted => FormatBytes(_recycleBinSize);
    public bool   HasBinContent           => _recycleBinSize > 0;

    // ── Folder presets ─────────────────────────────────────────────────────────

    public ObservableCollection<FolderPreset> Presets { get; } = [];

    // ── Auto-update ────────────────────────────────────────────────────────────

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { _updateAvailable = value; OnPropertyChanged(); }
    }

    private string _updateVersion = string.Empty;
    public string UpdateVersion
    {
        get => _updateVersion;
        private set { _updateVersion = value; OnPropertyChanged(); }
    }

    private string _updateDownloadUrl = string.Empty;

    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set { _isDownloadingUpdate = value; OnPropertyChanged(); }
    }

    private int _updateDownloadProgress;
    public int UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        private set { _updateDownloadProgress = value; OnPropertyChanged(); }
    }

    public ICommand DismissUpdateCommand { get; private set; } = null!;
    public ICommand InstallUpdateCommand { get; private set; } = null!;

    public async Task CheckForUpdatesAsync()
    {
        var info = await FileTinder.Services.UpdateService.CheckAsync();
        if (info == null) return;
        _updateDownloadUrl = info.DownloadUrl;
        UpdateVersion      = info.Version;
        UpdateAvailable    = true;
    }

    private async void ExecuteInstallUpdate(object? _)
    {
        if (IsDownloadingUpdate) return;
        IsDownloadingUpdate = true;
        try
        {
            var progress = new Progress<int>(p => UpdateDownloadProgress = p);
            await FileTinder.Services.UpdateService.DownloadAndInstallAsync(
                _updateDownloadUrl, progress);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Update failed:\n{ex.Message}", "Update error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally { IsDownloadingUpdate = false; }
    }

    // ── View modes ─────────────────────────────────────────────────────────────

    private ViewMode _activeViewMode = ViewMode.Swipe;
    public ViewMode ActiveViewMode
    {
        get => _activeViewMode;
        set
        {
            _activeViewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSwipeMode));
            OnPropertyChanged(nameof(IsGridMode));
            OnPropertyChanged(nameof(IsListMode));
        }
    }

    public bool IsSwipeMode => ActiveViewMode == ViewMode.Swipe;
    public bool IsGridMode  => ActiveViewMode == ViewMode.Grid;
    public bool IsListMode  => ActiveViewMode == ViewMode.List;

    /// <summary>All currently loaded files — used by grid and list views.</summary>
    public IEnumerable<FileItem> AllFiles => _allFiles;

    // ── Sort options ───────────────────────────────────────────────────────────

    public IReadOnlyList<(SortMode Value, string Label)> SortModes { get; } =
    [
        (SortMode.LargestFirst,     "Largest first"),
        (SortMode.SmallestFirst,    "Smallest first"),
        (SortMode.NewestFirst,      "Newest first"),
        (SortMode.OldestFirst,      "Oldest first"),
        (SortMode.LastAccessedFirst,"Last accessed"),
        (SortMode.JunkScoreFirst,   "🗑 Junk score"),
    ];

    // ── MTP properties ─────────────────────────────────────────────────────────

    private bool _isMtpSource;
    public bool IsMtpSource
    {
        get => _isMtpSource;
        private set { _isMtpSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFolder)); }
    }

    /// <summary>The WPD device ID of the currently loaded MTP source. Null if not MTP.</summary>
    public string? MtpDeviceId   => _mtpDeviceId;
    /// <summary>The friendly name of the device (e.g. "iPhone").</summary>
    public string? MtpDeviceName => _isMtpSource ? _mtpDeviceName : null;
    /// <summary>The MTP folder path currently being browsed (same as FolderPath for MTP).</summary>
    public string? MtpFolderPath => _isMtpSource ? FolderPath : null;

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

    public ICommand BrowseFolderCommand      { get; }
    public ICommand KeepCommand              { get; }
    public ICommand DeleteCommand            { get; }
    public ICommand ReloadCommand            { get; }
    public ICommand RefreshCommand           { get; }
    public ICommand CancelScanCommand        { get; }
    public ICommand ResetStatsCommand        { get; }
    public ICommand StartFreshCommand        { get; }
    public ICommand AddToNewBucketCommand    { get; }
    public ICommand AddToBucketCommand       { get; }
    public ICommand UndoCommand              { get; }
    public ICommand EmptyRecycleBinCommand   { get; }
    public ICommand ToggleDeletedPanelCommand { get; }
    public ICommand RestoreDeletedCommand    { get; }

    public MainViewModel()
    {
        BrowseFolderCommand       = new RelayCommand(BrowseFolder);
        KeepCommand               = new RelayCommand(Keep,   () => HasCurrentFile);
        DeleteCommand             = new RelayCommand(Delete, () => HasCurrentFile);
        ReloadCommand             = new RelayCommand(LoadFiles, () => HasFolder);
        RefreshCommand            = new RelayCommand(RefreshScan, () => HasFolder);
        CancelScanCommand         = new RelayCommand(CancelScan, () => IsScanning);
        ResetStatsCommand         = new RelayCommand(ResetStats);
        StartFreshCommand         = new RelayCommand(StartFresh, () => HasFolder);
        AddToNewBucketCommand     = new RelayCommand(AddToNewBucket, () => HasCurrentFile);
        AddToBucketCommand        = new RelayCommand<ReviewBucket>(AddToExistingBucket, b => b != null && HasCurrentFile);
        UndoCommand               = new RelayCommand(Undo, () => CanUndo);
        EmptyRecycleBinCommand    = new RelayCommand(EmptyRecycleBin, () => HasBinContent);
        ToggleDeletedPanelCommand = new RelayCommand(() => ShowDeletedPanel = !ShowDeletedPanel);
        RestoreDeletedCommand     = new RelayCommand<DeletedEntry>(RestoreDeletedEntry);
        DismissUpdateCommand      = new RelayCommand(() => UpdateAvailable = false);
        InstallUpdateCommand      = new RelayCommand(() => ExecuteInstallUpdate(null));

        // Load presets
        foreach (var p in _presetsService.Load())
            Presets.Add(p);

        // Check recycle bin size
        RefreshRecycleBinSize();

        Buckets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuckets));
        DeletedEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDeletedEntries));

        // When a download or backup needs sole access to the MTP device, cancel the scan first
        MtpDeviceService.BeforeExclusiveDeviceAccess += async () =>
        {
            if (IsScanning && _scanCts != null)
            {
                _scanCts.Cancel();
                await Task.Delay(600); // give STA thread time to see cancellation
            }
        };
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

        // ── Source-change detection ─────────────────────────────────────────────
        var newCacheKey    = ScanCacheService.MakeCacheKey(FolderPath, _mtpDeviceId);
        bool isSourceChange = newCacheKey != _currentCacheKey;
        _currentCacheKey   = newCacheKey;

        bool isDefaultFilters = _selectedTypeFilter == FileTypeCategory.All
                             && _selectedDateFilter  == DateFilter.Any;

        // ── Cache hit? Load instantly without a blocking overlay ───────────────
        if (isSourceChange && isDefaultFilters)
        {
            var cached = ScanCacheService.Load(newCacheKey, IsMtpSource);
            if (cached != null)
            {
                _scanCts?.Cancel();
                _isInitialSourceScan = false;
                OnPropertyChanged(nameof(IsLoadingInitial));

                lock (_pendingLock) _pendingFiles.Clear();
                _allFiles.Clear();
                _currentIndex = 0;
                ScanCount     = 0;
                IsScanning    = false;
                ResetStats();

                _allFiles.AddRange(cached.Value.Files);
                ScanCount          = _allFiles.Count;
                _filteredTotalSize = _allFiles.Sum(f => f.Size);
                SortFiles();
                RefreshCurrentCards();

                OnPropertyChanged(nameof(IsComplete));
                OnPropertyChanged(nameof(AllFiles));
                OnPropertyChanged(nameof(QueueInfo));
                OnPropertyChanged(nameof(ScanStatusText));

                LoadedFromCache = true;
                CacheAgeText    = ScanCacheService.FormatAge(cached.Value.ScannedAt);

                if (!IsMtpSource) TryRestoreSession();

                _ = Task.Run(() =>
                {
                    try { DuplicateDetector.MarkDuplicates(_allFiles, CancellationToken.None); }
                    catch { }
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        var tmp = _currentFile; _currentFile = null;
                        OnPropertyChanged(nameof(CurrentFile)); OnPropertyChanged(nameof(HasCurrentFile));
                        _currentFile = tmp;
                        OnPropertyChanged(nameof(CurrentFile)); OnPropertyChanged(nameof(HasCurrentFile));
                    });
                });
                return;
            }
        }

        // ── Cache miss — run a fresh scan ──────────────────────────────────────
        LoadedFromCache      = false;
        CacheAgeText         = string.Empty;
        _isInitialSourceScan = isSourceChange;   // show overlay only for new sources
        OnPropertyChanged(nameof(IsLoadingInitial));

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
        OnPropertyChanged(nameof(AllFiles));

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
        IsScanning           = false;
        _isInitialSourceScan = false;
        OnPropertyChanged(nameof(IsLoadingInitial));

        // Save to cache when using default filters (full unfiltered list)
        if (!ct.IsCancellationRequested && isDefaultFilters && _allFiles.Count > 0)
            ScanCacheService.SaveAsync(newCacheKey, _allFiles.ToList());

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

    /// <summary>Invalidates the cache for the current source and triggers a fresh scan.</summary>
    private void RefreshScan()
    {
        if (!HasFolder) return;
        var key = ScanCacheService.MakeCacheKey(FolderPath, _mtpDeviceId);
        ScanCacheService.Invalidate(key);
        _currentCacheKey = string.Empty;  // force source-change detection
        LoadFiles();
    }

    /// <summary>Cancels an in-progress scan and hides the loading overlay.</summary>
    private void CancelScan()
    {
        _scanCts?.Cancel();
        _isInitialSourceScan = false;
        IsScanning = false;
        OnPropertyChanged(nameof(IsLoadingInitial));
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

        // Sort by selected sort mode
        SortFiles();

        _filteredTotalSize = _allFiles.Sum(f => f.Size);
        OnPropertyChanged(nameof(ScanStatusText));   // re-fire after size is known
        OnPropertyChanged(nameof(QueueInfo));
        OnPropertyChanged(nameof(AllFiles));
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

                // Track for undo and recently deleted panel
                var entry = new DeletedEntry { File = file, DeletedAt = DateTime.Now };
                if (_undoStack.Count >= MaxUndoDepth) { /* pop oldest — Stack doesn't support it, use a list */ }
                PushUndo(entry);
                DeletedEntries.Insert(0, entry);
            }
        }
        Advance();
        SaveSession();
        RefreshRecycleBinSize();
    }

    private void PushUndo(DeletedEntry entry)
    {
        var list = _undoStack.ToList();
        list.Insert(0, entry);
        if (list.Count > MaxUndoDepth) list.RemoveAt(list.Count - 1);
        _undoStack.Clear();
        foreach (var e in list.AsEnumerable().Reverse()) _undoStack.Push(e);
        OnPropertyChanged(nameof(CanUndo));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _undoStack.Pop();
        OnPropertyChanged(nameof(CanUndo));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        bool restored = RecycleBinService.RestoreFromRecycleBin(entry.File.FullPath);
        if (restored)
        {
            FilesDeleted  = Math.Max(0, FilesDeleted - 1);
            SpaceFreed    = Math.Max(0, SpaceFreed - entry.File.Size);
            _filteredTotalSize += entry.File.Size;
            OnPropertyChanged(nameof(FilteredTotalSizeFormatted));

            // Re-insert the file at the current position so it shows up again
            if (_currentIndex > 0) _currentIndex--;
            _allFiles.Insert(_currentIndex, entry.File);
            FilesReviewed = Math.Max(0, FilesReviewed - 1);
            RefreshCurrentCards();
            OnPropertyChanged(nameof(IsComplete));
            DeletedEntries.Remove(entry);
            SaveSession();
            RefreshRecycleBinSize();
            ShowBanner($"↩  Restored: {entry.File.Name}");
        }
        else
        {
            ShowBanner($"⚠  Could not restore {entry.File.Name} — it may have already been emptied.");
        }
    }

    private void RestoreDeletedEntry(DeletedEntry? entry)
    {
        if (entry == null) return;
        bool restored = RecycleBinService.RestoreFromRecycleBin(entry.File.FullPath);
        if (restored)
        {
            DeletedEntries.Remove(entry);
            // Remove from undo stack too
            var list = _undoStack.Where(e => e != entry).ToList();
            _undoStack.Clear();
            foreach (var e in list) _undoStack.Push(e);
            OnPropertyChanged(nameof(CanUndo));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            RefreshRecycleBinSize();
            ShowBanner($"↩  Restored: {entry.File.Name}");
        }
        else
        {
            ShowBanner($"⚠  Could not restore {entry.File.Name}.");
        }
    }

    private void EmptyRecycleBin()
    {
        var result = System.Windows.MessageBox.Show(
            $"Empty the Recycle Bin ({RecycleBinSizeFormatted})?\n\nThis permanently deletes all files in the Recycle Bin.",
            "Empty Recycle Bin",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        RecycleBinService.EmptyRecycleBin();
        _undoStack.Clear();
        DeletedEntries.Clear();
        OnPropertyChanged(nameof(CanUndo));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        RefreshRecycleBinSize();
        ShowBanner("🗑  Recycle Bin emptied");
    }

    private void RefreshRecycleBinSize()
    {
        RecycleBinSize = RecycleBinService.GetRecycleBinSize();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public void PinCurrentFolder()
    {
        if (string.IsNullOrEmpty(FolderPath)) return;
        _presetsService.AddPreset([.. Presets], FolderPath);
        // Reload
        var updated = _presetsService.Load();
        Presets.Clear();
        foreach (var p in updated) Presets.Add(p);
    }

    public void RemovePreset(FolderPreset preset)
    {
        var list = Presets.ToList();
        _presetsService.RemovePreset(list, preset);
        Presets.Remove(preset);
    }

    public void LoadPreset(FolderPreset preset)
    {
        if (!Directory.Exists(preset.Path)) return;
        _mtpDeviceId = null;
        IsMtpSource  = false;
        FolderPath   = preset.Path;
        LoadFiles();
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
        // Assign a rotating accent colour
        var accentColors = new[] { "#7C3AED", "#DB2777", "#0EA5E9", "#16A34A", "#EA580C", "#D97706" };
        var bucket = new ReviewBucket(name)
        {
            AccentColor = accentColors[Buckets.Count % accentColors.Length]
        };

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

        bucket.OnConvertToSubfolder = b => ApplyBucketToFolder(b, null);
        bucket.OnApplyBucket        = b => ApplyBucketToFolder(b, b.DestinationPath);

        bucket.OnSetDestination = b =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description            = "Choose destination folder for this bucket",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
                SelectedPath           = b.DestinationPath ?? FolderPath ?? string.Empty,
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                b.DestinationPath = dialog.SelectedPath;
        };

        Buckets.Add(bucket);
        return bucket;
    }

    private void ApplyBucketToFolder(ReviewBucket b, string? destPath)
    {
        // If no explicit dest, create a subfolder named after the bucket
        if (string.IsNullOrEmpty(destPath))
        {
            if (string.IsNullOrEmpty(FolderPath)) return;
            var safeName = SanitizeFolderName(b.Name);
            if (string.IsNullOrEmpty(safeName)) return;
            destPath = Path.Combine(FolderPath, safeName);
        }

        try
        {
            Directory.CreateDirectory(destPath);
            foreach (var f in b.Files.ToList())
            {
                if (f.IsMtp) continue; // can't move MTP files this way
                var destFile = GetUniqueFilePath(Path.Combine(destPath, Path.GetFileName(f.FullPath)));
                File.Move(f.FullPath, destFile);
                b.Files.Remove(f);
            }
            if (b.Files.Count == 0) Buckets.Remove(b);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not move files:\n{ex.Message}",
                "Winnow", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
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
        _undoStack.Clear();
        DeletedEntries.Clear();
        OnPropertyChanged(nameof(CanUndo));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void SortFiles()
    {
        switch (_selectedSortMode)
        {
            case SortMode.LargestFirst:
                _allFiles.Sort((a, b) => b.Size.CompareTo(a.Size)); break;
            case SortMode.SmallestFirst:
                _allFiles.Sort((a, b) => a.Size.CompareTo(b.Size)); break;
            case SortMode.NewestFirst:
                _allFiles.Sort((a, b) => b.LastModified.CompareTo(a.LastModified)); break;
            case SortMode.OldestFirst:
                _allFiles.Sort((a, b) => a.LastModified.CompareTo(b.LastModified)); break;
            case SortMode.LastAccessedFirst:
                _allFiles.Sort((a, b) => a.LastAccessed.CompareTo(b.LastAccessed)); break;
            case SortMode.JunkScoreFirst:
                _allFiles.Sort((a, b) => b.JunkScore.CompareTo(a.JunkScore)); break;
        }
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

    // ── Grid / List view quick actions ─────────────────────────────────────────

    /// <summary>Delete a file directly from grid/list mode.</summary>
    public void QuickDeleteFile(FileItem f)
    {
        if (!DeleteFileItem(f)) return;

        int idx = _allFiles.IndexOf(f);
        _allFiles.Remove(f);
        if (idx < _currentIndex) _currentIndex = Math.Max(0, _currentIndex - 1);

        FilesDeleted++;
        SpaceFreed += f.Size;
        _filteredTotalSize = Math.Max(0, _filteredTotalSize - f.Size);
        OnPropertyChanged(nameof(FilteredTotalSizeFormatted));

        var entry = new DeletedEntry { File = f, DeletedAt = DateTime.Now };
        PushUndo(entry);
        DeletedEntries.Insert(0, entry);

        OnPropertyChanged(nameof(AllFiles));
        OnPropertyChanged(nameof(QueueInfo));
        OnPropertyChanged(nameof(IsComplete));
        RefreshCurrentCards();
        SaveSession();
        RefreshRecycleBinSize();
    }

    /// <summary>Mark a file as kept (skip) from grid/list mode.</summary>
    public void QuickKeepFile(FileItem f)
    {
        int idx = _allFiles.IndexOf(f);
        _allFiles.Remove(f);
        if (idx < _currentIndex) _currentIndex = Math.Max(0, _currentIndex - 1);

        FilesReviewed++;
        OnPropertyChanged(nameof(AllFiles));
        OnPropertyChanged(nameof(QueueInfo));
        OnPropertyChanged(nameof(IsComplete));
        RefreshCurrentCards();
    }

    /// <summary>Set the current card to the given file (for grid/list selection).</summary>
    public void SelectFile(FileItem f)
    {
        int idx = _allFiles.IndexOf(f);
        if (idx < 0) return;
        _currentIndex = idx;
        RefreshCurrentCards();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

