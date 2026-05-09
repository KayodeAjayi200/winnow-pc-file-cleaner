using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FileTinder.Views;

public partial class SubfolderPickerWindow : Window
{
    public List<string> SelectedPaths { get; private set; } = [];

    private readonly List<FolderEntry> _entries = [];

    public SubfolderPickerWindow(string rootPath)
    {
        InitializeComponent();
        RootPathText.Text = rootPath;

        // Enumerate immediate subfolders
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootPath)
                                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new FolderEntry(dir);
                entry.PropertyChanged += (_, _) => UpdateCounters();
                _entries.Add(entry);
            }
        }
        catch { }

        FolderList.ItemsSource = _entries;
        UpdateCounters();

        // Kick off background size counting
        _ = Task.Run(CountSizesAsync);
    }

    private async Task CountSizesAsync()
    {
        foreach (var entry in _entries)
        {
            try
            {
                long size  = 0;
                int  count = 0;
                await Task.Run(() =>
                {
                    foreach (var f in Directory.EnumerateFiles(entry.FullPath, "*",
                                                               SearchOption.AllDirectories))
                    {
                        try
                        {
                            size  += new FileInfo(f).Length;
                            count++;
                        }
                        catch { }
                    }
                });
                entry.SetSize(size, count);
            }
            catch { }
        }
    }

    private void UpdateCounters()
    {
        int checked_ = _entries.Count(e => e.IsChecked);
        SelectionCountText.Text = $"{checked_} of {_entries.Count} selected";
        FooterText.Text = checked_ == 0
            ? "No folders selected"
            : $"{checked_} folder{(checked_ == 1 ? "" : "s")} will be scanned";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries) entry.IsChecked = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries) entry.IsChecked = false;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedPaths = _entries.Where(e => e.IsChecked).Select(e => e.FullPath).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public class FolderEntry : INotifyPropertyChanged
{
    public string FullPath { get; }
    public string Name     { get; }

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    private string _sizeText = "…";
    public string SizeText
    {
        get => _sizeText;
        private set { _sizeText = value; OnPropertyChanged(); }
    }

    private string _subText = string.Empty;
    public string SubText
    {
        get => _subText;
        private set { _subText = value; OnPropertyChanged(); }
    }

    public FolderEntry(string path)
    {
        FullPath = path;
        Name     = System.IO.Path.GetFileName(path);
    }

    public void SetSize(long bytes, int fileCount)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            SizeText = FormatSize(bytes);
            SubText  = $"{fileCount:N0} files";
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
