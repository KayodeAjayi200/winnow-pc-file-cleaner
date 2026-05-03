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

    private const int FO_DELETE         = 0x0003;
    private const short FOF_ALLOWUNDO   = 0x0040;
    private const short FOF_NOCONFIRMATION = 0x0010;
    private const short FOF_SILENT      = 0x0004;

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
}
