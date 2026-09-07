using System.Runtime.InteropServices;
using System.Text;

namespace Hexnest.Core.Platform;

/// <summary>
/// libproc: the per process figures behind the macOS process table.
///
/// <see cref="System.Diagnostics.Process"/> would give some of this, but it costs a
/// <c>/proc</c>-style enumeration per property on Unix and it reports
/// <c>PrivateMemorySize64</c> as zero on macOS, which is exactly the column a process
/// table needs. libproc answers all of it in one call per process.
///
/// Reading another user's process requires being that user or being root. Those come
/// back as <see cref="ProcSnapshot.IsRestricted"/> with only the name filled in, and
/// the process table marks the row rather than inventing the missing numbers.
/// </summary>
internal static class MacProcNative
{
    private const string LibProc = "libproc.dylib";
    private const string LibSystem = "libSystem.dylib";

    private const int ProcAllPids = 1;
    private const int ProcPidTaskAllInfo = 2;

    /// <summary>
    /// PROC_PIDT_SHORTBSDINFO. The one flavour the kernel will answer for a process
    /// belonging to another user, which is what makes a restricted row nameable.
    /// </summary>
    private const int ProcPidShortBsdInfo = 13;

    private const int ProcPidPathInfoMaxSize = 4096;

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int proc_listpids(uint type, uint typeInfo, IntPtr buffer, int bufferSize);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize);

    [DllImport(LibProc, SetLastError = true)]
    private static extern int proc_pidpath(int pid, IntPtr buffer, uint bufferSize);

    [DllImport(LibProc, SetLastError = true)]
    private static extern int proc_name(int pid, IntPtr buffer, uint bufferSize);

    // =====================================================================================
    // Structures
    // =====================================================================================

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ProcBsdInfo
    {
        public uint Flags, Status, ExitStatus, Pid, ParentPid;
        public uint Uid, Gid, RealUid, RealGid, SavedUid, SavedGid;
        public uint Reserved;

        /// <summary>MAXCOMLEN. Truncated to 15 characters plus a NUL by the kernel.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Comm;

        /// <summary>2 * MAXCOMLEN. The longer name, when the kernel has one.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public uint OpenFiles, ProcessGroupId, JobControlCount;
        public uint TerminalDevice, TerminalProcessGroupId;
        public int Nice;
        public ulong StartSeconds, StartMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcTaskInfo
    {
        public ulong VirtualSize;
        public ulong ResidentSize;

        /// <summary>Cumulative user time in nanoseconds.</summary>
        public ulong TotalUser;

        /// <summary>Cumulative system time in nanoseconds.</summary>
        public ulong TotalSystem;

        public ulong ThreadsUser, ThreadsSystem;
        public int Policy, Faults, PageIns, CowFaults;
        public int MessagesSent, MessagesReceived, MachSyscalls, UnixSyscalls;
        public int ContextSwitches, ThreadCount, RunningThreads, Priority;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ProcTaskAllInfo
    {
        public ProcBsdInfo Bsd;
        public ProcTaskInfo Task;
    }

    /// <summary>
    /// proc_bsdshortinfo. Carries no memory or processor figures, which is exactly why
    /// the kernel hands it out for any process: there is nothing private in it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ProcBsdShortInfo
    {
        public uint Pid, ParentPid, ProcessGroupId, Status;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Comm;

        public uint Flags, Uid, Gid, RealUid, RealGid, SavedUid, SavedGid, Reserved;
    }

    /// <summary>One process as libproc reports it. Times are nanoseconds.</summary>
    internal sealed class ProcSnapshot
    {
        public int Pid { get; init; }
        public string Name { get; init; } = string.Empty;
        public ulong CpuNanoseconds { get; init; }
        public long ResidentBytes { get; init; }
        public long VirtualBytes { get; init; }
        public int ThreadCount { get; init; }
        public DateTime? StartedAt { get; init; }
        public bool IsRestricted { get; init; }
    }

    // =====================================================================================
    // Enumeration
    // =====================================================================================

    /// <summary>Every process id in the system, in kernel order.</summary>
    internal static int[] ListPids()
    {
        try
        {
            int bytes = proc_listpids(ProcAllPids, 0, IntPtr.Zero, 0);
            if (bytes <= 0) return Array.Empty<int>();

            // The table can grow between the sizing call and the read, so ask for
            // more room than the kernel just said it needed.
            int capacity = bytes + 64 * sizeof(int);
            var buffer = Marshal.AllocHGlobal(capacity);

            try
            {
                int written = proc_listpids(ProcAllPids, 0, buffer, capacity);
                if (written <= 0) return Array.Empty<int>();

                int count = written / sizeof(int);
                var pids = new int[count];
                Marshal.Copy(buffer, pids, 0, count);

                // proc_listpids pads the tail of the buffer with zeroes.
                return pids.Where(p => p > 0).ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Reads one process. Never throws: a process that exits between being listed and
    /// being read is the normal case, not an error.
    /// </summary>
    internal static ProcSnapshot? Read(int pid)
    {
        int size = Marshal.SizeOf<ProcTaskAllInfo>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            int written = proc_pidinfo(pid, ProcPidTaskAllInfo, 0, buffer, size);

            if (written < size)
            {
                // Another user's process, or one that has just gone. The name is still
                // readable through proc_name, so the row can at least be listed.
                var name = ReadName(pid);
                return name is null
                    ? null
                    : new ProcSnapshot { Pid = pid, Name = name, IsRestricted = true };
            }

            var info = Marshal.PtrToStructure<ProcTaskAllInfo>(buffer);

            var displayName = !string.IsNullOrWhiteSpace(info.Bsd.Name)
                ? info.Bsd.Name
                : info.Bsd.Comm;

            DateTime? started = info.Bsd.StartSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds((long)info.Bsd.StartSeconds).LocalDateTime
                : null;

            return new ProcSnapshot
            {
                Pid = pid,
                Name = displayName ?? string.Empty,
                CpuNanoseconds = info.Task.TotalUser + info.Task.TotalSystem,
                ResidentBytes = (long)info.Task.ResidentSize,
                VirtualBytes = (long)info.Task.VirtualSize,
                ThreadCount = info.Task.ThreadCount,
                StartedAt = started,
                IsRestricted = false
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// The short name of a process Hexnest is not allowed to open.
    ///
    /// <c>proc_name</c> is tried first because it returns the longer name, but it needs
    /// the same privileges that have already just been refused, so on a Mac that is not
    /// running as root it fails for every system daemon. The short BSD info is the
    /// fallback that always answers - without it, three hundred of the eight hundred
    /// processes on a normal Mac would silently vanish from the table.
    /// </summary>
    private static string? ReadName(int pid)
    {
        var buffer = Marshal.AllocHGlobal(256);

        try
        {
            int written = proc_name(pid, buffer, 256);

            if (written > 0)
            {
                var name = Marshal.PtrToStringUTF8(buffer);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // Fall through to the short info below.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        int size = Marshal.SizeOf<ProcBsdShortInfo>();
        var shortBuffer = Marshal.AllocHGlobal(size);

        try
        {
            if (proc_pidinfo(pid, ProcPidShortBsdInfo, 0, shortBuffer, size) < size) return null;

            var info = Marshal.PtrToStructure<ProcBsdShortInfo>(shortBuffer);
            return string.IsNullOrWhiteSpace(info.Comm) ? null : info.Comm;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(shortBuffer);
        }
    }

    /// <summary>Full path to a process' executable, or null when it cannot be read.</summary>
    internal static string? ReadPath(int pid)
    {
        var buffer = Marshal.AllocHGlobal(ProcPidPathInfoMaxSize);

        try
        {
            int written = proc_pidpath(pid, buffer, ProcPidPathInfoMaxSize);
            return written > 0 ? Marshal.PtrToStringUTF8(buffer) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Turns "/Applications/Safari.app/Contents/MacOS/Safari" into "Safari".
    ///
    /// A bundle's executable is buried four levels down and its own file name is what
    /// the kernel reports, so the useful name is the one on the .app folder.
    /// </summary>
    internal static string? BundleName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        int marker = executablePath.IndexOf(".app/", StringComparison.Ordinal);
        if (marker < 0) return null;

        var bundle = executablePath[..marker];
        int slash = bundle.LastIndexOf('/');

        return slash >= 0 && slash + 1 < bundle.Length ? bundle[(slash + 1)..] : null;
    }

    /// <summary>Escapes nothing and allocates nothing: used only for log lines.</summary>
    internal static string Describe(ProcSnapshot snapshot)
    {
        var text = new StringBuilder(64);
        text.Append(snapshot.Name).Append(" (").Append(snapshot.Pid).Append(')');
        if (snapshot.IsRestricted) text.Append(" [restricted]");
        return text.ToString();
    }
}
