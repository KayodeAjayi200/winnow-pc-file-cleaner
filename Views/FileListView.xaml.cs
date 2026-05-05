using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FileTinder.Models;
using FileTinder.ViewModels;

namespace FileTinder.Views;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
            FileDataGrid.ItemsSource = vm.AllFiles;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void FileDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
            Vm?.SelectFile(f);
    }

    private void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
        {
            Vm?.QuickDeleteFile(f);
            FileDataGrid.ItemsSource = Vm?.AllFiles;
        }
    }

    private void CtxKeep_Click(object sender, RoutedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
        {
            Vm?.QuickKeepFile(f);
            FileDataGrid.ItemsSource = Vm?.AllFiles;
        }
    }

    private void CtxOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f && !f.IsMtp)
        {
            var dir = Path.GetDirectoryName(f.FullPath);
            if (dir != null)
                Process.Start("explorer.exe", $"/select,\"{f.FullPath}\"");
        }
    }
}
