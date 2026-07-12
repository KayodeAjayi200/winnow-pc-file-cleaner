using System.Diagnostics;
using System.Text.Json;

namespace FileTinder.Services;

public enum ScheduleFrequency { None, Daily, Weekly }

public class CleanupSchedule
{
    public bool IsEnabled { get; set; }
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
    public int Hour { get; set; } = 3;
    public int Minute { get; set; } = 0;
    public DayOfWeek WeekDay { get; set; } = DayOfWeek.Sunday;
}

public static class ScheduledCleanupService
{
    private const string TaskName = "WinnowScheduledCleanup";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winnow", "cleanup_schedule.json");

    // ── Persistence ───────────────────────────────────────────────────────────

    public static CleanupSchedule Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<CleanupSchedule>(File.ReadAllText(SettingsPath))
                       ?? new CleanupSchedule();
        }
        catch { }
        return new CleanupSchedule();
    }

    private static void Save(CleanupSchedule s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    public static (bool ok, string message) Apply(CleanupSchedule schedule)
    {
        Delete();

        if (!schedule.IsEnabled)
        {
            Save(schedule);
            return (true, "Scheduled cleanup disabled.");
        }

        var script = BuildScript();
        var encoded = ToBase64(script);
        var time = $"{schedule.Hour:D2}:{schedule.Minute:D2}";

        string schedArgs = schedule.Frequency == ScheduleFrequency.Weekly
            ? $"/SC WEEKLY /D {schedule.WeekDay.ToString()[..3].ToUpper()} /ST {time}"
            : $"/SC DAILY /ST {time}";

        var args = $"/Create /TN \"{TaskName}\" {schedArgs} " +
                   $"/TR \"powershell.exe -WindowStyle Hidden -NonInteractive -EncodedCommand {encoded}\" " +
                   "/F";

        var (code, output) = RunSchtasks(args);

        if (code != 0)
            return (false, $"Could not create task: {output.Trim()}");

        Save(schedule);

        var when = schedule.Frequency == ScheduleFrequency.Daily
            ? $"daily at {time}"
            : $"weekly on {schedule.WeekDay} at {time}";
        return (true, $"Scheduled cleanup will run {when}.");
    }

    public static bool IsRegistered()
    {
        var (code, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return code == 0;
    }

    public static void Delete()
    {
        try { RunSchtasks($"/Delete /TN \"{TaskName}\" /F"); }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (int exitCode, string output) RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true
            };
            using var p = Process.Start(psi)!;
            var out_ = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            return (p.ExitCode, out_);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    private static string BuildScript() => @"
$targets = @(
    [IO.Path]::GetTempPath(),
    ($env:TEMP),
    ($env:LOCALAPPDATA + '\Temp'),
    'C:\Windows\Temp',
    'C:\Windows\Prefetch',
    ($env:LOCALAPPDATA + '\Microsoft\Windows\INetCache')
) | Sort-Object -Unique
foreach ($p in $targets) {
    if (Test-Path $p) {
        try { Get-ChildItem $p -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue }
        catch {}
    }
}
";

    private static string ToBase64(string s) =>
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(s));
}
