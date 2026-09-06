using System.Net;
using System.Runtime.InteropServices;
using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Monitoring;

/// <summary>
/// Per connection byte counters, and the process that owns each connection.
///
/// Windows has no user mode API that says "process X has downloaded Y bytes". What it
/// has is TCP ESTATS (RFC 4898), a per connection statistics block that has to be
/// switched on for each connection individually and then polled, plus
/// GetExtendedTcpTable, which maps every connection to its owning process id. Putting
/// the two together gives real per process traffic without a kernel driver and without
/// an ETW session.
///
/// Two consequences are honest limits rather than bugs, and the UI says so:
///
///   * It is TCP only. UDP has no equivalent counter, so QUIC, most video calls, DNS
///     and some game traffic are not attributed to a process. The machine wide totals
///     in <see cref="NetworkMonitor"/> come from the adapters and do include all of it,
///     which is why the per process column can add up to less than the total.
///   * Counters live and die with the connection. DrvNest accumulates the deltas it
///     observes, so a connection that opens and closes between two samples contributes
///     whatever it transferred while it existed and nothing more.
///
/// Enabling ESTATS needs an elevated token. DrvNest always has one.
/// </summary>
internal static class TcpEstats
{
    // =====================================================================================
    // Interop
    // =====================================================================================

    private const int AfInet = 2;
    private const int AfInet6 = 23;

    /// <summary>TCP_TABLE_OWNER_PID_ALL.</summary>
    private const int TcpTableOwnerPidAll = 5;

    /// <summary>TCP_ESTATS_TYPE.TcpConnectionEstatsData.</summary>
    private const int TcpConnectionEstatsData = 1;

    private const int NoError = 0;
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    /// <summary>MIB_TCPROW_LH: the first five fields of the owner-pid row.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    /// <summary>MIB_TCP6ROW, which puts State first and drops the owning pid.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6Row
    {
        public uint State;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
    }

    /// <summary>TCP_ESTATS_DATA_RW_v0: one BOOLEAN that turns collection on.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EstatsDataRw
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool EnableCollection;
    }

    /// <summary>TCP_ESTATS_DATA_ROD_v0. Only the two byte counters are used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EstatsDataRod
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr table, ref int size, bool order, int addressFamily, int tableClass, int reserved);

    [DllImport("iphlpapi.dll")]
    private static extern int SetPerTcpConnectionEStats(
        ref MibTcpRow row, int estatsType, ref EstatsDataRw rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll")]
    private static extern int GetPerTcpConnectionEStats(
        ref MibTcpRow row, int estatsType,
        IntPtr rw, uint rwVersion, uint rwSize,
        IntPtr ros, uint rosVersion, uint rosSize,
        IntPtr rod, uint rodVersion, uint rodSize);

    [DllImport("iphlpapi.dll")]
    private static extern int SetPerTcp6ConnectionEStats(
        ref MibTcp6Row row, int estatsType, ref EstatsDataRw rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll")]
    private static extern int GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row, int estatsType,
        IntPtr rw, uint rwVersion, uint rwSize,
        IntPtr ros, uint rosVersion, uint rosSize,
        IntPtr rod, uint rodVersion, uint rodSize);

    // =====================================================================================
    // Public shape
    // =====================================================================================

    /// <summary>One live TCP connection and its cumulative byte counters.</summary>
    internal sealed class Connection
    {
        public required string Key { get; init; }
        public int ProcessId { get; init; }
        public string RemoteEndpoint { get; init; } = string.Empty;
        public bool IsEstablished { get; init; }

        /// <summary>Bytes received on this connection since it opened, or 0 without ESTATS.</summary>
        public ulong BytesIn { get; init; }

        public ulong BytesOut { get; init; }

        /// <summary>False when ESTATS could not be read for this connection.</summary>
        public bool HasCounters { get; init; }
    }

    /// <summary>MIB_TCP_STATE.ESTABLISHED.</summary>
    private const uint StateEstablished = 5;

    /// <summary>
    /// Set once enabling ESTATS has failed in a way that will not recover, so the
    /// monitor stops paying for a call it knows is refused.
    /// </summary>
    private static bool _estatsUnavailable;

    internal static bool EstatsAvailable => !_estatsUnavailable;

    /// <summary>
    /// Enumerates every TCP connection with its owning process and byte counters.
    /// Never throws; an interop failure produces a shorter list, not an exception.
    /// </summary>
    internal static IReadOnlyList<Connection> Snapshot()
    {
        var connections = new List<Connection>(256);

        ReadIpv4(connections);
        ReadIpv6(connections);

        return connections;
    }

    // =====================================================================================
    // IPv4
    // =====================================================================================

    private static void ReadIpv4(List<Connection> into)
    {
        IntPtr table = IntPtr.Zero;

        try
        {
            int size = 0;
            int status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);

            if (status != ErrorInsufficientBuffer || size <= 0) return;

            table = Marshal.AllocHGlobal(size);

            status = GetExtendedTcpTable(table, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (status != NoError) return;

            int count = Marshal.ReadInt32(table);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            IntPtr cursor = table + 4;

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(cursor);
                cursor += rowSize;

                // A listening socket has no peer and no traffic worth attributing.
                if (row.State != StateEstablished) continue;

                var key = new MibTcpRow
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort
                };

                bool counted = TryReadIpv4Counters(ref key, out ulong bytesIn, out ulong bytesOut);

                into.Add(new Connection
                {
                    Key = $"4|{row.LocalAddr:X8}:{Port(row.LocalPort)}|{row.RemoteAddr:X8}:{Port(row.RemotePort)}",
                    ProcessId = (int)row.OwningPid,
                    RemoteEndpoint = $"{new IPAddress(BitConverter.GetBytes(row.RemoteAddr))}:{Port(row.RemotePort)}",
                    IsEstablished = true,
                    BytesIn = bytesIn,
                    BytesOut = bytesOut,
                    HasCounters = counted
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"IPv4 connection table unavailable: {ex.Message}");
        }
        finally
        {
            if (table != IntPtr.Zero) Marshal.FreeHGlobal(table);
        }
    }

    private static bool TryReadIpv4Counters(ref MibTcpRow row, out ulong bytesIn, out ulong bytesOut)
    {
        bytesIn = bytesOut = 0;

        if (_estatsUnavailable) return false;

        var enable = new EstatsDataRw { EnableCollection = true };
        uint rwSize = (uint)Marshal.SizeOf<EstatsDataRw>();

        // Enabling an already enabled connection is a no-op, so this can be called
        // every pass rather than tracked separately.
        int set = SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, ref enable, 0, rwSize, 0);

        if (set != NoError)
        {
            // ERROR_ACCESS_DENIED (5) means the process is not elevated; nothing will
            // change that during this run.
            if (set == 5) MarkUnavailable("access denied");
            return false;
        }

        int rodSize = Marshal.SizeOf<EstatsDataRod>();
        IntPtr rod = Marshal.AllocHGlobal(rodSize);

        try
        {
            int get = GetPerTcpConnectionEStats(
                ref row, TcpConnectionEstatsData,
                IntPtr.Zero, 0, 0,
                IntPtr.Zero, 0, 0,
                rod, 0, (uint)rodSize);

            if (get != NoError) return false;

            var data = Marshal.PtrToStructure<EstatsDataRod>(rod);
            bytesIn = data.DataBytesIn;
            bytesOut = data.DataBytesOut;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rod);
        }
    }

    // =====================================================================================
    // IPv6
    // =====================================================================================

    private static void ReadIpv6(List<Connection> into)
    {
        IntPtr table = IntPtr.Zero;

        try
        {
            int size = 0;
            int status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet6, TcpTableOwnerPidAll, 0);

            // A machine with IPv6 disabled returns ERROR_NOT_SUPPORTED; that is normal.
            if (status != ErrorInsufficientBuffer || size <= 0) return;

            table = Marshal.AllocHGlobal(size);

            status = GetExtendedTcpTable(table, ref size, false, AfInet6, TcpTableOwnerPidAll, 0);
            if (status != NoError) return;

            int count = Marshal.ReadInt32(table);
            int rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            IntPtr cursor = table + 4;

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(cursor);
                cursor += rowSize;

                if (row.State != StateEstablished) continue;

                var key = new MibTcp6Row
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalScopeId = row.LocalScopeId,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemoteScopeId = row.RemoteScopeId,
                    RemotePort = row.RemotePort
                };

                bool counted = TryReadIpv6Counters(ref key, out ulong bytesIn, out ulong bytesOut);

                var remote = new IPAddress(row.RemoteAddr, row.RemoteScopeId);

                into.Add(new Connection
                {
                    Key = $"6|{Convert.ToHexString(row.LocalAddr)}:{Port(row.LocalPort)}|" +
                          $"{Convert.ToHexString(row.RemoteAddr)}:{Port(row.RemotePort)}",
                    ProcessId = (int)row.OwningPid,
                    RemoteEndpoint = $"[{remote}]:{Port(row.RemotePort)}",
                    IsEstablished = true,
                    BytesIn = bytesIn,
                    BytesOut = bytesOut,
                    HasCounters = counted
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"IPv6 connection table unavailable: {ex.Message}");
        }
        finally
        {
            if (table != IntPtr.Zero) Marshal.FreeHGlobal(table);
        }
    }

    private static bool TryReadIpv6Counters(ref MibTcp6Row row, out ulong bytesIn, out ulong bytesOut)
    {
        bytesIn = bytesOut = 0;

        if (_estatsUnavailable) return false;

        var enable = new EstatsDataRw { EnableCollection = true };
        uint rwSize = (uint)Marshal.SizeOf<EstatsDataRw>();

        if (SetPerTcp6ConnectionEStats(ref row, TcpConnectionEstatsData, ref enable, 0, rwSize, 0) != NoError)
            return false;

        int rodSize = Marshal.SizeOf<EstatsDataRod>();
        IntPtr rod = Marshal.AllocHGlobal(rodSize);

        try
        {
            int get = GetPerTcp6ConnectionEStats(
                ref row, TcpConnectionEstatsData,
                IntPtr.Zero, 0, 0,
                IntPtr.Zero, 0, 0,
                rod, 0, (uint)rodSize);

            if (get != NoError) return false;

            var data = Marshal.PtrToStructure<EstatsDataRod>(rod);
            bytesIn = data.DataBytesIn;
            bytesOut = data.DataBytesOut;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rod);
        }
    }

    // =====================================================================================

    /// <summary>Ports come back in network byte order packed into a DWORD.</summary>
    private static int Port(uint value) => ((int)(value & 0xFF) << 8) | (int)((value >> 8) & 0xFF);

    private static void MarkUnavailable(string reason)
    {
        if (_estatsUnavailable) return;

        _estatsUnavailable = true;
        Log.Warn($"Per-process network counters are unavailable ({reason}); " +
                 "connection counts are still shown.");
    }
}
