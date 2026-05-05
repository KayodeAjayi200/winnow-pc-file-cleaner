using MediaDevices;
using FileTinder.Models;
using System.IO;

namespace FileTinder.Services;

/// <summary>
/// Wraps the Windows Portable Devices (WPD) API via the MediaDevices library.
/// Provides device enumeration, file listing, preview streaming, and delete.
/// </summary>
public static class MtpDeviceService
{
    // ── Device enumeration ────────────────────────────────────────────────────

    /// <summary>Returns all currently connected MTP/WPD devices.</summary>
    public static List<MtpDeviceInfo> GetConnectedDevices()
    {
        try
        {
            return MediaDevice.GetDevices()
                .Select(d =>
                {
                    try
                    {
                        d.Connect();
                        var info = new MtpDeviceInfo
                        {
                            DeviceId    = d.DeviceId,
                            FriendlyName = d.FriendlyName ?? d.Description ?? d.DeviceId
                        };
                        d.Disconnect();
                        return info;
                    }
                    catch
                    {
                        return new MtpDeviceInfo { DeviceId = d.DeviceId, FriendlyName = d.DeviceId };
                    }
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── Folder browsing ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the top-level storage folders on the device
    /// (e.g. "Internal Storage", "SD Card").
    /// </summary>
    public static List<string> GetRootFolders(string deviceId)
    {
        using var device = OpenDevice(deviceId);
        return device.GetRootDirectory().EnumerateDirectories()
            .Select(d => d.FullName)
            .ToList();
    }

    /// <summary>Returns immediate sub-directories of the given MTP path.</summary>
    public static List<string> GetSubfolders(string deviceId, string path)
    {
        using var device = OpenDevice(deviceId);
        try
        {
            return device.GetDirectoryInfo(path)
                .EnumerateDirectories()
                .Select(d => d.FullName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── File scanning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates files in <paramref name="path"/> on the device,
    /// streaming each <see cref="FileItem"/> back via <paramref name="onFile"/>.
    /// </summary>
    public static async Task ScanAsync(
        string deviceId,
        string path,
        bool recursive,
        FileTypeCategory typeFilter,
        DateFilter dateFilter,
        Action<FileItem> onFile,
        CancellationToken ct)
    {
        var cutoff = GetDateCutoff(dateFilter);

        await RunSta(() =>
        {
            using var device = OpenDevice(deviceId);
            try
            {
                var searchOption = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var file in device.EnumerateFiles(path, "*", searchOption))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info     = device.GetFileInfo(file);
                        var category = FileScanner.ClassifyExtension(
                            System.IO.Path.GetExtension(info.Name));

                        if (typeFilter != FileTypeCategory.All && category != typeFilter)
                            continue;
                        if (cutoff.HasValue && info.LastWriteTime < cutoff.Value)
                            continue;

                        onFile(new FileItem
                        {
                            Name         = info.Name,
                            FullPath     = info.FullName,   // WPD virtual path
                            Size         = (long)info.Length,
                            LastModified = info.LastWriteTime ?? DateTime.MinValue,
                            Category     = category,
                            IsMtp        = true,
                            MtpDeviceId  = deviceId,
                            MtpObjectId  = info.FullName   // use full path as stable ID
                        });
                    }
                    catch { /* skip inaccessible */ }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, ct);
    }

    // ── Download / Preview ────────────────────────────────────────────────────

    private static readonly string TempDir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winnow_preview");

    /// <summary>
    /// Downloads an MTP file to a local temp file and returns its path.
    /// The caller is responsible for deleting the file when done.
    /// </summary>
    public static string? DownloadToTemp(string deviceId, string mtpPath)
    {
        try
        {
            Directory.CreateDirectory(TempDir);
            // Clean old temp previews (keep only 3)
            CleanTempFiles(3);

            var ext      = System.IO.Path.GetExtension(mtpPath);
            var tempFile = System.IO.Path.Combine(TempDir, $"preview_{Guid.NewGuid():N}{ext}");

            using var device = OpenDevice(deviceId);
            using var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
            device.DownloadFile(mtpPath, stream);
            return tempFile;
        }
        catch
        {
            return null;
        }
    }

    private static void CleanTempFiles(int keepCount)
    {
        try
        {
            var files = Directory.GetFiles(TempDir)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(keepCount)
                .ToList();
            foreach (var f in files)
                f.Delete();
        }
        catch { }
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes a file from the device.
    /// MTP does not support Recycle Bin — this is irreversible.
    /// Returns true on success.
    /// </summary>
    public static bool DeleteFile(string deviceId, string mtpPath)
    {
        try
        {
            using var device = OpenDevice(deviceId);
            device.DeleteFile(mtpPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── STA threading helper ──────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="func"/> on a dedicated STA thread and returns its result as a Task.
    /// WPD COM APIs require STA; the ThreadPool uses MTA, which causes silent failures.
    /// </summary>
    public static Task<T> RunSta<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    /// <summary>Void STA runner with cancellation support — used for ScanAsync.</summary>
    public static Task RunSta(Action action, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                action();
                tcs.SetResult();
            }
            catch (OperationCanceledException) { tcs.SetCanceled(ct); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MediaDevice OpenDevice(string deviceId)
    {
        var device = MediaDevice.GetDevices().First(d => d.DeviceId == deviceId);
        device.Connect();
        return device;
    }

    private static DateTime? GetDateCutoff(DateFilter filter) => filter switch
    {
        DateFilter.Last7Days   => DateTime.Now.AddDays(-7),
        DateFilter.Last30Days  => DateTime.Now.AddDays(-30),
        DateFilter.Last6Months => DateTime.Now.AddMonths(-6),
        DateFilter.LastYear    => DateTime.Now.AddYears(-1),
        _                      => null
    };
}

// ── DTO ───────────────────────────────────────────────────────────────────────

public class MtpDeviceInfo
{
    public string DeviceId     { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public override string ToString() => FriendlyName;
}
