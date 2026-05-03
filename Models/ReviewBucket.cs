using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FileTinder.ViewModels;

namespace FileTinder.Models;

public class ReviewBucket : INotifyPropertyChanged
{
    private string _name;
    private bool   _isExpanded;
    private bool   _isRenaming;
    private string _pendingName = string.Empty;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set { _isRenaming = value; OnPropertyChanged(); }
    }

    public string PendingName
    {
        get => _pendingName;
        set { _pendingName = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FileItem> Files { get; } = [];

    // ── Callbacks wired by MainViewModel ─────────────────────────────────────

    public Action<ReviewBucket, FileItem>? OnDeleteFile        { get; set; }
    public Action<ReviewBucket, FileItem>? OnKeepFile          { get; set; }
    public Action<ReviewBucket>?           OnDeleteBucket      { get; set; }
    public Action<ReviewBucket>?           OnConvertToSubfolder { get; set; }

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand ToggleExpandCommand        { get; }
    public ICommand DeleteBucketCommand        { get; }
    public ICommand DeleteFileCommand          { get; }
    public ICommand KeepFileCommand            { get; }
    public ICommand RemoveFileCommand          { get; }
    public ICommand RenameCommand              { get; }
    public ICommand CommitRenameCommand        { get; }
    public ICommand CancelRenameCommand        { get; }
    public ICommand ConvertToSubfolderCommand  { get; }

    public ReviewBucket(string name)
    {
        _name = name;

        ToggleExpandCommand       = new RelayCommand(() => IsExpanded = !IsExpanded);
        DeleteBucketCommand       = new RelayCommand(() => OnDeleteBucket?.Invoke(this));
        DeleteFileCommand         = new RelayCommand<FileItem>(f => { if (f != null) OnDeleteFile?.Invoke(this, f); });
        KeepFileCommand           = new RelayCommand<FileItem>(f => { if (f != null) OnKeepFile?.Invoke(this, f); });
        RemoveFileCommand         = new RelayCommand<FileItem>(f => { if (f != null) Files.Remove(f); });
        RenameCommand             = new RelayCommand(() => { PendingName = Name; IsRenaming = true; });
        CommitRenameCommand       = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(PendingName)) Name = PendingName.Trim();
            IsRenaming = false;
        });
        CancelRenameCommand       = new RelayCommand(() => IsRenaming = false);
        ConvertToSubfolderCommand = new RelayCommand(() => OnConvertToSubfolder?.Invoke(this));

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(TotalSizeFormatted));
            OnPropertyChanged(nameof(Summary));
        };
    }

    // ── Computed ──────────────────────────────────────────────────────────────

    public int    FileCount           => Files.Count;
    public long   TotalSize           => Files.Sum(f => f.Size);
    public string TotalSizeFormatted  => FormatBytes(TotalSize);
    public string Summary             => $"{FileCount} file{(FileCount != 1 ? "s" : "")} · {TotalSizeFormatted}";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
