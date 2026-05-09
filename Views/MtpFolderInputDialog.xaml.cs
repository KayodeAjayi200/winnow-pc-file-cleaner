using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class MtpFolderInputDialog : Window
{
    // ── Public result ──────────────────────────────────────────────────────────

    public string? SelectedPath { get; private set; }

    // ── Private state ──────────────────────────────────────────────────────────

    private readonly string _deviceId;
    private string _currentPath = string.Empty;

    // Navigation stack: list of (label, path) pairs for the breadcrumb
    private readonly List<(string Label, string Path)> _breadcrumb = [];

    private class FolderEntry { public string Name { get; init; } = ""; public string Path { get; init; } = ""; }

    // ── Constructor ────────────────────────────────────────────────────────────

    public MtpFolderInputDialog(string deviceId, string startPath = @"\Internal Storage")
    {
        InitializeComponent();
        _deviceId = deviceId;
        Loaded += async (_, _) => await NavigateToAsync(startPath, label: "Device");
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private async Task NavigateToAsync(string path, string? label = null)
    {
        _currentPath = path;
        label ??= System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;

        // Update breadcrumb (rebuild from stack or append)
        bool alreadyInCrumb = _breadcrumb.Any(b => b.Path == path);
        if (!alreadyInCrumb)
            _breadcrumb.Add((label, path));
        else
        {
            // Navigated to an ancestor — trim everything after it
            int idx = _breadcrumb.FindIndex(b => b.Path == path);
            _breadcrumb.RemoveRange(idx + 1, _breadcrumb.Count - idx - 1);
        }

        RefreshBreadcrumb();
        CurrentPathLabel.Text = path;
        UpBtn.IsEnabled       = _breadcrumb.Count > 1;

        // Show loading
        LoadingPanel.Visibility  = Visibility.Visible;
        FolderListBox.Visibility = Visibility.Collapsed;
        EmptyLabel.Visibility    = Visibility.Collapsed;

        List<string> subfolders;
        try
        {
            subfolders = await Task.Run(() => MtpDeviceService.GetSubfolders(_deviceId, path));
        }
        catch
        {
            subfolders = [];
        }

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (subfolders.Count == 0)
        {
            EmptyLabel.Visibility = Visibility.Visible;
        }
        else
        {
            var items = subfolders
                .Select(p => new FolderEntry
                {
                    Name = System.IO.Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p,
                    Path = p
                })
                .OrderBy(f => f.Name)
                .ToList();

            FolderListBox.ItemsSource = items;
            FolderListBox.Visibility  = Visibility.Visible;
        }
    }

    private void RefreshBreadcrumb()
    {
        BreadcrumbControl.ItemsSource = null;
        BreadcrumbControl.ItemsSource = _breadcrumb.Select(b => new BreadcrumbItem { Label = b.Label, Path = b.Path }).ToList();
    }

    // ── Breadcrumb item ────────────────────────────────────────────────────────

    public class BreadcrumbItem { public string Label { get; init; } = ""; public string Path { get; init; } = ""; }

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

    private async void FolderList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderListBox.SelectedItem is FolderEntry entry)
            await NavigateToAsync(entry.Path, entry.Name);
    }

    private void FolderList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // No-op — kept for future use
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedPath = _currentPath;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
