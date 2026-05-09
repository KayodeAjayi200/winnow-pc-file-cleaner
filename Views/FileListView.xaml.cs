using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileTinder.Models;
using FileTinder.ViewModels;

namespace FileTinder.Views;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        FileDataGrid.MouseDoubleClick += FileDataGrid_DoubleClick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;

        if (e.NewValue is MainViewModel vm)
        {
            FileDataGrid.ItemsSource = vm.AllFiles;
            vm.PropertyChanged      += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AllFiles) && sender is MainViewModel vm)
            FileDataGrid.ItemsSource = vm.AllFiles;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void FileDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
            Vm?.SelectFile(f);
    }

    private void FileDataGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
            FilePreviewControl.OpenInDefaultApp(f);
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

    private void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
            FilePreviewControl.OpenInDefaultApp(f);
    }

    private void CtxOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (FileDataGrid.SelectedItem is FileItem f)
            FilePreviewControl.OpenFileLocation(f);
    }
}
