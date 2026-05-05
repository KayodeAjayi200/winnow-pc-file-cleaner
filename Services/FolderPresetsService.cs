using System.IO;
using System.Text.Json;
using FileTinder.Models;

namespace FileTinder.Services;

public class FolderPresetsService
{
    private static readonly string _presetPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Winnow", "presets.json");

    private static readonly List<FolderPreset> _defaults =
    [
        new FolderPreset { Name = "Downloads", Path = GetSpecialFolder(Environment.SpecialFolder.UserProfile, "Downloads"), Icon = "⬇" },
        new FolderPreset { Name = "Desktop",   Path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),   Icon = "🖥" },
        new FolderPreset { Name = "Documents", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Icon = "📄" },
        new FolderPreset { Name = "Pictures",  Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),  Icon = "🖼" },
        new FolderPreset { Name = "Videos",    Path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),    Icon = "🎬" },
        new FolderPreset { Name = "Music",     Path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),     Icon = "🎵" },
    ];

    public List<FolderPreset> Load()
    {
        try
        {
            if (File.Exists(_presetPath))
            {
                var json = File.ReadAllText(_presetPath);
                var saved = JsonSerializer.Deserialize<List<FolderPreset>>(json);
                if (saved != null && saved.Count > 0)
                    return saved;
            }
        }
        catch { }

        // Return defaults filtered to paths that exist
        return _defaults
            .Where(p => !string.IsNullOrEmpty(p.Path) && Directory.Exists(p.Path))
            .ToList();
    }

    public void Save(List<FolderPreset> presets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_presetPath)!);
            File.WriteAllText(_presetPath, JsonSerializer.Serialize(presets,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void AddPreset(List<FolderPreset> presets, string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = path;

        if (!presets.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            presets.Add(new FolderPreset { Name = name, Path = path, Icon = "📌" });
            Save(presets);
        }
    }

    public void RemovePreset(List<FolderPreset> presets, FolderPreset preset)
    {
        presets.Remove(preset);
        Save(presets);
    }

    private static string GetSpecialFolder(Environment.SpecialFolder root, string subFolder)
    {
        var rootPath = Environment.GetFolderPath(root);
        return string.IsNullOrEmpty(rootPath) ? string.Empty : Path.Combine(rootPath, subFolder);
    }
}
