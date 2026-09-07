using System.Runtime.InteropServices;
using System.Text;

namespace Hexnest.Core.Platform;

/// <summary>
/// The libSystem entry points the macOS build needs.
///
/// This is the counterpart of <c>Platform/NativeMethods.cs</c> on Windows and it is
/// held to the same rule: every function here is a documented, public, non-deprecated
/// part of the system, and nothing in Hexnest shells out to a command line tool for a
/// number that a system call will give it.
///
/// Two families are used.
///
///   sysctl        Machine facts and a handful of kernel counters. Cheap, stable, and
///                 the same API <c>/usr/sbin/sysctl</c> is a thin wrapper over.
///
///   mach          Live processor and memory statistics. <c>host_statistics64</c> and
///                 <c>host_processor_info</c> are what Activity Monitor reads, so the
///                 numbers Hexnest shows and the numbers Activity Monitor shows come
///                 from the same place and agree.
///
/// libproc supplies the per process figures and lives in <see cref="MacProcNative"/>
/// below, because its structures are large enough to be worth keeping separate.
/// </summary>
internal static class MacNative
{
    private const string LibSystem = "libSystem.dylib";

    // =====================================================================================
    // sysctl
    // =====================================================================================

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int sysctlbyname(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr oldp,
        ref IntPtr oldlenp,
        IntPtr newp,
        IntPtr newlen);

    /// <summary>Reads a string sysctl, e.g. <c>hw.model</c>. Null when the key does not exist.</summary>
    internal static string? SysctlString(string name)
    {
        try
        {
            var length = IntPtr.Zero;
            if (sysctlbyname(name, IntPtr.Zero, ref length, IntPtr.Zero, IntPtr.Zero) != 0) return null;
            if (length == IntPtr.Zero) return null;

            var buffer = Marshal.AllocHGlobal(length);

            try
            {
                if (sysctlbyname(name, buffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0) return null;

                // The kernel includes the terminating NUL in the reported length.
                int bytes = (int)length;
                if (bytes > 0 && Marshal.ReadByte(buffer, bytes - 1) == 0) bytes--;

                var managed = new byte[bytes];
                Marshal.Copy(buffer, managed, 0, bytes);
                return Encoding.UTF8.GetString(managed);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads an integer sysctl of any width, e.g. <c>hw.memsize</c>.</summary>
    internal static long? SysctlInt64(string name)
    {
        try
        {
            var length = (IntPtr)8;
            var buffer = Marshal.AllocHGlobal(8);

            try
            {
                Marshal.WriteInt64(buffer, 0);

                if (sysctlbyname(name, buffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0) return null;

                // Several of these are declared as 32-bit in the kernel, so a raw
                // 64-bit read of a 4-byte value would pick up whatever follows it.
                return (int)length == 4 ? Marshal.ReadInt32(buffer) : Marshal.ReadInt64(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Wall clock time the machine has been up, from <c>kern.boottime</c>.
    ///
    /// Deliberately not <see cref="Environment.TickCount64"/>: that is CLOCK_MONOTONIC
    /// on Unix, which on macOS stops while the lid is shut. On a laptop the two answers
    /// differ by days.
    /// </summary>
    internal static TimeSpan Uptime()
    {
        try
        {
            // struct timeval on LP64: { int64 tv_sec; int32 tv_usec; } padded to 16.
            var length = (IntPtr)16;
            var buffer = Marshal.AllocHGlobal(16);

            try
            {
                if (sysctlbyname("kern.boottime", buffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0)
                    return TimeSpan.Zero;

                long bootSeconds = Marshal.ReadInt64(buffer, 0);
                if (bootSeconds <= 0) return TimeSpan.Zero;

                var booted = DateTimeOffset.FromUnixTimeSeconds(bootSeconds);
                var uptime = DateTimeOffset.UtcNow - booted;

                return uptime > TimeSpan.Zero ? uptime : TimeSpan.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>Swap file usage, from <c>vm.swapusage</c>. All zero when swap is off.</summary>
    internal static (long Total, long Used) SwapUsage()
    {
        try
        {
            // struct xsw_usage { uint64 total; uint64 avail; uint64 used; int32 pagesize; int32 encrypted; }
            var length = (IntPtr)32;
            var buffer = Marshal.AllocHGlobal(32);

            try
            {
                if (sysctlbyname("vm.swapusage", buffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0)
                    return (0, 0);

                return (Marshal.ReadInt64(buffer, 0), Marshal.ReadInt64(buffer, 16));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return (0, 0);
        }
    }

    // =====================================================================================
    // mach: memory
    // =====================================================================================

    /// <summary>
    /// vm_statistics64_data_t. The field order is fixed by the kernel and getting it
    /// wrong is silent, so it is transcribed here in full even though Hexnest reads
    /// only six of the counters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VmStatistics64
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public ulong ZeroFillCount;
        public ulong Reactivations;
        public ulong PageIns;
        public ulong PageOuts;
        public ulong Faults;
        public ulong CowFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong SwapIns;
        public ulong SwapOuts;
        public uint CompressorPageCount;
        public uint ThrottledCount;
        public uint ExternalPageCount;
        public uint InternalPageCount;
        public ulong TotalUncompressedPagesInCompressor;
    }

    /// <summary>HOST_VM_INFO64. sizeof(vm_statistics64_data_t) / sizeof(integer_t).</summary>
    private const int HostVmInfo64 = 4;
    private const int HostVmInfo64Count = 38;

    [DllImport(LibSystem)]
    private static extern IntPtr mach_host_self();

    [DllImport(LibSystem)]
    private static extern int host_statistics64(IntPtr host, int flavor, out VmStatistics64 info, ref int count);

    /// <summary>One sample of the virtual memory system, or null when the call fails.</summary>
    internal static VmStatistics64? ReadVmStatistics()
    {
        try
        {
            int count = HostVmInfo64Count;
            return host_statistics64(mach_host_self(), HostVmInfo64, out var info, ref count) == 0
                ? info
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Virtual memory page size. 16 KB on Apple silicon, 4 KB on Intel.</summary>
    internal static long PageSize { get; } = SysctlInt64("hw.pagesize") ?? 4096;

    // =====================================================================================
    // mach: processor time
    // =====================================================================================

    /// <summary>PROCESSOR_CPU_LOAD_INFO.</summary>
    private const int ProcessorCpuLoadInfo = 2;

    /// <summary>CPU_STATE_USER, CPU_STATE_SYSTEM, CPU_STATE_IDLE, CPU_STATE_NICE.</summary>
    private const int CpuStateMax = 4;

    [DllImport(LibSystem)]
    private static extern int host_processor_info(
        IntPtr host, int flavor, out uint processorCount, out IntPtr info, out int infoCount);

    [DllImport(LibSystem)]
    private static extern int vm_deallocate(IntPtr task, IntPtr address, IntPtr size);

    [DllImport(LibSystem)]
    private static extern IntPtr mach_task_self();

    /// <summary>
    /// Cumulative tick counters per logical processor: user, system, idle, nice.
    ///
    /// Returned as raw totals rather than as percentages, because a load figure is
    /// only meaningful as the difference between two samples and it is the caller
    /// that owns the previous one.
    /// </summary>
    internal static long[][]? ReadProcessorTicks()
    {
        IntPtr info = IntPtr.Zero;
        int infoCount = 0;

        try
        {
            if (host_processor_info(mach_host_self(), ProcessorCpuLoadInfo,
                    out uint processors, out info, out infoCount) != 0)
            {
                return null;
            }

            if (processors == 0 || info == IntPtr.Zero) return null;

            var result = new long[processors][];

            for (int cpu = 0; cpu < processors; cpu++)
            {
                var states = new long[CpuStateMax];

                for (int state = 0; state < CpuStateMax; state++)
                {
                    // natural_t is unsigned 32-bit; widening keeps the arithmetic honest
                    // when a counter passes 2^31.
                    states[state] = (uint)Marshal.ReadInt32(info, (cpu * CpuStateMax + state) * 4);
                }

                result[cpu] = states;
            }

            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info != IntPtr.Zero)
            {
                try { vm_deallocate(mach_task_self(), info, (IntPtr)(infoCount * 4)); }
                catch { /* The kernel reclaims it when the process exits either way. */ }
            }
        }
    }

    internal const int CpuStateUser = 0;
    internal const int CpuStateSystem = 1;
    internal const int CpuStateIdle = 2;
    internal const int CpuStateNice = 3;

    // =====================================================================================
    // Network interface counters
    // =====================================================================================

    /// <summary>
    /// if_data64 as it appears in the NET_RT_IFLIST2 reply. Only the byte counters are
    /// read, but the whole structure is laid out because the offsets depend on it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IfData64
    {
        public byte Type, TypeLen, Physical, AddrLen, HdrLen, RecvQuota, XmitQuota, Unused1;
        public uint Mtu, Metric;
        public ulong Baudrate;
        public ulong InPackets, InErrors, OutPackets, OutErrors, Collisions, InBytes, OutBytes;
        public ulong InMulticast, OutMulticast, InQueueDrops, NoProto;
        public uint RecvTiming, XmitTiming;
        public long LastChangeSeconds;
        public int LastChangeMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IfMsgHdr2
    {
        public ushort MsgLength;
        public byte Version;
        public byte Type;
        public int Addrs;
        public int Flags;
        public ushort Index;
        public int SendQueueLength;
        public int SendQueueMaxLength;
        public int SendQueueDrops;
        public int TimeSinceLastChange;
        public IfData64 Data;
    }

    private const int CtlNet = 4;
    private const int AfRoute = 17;
    private const int NetRtIflist2 = 6;
    private const int RtmIfInfo2 = 0x12;

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int sysctl(
        int[] name, uint nameLength, IntPtr oldp, ref IntPtr oldlenp, IntPtr newp, IntPtr newlen);

    /// <summary>
    /// Cumulative bytes in and out for every interface, keyed by interface index.
    ///
    /// This is the same NET_RT_IFLIST2 data that <c>netstat -ib</c> prints. It is
    /// exact for wired, tunnel and loopback interfaces. Some Wi-Fi drivers on Apple
    /// silicon publish a zero inbound counter here, which is why
    /// <c>Monitoring/Mac/NetworkMonitor.cs</c> does not treat it as the only source.
    /// </summary>
    internal static Dictionary<int, (long In, long Out)> ReadInterfaceCounters()
    {
        var result = new Dictionary<int, (long, long)>();

        try
        {
            // { CTL_NET, PF_ROUTE, 0 (protocol), 0 (family: all), NET_RT_IFLIST2, 0 }
            var mib = new[] { CtlNet, AfRoute, 0, 0, NetRtIflist2, 0 };

            var length = IntPtr.Zero;
            if (sysctl(mib, 6, IntPtr.Zero, ref length, IntPtr.Zero, IntPtr.Zero) != 0) return result;
            if (length == IntPtr.Zero) return result;

            var buffer = Marshal.AllocHGlobal(length);

            try
            {
                if (sysctl(mib, 6, buffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0) return result;

                long total = (long)length;
                long offset = 0;

                while (offset + 4 < total)
                {
                    ushort messageLength = (ushort)Marshal.ReadInt16(buffer, (int)offset);
                    if (messageLength == 0) break;

                    byte type = Marshal.ReadByte(buffer, (int)offset + 3);

                    if (type == RtmIfInfo2 && messageLength >= Marshal.SizeOf<IfMsgHdr2>())
                    {
                        var header = Marshal.PtrToStructure<IfMsgHdr2>(buffer + (int)offset);
                        result[header.Index] = ((long)header.Data.InBytes, (long)header.Data.OutBytes);
                    }

                    offset += messageLength;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // A monitor that cannot read a counter shows nothing, it does not crash.
        }

        return result;
    }

    /// <summary>Interface index for a name such as "en0"; 0 when there is no such interface.</summary>
    [DllImport(LibSystem, EntryPoint = "if_nametoindex")]
    internal static extern uint InterfaceIndex([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // =====================================================================================
    // Identity
    // =====================================================================================

    [DllImport(LibSystem)]
    private static extern uint geteuid();

    /// <summary>True when the process runs as root.</summary>
    internal static bool IsRoot()
    {
        try { return geteuid() == 0; }
        catch { return false; }
    }
}
