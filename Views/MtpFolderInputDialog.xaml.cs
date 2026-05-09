using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class MtpFolderInputDialog : Window
{
    // ── Public result ──────────────────────────────────────────────────────────

    /// <summary>All folder paths the user checked (multi-select). Populated on OK.</summary>
    public List<string> SelectedPaths { get; private set; } = [];

    // ── Private state ──────────────────────────────────────────────────────────

    private readonly string _deviceId;
    private string _currentPath = string.Empty;

    // Persists checked state across navigation levels
    private readonly HashSet<string> _checkedPaths = [];

    private readonly List<(string Label, string Path)> _breadcrumb = [];

    // ── FolderEntry (notifies checkbox changes) ─────────────────────────────────

    private class FolderEntry : INotifyPropertyChanged
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class BreadcrumbItem { public string Label { get; init; } = ""; public string Path { get; init; } = ""; }

    // ── Constructor ────────────────────────────────────────────────────────────

    public MtpFolderInputDialog(string deviceId, string startPath = @"\Internal Storage")
    {
        InitializeComponent();
        _deviceId = deviceId;

        // Open at the parent so the user can see siblings and navigate freely
        var parent = GetParentPath(startPath);
        Loaded += async (_, _) => await NavigateToAsync(parent ?? startPath, label: "Device");
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private async Task NavigateToAsync(string path, string? label = null)
    {
        _currentPath = path;
        label ??= System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;

        bool alreadyInCrumb = _breadcrumb.Any(b => b.Path == path);
        if (!alreadyInCrumb)
            _breadcrumb.Add((label, path));
        else
        {
            int idx = _breadcrumb.FindIndex(b => b.Path == path);
            _breadcrumb.RemoveRange(idx + 1, _breadcrumb.Count - idx - 1);
        }

        RefreshBreadcrumb();
        UpBtn.IsEnabled = _breadcrumb.Count > 1;

        LoadingPanel.Visibility      = Visibility.Visible;
        FolderScrollViewer.Visibility = Visibility.Collapsed;
        EmptyLabel.Visibility        = Visibility.Collapsed;

        List<string> subfolders;
        try { subfolders = await MtpDeviceService.GetSubfoldersAsync(_deviceId, path); }
        catch { subfolders = []; }

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (subfolders.Count == 0)
        {
            EmptyLabel.Visibility = Visibility.Visible;
        }
        else
        {
            var items = subfolders
                .Select(p =>
                {
                    var entry = new FolderEntry
                    {
                        Name = System.IO.Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p,
                        Path = p,
                        IsChecked = _checkedPaths.Contains(p)
                    };
                    // Track checkbox toggles across navigation levels
                    entry.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(FolderEntry.IsChecked)) return;
                        if (entry.IsChecked) _checkedPaths.Add(entry.Path);
                        else _checkedPaths.Remove(entry.Path);
                        RefreshFooter();
                    };
                    return entry;
                })
                .OrderBy(f => f.Name)
                .ToList();

            FolderItemsControl.ItemsSource = items;
            FolderScrollViewer.Visibility  = Visibility.Visible;
        }

        RefreshFooter();
    }

    private void RefreshBreadcrumb()
    {
        BreadcrumbControl.ItemsSource = null;
        BreadcrumbControl.ItemsSource = _breadcrumb
            .Select(b => new BreadcrumbItem { Label = b.Label, Path = b.Path })
            .ToList();
    }

    private void RefreshFooter()
    {
        int n = _checkedPaths.Count;
        ConfirmBtn.IsEnabled = n > 0;
        SelectionCountLabel.Text = n == 0
            ? "Check folders to select · › to open"
            : $"{n} folder{(n == 1 ? "" : "s")} selected";
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('\\').TrimEnd('/');
        int sep = trimmed.LastIndexOfAny(['\\', '/']);
        return sep > 0 ? trimmed[..sep] : null;
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private async void BreadcrumbSegment_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is BreadcrumbItem item)
            await NavigateToAsync(item.Path, item.Label);
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_breadcrumb.Count < 2) return;
        var parent = _breadcrumb[^2];
        await NavigateToAsync(parent.Path, parent.Label);
    }

    private async void DrillIn_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is FolderEntry entry)
            await NavigateToAsync(entry.Path, entry.Name);
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedPaths = [.. _checkedPaths];
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
