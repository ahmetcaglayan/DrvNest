using System.Runtime.InteropServices;

namespace Hexnest.Core.Cleanup;

/// <summary>
/// The shell and memory entry points the cleaner needs.
///
/// Two of these exist because .NET has no managed equivalent at all: nothing in the base
/// class library sends a file to the Recycle Bin, and nothing reports how much is in it.
/// The rest is the memory trimming, which is a pair of documented psapi and kernel32
/// calls.
/// </summary>
internal static class CleanupInterop
{
    // =====================================================================================
    // Recycle Bin
    // =====================================================================================

    /// <summary>
    /// SHQUERYRBINFO. The field order is { cbSize, i64Size, i64NumItems } and getting it
    /// the other way round is silent: both are 64-bit integers, so the call succeeds and
    /// reports the byte count as a file count and the file count as a size.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShQueryRbInfo
    {
        public int StructSize;
        public long BinSize;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    internal static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    internal const uint RecycleNoConfirmation = 0x00000001;
    internal const uint RecycleNoProgressUi = 0x00000002;
    internal const uint RecycleNoSound = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
    internal static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);

    // =====================================================================================
    // File operations
    // =====================================================================================

    internal const uint FoDelete = 0x0003;

    internal const ushort FofSilent = 0x0004;
    internal const ushort FofNoConfirmation = 0x0010;
    internal const ushort FofAllowUndo = 0x0040;      // this is what "Recycle Bin" means
    internal const ushort FofNoErrorUi = 0x0400;
    internal const ushort FofNoConfirmMkDir = 0x0200;

    /// <summary>
    /// SHFILEOPSTRUCTW. Pack = 1 is required: the ANSI and Unicode versions of this struct
    /// are packed, and the default alignment produces a layout the shell reads as garbage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    internal struct ShFileOpStruct
    {
        public IntPtr Window;
        public uint Function;

        /// <summary>Double-null-terminated list of source paths.</summary>
        public string From;

        public string? To;
        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    internal static extern int SHFileOperation(ref ShFileOpStruct operation);

    // =====================================================================================
    // Memory
    // =====================================================================================

    /// <summary>
    /// Asks Windows to page out a process' working set.
    ///
    /// This is what every "RAM cleaner" does, and what it actually achieves is narrower
    /// than those tools claim: the pages are written to the page file and read back the
    /// moment the program touches them again. It genuinely frees physical memory right
    /// now; it does not make anything faster, and usually makes the trimmed program
    /// slower for a few seconds afterwards.
    /// </summary>
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>
    /// Trims the system file cache when both size arguments are -1.
    /// Needs SeIncreaseQuotaPrivilege, which an elevated token has.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemFileCacheSize(
        IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

    internal static readonly IntPtr TrimFileCache = new(-1);

    // =====================================================================================
    // Paths
    // =====================================================================================

    /// <summary>
    /// FILE_ATTRIBUTE_REPARSE_POINT. A junction or symlink must never be walked into by a
    /// deleting scan: %LOCALAPPDATA% in particular is full of them, and following one is
    /// how a "clear the cache" feature deletes somebody's documents.
    /// </summary>
    internal const FileAttributes ReparsePoint = FileAttributes.ReparsePoint;
}
