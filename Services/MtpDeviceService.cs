using MediaDevices;
using FileTinder.Models;
using System.IO;
using System.Windows.Threading;

namespace FileTinder.Services;

/// <summary>Metadata about an MTP/WPD folder including name, path, file count, and total size.</summary>
public record MtpFolderInfo(string Path, string Name, long Size, int FileCount);

/// <summary>Progress snapshot during a folder-copy operation.</summary>
public record CopyProgress(
    int       FilesCopied,
    int       TotalFiles,
    long      BytesCopied,
    long      TotalBytes,
    string    CurrentFile,
    double    SpeedBps         = 0,
    TimeSpan? Eta              = null,
    int       Errors           = 0,
    string?   LocalPreviewPath = null);

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
                            DeviceId     = d.DeviceId,
                            FriendlyName = d.FriendlyName ?? d.Description ?? d.DeviceId
                        };
                        d.Disconnect();
                        // Do NOT dispose here — GetDevices() may return cached COM proxies;
                        // disposing invalidates the RCW for subsequent OpenDevice() calls.
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

        // Acquire the device mutex for the entire scan so exclusive ops know when
        // it's safe to open a competing connection.
        await _deviceSemaphore.WaitAsync(ct);
        try
        {
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
                                FullPath     = info.FullName,
                                Size         = (long)info.Length,
                                LastModified = info.LastWriteTime ?? DateTime.MinValue,
                                Category     = category,
                                IsMtp        = true,
                                MtpDeviceId  = deviceId,
                                MtpObjectId  = info.FullName
                            });
                        }
                        catch { /* skip inaccessible */ }
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, ct);
        }
        finally
        {
            _deviceSemaphore.Release();
        }
    }

    // ── Device mutex ─────────────────────────────────────────────────────────
    //
    // iPhones (and most MTP devices) only allow ONE WPD connection at a time.
    // _deviceSemaphore is a mutex that every device operation must acquire before
    // opening a connection and release after closing it.
    //
    // ScanAsync holds it for the entire scan.
    // Exclusive ops (download, backup listing, copy) acquire it via YieldDeviceAsync
    // which first fires BeforeExclusiveDeviceAccess (cancels the scan) then WAITS
    // until the semaphore is actually free — i.e. until the scan has truly closed
    // its device connection — rather than using a blind delay.

    private static readonly SemaphoreSlim _deviceSemaphore = new(1, 1);

    // ── Download / Preview ────────────────────────────────────────────────────

    private static readonly string TempDir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winnow_preview");

    /// <summary>
    /// Raised just before an exclusive MTP operation needs sole device access.
    /// Subscribers should cancel any active scan and return immediately.
    /// YieldDeviceAsync will block until the semaphore confirms the scan released the device.
    /// </summary>
    public static event Func<Task>? BeforeExclusiveDeviceAccess;

    /// <summary>
    /// Cancels any active scan then acquires the device semaphore, guaranteeing
    /// the device connection has been released before returning.
    /// The caller MUST release _deviceSemaphore in a finally block after use.
    /// </summary>
    private static async Task YieldDeviceAsync(CancellationToken ct = default)
    {
        // Ask the scanner to cancel
        if (BeforeExclusiveDeviceAccess != null)
            await BeforeExclusiveDeviceAccess.Invoke();

        // Block until the device is actually free (scan has closed its connection).
        // 20 s timeout handles pathological cases (very slow COM calls on the STA).
        bool acquired = await _deviceSemaphore.WaitAsync(20_000, ct);
        if (!acquired)
        {
            // Device didn't free up in time — attempt anyway; worst case the
            // retry loop in the caller will try again.
        }
        // Semaphore is now held by this caller; it must Release() in a finally.
    }

    /// <summary>
    /// Gets the friendly display name of a connected MTP device.
    /// </summary>
    public static string GetDeviceFriendlyName(string deviceId)
    {
        try
        {
            // Run synchronously on a fresh STA thread
            return RunStaFresh<string>(() =>
            {
                var device = MediaDevice.GetDevices().FirstOrDefault(d => d.DeviceId == deviceId);
                if (device == null) return "Device";
                device.Connect();
                var name = device.FriendlyName ?? device.Description ?? "Device";
                device.Disconnect();
                return name;
            }).GetAwaiter().GetResult();
        }
        catch { return "Device"; }
    }

    /// <summary>
    /// Downloads an MTP file to a local temp file, reporting bytes written via
    /// <paramref name="progress"/> (bytesWritten, totalBytes).
    /// Returns the local temp path, or null on failure.
    /// </summary>
    public static async Task<string?> DownloadToTempAsync(
        string deviceId,
        string mtpPath,
        long fileSize,
        IProgress<(long written, long total)>? progress = null,
        CancellationToken ct = default)
    {
        // Cancels any active scan and acquires _deviceSemaphore
        await YieldDeviceAsync(ct);
        try
        {
            return await RunStaFresh<string?>(() =>
            {
                try
                {
                    Directory.CreateDirectory(TempDir);
                    CleanTempFiles(3);

                    var ext      = System.IO.Path.GetExtension(mtpPath);
                    var tempFile = System.IO.Path.Combine(TempDir, $"preview_{Guid.NewGuid():N}{ext}");

                    using var device = OpenDevice(deviceId);
                    using var fsOut  = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 131072);
                    using var prog   = new ProgressStream(fsOut, fileSize, progress, ct);
                    device.DownloadFile(mtpPath, prog);
                    return tempFile;
                }
                catch (OperationCanceledException) { return null; }
                catch { return null; }
            }, ct);
        }
        finally
        {
            _deviceSemaphore.Release();
        }
    }

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

    /// <summary>Wraps a write-only stream and reports bytes-written progress.</summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long   _total;
        private readonly IProgress<(long, long)>? _progress;
        private readonly CancellationToken _ct;
        private long _written;

        public ProgressStream(Stream inner, long total,
            IProgress<(long, long)>? progress, CancellationToken ct)
        {
            _inner    = inner;
            _total    = total;
            _progress = progress;
            _ct       = ct;
        }

        public override bool CanWrite => true;
        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override long Length   => _total;
        public override long Position { get => _written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _ct.ThrowIfCancellationRequested();
            _inner.Write(buffer, offset, count);
            _written += count;
            _progress?.Report((_written, _total));
        }

        public override void Flush()             => _inner.Flush();
        public override int  Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s)   => throw new NotSupportedException();
        public override void SetLength(long v)             => throw new NotSupportedException();
        protected override void Dispose(bool d) { if (d) _inner.Dispose(); base.Dispose(d); }
    }

    // ── Folder metadata ───────────────────────────────────────────────────────

    /// <summary>
    /// Phase 1 (fast): returns sub-directory names immediately — no size calculation.
    /// Call <see cref="CalculateFolderSizesAsync"/> afterwards to fill in sizes.
    /// </summary>
    public static async Task<List<MtpFolderInfo>> GetSubfoldersQuickAsync(
        string deviceId, string path, CancellationToken ct = default)
    {
        await YieldDeviceAsync(ct);
        try
        {
            return await RunStaFresh<List<MtpFolderInfo>>(() =>
            {
                using var device = OpenDevice(deviceId);
                var dir = device.GetDirectoryInfo(path);
                var result = new List<MtpFolderInfo>();
                foreach (var sub in dir.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    result.Add(new MtpFolderInfo(sub.FullName, sub.Name, -1, -1));
                }
                return result;
            }, ct);
        }
        finally
        {
            _deviceSemaphore.Release();
        }
    }

    /// <summary>
    /// Phase 2 (slow): walks each folder and reports (path, size, fileCount) via
    /// <paramref name="onFolderSized"/> as each one finishes — keeps a single
    /// device connection open for the whole batch for performance.
    /// </summary>
    public static async Task CalculateFolderSizesAsync(
        string deviceId,
        IEnumerable<string> folderPaths,
        Action<string, long, int> onFolderSized,
        CancellationToken ct = default)
    {
        await _deviceSemaphore.WaitAsync(ct);
        try
        {
            await RunStaFreshVoid(() =>
            {
                using var device = OpenDevice(deviceId);
                foreach (var folderPath in folderPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    long size  = 0;
                    int  count = 0;
                    try
                    {
                        foreach (var f in device.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            try { var fi = device.GetFileInfo(f); size += (long)fi.Length; count++; }
                            catch { }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }

                    onFolderSized(folderPath, size, count);
                }
            }, ct);
        }
        finally
        {
            _deviceSemaphore.Release();
        }
    }

    // ── Copy to PC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies a list of MTP folders (and all their contents) to a local directory.
    /// Uses a producer-consumer pipeline: the STA thread downloads files into a
    /// bounded channel while the caller thread writes buffered bytes to disk,
    /// overlapping MTP I/O with disk I/O for better throughput.
    /// One device connection is kept for the entire batch to avoid per-file
    /// Connect/Disconnect overhead.
    /// </summary>
    public static async Task CopyFoldersAsync(
        string                  deviceId,
        IList<string>           mtpFolderPaths,
        string                  localDestRoot,
        bool                    skipExisting,
        IProgress<CopyProgress>? progress,
        CancellationToken       ct = default)
    {
        // ── Phase 1: Enumerate all files ──────────────────────────────────────
        // Report a sentinel progress so the UI shows a live "counting" message.
        // TotalFiles = -1 signals "enumeration mode" to the UI.
        progress?.Report(new CopyProgress(0, -1, 0, 0, "Scanning folders on device…"));

        await _deviceSemaphore.WaitAsync(ct);
        List<(string mtpFile, long size)> files;
        try
        {
            files = await RunSta(() =>
            {
                using var device = OpenDevice(deviceId);
                var list = new List<(string mtpFile, long size)>();
                int reportEvery = 25;
                foreach (var folder in mtpFolderPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    string folderName = System.IO.Path.GetFileName(folder.TrimEnd('\\'));
                    try
                    {
                        foreach (var f in device.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var info = device.GetFileInfo(f);
                                list.Add((f, (long)info.Length));
                            }
                            catch { list.Add((f, 0)); }

                            if (list.Count % reportEvery == 0)
                                progress?.Report(new CopyProgress(list.Count, -1, 0, 0,
                                    $"Counting files in {folderName}… {list.Count:N0} found"));
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                return list;
            });
        }
        finally
        {
            _deviceSemaphore.Release();
        }

        long totalBytes = files.Sum(f => f.size);
        int  totalCount = files.Count;
        long bytesDone  = 0;
        int  countDone  = 0;
        int  errorCount = 0;
        var  startTime  = DateTime.UtcNow;

        // ── Phase 2: Pipeline copy ────────────────────────────────────────────
        var channel = System.Threading.Channels.Channel.CreateBounded<
            (string? localPath, byte[]? data, long fileSize, string fileName)>(
            new System.Threading.Channels.BoundedChannelOptions(4)
            {
                FullMode     = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        // Producer: acquire device mutex for the entire copy batch.
        await _deviceSemaphore.WaitAsync(ct);
        var producer = RunSta(() =>
        {
            try
            {
                using var device = OpenDevice(deviceId);
                try
                {
                    foreach (var (mtpFile, fileSize) in files)
                    {
                        if (ct.IsCancellationRequested) break;

                        string localPath = BuildLocalPath(localDestRoot, mtpFile);
                        string fileName  = System.IO.Path.GetFileName(mtpFile);

                        if (skipExisting && System.IO.File.Exists(localPath))
                        {
                            channel.Writer.WriteAsync((null, null, fileSize, fileName))
                                          .AsTask().GetAwaiter().GetResult();
                            continue;
                        }

                        try
                        {
                            var ms = new System.IO.MemoryStream((int)Math.Max(fileSize, 1024));
                            device.DownloadFile(mtpFile, ms);
                            channel.Writer.WriteAsync((localPath, ms.ToArray(), fileSize, fileName))
                                          .AsTask().GetAwaiter().GetResult();
                        }
                        catch
                        {
                            System.Threading.Interlocked.Increment(ref errorCount);
                            channel.Writer.WriteAsync((null, null, fileSize, fileName))
                                          .AsTask().GetAwaiter().GetResult();
                        }
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }
            finally
            {
                _deviceSemaphore.Release();
            }
        });

        // Consumer: reads buffered data and writes to disk asynchronously,
        // overlapping with the next MTP download happening on the STA.
        await foreach (var (localPath, data, fileSize, fileName)
                       in channel.Reader.ReadAllAsync(ct))
        {
            string? previewPath = null;
            if (localPath != null && data != null)
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(localPath)!);
                await System.IO.File.WriteAllBytesAsync(localPath, data, ct);
                if (IsImageExtension(System.IO.Path.GetExtension(localPath)))
                    previewPath = localPath;
            }

            bytesDone += fileSize;
            countDone++;
            double elapsed  = (DateTime.UtcNow - startTime).TotalSeconds;
            double speedBps = elapsed > 0.5 ? bytesDone / elapsed : 0;
            TimeSpan? eta   = speedBps > 0
                ? TimeSpan.FromSeconds((totalBytes - bytesDone) / speedBps)
                : (TimeSpan?)null;
            progress?.Report(new CopyProgress(
                countDone, totalCount, bytesDone, totalBytes,
                fileName, speedBps, eta, errorCount, previewPath));
        }

        await producer; // propagate any unhandled producer exception
    }

    private static readonly HashSet<string> _imageExts =
        new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".webp", ".tiff", ".tif" };

    private static bool IsImageExtension(string ext) => _imageExts.Contains(ext);

    private static string BuildLocalPath(string localDestRoot, string mtpFile)
    {
        // mtpFile:       \Internal Storage\202503_xxx\DCIM\IMG_001.JPG
        // We want:       localDestRoot\202503_xxx\DCIM\IMG_001.JPG
        //
        // Strategy: skip the first two segments (\Internal Storage and possibly
        // a top-level root), keeping from the first "meaningful" folder.
        // We split on backslash, drop empty + the first segment (root/storage name),
        // and keep the rest.
        var parts = mtpFile.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // Drop \Internal Storage (or whichever root storage the user selected)
        // by keeping segments starting at index 1 (i.e. the monthly folder onward).
        // If there are fewer than 2 parts, use the filename directly.
        string relative = parts.Length >= 2
            ? string.Join(System.IO.Path.DirectorySeparatorChar, parts.Skip(1))
            : parts[0];
        return System.IO.Path.Combine(localDestRoot, relative);
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

    // ── Persistent STA thread ─────────────────────────────────────────────────
    //
    // WPD COM objects are STA-bound.  Spinning a new STA thread per call releases
    // the RCW when that thread exits, causing InvalidComObjectException on the next
    // call.  Fix: one long-lived STA thread with a Dispatcher message pump so all
    // WPD COM calls share the same apartment for the app's lifetime.

    private static readonly Lazy<Dispatcher> _staDispatcher = new(() =>
    {
        Dispatcher? d = null;
        var ready = new System.Threading.ManualResetEventSlim();
        var thread = new System.Threading.Thread(() =>
        {
            d = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();   // keeps the thread alive with a COM message pump
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "WinnowMTP-STA";
        thread.Start();
        ready.Wait();
        return d!;
    });

    /// <summary>Runs <paramref name="func"/> on the persistent MTP STA thread (scanner thread).</summary>
    public static Task<T> RunSta<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _staDispatcher.Value.InvokeAsync(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>Void version of RunSta with cancellation support.</summary>
    public static Task RunSta(Action action, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _staDispatcher.Value.InvokeAsync(() =>
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
        return tcs.Task;
    }

    /// <summary>
    /// Runs <paramref name="func"/> on a FRESH STA thread — independent of the
    /// shared scanner STA thread, so backup/preview ops never queue behind a scan.
    /// </summary>
    private static Task<T> RunStaFresh<T>(Func<T> func, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                tcs.SetResult(func());
            }
            catch (OperationCanceledException) { tcs.SetCanceled(ct); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    /// <summary>Void version of RunStaFresh.</summary>
    private static Task RunStaFreshVoid(Action action, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
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
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
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
