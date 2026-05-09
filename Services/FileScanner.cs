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

    /// <summary>
    /// Streams files from multiple selected subfolders (always recursive within each).
    /// </summary>
    public static async Task ScanMultipleAsync(
        IEnumerable<string> folderPaths,
        FileTypeCategory typeFilter,
        DateFilter dateFilter,
        Action<FileItem> onFileFound,
        CancellationToken ct)
    {
        var cutoff = GetDateCutoff(dateFilter);

        await Task.Run(() =>
        {
            foreach (var folderPath in folderPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(folderPath)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
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
                        catch { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
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
            LastAccessed = info.LastAccessTime,
            JunkScore    = CalculateJunkScore(info),
            Category     = ClassifyExtension(info.Extension)
        };
    }

    public static int CalculateJunkScore(FileInfo info)
    {
        int score = 0;

        // Old files: 1-2 years → +20, >2 years → +40
        var age = DateTime.Now - info.LastWriteTime;
        if (age.TotalDays > 730) score += 40;
        else if (age.TotalDays > 365) score += 20;

        // Temp / junk extensions
        var ext = info.Extension.ToLowerInvariant();
        if (_junkExtensions.Contains(ext)) score += 30;

        // Known junk filenames (case-insensitive)
        var nameNoExt = Path.GetFileNameWithoutExtension(info.Name).ToLowerInvariant();
        if (_junkNames.Contains(nameNoExt)) score += 30;

        // Duplicate-like name patterns: "file (1)", "copy of file", "file - copy"
        if (_dupPattern.IsMatch(nameNoExt)) score += 15;

        return Math.Min(score, 100);
    }

    private static readonly HashSet<string> _junkExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".bak", ".old", ".cache", ".dmp", ".log", ".crdownload", ".part"
    };

    private static readonly HashSet<string> _junkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", ".ds_store", "desktop.ini", "ehthumbs.db", "ehthumbs_vista.db",
        "$recycle.bin", "ntuser.dat.log"
    };

    private static readonly System.Text.RegularExpressions.Regex _dupPattern = new(
        @"[\s_-]\(\d+\)$|[\s_-]copy(\s+\d+)?$|\bcopy\s+of\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static FileTypeCategory ClassifyExtension(string extension)
    {
        foreach (var (cat, exts) in _extensionMap)
            if (exts.Contains(extension))
                return cat;
        return FileTypeCategory.Other;
    }

    public static DateTime? GetDateCutoff(DateFilter filter) => filter switch
    {
        DateFilter.Last7Days   => DateTime.Now.AddDays(-7),
        DateFilter.Last30Days  => DateTime.Now.AddDays(-30),
        DateFilter.Last6Months => DateTime.Now.AddMonths(-6),
        DateFilter.LastYear    => DateTime.Now.AddYears(-1),
        _                      => null
    };
}
