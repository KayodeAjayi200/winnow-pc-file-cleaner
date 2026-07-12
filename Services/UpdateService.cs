using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace FileTinder.Services;

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes);

public static class UpdateService
{
    private const string Owner = "KayodeAjayi200";
    private const string Repo  = "winnow-pc-file-cleaner";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "Winnow" } }
    };

    /// <summary>The version embedded in the running assembly (e.g. "1.0.3").</summary>
    public static string CurrentVersion => CurrentDisplayVersion;

    /// <summary>The release label shown in the UI, formatted as yyyyMMdd.increment.</summary>
    public static string CurrentDisplayVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Hits the GitHub releases API and returns info if a newer version is available.
    /// Returns null on any error so callers never need to handle exceptions.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var url  = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            var json = await _http.GetStringAsync(url);
            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag      = root.GetProperty("tag_name").GetString() ?? "";
            var latest   = tag.TrimStart('v');
            var notes    = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            // Find the installer .exe asset
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (downloadUrl == null) return null;
            return IsNewer(latest, CurrentVersion) ? new UpdateInfo(latest, downloadUrl, notes) : null;
        }
        catch
        {
            return null; // never disrupt the UX
        }
    }

    /// <summary>
    /// Downloads the installer to a temp file, launches it with /SILENT, then shuts down.
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        string downloadUrl,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "WinnowSetup_update.exe");

        using var response = await _http.GetAsync(
            downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file   = File.Create(tempFile);

        var buffer     = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0) progress.Report((int)(downloaded * 100 / total));
        }

        file.Flush();
        file.Close();

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempFile)
        {
            Arguments       = "/SILENT /CLOSEAPPLICATIONS",
            UseShellExecute = true
        });

        System.Windows.Application.Current.Dispatcher.Invoke(
            System.Windows.Application.Current.Shutdown);
    }

    private static bool IsNewer(string latest, string current)
    {
        // Try standard semantic version first
        if (Version.TryParse(Normalise(latest),  out var l) &&
            Version.TryParse(Normalise(current), out var c))
            return l > c;

        // Fall back to component-by-component long comparison (handles yyyyMMdd.N)
        return CompareComponents(latest, current) > 0;
    }

    private static int CompareComponents(string a, string b)
    {
        var pa = a.Split('.').Select(s => long.TryParse(s, out var n) ? n : 0L).ToArray();
        var pb = b.Split('.').Select(s => long.TryParse(s, out var n) ? n : 0L).ToArray();
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            long av = i < pa.Length ? pa[i] : 0L;
            long bv = i < pb.Length ? pb[i] : 0L;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    // Ensure at least two components for Version.Parse
    private static string Normalise(string v) =>
        v.Contains('.') ? v : v + ".0";
}
