using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        SortCombo.ItemsSource   = _vm.SortModes.Select(s => s.Label).ToList();
        SortCombo.SelectedIndex = 0;

        // Wire buckets panel to the VM's Buckets collection
        BucketsPanelControl.BucketList.ItemsSource = _vm.Buckets;

        // Ctrl+Z → Undo
        InputBindings.Add(new KeyBinding(_vm.UndoCommand, new KeyGesture(Key.Z, ModifierKeys.Control)));

        // Return focus to card stack whenever a scan finishes
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsScanning) && !_vm.IsScanning)
                Dispatcher.BeginInvoke(() => CardStack.Focus());
        };

        Loaded += async (_, _) =>
        {
            CardStack.Focus();
            // Sync mute icon to the default muted state
            MuteIcon.Text = SoundService.Instance.MuteIcon;
            VolumeSlider.IsEnabled = !SoundService.Instance.IsMuted;

            // Check for updates in background — never blocks startup
            await _vm.CheckForUpdatesAsync();
        };
    }

    private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var idx = TypeFilterCombo.SelectedIndex;
        if (idx >= 0 && idx < _vm.TypeFilters.Count)
            _vm.SelectedTypeFilter = _vm.TypeFilters[idx].Value;
    }

    private void DateFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var idx = DateFilterCombo.SelectedIndex;
        if (idx >= 0 && idx < _vm.DateFilters.Count)
            _vm.SelectedDateFilter = _vm.DateFilters[idx].Value;
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var idx = SortCombo.SelectedIndex;
        if (idx >= 0 && idx < _vm.SortModes.Count)
            _vm.SelectedSortMode = _vm.SortModes[idx].Value;
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

    private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        new ShortcutsOverlay { Owner = this }.ShowDialog();
        CardStack.Focus();
    }

    private void PresetsButton_Click(object sender, RoutedEventArgs e)
    {
        PresetsStack.Children.Clear();

        // Pin current folder option
        if (_vm.HasFolder)
        {
            var pinBtn = MakePresetButton("📌  Pin current folder", () =>
            {
                _vm.PinCurrentFolder();
                PresetsPopup.IsOpen = false;
            });
            PresetsStack.Children.Add(pinBtn);
            PresetsStack.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });
        }

        foreach (var preset in _vm.Presets)
        {
            var p = preset;
            var btn = MakePresetButton($"{p.Icon}  {p.Name}", () =>
            {
                _vm.LoadPreset(p);
                PresetsPopup.IsOpen = false;
                CardStack.Focus();
            });
            btn.ContextMenu = BuildPresetContextMenu(p);
            PresetsStack.Children.Add(btn);
        }

        if (PresetsStack.Children.Count == 0)
        {
            PresetsStack.Children.Add(new TextBlock
            {
                Text = "No presets yet.\nBrowse a folder to pin it.",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(4, 2, 4, 2)
            });
        }

        PresetsPopup.IsOpen = true;
    }

    private static System.Windows.Controls.Button MakePresetButton(string text, Action action)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private System.Windows.Controls.ContextMenu BuildPresetContextMenu(FileTinder.Models.FolderPreset preset)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var removeItem = new System.Windows.Controls.MenuItem { Header = "✕ Remove preset" };
        removeItem.Click += (_, _) =>
        {
            _vm.RemovePreset(preset);
            PresetsPopup.IsOpen = false;
        };
        menu.Items.Add(removeItem);
        return menu;
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

    private void BackupDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.MtpDeviceId == null || _vm.MtpFolderPath == null) return;
        var win = new Views.MtpCopyWindow(_vm.MtpDeviceId, _vm.MtpDeviceName ?? "Device",
                                          _vm.MtpFolderPath)
        {
            Owner = this
        };
        win.Show();
    }

    private void SubfolderPill_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.IncludeSubfolders = !_vm.IncludeSubfolders;
        SubfolderPill.BorderBrush = _vm.IncludeSubfolders
            ? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7C3AED"))
            : new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A3A"));
        CardStack.Focus();
    }

    private void ClearSubfolderSelection_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedScanFolders = null;
        _vm.LoadFiles();
    }

    // ── View mode toggles ──────────────────────────────────────────────────────

    private void SwipeModeBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.ActiveViewMode = FileTinder.Models.ViewMode.Swipe;
        CardStack.Focus();
    }

    private void GridModeBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.ActiveViewMode = FileTinder.Models.ViewMode.Grid;
        GridView.Focus();
    }

    private void ListModeBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.ActiveViewMode = FileTinder.Models.ViewMode.List;
        ListView.Focus();
    }
}
