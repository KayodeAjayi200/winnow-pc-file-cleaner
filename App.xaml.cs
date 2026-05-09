using System.IO;
using System.Windows;

namespace FileTinder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CleanWinnowPreviewFolder();
    }

    private static void CleanWinnowPreviewFolder()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "winnow_preview");
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try { File.Delete(file); } catch { /* skip locked files */ }
            }
        }
        catch { }
    }
}