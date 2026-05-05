using System.Runtime.InteropServices;

namespace FileTinder.Services;

public static class RecycleBinService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)] public int wFunc;
        public string pFrom;
        public string pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    private const int FO_DELETE              = 0x0003;
    private const short FOF_ALLOWUNDO        = 0x0040;
    private const short FOF_NOCONFIRMATION   = 0x0010;
    private const short FOF_SILENT           = 0x0004;

    private const uint SHERB_NOCONFIRMATION  = 0x00000001;
    private const uint SHERB_NOPROGRESSUI    = 0x00000002;
    private const uint SHERB_NOSOUND         = 0x00000004;

    /// <summary>Sends a file to the Recycle Bin silently. Returns true on success.</summary>
    public static bool SendToRecycleBin(string filePath)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd   = IntPtr.Zero,
            wFunc  = FO_DELETE,
            pFrom  = filePath + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
        };
        return SHFileOperation(ref op) == 0;
    }

    /// <summary>
    /// Attempts to restore a file from the Recycle Bin to its original location.
    /// Uses Shell32 COM automation. Returns true if restored.
    /// </summary>
    public static bool RestoreFromRecycleBin(string originalPath)
    {
        try
        {
            dynamic shell = System.Activator.CreateInstance(
                System.Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic bin = shell.NameSpace(10); // 10 = Recycle Bin

            foreach (var item in bin.Items())
            {
                // Column 1 = original location
                string originalLocation = bin.GetDetailsOf(item, 1);
                string name = item.Name;
                var expected = System.IO.Path.GetDirectoryName(originalPath) ?? string.Empty;
                var expectedName = System.IO.Path.GetFileName(originalPath);

                if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)
                    && originalLocation.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    item.InvokeVerb("undelete");
                    return true;
                }
            }
        }
        catch { /* COM errors or locale differences */ }
        return false;
    }

    /// <summary>Empties the Recycle Bin silently. Returns true on success.</summary>
    public static bool EmptyRecycleBin()
    {
        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        return hr == 0 || hr == unchecked((int)0x80070057); // S_OK or E_INVALIDARG (already empty)
    }

    /// <summary>Returns the total size in bytes currently in the Recycle Bin.</summary>
    public static long GetRecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.i64Size : 0;
    }
}
