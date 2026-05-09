using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Forms;
using FileTinder.Models;
using FileTinder.Services;

namespace FileTinder.Views;

/// <summary>
/// Lets the user copy selected MTP folders (e.g. monthly iPhone photo albums)
/// to a local destination with progress feedback.
/// </summary>
public partial class MtpCopyWindow : Window
{
    // ── View model items ───────────────────────────────────────────────────────

    public class FolderItem : INotifyPropertyChanged
    {
        public string Path        { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;

        private long _size = -1;
        private int  _fileCount = -1;

        public long Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(FileCountText)); }
        }

        public int FileCount
        {
            get => _fileCount;
            set { _fileCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileCountText)); }
        }

        public string SizeText      => _size < 0 ? "Calculating…" : FormatSize(_size);
        public string FileCountText => _fileCount < 0 ? "" : (_fileCount == 1 ? "1 file" : $"{_fileCount:N0} files");

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    // ── Fields ─────────────────────────────────────────────────────────────────

    private readonly string _deviceId;
    private List<string>    _sourcePaths;
    private string          _destPath = string.Empty;
    private CancellationTokenSource? _cts;
    private bool _copying;
    private List<string>? _lastCopiedPaths;

    private readonly ObservableCollection<FolderItem> _folders = new();

    // ── Constructor ────────────────────────────────────────────────────────────

    public MtpCopyWindow(string deviceId, string deviceName, List<string> sourcePaths)
    {
        InitializeComponent();
        _deviceId    = deviceId;
        _sourcePaths = sourcePaths;

        DeviceNameText.Text  = $"Device: {deviceName}";
        SourcePathText.Text  = sourcePaths.Count == 1
            ? sourcePaths[0]
            : $"{sourcePaths.Count} folders selected";
        FoldersItemsControl.ItemsSource = _folders;

        Loaded += async (_, _) => await LoadFoldersAsync();
    }

    // ── Folder loading ─────────────────────────────────────────────────────────

    private CancellationTokenSource? _sizeCts;

    private void SetLoadingStatus(string main, string sub = "")
    {
        LoadingText.Text    = main;
        LoadingSubText.Text = sub;
    }

    private async Task LoadFoldersAsync()
    {
        _sizeCts?.Cancel();
        _sizeCts = new CancellationTokenSource();
        var sizeCt = _sizeCts.Token;

        LoadingPanel.Visibility       = Visibility.Visible;
        FolderScrollViewer.Visibility = Visibility.Collapsed;
        EmptyText.Visibility          = Visibility.Collapsed;
        CopyBtn.IsEnabled             = false;
        _folders.Clear();
        SetLoadingStatus("Connecting to device…", "Pausing any active scan");

        // ── Phase 1: build folder list ────────────────────────────────────────
        if (_sourcePaths.Count > 1)
        {
            // Multiple folders already selected — they ARE the items; no lookup needed
            SetLoadingStatus("Loading folders…");
            foreach (var p in _sourcePaths)
            {
                string displayName = System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(displayName)) displayName = p;
                _folders.Add(new FolderItem { Path = p, DisplayName = displayName });
            }
        }
        else
        {
            // Single path — list its sub-folders; if none, the path itself is the leaf
            List<MtpFolderInfo>? infos = null;
            try
            {
                infos = await MtpDeviceService.GetSubfoldersQuickAsync(
                    _deviceId, _sourcePaths[0], sizeCt);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                EmptyText.Text          = $"Could not connect to device:\n{ex.Message}";
                EmptyText.Visibility    = Visibility.Visible;
                return;
            }

            if (infos == null || sizeCt.IsCancellationRequested) return;

            SetLoadingStatus("Loading folders…");
            foreach (var info in infos.OrderByDescending(i => i.Name))
                _folders.Add(new FolderItem { Path = info.Path, DisplayName = info.Name });

            if (_folders.Count == 0)
            {
                // No sub-folders — the selected path IS the leaf
                string leafName = System.IO.Path.GetFileName(_sourcePaths[0].TrimEnd('\\', '/'));
                _folders.Add(new FolderItem
                {
                    Path        = _sourcePaths[0],
                    DisplayName = string.IsNullOrEmpty(leafName) ? _sourcePaths[0] : leafName
                });
            }
        }

        LoadingPanel.Visibility       = Visibility.Collapsed;
        FolderScrollViewer.Visibility = Visibility.Visible;
        UpdateSummary();
        RefreshCopyBtn();

        // ── Phase 2: calculate sizes in the background (non-blocking) ─────────
        var folderMap = _folders.ToDictionary(f => f.Path);

        try
        {
            await MtpDeviceService.CalculateFolderSizesAsync(
                _deviceId,
                folderMap.Keys.ToList(),
                (path, size, count) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (folderMap.TryGetValue(path, out var item))
                        {
                            item.Size      = size;
                            item.FileCount = count;
                            UpdateSummary();
                        }
                    });
                },
                sizeCt);
        }
        catch (OperationCanceledException) { /* user navigated away — fine */ }
        catch { /* size calc failed silently — names already shown */ }
    }

    // ── Source change ──────────────────────────────────────────────────────────

    private async void ChangeSource_Click(object sender, RoutedEventArgs e)
    {
        _sizeCts?.Cancel();
        var win = new MtpFolderInputDialog(_deviceId, _sourcePaths.Count == 1 ? _sourcePaths[0] : "") { Owner = this };
        if (win.ShowDialog() == true && win.SelectedPaths.Count > 0)
        {
            _sourcePaths = win.SelectedPaths;
            _sizeCts = new CancellationTokenSource();
            var sizeCt = _sizeCts.Token;

            // Populate folder list directly from user's selections
            _folders.Clear();
            foreach (var p in win.SelectedPaths)
            {
                var name = System.IO.Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p;
                _folders.Add(new FolderItem { Path = p, DisplayName = name });
            }

            SourcePathText.Text = win.SelectedPaths.Count == 1
                ? win.SelectedPaths[0]
                : $"{win.SelectedPaths.Count} folders selected";

            LoadingPanel.Visibility       = Visibility.Collapsed;
            FolderScrollViewer.Visibility = Visibility.Visible;
            RefreshCopyBtn();

            // Calculate sizes in the background
            var folderMap = _folders.ToDictionary(f => f.Path);
            try
            {
                await MtpDeviceService.CalculateFolderSizesAsync(
                    _deviceId,
                    folderMap.Keys.ToList(),
                    (path, size, count) =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (folderMap.TryGetValue(path, out var item))
                            {
                                item.Size      = size;
                                item.FileCount = count;
                                UpdateSummary();
                            }
                        });
                    },
                    sizeCt);
            }
            catch (OperationCanceledException) { }
            catch { }
        }
    }

    // ── Destination browse ─────────────────────────────────────────────────────

    private void BrowseDest_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Select destination folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (!string.IsNullOrEmpty(_destPath))
            dlg.InitialDirectory = _destPath;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _destPath         = dlg.SelectedPath;
            DestPathText.Text = _destPath;
            RefreshCopyBtn();
        }
    }

    // ── Select all / none ──────────────────────────────────────────────────────

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _folders) f.IsSelected = true;
        UpdateSummary();
        RefreshCopyBtn();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _folders) f.IsSelected = false;
        UpdateSummary();
        RefreshCopyBtn();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void RefreshCopyBtn()
    {
        CopyBtn.IsEnabled = !_copying
            && !string.IsNullOrEmpty(_destPath)
            && _folders.Any(f => f.IsSelected);
    }

    private void UpdateSummary()
    {
        var selected = _folders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SummaryText.Text = "No folders selected";
            return;
        }

        bool anyPending = selected.Any(f => f.Size < 0);
        long totalBytes = selected.Where(f => f.Size >= 0).Sum(f => f.Size);
        int  totalFiles = selected.Where(f => f.FileCount >= 0).Sum(f => f.FileCount);

        SummaryText.Text = $"{selected.Count} folder{(selected.Count == 1 ? "" : "s")} · "
                         + (anyPending ? "calculating size…"
                                       : $"{totalFiles:N0} files · {FormatSize(totalBytes)}");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    // ── Copy ───────────────────────────────────────────────────────────────────

    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedPaths = _folders.Where(f => f.IsSelected).Select(f => f.Path).ToList();
        if (selectedPaths.Count == 0 || string.IsNullOrEmpty(_destPath)) return;

        // Cancel background size-calculation — it holds the device semaphore and
        // would block the copy from starting until ALL folders are scanned.
        _sizeCts?.Cancel();
        _sizeCts = null;

        _copying           = true;
        _cts               = new CancellationTokenSource();
        CopyBtn.IsEnabled  = false;
        CancelBtn.Content  = "Stop";
        ProgressPanel.Visibility        = Visibility.Visible;
        CopyProgressBar.IsIndeterminate = true;
        CopyProgressBar.Value           = 0;
        ProgressLabel.Text              = "Connecting to device…";
        ProgressPctText.Text            = "…";

        var progress = new Progress<CopyProgress>(p =>
        {
            // TotalFiles == -1 is the "enumeration" sentinel — no copying has started yet
            if (p.TotalFiles < 0)
            {
                CopyProgressBar.IsIndeterminate = true;
                ProgressPctText.Text = "…";
                ProgressLabel.Text   = p.FilesCopied > 0
                    ? $"Found {p.FilesCopied:N0} files so far…"
                    : "Scanning device folders…";
                CurrentFileText.Text = p.CurrentFile;
                SpeedText.Text       = string.Empty;
                EtaText.Text         = string.Empty;
                return;
            }

            CopyProgressBar.IsIndeterminate = false;
            double pct = p.TotalFiles == 0 ? 0 : 100.0 * p.FilesCopied / p.TotalFiles;
            CopyProgressBar.Value  = pct;
            ProgressPctText.Text   = $"{pct:F0}%";
            ProgressLabel.Text     = $"{p.FilesCopied:N0} / {p.TotalFiles:N0} files  ·  "
                                   + $"{FormatSize(p.BytesCopied)} / {FormatSize(p.TotalBytes)}";
            CurrentFileText.Text   = p.CurrentFile;

            // Speed
            SpeedText.Text = p.SpeedBps >= 1_048_576
                ? $"{p.SpeedBps / 1_048_576:F1} MB/s"
                : p.SpeedBps >= 1024
                    ? $"{p.SpeedBps / 1024:F0} KB/s"
                    : string.Empty;

            // ETA
            if (p.Eta is { } eta && eta.TotalSeconds > 0)
            {
                EtaText.Text = eta.TotalHours >= 1
                    ? $"Est. {(int)eta.TotalHours}h {eta.Minutes}m remaining"
                    : eta.TotalMinutes >= 1
                        ? $"Est. {(int)eta.TotalMinutes}m {eta.Seconds}s remaining"
                        : $"Est. {eta.Seconds}s remaining";
            }
            else
            {
                EtaText.Text = string.Empty;
            }

            // Error count
            if (p.Errors > 0)
            {
                ErrorCountText.Text       = $"⚠ {p.Errors} file{(p.Errors == 1 ? "" : "s")} skipped due to errors";
                ErrorCountText.Visibility = Visibility.Visible;
            }

            // Live thumbnail for image files
            if (p.LocalPreviewPath != null)
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.DecodePixelWidth  = 128;
                    bmp.CacheOption       = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource         = new Uri(p.LocalPreviewPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    ThumbImage.Source   = bmp;
                    ThumbBorder.Visibility = Visibility.Visible;
                }
                catch { /* non-critical */ }
            }
        });

        try
        {
            bool skipExisting = SkipExistingCheck.IsChecked == true;
            await MtpDeviceService.CopyFoldersAsync(
                _deviceId, selectedPaths, _destPath, skipExisting, progress, _cts.Token);

            CopyProgressBar.IsIndeterminate = false;
            CopyProgressBar.Value = 100;
            ProgressLabel.Text    = "✅ Copy complete!";
            ProgressPctText.Text  = "Done";
            CurrentFileText.Text  = string.Empty;
            SummaryText.Text      = "Copy finished successfully";

            // Show delete-from-device button
            _lastCopiedPaths = selectedPaths;
            DeleteFromDeviceBtn.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            CopyProgressBar.IsIndeterminate = false;
            ProgressLabel.Text   = "⏹ Stopped by user";
            ProgressPctText.Text = string.Empty;
            SummaryText.Text     = "Copy cancelled";
        }
        catch (Exception ex)
        {
            CopyProgressBar.IsIndeterminate = false;
            System.Windows.MessageBox.Show(
                $"Copy failed:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ProgressLabel.Text = "❌ Error — copy incomplete";
        }
        finally
        {
            _copying          = false;
            CancelBtn.Content = "Close";
            RefreshCopyBtn();
        }
    }

    private async void DeleteFromDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCopiedPaths is not { Count: > 0 }) return;

        int count  = _lastCopiedPaths.Count;
        string msg = count == 1
            ? $"Permanently delete '{System.IO.Path.GetFileName(_lastCopiedPaths[0].TrimEnd('\\'))}' from the device?\n\nThis cannot be undone."
            : $"Permanently delete {count} folders from the device?\n\nThis cannot be undone.";

        var result = System.Windows.MessageBox.Show(
            msg, "Delete from Device",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        DeleteFromDeviceBtn.IsEnabled = false;
        ProgressLabel.Text            = "🗑 Deleting from device…";
        ProgressPctText.Text          = "…";
        CopyProgressBar.IsIndeterminate = true;
        CopyProgressBar.Value           = 0;
        ProgressPanel.Visibility        = Visibility.Visible;

        var delProgress = new Progress<string>(msg => ProgressLabel.Text = msg);
        using var cts   = new CancellationTokenSource();

        try
        {
            await MtpDeviceService.DeleteFoldersAsync(
                _deviceId, _lastCopiedPaths, delProgress, cts.Token);

            CopyProgressBar.IsIndeterminate = false;
            CopyProgressBar.Value           = 100;
            ProgressLabel.Text              = "🗑 Deleted from device";
            ProgressPctText.Text            = "Done";
            SummaryText.Text                = "Folders deleted from device";
            // Hide the delete button — nothing left to delete
            DeleteFromDeviceBtn.Visibility  = Visibility.Collapsed;
            _lastCopiedPaths                = null;
        }
        catch (Exception ex)
        {
            CopyProgressBar.IsIndeterminate = false;
            System.Windows.MessageBox.Show(
                $"Delete failed:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ProgressLabel.Text            = "❌ Delete failed";
            DeleteFromDeviceBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_copying)
        {
            _cts?.Cancel();
        }
        else
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_copying)
        {
            var result = System.Windows.MessageBox.Show(
                "A copy is in progress. Stop it and close?",
                "Close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
        base.OnClosing(e);
    }
}
