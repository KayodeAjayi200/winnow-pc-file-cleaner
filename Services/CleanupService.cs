using System.IO;
using System.Runtime.InteropServices;

namespace FileTinder.Services;

public enum CleanupCategory { Basic, System, Privacy }

public record CleanupTarget(string Label, string Path, CleanupCategory Category, string? Glob = null);

public record CleanupScanResult(CleanupTarget Target, long TotalBytes, int FileCount, IReadOnlyList<string> Files);

public class CleanupService
{
    // ── Targets ──────────────────────────────────────────────────────────────

    public static IReadOnlyList<CleanupTarget> GetTargets()
    {
        var temp     = Path.GetTempPath();                              // %TEMP%
        var local    = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var winDir   = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return
        [
            // ── Basic ──────────────────────────────────────────────────────
            new("User temp files",        temp,                                                  CleanupCategory.Basic),
            new("Windows prefetch",       Path.Combine(winDir, "Prefetch"),                      CleanupCategory.Basic),

            // ── System ─────────────────────────────────────────────────────
            new("Windows temp",           Path.Combine(winDir, "Temp"),                          CleanupCategory.System),
            new("Windows Update cache",   Path.Combine(winDir, "SoftwareDistribution", "Download"), CleanupCategory.System),
            new("Thumbnail cache",        Path.Combine(local,  "Microsoft", "Windows", "Explorer"), CleanupCategory.System, "thumbcache_*.db"),
            new("Error reports",          Path.Combine(local,  "Microsoft", "Windows", "WER"),   CleanupCategory.System),
            new("Crash dumps",            Path.Combine(winDir, "Minidump"),                      CleanupCategory.System),

            // ── Privacy ────────────────────────────────────────────────────
            new("Recent files list",      Path.Combine(appData, "Microsoft", "Windows", "Recent"), CleanupCategory.Privacy),
            new("Chrome cache",           Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), CleanupCategory.Privacy),
            new("Edge cache",             Path.Combine(local, "Microsoft", "Edge",   "User Data", "Default", "Cache"), CleanupCategory.Privacy),
            new("Firefox cache",          Path.Combine(local, "Mozilla", "Firefox", "Profiles"),  CleanupCategory.Privacy),
        ];
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    public static async Task<IReadOnlyList<CleanupScanResult>> ScanAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<CleanupScanResult>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(target.Label);

            var result = await Task.Run(() => ScanTarget(target), ct);
            results.Add(result);
        }

        return results;
    }

    private static CleanupScanResult ScanTarget(CleanupTarget target)
    {
        var files   = new List<string>();
        long totalBytes = 0;

        if (!Directory.Exists(target.Path))
            return new CleanupScanResult(target, 0, 0, files);

        try
        {
            var pattern  = target.Glob ?? "*";
            var searchOpt = target.Glob != null
                ? SearchOption.TopDirectoryOnly
                : SearchOption.AllDirectories;

            foreach (var file in Directory.EnumerateFiles(target.Path, pattern, searchOpt))
            {
                try
                {
                    var info = new FileInfo(file);
                    files.Add(file);
                    totalBytes += info.Length;
                }
                catch { /* skip locked/inaccessible files */ }
            }
        }
        catch { /* skip entire target if access denied */ }

        return new CleanupScanResult(target, totalBytes, files.Count, files);
    }

    // ── Clean ─────────────────────────────────────────────────────────────────

    public static async Task<(int deleted, long bytesFreed)> CleanAsync(
        IEnumerable<CleanupScanResult> results,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        int deleted = 0;
        long freed = 0;

        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Cleaning {result.Target.Label}…");

            var (d, b) = await Task.Run(() => DeleteFiles(result.Files), ct);
            deleted += d;
            freed   += b;
        }

        return (deleted, freed);
    }

    private static (int deleted, long freed) DeleteFiles(IEnumerable<string> files)
    {
        int deleted = 0;
        long freed  = 0;

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                long size = info.Length;
                File.Delete(file);
                deleted++;
                freed += size;
            }
            catch { /* skip locked files */ }
        }

        return (deleted, freed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)        return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
