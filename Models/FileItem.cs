using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileTinder.Models;

public class FileItem : INotifyPropertyChanged
{
    public string Name         { get; init; } = string.Empty;
    public string FullPath     { get; init; } = string.Empty;
    public long   Size         { get; init; }
    public DateTime LastModified { get; init; }
    public FileTypeCategory Category { get; init; }

    public string SizeFormatted  => FormatSize(Size);
    public string CategoryLabel  => Category.ToString();
    public string IconChar       => GetIconChar(Category);
    public string CategoryColor  => GetCategoryColor(Category);
    public string DirectoryPath  => System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;

    // ── Duplicate detection ───────────────────────────────────────────────────

    private bool _isDuplicate;
    public bool IsDuplicate
    {
        get => _isDuplicate;
        set { _isDuplicate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DuplicateBadgeText)); }
    }

    private int _duplicateCount;
    public int DuplicateCount
    {
        get => _duplicateCount;
        set { _duplicateCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DuplicateBadgeText)); }
    }

    public string? DuplicateGroupKey { get; set; }

    public string DuplicateBadgeText =>
        IsDuplicate ? $"🔄  {DuplicateCount} copies detected" : string.Empty;

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };

    private static string GetIconChar(FileTypeCategory cat) => cat switch
    {
        FileTypeCategory.Image    => "🖼",
        FileTypeCategory.Video    => "🎬",
        FileTypeCategory.Document => "📄",
        FileTypeCategory.Audio    => "🎵",
        FileTypeCategory.Archive  => "📦",
        _                         => "📁"
    };

    private static string GetCategoryColor(FileTypeCategory cat) => cat switch
    {
        FileTypeCategory.Image    => "#FF6B9D",
        FileTypeCategory.Video    => "#C77DFF",
        FileTypeCategory.Document => "#4CC9F0",
        FileTypeCategory.Audio    => "#F8961E",
        FileTypeCategory.Archive  => "#F3722C",
        _                         => "#90E0EF"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
