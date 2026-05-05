using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FileTinder.Models;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class FilePreviewControl : UserControl
{
    // ── Dependency property ────────────────────────────────────────────────────

    public static readonly DependencyProperty FileItemProperty =
        DependencyProperty.Register(
            nameof(FileItem),
            typeof(FileItem),
            typeof(FilePreviewControl),
            new PropertyMetadata(null, OnFileItemChanged));

    public FileItem? FileItem
    {
        get => (FileItem?)GetValue(FileItemProperty);
        set => SetValue(FileItemProperty, value);
    }

    private static void OnFileItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FilePreviewControl)d).UpdatePreview(e.NewValue as FileItem);

    // ── State ──────────────────────────────────────────────────────────────────

    private string? _loadingPath;
    private bool    _videoPlaying;

    // Text extensions we show inline
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".ini", ".cfg", ".json", ".xml",
        ".yaml", ".yml", ".toml", ".html", ".htm", ".css", ".js", ".ts",
        ".cs", ".py", ".java", ".cpp", ".c", ".h", ".rs", ".go", ".rb",
        ".sh", ".bat", ".ps1", ".sql", ".r", ".swift", ".kt", ".dart",
    };

    public FilePreviewControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopVideo();
    }

    // ── Open in default app (called from CardStackControl) ─────────────────────

    public static void OpenInDefaultApp(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not open file:\n{ex.Message}",
                "Open failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// Opens Windows Explorer with the file pre-selected (local files only).
    public static void OpenFileLocation(FileItem file)
    {
        if (file.IsMtp)
        {
            System.Windows.MessageBox.Show(
                "Open file location is not available for files on portable devices.",
                "Not supported",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.FullPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not open file location:\n{ex.Message}",
                "Open location failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>For backwards compat — local files only.</summary>
    public static void OpenFileLocation(string path) =>
        OpenFileLocation(new FileTinder.Models.FileItem { FullPath = path });

    // ── Main update ────────────────────────────────────────────────────────────

    private string? _currentTempFile;

    private void UpdatePreview(FileItem? file)
    {
        StopVideo();
        HideAll();
        _loadingPath = file?.FullPath;

        if (file == null) return;

        if (file.IsMtp)
        {
            LoadMtpPreview(file);
            return;
        }

        switch (file.Category)
        {
            case FileTypeCategory.Image:
                LoadingText.Visibility = Visibility.Visible;
                LoadImageAsync(file.FullPath);
                break;

            case FileTypeCategory.Video:
                LoadVideo(file.FullPath);
                break;

            case FileTypeCategory.Document when TextExtensions.Contains(
                System.IO.Path.GetExtension(file.Name)):
                LoadTextPreview(file.FullPath);
                break;

            default:
                ShowDefaultIcon(file);
                break;
        }
    }

    // ── MTP preview (download to temp, then load normally) ─────────────────────

    private async void LoadMtpPreview(FileItem file)
    {
        if (file.MtpDeviceId == null) { ShowDefaultIcon(file); return; }

        // Only download previewable types
        if (file.Category != FileTypeCategory.Image && file.Category != FileTypeCategory.Video)
        {
            ShowDefaultIcon(file);
            return;
        }

        LoadingText.Visibility = Visibility.Visible;
        var expectedPath = file.FullPath;

        var tempPath = await MtpDeviceService.RunSta(() =>
            MtpDeviceService.DownloadToTemp(file.MtpDeviceId, file.FullPath));

        if (_loadingPath != expectedPath) return; // navigated away

        // Clean previous temp file
        if (_currentTempFile != null && _currentTempFile != tempPath)
        {
            try { System.IO.File.Delete(_currentTempFile); } catch { }
        }
        _currentTempFile = tempPath;

        if (tempPath == null)
        {
            LoadingText.Visibility = Visibility.Collapsed;
            ShowDefaultIcon(file);
            return;
        }

        if (file.Category == FileTypeCategory.Image)
            LoadImageAsync(tempPath);
        else
            LoadVideo(tempPath);
    }

    // ── Image loading (async, freeze for thread safety) ────────────────────────

    private async void LoadImageAsync(string path)
    {
        try
        {
            var bi = await System.Threading.Tasks.Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource         = new Uri(path);
                bmp.DecodePixelWidth  = 360;
                bmp.CacheOption       = BitmapCacheOption.OnLoad;
                bmp.CreateOptions     = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });

            if (_loadingPath != path) return; // stale result

            ImagePreview.Source     = bi;
            ImagePreview.Visibility = Visibility.Visible;
            LoadingText.Visibility  = Visibility.Collapsed;
        }
        catch
        {
            if (_loadingPath != path) return;
            LoadingText.Visibility = Visibility.Collapsed;
            ShowFallbackIcon("🖼", "Can't display image");
        }
    }

    // ── Video: first-frame thumbnail + play/pause ──────────────────────────────

    private void LoadVideo(string path)
    {
        try
        {
            VideoPreview.Source     = new Uri(path);
            VideoPreview.Visibility = Visibility.Visible;
            VideoPreview.Play(); // triggers MediaOpened, which will pause at frame 0
            _videoPlaying = true;
        }
        catch
        {
            ShowDefaultIcon(new FileItem { Category = FileTypeCategory.Video });
        }
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        // Pause immediately to show the first frame as a thumbnail
        VideoPreview.Pause();
        VideoPreview.Position     = TimeSpan.Zero;
        _videoPlaying             = false;
        PlayPauseIcon.Text        = "▶";
        VideoPlayOverlay.Visibility = Visibility.Visible;
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        VideoPreview.Visibility = Visibility.Collapsed;
        ShowFallbackIcon("🎬", "Can't play video");
    }

    private void VideoPlayOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_videoPlaying)
        {
            VideoPreview.Pause();
            _videoPlaying       = false;
            PlayPauseIcon.Text  = "▶";
        }
        else
        {
            VideoPreview.Play();
            _videoPlaying       = true;
            PlayPauseIcon.Text  = "⏸";
        }
    }

    private void StopVideo()
    {
        try
        {
            if (VideoPreview.Source != null)
            {
                VideoPreview.Stop();
                VideoPreview.Source  = null;
            }
        }
        catch { }
        _videoPlaying = false;
    }

    // ── Text preview ───────────────────────────────────────────────────────────

    private void LoadTextPreview(string path)
    {
        try
        {
            var lines = System.IO.File.ReadLines(path).Take(12);
            TextPreviewText.Text       = string.Join("\n", lines);
            TextPreviewBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            ShowFallbackIcon("📄", "Can't read file");
        }
    }

    // ── Fallback icon ──────────────────────────────────────────────────────────

    private void ShowDefaultIcon(FileItem file)
    {
        ShowFallbackIcon(GetIcon(file.Category), file.Category.ToString());
    }

    private void ShowFallbackIcon(string icon, string label)
    {
        DefaultIcon.Text        = icon;
        DefaultLabel.Text       = label;
        DefaultPanel.Visibility = Visibility.Visible;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void HideAll()
    {
        ImagePreview.Source          = null;
        ImagePreview.Visibility      = Visibility.Collapsed;
        VideoPreview.Visibility      = Visibility.Collapsed;
        VideoPlayOverlay.Visibility  = Visibility.Collapsed;
        TextPreviewBorder.Visibility = Visibility.Collapsed;
        DefaultPanel.Visibility      = Visibility.Collapsed;
        LoadingText.Visibility       = Visibility.Collapsed;
    }

    private static string GetIcon(FileTypeCategory cat) => cat switch
    {
        FileTypeCategory.Image    => "🖼",
        FileTypeCategory.Video    => "🎬",
        FileTypeCategory.Audio    => "🎵",
        FileTypeCategory.Document => "📄",
        FileTypeCategory.Archive  => "📦",
        _                         => "📁",
    };
}
