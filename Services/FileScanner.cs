using FileTinder.Models;

namespace FileTinder.Services;

public static class FileScanner
{
    private static readonly Dictionary<FileTypeCategory, HashSet<string>> _extensionMap = new()
    {
        [FileTypeCategory.Image]    = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg", ".ico", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw" },
        [FileTypeCategory.Video]    = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts" },
        [FileTypeCategory.Document] = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md", ".json", ".xml", ".html", ".htm" },
        [FileTypeCategory.Audio]    = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".aiff" },
        [FileTypeCategory.Archive]  = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".dmg" },
    };

    public static List<FileItem> Scan(
        string folderPath,
        FileTypeCategory typeFilter,
        DateFilter dateFilter,
        bool recursive = false)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var cutoff = GetDateCutoff(dateFilter);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*", searchOption)
                .AsParallel()
                .Select(path =>
                {
                    try { return BuildFileItem(path); }
                    catch { return null; }
                })
                .Where(f => f != null
                    && (typeFilter == FileTypeCategory.All || f.Category == typeFilter)
                    && (cutoff == null || f.LastModified >= cutoff))
                .Cast<FileItem>()
                .OrderByDescending(f => f.Size)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Streams files one-by-one as they are discovered, allowing the UI to update
    /// before the entire folder tree has been scanned.
    /// </summary>
    public static async Task ScanStreamingAsync(
        string folderPath,
        FileTypeCategory typeFilter,
        DateFilter dateFilter,
        bool recursive,
        Action<FileItem> onFileFound,
        CancellationToken ct)
    {
        if (!Directory.Exists(folderPath)) return;

        var cutoff       = GetDateCutoff(dateFilter);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        await Task.Run(() =>
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(folderPath, "*", searchOption))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var item = BuildFileItem(path);
                        if ((typeFilter == FileTypeCategory.All || item.Category == typeFilter)
                            && (cutoff == null || item.LastModified >= cutoff))
                        {
                            onFileFound(item);
                        }
                    }
                    catch { /* skip inaccessible files */ }
                }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch { /* ignore top-level enumeration errors */ }
        }, ct);
    }

    private static FileItem BuildFileItem(string path)
    {
        var info = new FileInfo(path);
        return new FileItem
        {
            Name         = info.Name,
            FullPath     = info.FullName,
            Size         = info.Length,
            LastModified = info.LastWriteTime,
            Category     = ClassifyExtension(info.Extension)
        };
    }

    public static FileTypeCategory ClassifyExtension(string extension)
    {
        foreach (var (cat, exts) in _extensionMap)
            if (exts.Contains(extension))
                return cat;
        return FileTypeCategory.Other;
    }

    private static DateTime? GetDateCutoff(DateFilter filter) => filter switch
    {
        DateFilter.Last7Days   => DateTime.Now.AddDays(-7),
        DateFilter.Last30Days  => DateTime.Now.AddDays(-30),
        DateFilter.Last6Months => DateTime.Now.AddMonths(-6),
        DateFilter.LastYear    => DateTime.Now.AddYears(-1),
        _                      => null
    };
}
