using System.Diagnostics;
using System.Net.NetworkInformation;
using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Monitoring;

/// <summary>
/// Watches what the machine and its processes are doing on the network.
///
/// Two different measurements, deliberately kept apart because they answer two
/// different questions and disagree by design:
///
///   * The machine wide figure is the sum of the adapters' own byte counters. It is
///     exact and covers everything - TCP, UDP, QUIC, broadcast, the lot.
///   * The per process figure comes from TCP ESTATS (see <see cref="TcpEstats"/>) and
///     covers TCP only. It will normally add up to slightly less than the machine
///     total, and the page says so rather than pretending the two must match.
///
/// Like <see cref="SystemMonitor"/>, nothing runs until <see cref="Start"/> is called
/// and everything stops on <see cref="Stop"/>. There is no background service.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private System.Threading.Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _running;
    private bool _disposed;

    // Adapter totals from the previous sample, keyed by adapter id.
    private readonly Dictionary<string, (long Received, long Sent)> _adapterCounters =
        new(StringComparer.Ordinal);

    // Per connection cumulative counters from the previous sample.
    private readonly Dictionary<string, (ulong In, ulong Out)> _connectionCounters =
        new(StringComparer.Ordinal);

    // Per process running totals accumulated across the whole watching session.
    private readonly Dictionary<int, (long In, long Out)> _processTotals = new();

    private readonly Dictionary<int, string> _processNames = new();

    private long _sessionDownload;
    private long _sessionUpload;
    private long _baselineDownload;
    private long _baselineUpload;
    private bool _hasBaseline;
    private double _prevElapsedSeconds;
    private double _sessionStartedSeconds;

    private readonly int _selfPid = Environment.ProcessId;

    /// <summary>Raised on a thread pool thread after every sample.</summary>
    public event Action<NetworkMetrics, IReadOnlyList<ProcessNetworkMetrics>>? Sampled;

    public NetworkMetrics? Latest { get; private set; }

    public IReadOnlyList<ProcessNetworkMetrics> LatestProcesses { get; private set; } =
        Array.Empty<ProcessNetworkMetrics>();

    /// <summary>True when per-process byte counters could not be enabled.</summary>
    public bool PerProcessCountersAvailable => TcpEstats.EstatsAvailable;

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            var clamped = TimeSpan.FromMilliseconds(
                Math.Clamp(value.TotalMilliseconds, 500, 60_000));

            if (clamped == _interval) return;
            _interval = clamped;

            lock (_gate)
            {
                if (_running) _timer?.Change(TimeSpan.Zero, _interval);
            }
        }
    }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;

            _running = true;
            _prevElapsedSeconds = 0;
            _sessionStartedSeconds = _clock.Elapsed.TotalSeconds;

            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, _interval);
        }

        Log.Debug("Network monitor started.");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            _timer?.Dispose();
            _timer = null;
        }

        Log.Debug("Network monitor stopped.");
    }

    /// <summary>Zeroes the session totals without losing the adapter baselines.</summary>
    public void ResetSession()
    {
        lock (_gate)
        {
            _sessionDownload = 0;
            _sessionUpload = 0;
            _processTotals.Clear();
            _sessionStartedSeconds = _clock.Elapsed.TotalSeconds;
        }
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }

    // =====================================================================================

    private void Tick()
    {
        if (!Monitor.TryEnter(_gate, 0)) return;

        try
        {
            if (!_running) return;

            double now = _clock.Elapsed.TotalSeconds;
            double interval = _prevElapsedSeconds > 0 ? now - _prevElapsedSeconds : 0;
            _prevElapsedSeconds = now;

            var connections = TcpEstats.Snapshot();
            var processes = MeasureProcesses(connections, interval);
            var metrics = MeasureAdapters(interval, now, connections.Count);

            Latest = metrics;
            LatestProcesses = processes;

            Sampled?.Invoke(metrics, processes);
        }
        catch (Exception ex)
        {
            Log.Debug($"Network sample failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    // =====================================================================================
    // Machine wide
    // =====================================================================================

    private NetworkMetrics MeasureAdapters(double interval, double now, int connectionCount)
    {
        double download = 0, upload = 0;
        long totalReceived = 0, totalSent = 0;

        var adapters = new List<NetworkAdapterMetrics>(4);

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Loopback and tunnels would double count traffic that never left the box.
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPInterfaceStatistics statistics;

                try { statistics = nic.GetIPStatistics(); }
                catch { continue; }

                long received = statistics.BytesReceived;
                long sent = statistics.BytesSent;

                totalReceived += received;
                totalSent += sent;

                double downRate = 0, upRate = 0;

                if (interval > 0 && _adapterCounters.TryGetValue(nic.Id, out var previous))
                {
                    // A 32-bit counter wrap or an adapter reset shows as a negative delta.
                    if (received >= previous.Received) downRate = (received - previous.Received) / interval;
                    if (sent >= previous.Sent) upRate = (sent - previous.Sent) / interval;
                }

                _adapterCounters[nic.Id] = (received, sent);

                download += downRate;
                upload += upRate;

                adapters.Add(new NetworkAdapterMetrics
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    Kind = Describe(nic.NetworkInterfaceType),
                    IsUp = nic.OperationalStatus == OperationalStatus.Up,
                    LinkSpeedBps = nic.Speed > 0 ? nic.Speed : null,
                    IpAddress = FirstUnicastAddress(nic),
                    MacAddress = FormatMac(nic.GetPhysicalAddress()),
                    DownloadBytesPerSecond = downRate,
                    UploadBytesPerSecond = upRate,
                    TotalDownloadBytes = received,
                    TotalUploadBytes = sent
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Adapter enumeration failed: {ex.Message}");
        }

        // The session total is measured from the first sample rather than from boot, so
        // "since you opened this page" means exactly that.
        if (!_hasBaseline)
        {
            _baselineDownload = totalReceived;
            _baselineUpload = totalSent;
            _hasBaseline = true;
        }
        else
        {
            _sessionDownload = Math.Max(0, totalReceived - _baselineDownload);
            _sessionUpload = Math.Max(0, totalSent - _baselineUpload);
        }

        adapters.Sort((a, b) =>
        {
            int byState = b.IsUp.CompareTo(a.IsUp);
            if (byState != 0) return byState;

            double aRate = a.DownloadBytesPerSecond + a.UploadBytesPerSecond;
            double bRate = b.DownloadBytesPerSecond + b.UploadBytesPerSecond;
            return bRate.CompareTo(aRate);
        });

        return new NetworkMetrics
        {
            IntervalSeconds = interval,
            DownloadBytesPerSecond = download,
            UploadBytesPerSecond = upload,
            SessionDownloadBytes = _sessionDownload,
            SessionUploadBytes = _sessionUpload,
            TotalDownloadBytes = totalReceived,
            TotalUploadBytes = totalSent,
            SessionDuration = TimeSpan.FromSeconds(Math.Max(0, now - _sessionStartedSeconds)),
            Adapters = adapters,
            ConnectionCount = connectionCount
        };
    }

    // =====================================================================================
    // Per process
    // =====================================================================================

    private IReadOnlyList<ProcessNetworkMetrics> MeasureProcesses(
        IReadOnlyList<TcpEstats.Connection> connections,
        double interval)
    {
        var deltas = new Dictionary<int, (long In, long Out)>();
        var counts = new Dictionary<int, int>();
        var endpoints = new Dictionary<int, List<string>>();

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in connections)
        {
            int pid = connection.ProcessId;

            counts[pid] = counts.GetValueOrDefault(pid) + 1;

            if (!endpoints.TryGetValue(pid, out var list))
            {
                list = new List<string>(8);
                endpoints[pid] = list;
            }

            // Eight is enough to recognise what a process is talking to without turning
            // the detail panel into a packet capture.
            if (list.Count < 8 && !list.Contains(connection.RemoteEndpoint))
                list.Add(connection.RemoteEndpoint);

            if (!connection.HasCounters) continue;

            seen.Add(connection.Key);

            long deltaIn, deltaOut;

            if (_connectionCounters.TryGetValue(connection.Key, out var previous))
            {
                // Counters only ever grow while a connection lives; a smaller value means
                // the tuple was reused by a new connection, so the whole value is new.
                deltaIn = connection.BytesIn >= previous.In
                    ? (long)(connection.BytesIn - previous.In)
                    : (long)connection.BytesIn;

                deltaOut = connection.BytesOut >= previous.Out
                    ? (long)(connection.BytesOut - previous.Out)
                    : (long)connection.BytesOut;
            }
            else
            {
                // First sight of a connection. ESTATS started counting when DrvNest
                // enabled it, so everything it reports happened while we were watching.
                deltaIn = (long)connection.BytesIn;
                deltaOut = (long)connection.BytesOut;
            }

            _connectionCounters[connection.Key] = (connection.BytesIn, connection.BytesOut);

            var current = deltas.GetValueOrDefault(pid);
            deltas[pid] = (current.In + deltaIn, current.Out + deltaOut);
        }

        // Closed connections must not keep their entry: the tuple can be reused.
        if (_connectionCounters.Count > seen.Count)
        {
            foreach (var key in _connectionCounters.Keys.Where(k => !seen.Contains(k)).ToList())
                _connectionCounters.Remove(key);
        }

        var rows = new List<ProcessNetworkMetrics>(counts.Count);

        foreach (var (pid, connectionCount) in counts)
        {
            var delta = deltas.GetValueOrDefault(pid);

            var total = _processTotals.GetValueOrDefault(pid);
            total = (total.In + delta.In, total.Out + delta.Out);
            _processTotals[pid] = total;

            rows.Add(new ProcessNetworkMetrics
            {
                ProcessId = pid,
                Name = ResolveName(pid),
                DownloadBytesPerSecond = interval > 0 ? delta.In / interval : 0,
                UploadBytesPerSecond = interval > 0 ? delta.Out / interval : 0,
                SessionDownloadBytes = total.In,
                SessionUploadBytes = total.Out,
                ConnectionCount = connectionCount,
                RemoteEndpoints = endpoints.GetValueOrDefault(pid) ?? (IReadOnlyList<string>)Array.Empty<string>(),
                IsSelf = pid == _selfPid
            });
        }

        rows.Sort((a, b) =>
        {
            double aRate = a.DownloadBytesPerSecond + a.UploadBytesPerSecond;
            double bRate = b.DownloadBytesPerSecond + b.UploadBytesPerSecond;

            int byRate = bRate.CompareTo(aRate);
            if (byRate != 0) return byRate;

            long aTotal = a.SessionDownloadBytes + a.SessionUploadBytes;
            long bTotal = b.SessionDownloadBytes + b.SessionUploadBytes;
            return bTotal.CompareTo(aTotal);
        });

        return rows;
    }

    /// <summary>
    /// Process names are cached: opening a process handle is far more expensive than the
    /// rest of the sample put together, and a pid's name cannot change while it lives.
    /// </summary>
    private string ResolveName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var cached)) return cached;

        string name;

        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch
        {
            // pid 0 is the system idle process and 4 is the kernel; neither can be opened.
            name = pid switch
            {
                0 => "System Idle",
                4 => "System",
                _ => $"PID {pid}"
            };
        }

        // Bounded so a machine that churns through processes cannot grow this forever.
        if (_processNames.Count > 2048) _processNames.Clear();

        _processNames[pid] = name;
        return name;
    }

    // =====================================================================================

    private static string Describe(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ppp => "PPP",
        NetworkInterfaceType.Wman or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile",
        _ => type.ToString()
    };

    private static string? FirstUnicastAddress(NetworkInterface nic)
    {
        try
        {
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return address.Address.ToString();
            }
        }
        catch
        {
            // An adapter that is going down can fail here; it simply has no address to show.
        }

        return null;
    }

    private static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join(':', bytes.Select(b => b.ToString("X2")));
    }
}
