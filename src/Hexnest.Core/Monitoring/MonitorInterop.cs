using System.Runtime.InteropServices;

namespace Hexnest.Core.Monitoring;

/// <summary>
/// The Win32 surface the monitors need.
///
/// Everything here is a documented kernel32 or ntdll entry point that has existed
/// since Windows Vista, so none of it needs a fallback path for the Windows 10 1607
/// floor Hexnest supports. Nothing here allocates a performance counter handle:
/// PDH is a per-process cost that shows up in Hexnest's own CPU column, and the raw
/// system calls give the same numbers for free.
/// </summary>
internal static class MonitorInterop
{
    // =====================================================================================
    // Processor time
    // =====================================================================================

    /// <summary>A FILETIME as two 32-bit halves; the API writes it unaligned.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    /// <summary>
    /// SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION, one per logical processor.
    /// The times are 100 ns units since boot.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessorPerformanceInformation
    {
        public long IdleTime;
        public long KernelTime;   // includes IdleTime, exactly as GetSystemTimes reports it
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    internal const int SystemProcessorPerformanceInformationClass = 8;
    internal const int SystemPerformanceInformationClass = 2;

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    // =====================================================================================
    // Memory
    // =====================================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>
    /// PERFORMANCE_INFORMATION. Page counts, not bytes: multiply by PageSize.
    /// This is where the commit charge and the pool sizes come from.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PerformanceInformation
    {
        public int Size;
        public IntPtr CommitTotal;
        public IntPtr CommitLimit;
        public IntPtr CommitPeak;
        public IntPtr PhysicalTotal;
        public IntPtr PhysicalAvailable;
        public IntPtr SystemCache;
        public IntPtr KernelTotal;
        public IntPtr KernelPaged;
        public IntPtr KernelNonpaged;
        public IntPtr PageSize;
        public int HandleCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    [DllImport("psapi.dll", EntryPoint = "GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(out PerformanceInformation info, int size);

    // =====================================================================================
    // Power
    // =====================================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;       // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;        // 128 no battery, 255 unknown, 8 charging
        public byte BatteryLifePercent; // 255 unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;     // seconds, -1 unknown
        public int BatteryFullLifeTime; // seconds, -1 unknown
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // =====================================================================================
    // Processor frequency
    // =====================================================================================

    /// <summary>
    /// PROCESSOR_POWER_INFORMATION, one per logical processor.
    /// CurrentMhz is what the processor is actually clocked at right now.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    internal const int ProcessorInformation = 11;

    [DllImport("powrprof.dll")]
    internal static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    // =====================================================================================
    // Process I/O
    // =====================================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    // =====================================================================================
    // Volume throughput
    // =====================================================================================

    /// <summary>
    /// DISK_PERFORMANCE. The trailing StorageManagerName[8] is included so the struct
    /// size matches what the driver expects; the fields after QueryTime are not read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DiskPerformance
    {
        public long BytesRead;
        public long BytesWritten;
        public long ReadTime;
        public long WriteTime;
        public long IdleTime;
        public uint ReadCount;
        public uint WriteCount;
        public uint QueueDepth;
        public uint SplitCount;
        public long QueryTime;
        public uint StorageDeviceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string StorageManagerName;
    }

    internal const uint IoctlDiskPerformance = 0x00070020;

    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "CreateFileW")]
    internal static extern SafeVolumeHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeVolumeHandle device,
        uint controlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    /// <summary>A volume handle that closes itself, so a failed ioctl cannot leak one.</summary>
    internal sealed class SafeVolumeHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeVolumeHandle() : base(true) { }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    // =====================================================================================
    // Logical processor topology
    // =====================================================================================

    internal const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref int returnedLength);

    internal const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// Counts physical cores by walking the variable length
    /// SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX list and counting the core relations.
    /// Returns null rather than a guess when the call fails.
    /// </summary>
    internal static int? CountPhysicalCores()
    {
        int length = 0;

        if (GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length))
            return null; // A zero length success would mean there is nothing to read.

        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || length <= 0) return null;

        var buffer = Marshal.AllocHGlobal(length);

        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                return null;

            int cores = 0;
            int offset = 0;

            // Each entry starts with { DWORD Relationship; DWORD Size; }, so the list is
            // walked by its own Size field rather than by sizeof(T).
            while (offset + 8 <= length)
            {
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;

                if (Marshal.ReadInt32(buffer, offset) == RelationProcessorCore) cores++;
                offset += size;
            }

            return cores > 0 ? cores : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
