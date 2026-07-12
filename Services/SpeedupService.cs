using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace FileTinder.Services;

// ── Models ─────────────────────────────────────────────────────────────────────

public enum StartupLocation { RegistryUser, RegistrySystem, StartupFolder }
public enum PerformanceImpact { High, Medium, Low, Unknown }

public class StartupItem
{
    public string Name { get; set; } = "";
    public string? Command { get; set; }
    public string Key { get; set; } = "";
    public StartupLocation Location { get; set; }
    public bool IsEnabled { get; set; } = true;
    public PerformanceImpact Impact { get; set; } = PerformanceImpact.Unknown;

    public string ImpactLabel => Impact switch
    {
        PerformanceImpact.High    => "High",
        PerformanceImpact.Medium  => "Medium",
        PerformanceImpact.Low     => "Low",
        _                         => "Unknown"
    };

    public string ImpactColor => Impact switch
    {
        PerformanceImpact.High    => "#EF4444",
        PerformanceImpact.Medium  => "#F59E0B",
        PerformanceImpact.Low     => "#10B981",
        _                         => "#6B7280"
    };

    public string LocationLabel => Location switch
    {
        StartupLocation.RegistryUser   => "Registry (User)",
        StartupLocation.RegistrySystem => "Registry (System)",
        StartupLocation.StartupFolder  => "Startup Folder",
        _                              => "Unknown"
    };

    // Startup-folder items can only be toggled by moving the file; skip for now
    public bool CanToggle => Location != StartupLocation.StartupFolder;
}

public class SpeedupHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Action      { get; set; } = "";
    public string ItemName    { get; set; } = "";
    public string Details     { get; set; } = "";
}

// ── Service ────────────────────────────────────────────────────────────────────

public static class SpeedupService
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winnow", "speedup_history.json");

    // Known patterns that typically have measurable boot impact
    private static readonly string[] HighImpact =
    [
        "OneDrive", "Teams", "Spotify", "Discord", "Slack", "Skype",
        "Steam", "EpicGames", "AdobeUpdater", "Creative Cloud",
        "Dropbox", "GoogleDriveFS", "Box", "CCleaner", "Cortana",
        "iTunes", "iCloud", "WhatsApp", "Zoom"
    ];

    private static readonly string[] LowImpact =
    [
        "SecurityHealth", "Windows Security", "Realtek", "NvBackend",
        "igfxTray", "IntelPowerGadget", "RTSS"
    ];

    // ── Read ──────────────────────────────────────────────────────────────────

    public static List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();

        ReadRegistry(
            Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            StartupLocation.RegistryUser, items);

        ReadRegistry(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            StartupLocation.RegistrySystem, items);

        ReadStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            items);

        return items;
    }

    private static void ReadRegistry(
        RegistryKey hive, string runPath, string approvedPath,
        StartupLocation loc, List<StartupItem> items)
    {
        try
        {
            using var runKey = hive.OpenSubKey(runPath, false);
            if (runKey == null) return;

            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var apKey = hive.OpenSubKey(approvedPath, false);
                if (apKey != null)
                {
                    foreach (var vn in apKey.GetValueNames())
                    {
                        // disabled = first byte 0x03; enabled = 0x02
                        if (apKey.GetValue(vn) is byte[] b && b.Length > 0 && b[0] == 0x03)
                            disabled.Add(vn);
                    }
                }
            }
            catch { /* approved key may not exist yet */ }

            foreach (var vn in runKey.GetValueNames())
            {
                var cmd = runKey.GetValue(vn)?.ToString();
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                items.Add(new StartupItem
                {
                    Name      = vn,
                    Command   = cmd,
                    Key       = vn,
                    Location  = loc,
                    IsEnabled = !disabled.Contains(vn),
                    Impact    = ClassifyImpact(vn, cmd)
                });
            }
        }
        catch { /* registry access restricted on this machine */ }
    }

    private static void ReadStartupFolder(string folder, List<StartupItem> items)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.GetFiles(folder, "*.lnk"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                items.Add(new StartupItem
                {
                    Name     = name,
                    Command  = f,
                    Key      = f,
                    Location = StartupLocation.StartupFolder,
                    IsEnabled = true,
                    Impact   = ClassifyImpact(name, f)
                });
            }
        }
        catch { }
    }

    private static PerformanceImpact ClassifyImpact(string name, string? cmd)
    {
        var text = $"{name} {cmd}";
        if (HighImpact.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase)))
            return PerformanceImpact.High;
        if (LowImpact.Any(l => text.Contains(l, StringComparison.OrdinalIgnoreCase)))
            return PerformanceImpact.Low;
        return PerformanceImpact.Medium;
    }

    // ── Toggle ────────────────────────────────────────────────────────────────

    public static bool SetItemEnabled(StartupItem item, bool enable)
    {
        if (!item.CanToggle) return false;

        try
        {
            var hive = item.Location == StartupLocation.RegistryUser
                ? Registry.CurrentUser : Registry.LocalMachine;
            const string approved =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

            using var key = hive.OpenSubKey(approved, true)
                ?? hive.CreateSubKey(approved);
            if (key == null) return false;

            var data = new byte[12];
            data[0] = enable ? (byte)0x02 : (byte)0x03;
            key.SetValue(item.Key, data, RegistryValueKind.Binary);
            item.IsEnabled = enable;
            return true;
        }
        catch { return false; }
    }

    // ── Boot / uptime ─────────────────────────────────────────────────────────

    public static TimeSpan GetUptime() =>
        TimeSpan.FromMilliseconds(Environment.TickCount64);

    public static string FormatUptime(TimeSpan t)
    {
        if (t.TotalDays >= 1)
            return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{t.Minutes}m";
    }

    // ── History ───────────────────────────────────────────────────────────────

    public static List<SpeedupHistoryEntry> GetHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            return JsonSerializer.Deserialize<List<SpeedupHistoryEntry>>(
                       File.ReadAllText(HistoryPath)) ?? [];
        }
        catch { return []; }
    }

    public static void AddHistory(SpeedupHistoryEntry entry)
    {
        try
        {
            var list = GetHistory();
            list.Insert(0, entry);
            if (list.Count > 200) list = [.. list.Take(200)];
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list));
        }
        catch { }
    }
}
