using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FileTinder.Services;

/// <summary>
/// Retrieves the Windows Shell thumbnail for any local file using
/// the IShellItemImageFactory COM interface — the same API Explorer uses.
/// </summary>
public static class ShellThumbnailService
{
    // ── COM / P-Invoke declarations ───────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit  = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly   = 0x02,
        IconOnly     = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly  = 0x10,
        CropToSquare = 0x20,
        WideThumbnails = 0x40,
        IconBackground = 0x80,
        ScaleUp      = 0x100,
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a frozen <see cref="BitmapSource"/> for the file's shell thumbnail,
    /// or <c>null</c> if the thumbnail cannot be obtained.
    /// Safe to call from a background thread.
    /// </summary>
    public static BitmapSource? GetThumbnail(string path, int size = 256)
    {
        if (!File.Exists(path)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
        }

        BitmapSource? result = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);

            int hr = factory.GetImage(new SIZE { cx = size, cy = size },
                SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk, out IntPtr hBitmap);

            if (hr == 0 && hBitmap != IntPtr.Zero)
            {
                result = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
                DeleteObject(hBitmap);
            }
        }
        catch { /* return null */ }

        lock (_lock) { _cache[path] = result; }
        return result;
    }

    /// <summary>Clears the in-memory thumbnail cache.</summary>
    public static void ClearCache()
    {
        lock (_lock) _cache.Clear();
    }
}
