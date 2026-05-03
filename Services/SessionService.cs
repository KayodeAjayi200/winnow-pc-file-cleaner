using System.Text.Json;

namespace FileTinder.Services;

public record SessionState(
    string FolderPath,
    int    CurrentIndex,
    string TypeFilter,
    string DateFilter,
    bool   IncludeSubfolders,
    int    FilesReviewed,
    int    FilesKept,
    int    FilesDeleted,
    long   SpaceFreed
);

public static class SessionService
{
    private static readonly string _appDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileTinder");

    private static readonly string _sessionsFile =
        Path.Combine(_appDataDir, "sessions.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public static SessionState? Load(string folderPath)
    {
        try
        {
            if (!File.Exists(_sessionsFile)) return null;
            var dict = JsonSerializer.Deserialize<Dictionary<string, SessionState>>(
                File.ReadAllText(_sessionsFile));
            return dict?.GetValueOrDefault(Normalize(folderPath));
        }
        catch { return null; }
    }

    public static void Save(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            var dict = ReadAll();
            dict[Normalize(state.FolderPath)] = state;
            File.WriteAllText(_sessionsFile, JsonSerializer.Serialize(dict, _opts));
        }
        catch { }
    }

    public static void Clear(string folderPath)
    {
        try
        {
            var dict = ReadAll();
            if (dict.Remove(Normalize(folderPath)))
                File.WriteAllText(_sessionsFile, JsonSerializer.Serialize(dict, _opts));
        }
        catch { }
    }

    private static Dictionary<string, SessionState> ReadAll()
    {
        try
        {
            if (!File.Exists(_sessionsFile)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, SessionState>>(
                File.ReadAllText(_sessionsFile)) ?? [];
        }
        catch { return []; }
    }

    private static string Normalize(string path) =>
        path.TrimEnd('\\', '/').ToLowerInvariant();
}
