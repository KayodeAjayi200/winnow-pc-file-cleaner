using System.Windows;
using System.Windows.Controls;
using FileTinder.Models;
using FileTinder.ViewModels;

namespace FileTinder.Views;

public partial class FileGridView : UserControl
{
    public FileGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── Tile size dependency property ──────────────────────────────────────────

    public static readonly DependencyProperty TileSizeProperty =
        DependencyProperty.Register(nameof(TileSize), typeof(double), typeof(FileGridView),
            new PropertyMetadata(140.0));

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    // ── VM wiring ──────────────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
            TileList.ItemsSource = vm.AllFiles;
    }

    // ── Tile clicked → select ─────────────────────────────────────────────────

    internal void TileList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // ── Quick actions ─────────────────────────────────────────────────────────

    private void QuickDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is FrameworkElement fe && fe.Tag is FileItem f)
        {
            vm.QuickDeleteFile(f);
            TileList.ItemsSource = vm.AllFiles;
        }
    }

    private void QuickKeep_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is FrameworkElement fe && fe.Tag is FileItem f)
        {
            vm.QuickKeepFile(f);
            TileList.ItemsSource = vm.AllFiles;
        }
    }

    private void QuickOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FileItem f && !f.IsMtp)
            FilePreviewControl.OpenInDefaultApp(f.FullPath);
    }

    private void QuickOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FileItem f)
            FilePreviewControl.OpenFileLocation(f);
    }
}
