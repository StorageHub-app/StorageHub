using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StorageHub.Desktop;

/// <summary>
/// Sends a file or folder to the Recycle Bin, as Explorer's Delete does and as 1.x did.
/// </summary>
/// <remarks>
/// SHFileOperation with FOF_ALLOWUNDO is what puts an item in the Recycle Bin rather than removing
/// it. Silent and without its own confirmation or error UI, because StorageHub has already asked
/// and reports what happened itself.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsRecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    internal static void Send(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            // The list is double-null terminated; the string's own terminator is the first.
            pFrom = fullPath + '\0',
            fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi)
        };

        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.fAnyOperationsAborted)
        {
            throw new IOException(
                $"Windows could not move '{Path.GetFileName(fullPath)}' to the Recycle Bin (0x{result:X}).");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);
}
