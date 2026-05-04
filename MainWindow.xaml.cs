using System.Windows;
using FileTinder.Models;
using FileTinder.Services;
using FileTinder.ViewModels;
using FileTinder.Views;

namespace FileTinder;

public partial class MainWindow : Window
{
    private MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        TypeFilterCombo.ItemsSource   = _vm.TypeFilters.Select(f => f.Label).ToList();
        TypeFilterCombo.SelectedIndex = 0;
        DateFilterCombo.ItemsSource   = _vm.DateFilters.Select(f => f.Label).ToList();
        DateFilterCombo.SelectedIndex = 0;

        // Wire buckets panel to the VM's Buckets collection
        BucketsPanelControl.BucketList.ItemsSource = _vm.Buckets;

        // Return focus to card stack whenever a scan finishes
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsScanning) && !_vm.IsScanning)
                Dispatcher.BeginInvoke(() => CardStack.Focus());
        };

        Loaded += (_, _) =>
        {
            CardStack.Focus();
            // Sync mute icon to the default muted state
            MuteIcon.Text = SoundService.Instance.MuteIcon;
            VolumeSlider.IsEnabled = !SoundService.Instance.IsMuted;
        };
    }

    private void TypeFilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var idx = TypeFilterCombo.SelectedIndex;
        if (idx >= 0 && idx < _vm.TypeFilters.Count)
            _vm.SelectedTypeFilter = _vm.TypeFilters[idx].Value;
    }

    private void DateFilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var idx = DateFilterCombo.SelectedIndex;
        if (idx >= 0 && idx < _vm.DateFilters.Count)
            _vm.SelectedDateFilter = _vm.DateFilters[idx].Value;
    }

    private void KeepButton_Click(object sender, RoutedEventArgs e)
    {
        CardStack.CommitSwipe(isKeep: true);
        CardStack.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        CardStack.CommitSwipe(isKeep: false);
        CardStack.Focus();
    }

    private void BucketButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CurrentFile == null) return;

        var menu = new System.Windows.Controls.ContextMenu();

        var newBucketItem = new System.Windows.Controls.MenuItem { Header = "⊕  New bucket…" };
        newBucketItem.Click += (_, _) =>
        {
            var dlg = new Views.NameDialog("Name your new bucket:", $"Bucket {_vm.Buckets.Count + 1}") { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _vm.AddToNewBucket(dlg.BucketName);
            }
            CardStack.Focus();
        };
        menu.Items.Add(newBucketItem);

        if (_vm.Buckets.Count > 0)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (var bucket in _vm.Buckets)
            {
                var b = bucket; // capture
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = $"🪣  {b.Name}  ({b.FileCount} files)"
                };
                item.Click += (_, _) => { _vm.AddToExistingBucket(b); CardStack.Focus(); };
                menu.Items.Add(item);
            }
        }

        menu.PlacementTarget = BucketButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen    = true;
    }

    private void NewBucketHeader_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.NameDialog("Name your new bucket:", $"Bucket {_vm.Buckets.Count + 1}") { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var bucket = _vm.CreateBucket(dlg.BucketName);
            bucket.IsExpanded = true;
        }
        CardStack.Focus();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        var snd = SoundService.Instance;
        snd.IsMuted      = !snd.IsMuted;
        MuteIcon.Text    = snd.MuteIcon;
        VolumeSlider.IsEnabled = !snd.IsMuted;
        CardStack.Focus();
    }

    private void VolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        SoundService.Instance.Volume = e.NewValue;
    }

    private void BrowseDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Views.MtpDevicePickerWindow { Owner = this };
        if (picker.ShowDialog() == true
            && picker.SelectedDeviceId  != null
            && picker.SelectedFolderPath != null
            && picker.SelectedDeviceName != null)
        {
            _vm.LoadMtpFiles(picker.SelectedDeviceId, picker.SelectedFolderPath, picker.SelectedDeviceName);
        }
        CardStack.Focus();
    }

    private void SubfolderPill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm.IncludeSubfolders = !_vm.IncludeSubfolders;
        SubfolderPill.BorderBrush = _vm.IncludeSubfolders
            ? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7C3AED"))
            : new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A3A"));
        CardStack.Focus();
    }
}
