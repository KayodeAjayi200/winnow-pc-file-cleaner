using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class MtpDevicePickerWindow : Window
{
    // ── Public results ─────────────────────────────────────────────────────────
    public string?       SelectedDeviceId   { get; private set; }
    public string?       SelectedDeviceName { get; private set; }
    public List<string>  SelectedFolderPaths { get; private set; } = [];

    // Backward-compat single path (first selected)
    public string? SelectedFolderPath => SelectedFolderPaths.Count > 0
        ? SelectedFolderPaths[0] : null;

    // ── Private state ──────────────────────────────────────────────────────────

    private string _currentDeviceId = string.Empty;
    private string _currentPath     = string.Empty;
    private CancellationTokenSource _navCts = new();

    private readonly HashSet<string> _checkedPaths = [];
    private readonly List<(string Label, string Path)> _breadcrumb = [];

    // ── FolderEntry ────────────────────────────────────────────────────────────

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

    public MtpDevicePickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Load devices ──────────────────────────────────────────────────────────

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        DeviceList.ItemsSource    = null;
        NoDevicesText.Visibility  = Visibility.Collapsed;
        LoadingDevices.Visibility = Visibility.Visible;
        ResetFolderPanel();
        OkButton.IsEnabled = false;

        try
        {
            var devices = await MtpDeviceService.RunSta(MtpDeviceService.GetConnectedDevices);
            LoadingDevices.Visibility = Visibility.Collapsed;
            if (devices.Count == 0) NoDevicesText.Visibility = Visibility.Visible;
            else
            {
                DeviceList.ItemsSource       = devices;
                DeviceList.DisplayMemberPath = "FriendlyName";
            }
        }
        catch (Exception ex)
        {
            LoadingDevices.Visibility = Visibility.Collapsed;
            ShowError("Could not scan for devices", ex);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var devices = await MtpDeviceService.RunSta(MtpDeviceService.GetConnectedDevices);
            LoadingDevices.Visibility = Visibility.Collapsed;
            if (devices.Count == 0) NoDevicesText.Visibility = Visibility.Visible;
            else
            {
                DeviceList.ItemsSource       = devices;
                DeviceList.DisplayMemberPath = "FriendlyName";
            }
        }
        catch (Exception ex)
        {
            LoadingDevices.Visibility = Visibility.Collapsed;
            ShowError("Could not scan for devices", ex);
        }
    }

    // ── Device selection ──────────────────────────────────────────────────────

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is not MtpDeviceInfo dev) return;

        _currentDeviceId = dev.DeviceId;
        SelectedDeviceId   = dev.DeviceId;
        SelectedDeviceName = dev.FriendlyName;

        // Reset everything for the new device
        _checkedPaths.Clear();
        _breadcrumb.Clear();
        OkButton.IsEnabled = false;
        SelectedPathText.Text = string.Empty;

        BreadcrumbBar.Visibility = Visibility.Visible;
        await NavigateFolderAsync("", "Device");
    }

    // ── Flat folder navigation ─────────────────────────────────────────────────

    private async Task NavigateFolderAsync(string path, string? label = null)
    {
        // Cancel any in-flight navigation
        _navCts.Cancel();
        _navCts = new CancellationTokenSource();
        var ct = _navCts.Token;

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
        FolderUpBtn.IsEnabled = _breadcrumb.Count > 1;

        FolderHint.Visibility         = Visibility.Collapsed;
        FolderLoadingPanel.Visibility  = Visibility.Visible;
        FolderLoadingText.Text         = "Loading folders…";
        FolderScrollViewer.Visibility  = Visibility.Collapsed;
        FolderEmptyLabel.Visibility    = Visibility.Collapsed;
        FolderErrorPanel.Visibility    = Visibility.Collapsed;

        // Show a "still loading" hint after 1s so the user knows something is happening
        _ = Task.Delay(1000, ct).ContinueWith(_ =>
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.BeginInvoke(() => FolderLoadingText.Text = "Waiting for scan to pause…");
        }, TaskScheduler.Default);

        List<MtpFolderInfo> folderInfos;
        try
        {
            folderInfos = await MtpDeviceService.GetFoldersQuickAsync(_currentDeviceId, path, ct);
        }
        catch (OperationCanceledException)
        {
            return; // navigated elsewhere — silently discard
        }
        catch (Exception ex)
        {
            FolderLoadingPanel.Visibility = Visibility.Collapsed;
            FolderErrorLabel.Text         = $"Could not load folders: {ex.Message}";
            FolderErrorPanel.Visibility   = Visibility.Visible;
            return;
        }

        if (ct.IsCancellationRequested) return;

        FolderLoadingPanel.Visibility = Visibility.Collapsed;

        if (folderInfos.Count == 0)
        {
            FolderEmptyLabel.Visibility = Visibility.Visible;
        }
        else
        {
            var items = folderInfos
                .Select(info =>
                {
                    var entry = new FolderEntry
                    {
                        Name      = info.Name,
                        Path      = info.Path,
                        IsChecked = _checkedPaths.Contains(info.Path)
                    };
                    entry.PropertyChanged += (_, ev) =>
                    {
                        if (ev.PropertyName != nameof(FolderEntry.IsChecked)) return;
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
        FolderBreadcrumb.ItemsSource = null;
        FolderBreadcrumb.ItemsSource = _breadcrumb
            .Select(b => new BreadcrumbItem { Label = b.Label, Path = b.Path })
            .ToList();
    }

    private void RefreshFooter()
    {
        int n = _checkedPaths.Count;
        OkButton.IsEnabled    = n > 0;
        SelectedPathText.Text = n == 0
            ? "Check folders to select · › to browse inside"
            : $"{n} folder{(n == 1 ? "" : "s")} selected";
    }

    private void ResetFolderPanel()
    {
        _navCts.Cancel();
        _navCts = new CancellationTokenSource();
        _currentDeviceId = string.Empty;
        _currentPath     = string.Empty;
        _checkedPaths.Clear();
        _breadcrumb.Clear();
        FolderBreadcrumb.ItemsSource   = null;
        FolderItemsControl.ItemsSource = null;
        FolderHint.Visibility          = Visibility.Visible;
        FolderLoadingPanel.Visibility  = Visibility.Collapsed;
        FolderScrollViewer.Visibility  = Visibility.Collapsed;
        FolderEmptyLabel.Visibility    = Visibility.Collapsed;
        FolderErrorPanel.Visibility    = Visibility.Collapsed;
        BreadcrumbBar.Visibility       = Visibility.Collapsed;
        FolderUpBtn.IsEnabled          = false;
        SelectedPathText.Text          = string.Empty;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void FolderBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is BreadcrumbItem item)
            await NavigateFolderAsync(item.Path, item.Label);
    }

    private async void FolderUp_Click(object sender, RoutedEventArgs e)
    {
        if (_breadcrumb.Count < 2) return;
        var parent = _breadcrumb[^2];
        await NavigateFolderAsync(parent.Path, parent.Label);
    }

    private async void FolderRetry_Click(object sender, RoutedEventArgs e)
        => await NavigateFolderAsync(_currentPath);

    private async void FolderDrillIn_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is FolderEntry entry)
            await NavigateFolderAsync(entry.Path, entry.Name);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedFolderPaths = [.. _checkedPaths];
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Error helper ──────────────────────────────────────────────────────────

    private void ShowError(string message, Exception? ex = null)
    {
        var detail = ex != null ? $"\n\n{ex.GetType().Name}: {ex.Message}" : string.Empty;
        System.Windows.MessageBox.Show(message + detail, "Device Error",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
