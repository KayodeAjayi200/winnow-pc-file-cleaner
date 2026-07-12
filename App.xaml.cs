using System.IO;
using System.Windows;

namespace FileTinder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CleanWinnowPreviewFolder();

        // Log unhandled UI-thread exceptions to a temp file so we can diagnose issues
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var log = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Winnow", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
                System.IO.File.AppendAllText(log,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\n\n");
            }
            catch { }
            // Do NOT mark as handled — let WPF show the default error dialog
        };
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