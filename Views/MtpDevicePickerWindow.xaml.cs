using System.Windows;
using System.Windows.Controls;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class MtpDevicePickerWindow : Window
{
    // ── Result ─────────────────────────────────────────────────────────────────
    public string? SelectedDeviceId   { get; private set; }
    public string? SelectedFolderPath { get; private set; }
    public string? SelectedDeviceName { get; private set; }

    public MtpDevicePickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Load devices ──────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var devices = await Task.Run(MtpDeviceService.GetConnectedDevices);

        LoadingDevices.Visibility = Visibility.Collapsed;

        if (devices.Count == 0)
        {
            NoDevicesText.Visibility = Visibility.Visible;
        }
        else
        {
            DeviceList.ItemsSource   = devices;
            DeviceList.DisplayMemberPath = "FriendlyName";
        }
    }

    // ── Device selection ──────────────────────────────────────────────────────

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is not MtpDeviceInfo dev) return;

        FolderHint.Visibility  = Visibility.Collapsed;
        FolderTree.Items.Clear();
        OkButton.IsEnabled     = false;
        SelectedPathText.Text  = string.Empty;

        var roots = await Task.Run(() => MtpDeviceService.GetRootFolders(dev.DeviceId));

        foreach (var root in roots)
        {
            var node = CreateNode(root, dev.DeviceId);
            FolderTree.Items.Add(node);
        }

        if (FolderTree.Items.Count == 0)
            FolderHint.Visibility = Visibility.Visible;
    }

    // ── Folder tree ───────────────────────────────────────────────────────────

    private TreeViewItem CreateNode(string path, string deviceId)
    {
        var node = new TreeViewItem
        {
            Header = System.IO.Path.GetFileName(path).TrimEnd('/') is { Length: > 0 } n ? n : path,
            Tag    = (deviceId, path)
        };
        // Placeholder child so the expand arrow appears
        node.Items.Add(new TreeViewItem { Header = "⏳ Loading…", Tag = "__placeholder__" });
        return node;
    }

    private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem node) return;
        if (node.Tag is not (string devId, string folderPath)) return;

        // Only load if the single placeholder is still there
        if (node.Items.Count != 1
            || node.Items[0] is not TreeViewItem first
            || first.Tag as string != "__placeholder__") return;

        node.Items.Clear();

        var subs = await Task.Run(() => MtpDeviceService.GetSubfolders(devId, folderPath));
        foreach (var sub in subs)
            node.Items.Add(CreateNode(sub, devId));

        if (node.Items.Count == 0)
            node.Items.Add(new TreeViewItem { Header = "(empty)", IsEnabled = false });
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DeviceList.SelectedItem is not MtpDeviceInfo dev) return;
        if (FolderTree.SelectedItem is not TreeViewItem node) return;
        if (node.Tag is not (string _, string path)) return;

        SelectedDeviceId   = dev.DeviceId;
        SelectedDeviceName = dev.FriendlyName;
        SelectedFolderPath = path;
        SelectedPathText.Text = $"{dev.FriendlyName}  ›  {path}";
        OkButton.IsEnabled = true;
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
