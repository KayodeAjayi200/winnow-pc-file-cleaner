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
    private const short FOF_NOERRORUI        = 0x0400;

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
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>
    /// Attempts to restore a file from the Recycle Bin to its original location.
    /// Parses $RECYCLE.BIN\$I* metadata files directly (language-agnostic).
    /// Falls back to Shell COM automation if direct parsing fails.
    /// Returns true if the file was restored.
    /// </summary>
    public static bool RestoreFromRecycleBin(string originalPath, long? originalSize = null)
    {
        return TryRestoreViaRecycleBinFiles(originalPath)
            || TryRestoreViaCom(originalPath);
    }

    // ── Primary: parse $RECYCLE.BIN metadata ─────────────────────────────────

    private static bool TryRestoreViaRecycleBinFiles(string originalPath)
    {
        try
        {
            var driveRoot = System.IO.Path.GetPathRoot(originalPath);
            if (driveRoot == null) return false;

            var recycleBin = System.IO.Path.Combine(driveRoot, "$RECYCLE.BIN");
            if (!Directory.Exists(recycleBin)) return false;

            foreach (var sidDir in Directory.GetDirectories(recycleBin))
            {
                try
                {
                    foreach (var iFile in Directory.GetFiles(sidDir, "$I*"))
                    {
                        try
                        {
                            if (!TryParseIFile(iFile, out var storedPath)) continue;
                            if (!string.Equals(storedPath, originalPath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Derive the matching $R file from the $I name
                            var baseName = System.IO.Path.GetFileNameWithoutExtension(iFile); // "$IXXXXXX"
                            var rBase    = "$R" + baseName[2..];                              // "$RXXXXXX"
                            var ext      = System.IO.Path.GetExtension(iFile);
                            var rFile    = System.IO.Path.Combine(sidDir, rBase + ext);

                            if (!File.Exists(rFile)) continue;

                            var dir = System.IO.Path.GetDirectoryName(originalPath);
                            if (dir != null) Directory.CreateDirectory(dir);

                            File.Move(rFile, originalPath, overwrite: true);
                            try { File.Delete(iFile); } catch { /* non-fatal */ }
                            return true;
                        }
                        catch { /* skip unreadable $I file */ }
                    }
                }
                catch { /* skip inaccessible SID dirs */ }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Parses a Windows $I metadata file to extract the original file path.
    /// Format: 8-byte version, 8-byte size, 8-byte FILETIME, then:
    ///   v1 (Vista–8.1): 260 UTF-16 chars (fixed)
    ///   v2 (Win10+):    4-byte char-count, then N UTF-16 chars
    /// </summary>
    private static bool TryParseIFile(string iFilePath, out string? originalPath)
    {
        originalPath = null;
        try
        {
            using var fs = File.Open(iFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 28) return false;

            using var reader = new BinaryReader(fs, System.Text.Encoding.Unicode);
            long version = reader.ReadInt64(); // signature / version
            reader.ReadInt64();                // original file size
            reader.ReadInt64();                // deletion FILETIME

            if (version == 1) // Vista / 7 / 8 / 8.1
            {
                originalPath = new string(reader.ReadChars(260)).TrimEnd('\0');
            }
            else if (version == 2) // Windows 10 / 11
            {
                int charCount = reader.ReadInt32();
                if (charCount is <= 0 or > 32_768) return false;
                originalPath = new string(reader.ReadChars(charCount)).TrimEnd('\0');
            }
            else { return false; }

            return !string.IsNullOrWhiteSpace(originalPath);
        }
        catch { return false; }
    }

    // ── Fallback: Shell COM automation ────────────────────────────────────────

    private static bool TryRestoreViaCom(string originalPath)
    {
        try
        {
            dynamic shell = System.Activator.CreateInstance(
                System.Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic bin = shell.NameSpace(10); // 10 = Recycle Bin

            var expectedName = System.IO.Path.GetFileName(originalPath);
            var expectedDir  = System.IO.Path.GetDirectoryName(originalPath) ?? string.Empty;

            foreach (var item in bin.Items())
            {
                string itemName     = item.Name;
                string itemLocation = bin.GetDetailsOf(item, 1); // Original Location column

                if (string.Equals(itemName, expectedName, StringComparison.OrdinalIgnoreCase)
                    && itemLocation.Contains(expectedDir, StringComparison.OrdinalIgnoreCase))
                {
                    item.InvokeVerb("undelete");
                    return true;
                }
            }
        }
        catch { }
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
