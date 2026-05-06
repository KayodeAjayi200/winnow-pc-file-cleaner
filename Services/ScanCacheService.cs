using System.IO;
using System.Text.Json;
using FileTinder.Models;

namespace FileTinder.Services;

/// <summary>
/// Persists scan results to disk so subsequent opens of the same folder load
/// instantly instead of re-scanning.
///
/// Cache location: %AppData%\Winnow\cache\{key}.json
/// TTL: 4 hours for local folders, 24 hours for MTP devices.
/// Only the full-unfiltered scan (All types + Any date) is cached; filtered
/// scans always run fresh so results are accurate.
/// </summary>
public static class ScanCacheService
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Winnow", "cache");

    private static readonly TimeSpan LocalTtl = TimeSpan.FromHours(4);
    private static readonly TimeSpan MtpTtl   = TimeSpan.FromHours(24);

    // ── Private DTO (not exported) ────────────────────────────────────────────

    private sealed record CacheEntry(
        long   ScannedAtTicks,
        List<CachedItem> Items);

    private sealed record CachedItem(
        string Name,
        string FullPath,
        long   Size,
        long   LastModifiedTicks,
        long   LastAccessedTicks,
        int    JunkScore,
        FileTypeCategory Category,
        bool   IsMtp,
        string? MtpDeviceId,
        string? MtpObjectId);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Stable key for a given source (folder path + optional device id).</summary>
    public static string MakeCacheKey(string folderPath, string? deviceId)
    {
        var raw   = $"{deviceId ?? "local"}|{folderPath.ToLowerInvariant()}";
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLower();
    }

    /// <summary>
    /// Attempts to load a cached file list.
    /// Returns null if no cache, or if the cache has expired.
    /// </summary>
    public static (List<FileItem> Files, DateTime ScannedAt)? Load(
        string cacheKey, bool isMtp)
    {
        try
        {
            var path = Path.Combine(CacheDir, $"{cacheKey}.json");
            if (!File.Exists(path)) return null;

            var ttl = isMtp ? MtpTtl : LocalTtl;
            var age = DateTime.Now - File.GetLastWriteTime(path);
            if (age > ttl) return null;

            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path));
            if (entry == null || entry.Items.Count == 0) return null;

            var scannedAt = new DateTime(entry.ScannedAtTicks, DateTimeKind.Local);
            var files = entry.Items.Select(i => new FileItem
            {
                Name         = i.Name,
                FullPath     = i.FullPath,
                Size         = i.Size,
                LastModified = new DateTime(i.LastModifiedTicks, DateTimeKind.Local),
                LastAccessed = new DateTime(i.LastAccessedTicks, DateTimeKind.Local),
                JunkScore    = i.JunkScore,
                Category     = i.Category,
                IsMtp        = i.IsMtp,
                MtpDeviceId  = i.MtpDeviceId,
                MtpObjectId  = i.MtpObjectId,
            }).ToList();

            return (files, scannedAt);
        }
        catch { return null; }
    }

    /// <summary>Saves a file list to the cache (fire-and-forget via Task.Run).</summary>
    public static void SaveAsync(string cacheKey, List<FileItem> files)
        => Task.Run(() => SaveCore(cacheKey, files));

    private static void SaveCore(string cacheKey, List<FileItem> files)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var entry = new CacheEntry(
                DateTime.Now.Ticks,
                files.Select(f => new CachedItem(
                    f.Name, f.FullPath, f.Size,
                    f.LastModified.Ticks, f.LastAccessed.Ticks, f.JunkScore,
                    f.Category, f.IsMtp, f.MtpDeviceId, f.MtpObjectId
                )).ToList()
            );
            var json = JsonSerializer.Serialize(entry);
            File.WriteAllText(Path.Combine(CacheDir, $"{cacheKey}.json"), json);
        }
        catch { /* silently ignore cache write failures */ }
    }

    /// <summary>Deletes the cache entry so next load triggers a fresh scan.</summary>
    public static void Invalidate(string cacheKey)
    {
        try { File.Delete(Path.Combine(CacheDir, $"{cacheKey}.json")); }
        catch { }
    }

    /// <summary>Human-readable age string, e.g. "2 hours ago" or "just now".</summary>
    public static string FormatAge(DateTime scannedAt)
    {
        var age = DateTime.Now - scannedAt;
        if (age.TotalMinutes < 2)  return "just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalHours < 2)    return "1 hour ago";
        if (age.TotalHours < 24)   return $"{(int)age.TotalHours} hours ago";
        return $"{(int)age.TotalDays} days ago";
    }

    // ── Backup folder-size cache ───────────────────────────────────────────────

    /// <summary>One cached folder with its computed size + file count.</summary>
    public sealed record FolderSizeEntry(string Path, long Size, int FileCount);

    private sealed record FolderSizeCacheEntry(long ScannedAtTicks, List<FolderSizeEntry> Folders);

    /// <summary>
    /// Cache key for a backup folder-size listing.
    /// Prefixed with "bs_" so it never collides with file-scan keys.
    /// </summary>
    public static string MakeBackupCacheKey(string deviceId, string sourcePath)
    {
        var raw   = $"backup|{deviceId}|{sourcePath.ToLowerInvariant()}";
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return "bs_" + Convert.ToHexString(bytes).ToLower();
    }

    /// <summary>
    /// Loads cached folder sizes.  Returns null if no cache or TTL expired (24 h for MTP).
    /// </summary>
    public static (List<FolderSizeEntry> Folders, DateTime ScannedAt)? LoadFolderSizes(string cacheKey)
    {
        try
        {
            var path = Path.Combine(CacheDir, $"{cacheKey}.json");
            if (!File.Exists(path)) return null;

            if (DateTime.Now - File.GetLastWriteTime(path) > MtpTtl) return null;

            var entry = JsonSerializer.Deserialize<FolderSizeCacheEntry>(File.ReadAllText(path));
            if (entry == null || entry.Folders.Count == 0) return null;

            return (entry.Folders, new DateTime(entry.ScannedAtTicks, DateTimeKind.Local));
        }
        catch { return null; }
    }

    /// <summary>Saves folder sizes to cache (fire-and-forget).</summary>
    public static void SaveFolderSizesAsync(string cacheKey, List<FolderSizeEntry> folders)
        => Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var entry = new FolderSizeCacheEntry(DateTime.Now.Ticks, folders);
                File.WriteAllText(
                    Path.Combine(CacheDir, $"{cacheKey}.json"),
                    JsonSerializer.Serialize(entry));
            }
            catch { }
        });
}
