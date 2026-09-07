using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;

namespace Hexnest.Core.Monitoring;

/// <summary>
/// Samples the machine's network usage on a timer.
///
/// Same contract as the Windows monitor: nothing is measured until <see cref="Start"/>
/// is called, everything stops on <see cref="Stop"/>, and there is no background
/// service.
///
/// macOS needs two sources rather than one, and the reason is worth writing down.
///
///   Interface counters (NET_RT_IFLIST2, the same data `netstat -ib` prints) give the
///   per adapter totals, the link speed and the addresses. They are exact for wired,
///   tunnel and loopback interfaces. On a Mac with a VPN filter installed - and on some
///   Apple silicon Wi-Fi drivers - the inbound counter for en0 sits at zero, because
///   the packets are accounted for somewhere else before the interface sees them.
///   <see cref="NetworkAdapterMetrics"/> rows in that state are still shown, because
///   they are what the interface really reports.
///
///   nettop gives the live throughput and the per process breakdown. It is a first
///   party tool, it needs no privileges, and it reads the same kernel statistics
///   Activity Monitor's Network tab reads, so the two agree. It is started once when
///   the page opens and streams a sample per second into this class, rather than being
///   run per sample; when it is not available the monitor falls back to the interface
///   counters and says so through <see cref="PerProcessCountersAvailable"/>.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private System.Threading.Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _running;
    private bool _disposed;

    // Adapter totals from the previous sample, keyed by interface index.
    private readonly Dictionary<int, (long Received, long Sent)> _adapterCounters = new();

    // Per process running totals accumulated across the whole watching session.
    private readonly Dictionary<int, ProcessTotals> _processTotals = new();

    private long _sessionDownload;
    private long _sessionUpload;
    private double _prevElapsedSeconds;
    private double _sessionStartedSeconds;

    private readonly int _selfPid = Environment.ProcessId;

    private NettopReader? _nettop;

    private sealed class ProcessTotals
    {
        public string Name = string.Empty;
        public long In;
        public long Out;
        public long DeltaIn;
        public long DeltaOut;
    }

    /// <summary>Raised on a thread pool thread after every sample.</summary>
    public event Action<NetworkMetrics, IReadOnlyList<ProcessNetworkMetrics>>? Sampled;

    public NetworkMetrics? Latest { get; private set; }

    public IReadOnlyList<ProcessNetworkMetrics> LatestProcesses { get; private set; } =
        Array.Empty<ProcessNetworkMetrics>();

    /// <summary>True once nettop has delivered a sample, so per process rows are real.</summary>
    public bool PerProcessCountersAvailable => _nettop?.HasData == true;

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
            _adapterCounters.Clear();

            // The session totals and the session clock are one reading: the page shows
            // "2.0 GB in 00:04" as a single statement. Navigating away and back calls
            // Stop() then Start(), so restarting only the clock would leave the bytes
            // from the previous visit divided by a few seconds.
            ResetSessionCore();

            _nettop = new NettopReader();
            _nettop.Start();

            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, _interval);
        }

        Log.Debug($"Network monitor started at {_interval.TotalMilliseconds:0} ms.");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            _timer?.Dispose();
            _timer = null;

            _nettop?.Dispose();
            _nettop = null;
        }

        Log.Debug("Network monitor stopped.");
    }

    /// <summary>Zeroes the session totals without interrupting the sampling.</summary>
    public void ResetSession()
    {
        lock (_gate) ResetSessionCore();
    }

    /// <summary>The four values that make up "this session". Caller holds the gate.</summary>
    private void ResetSessionCore()
    {
        _sessionDownload = 0;
        _sessionUpload = 0;
        _sessionStartedSeconds = _clock.Elapsed.TotalSeconds;
        _processTotals.Clear();
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }

    // =====================================================================================
    // Sampling
    // =====================================================================================

    private void Tick()
    {
        if (!Monitor.TryEnter(_gate, 0)) return;

        try
        {
            if (!_running) return;

            var (metrics, processes) = Sample();

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

    private (NetworkMetrics, IReadOnlyList<ProcessNetworkMetrics>) Sample()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double interval = _prevElapsedSeconds > 0 ? now - _prevElapsedSeconds : 0;
        _prevElapsedSeconds = now;

        var adapters = ReadAdapters(interval, out long totalIn, out long totalOut,
            out double adapterDown, out double adapterUp);

        // nettop is the better source when it is running: it sees the traffic even when
        // a VPN filter has taken it off the interface counters.
        var perProcess = DrainNettop(interval, out double processDown, out double processUp);

        bool useNettop = _nettop?.HasData == true;

        double download = useNettop ? processDown : adapterDown;
        double upload = useNettop ? processUp : adapterUp;

        if (interval > 0)
        {
            _sessionDownload += (long)Math.Round(download * interval);
            _sessionUpload += (long)Math.Round(upload * interval);
        }

        var metrics = new NetworkMetrics
        {
            IntervalSeconds = interval,
            DownloadBytesPerSecond = download,
            UploadBytesPerSecond = upload,
            SessionDownloadBytes = _sessionDownload,
            SessionUploadBytes = _sessionUpload,
            TotalDownloadBytes = totalIn,
            TotalUploadBytes = totalOut,
            SessionDuration = TimeSpan.FromSeconds(Math.Max(0, now - _sessionStartedSeconds)),
            Adapters = adapters,
            ConnectionCount = CountConnections()
        };

        return (metrics, perProcess);
    }

    // =====================================================================================
    // Adapters
    // =====================================================================================

    /// <summary>
    /// One row per interface that is up and is not loopback, with its rates worked out
    /// from the difference against the previous sample.
    /// </summary>
    private IReadOnlyList<NetworkAdapterMetrics> ReadAdapters(
        double interval, out long totalIn, out long totalOut,
        out double downloadPerSecond, out double uploadPerSecond)
    {
        totalIn = 0;
        totalOut = 0;
        downloadPerSecond = 0;
        uploadPerSecond = 0;

        var rows = new List<NetworkAdapterMetrics>(8);

        try
        {
            var counters = MacNative.ReadInterfaceCounters();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    int index = (int)MacNative.InterfaceIndex(nic.Id);
                    if (index == 0 || !counters.TryGetValue(index, out var current)) continue;

                    // A tunnel or bridge that has never carried a packet is noise on a
                    // page that is meant to show what the machine is doing.
                    bool isUp = nic.OperationalStatus == OperationalStatus.Up;
                    if (!isUp && current.In == 0 && current.Out == 0) continue;

                    double adapterDown = 0, adapterUp = 0;

                    if (_adapterCounters.TryGetValue(index, out var previous) && interval > 0)
                    {
                        // Counters reset when an interface is torn down and rebuilt, and
                        // a negative delta is that, not a burst of traffic.
                        long deltaIn = Math.Max(0, current.In - previous.Received);
                        long deltaOut = Math.Max(0, current.Out - previous.Sent);

                        adapterDown = deltaIn / interval;
                        adapterUp = deltaOut / interval;

                        downloadPerSecond += adapterDown;
                        uploadPerSecond += adapterUp;
                    }

                    _adapterCounters[index] = (current.In, current.Out);

                    totalIn += current.In;
                    totalOut += current.Out;

                    rows.Add(new NetworkAdapterMetrics
                    {
                        Id = nic.Id,
                        Name = nic.Name,
                        Description = DescribeInterface(nic),
                        Kind = nic.NetworkInterfaceType.ToString(),
                        IsUp = isUp,
                        LinkSpeedBps = nic.Speed > 0 ? nic.Speed : null,
                        IpAddress = FirstAddress(nic),
                        MacAddress = FormatMac(nic),
                        DownloadBytesPerSecond = adapterDown,
                        UploadBytesPerSecond = adapterUp,
                        TotalDownloadBytes = current.In,
                        TotalUploadBytes = current.Out
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug($"Adapter skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Adapter list unavailable: {ex.Message}");
        }

        rows.Sort((a, b) =>
        {
            int byState = b.IsUp.CompareTo(a.IsUp);
            if (byState != 0) return byState;

            return (b.TotalDownloadBytes + b.TotalUploadBytes)
                .CompareTo(a.TotalDownloadBytes + a.TotalUploadBytes);
        });

        return rows;
    }

    /// <summary>
    /// A name a person would recognise. macOS calls its interfaces en0 and utun3; the
    /// friendly names ("Wi-Fi", "Thunderbolt Bridge") live in the network preferences
    /// database, so the kind is used instead, which is the part that actually matters.
    /// </summary>
    private static string DescribeInterface(NetworkInterface nic) => nic.NetworkInterfaceType switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet => "Ethernet",
        NetworkInterfaceType.Tunnel => "Tunnel",
        NetworkInterfaceType.Ppp => "PPP",
        _ => nic.NetworkInterfaceType.ToString()
    };

    private static string? FirstAddress(NetworkInterface nic)
    {
        try
        {
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    return address.Address.ToString();
            }
        }
        catch
        {
            // An interface can disappear between being listed and being asked.
        }

        return null;
    }

    private static string? FormatMac(NetworkInterface nic)
    {
        try
        {
            var bytes = nic.GetPhysicalAddress().GetAddressBytes();
            return bytes.Length == 0 ? null : string.Join(':', bytes.Select(b => b.ToString("x2")));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Established TCP connections, machine wide.</summary>
    private static int CountConnections()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length;
        }
        catch (Exception ex)
        {
            Log.Debug($"Connection count unavailable: {ex.Message}");
            return 0;
        }
    }

    // =====================================================================================
    // Per process
    // =====================================================================================

    /// <summary>
    /// Folds whatever nettop has reported since the last sample into the running
    /// per process totals, and returns the rows sorted by current throughput.
    /// </summary>
    private IReadOnlyList<ProcessNetworkMetrics> DrainNettop(
        double interval, out double downloadPerSecond, out double uploadPerSecond)
    {
        downloadPerSecond = 0;
        uploadPerSecond = 0;

        var reader = _nettop;
        if (reader is null || !reader.HasData) return Array.Empty<ProcessNetworkMetrics>();

        var (drained, blocks) = reader.Drain();

        // Nothing arrived this tick: nettop's second has not elapsed yet. Reporting a
        // rate of zero here is what made a steady transfer blink, so the previous
        // reading stands until the next block lands.
        if (blocks == 0 && drained.Count == 0)
        {
            downloadPerSecond = _lastDownloadRate;
            uploadPerSecond = _lastUploadRate;
            return _lastProcessRows;
        }

        // One nettop block is one second, whatever the sampler's own tick gap was.
        double span = blocks > 0 ? blocks : Math.Max(interval, 1);

        foreach (var totals in _processTotals.Values)
        {
            totals.DeltaIn = 0;
            totals.DeltaOut = 0;
        }

        foreach (var (pid, name, bytesIn, bytesOut) in drained)
        {
            if (!_processTotals.TryGetValue(pid, out var totals))
            {
                totals = new ProcessTotals { Name = name };
                _processTotals[pid] = totals;
            }

            if (!string.IsNullOrWhiteSpace(name)) totals.Name = name;

            totals.In += bytesIn;
            totals.Out += bytesOut;
            totals.DeltaIn += bytesIn;
            totals.DeltaOut += bytesOut;
        }

        var rows = new List<ProcessNetworkMetrics>(_processTotals.Count);

        foreach (var (pid, totals) in _processTotals)
        {
            double down = totals.DeltaIn / span;
            double up = totals.DeltaOut / span;

            downloadPerSecond += down;
            uploadPerSecond += up;

            // A process that has moved nothing at all since the page opened is not
            // interesting; one that is merely idle right now still is.
            if (totals.In == 0 && totals.Out == 0) continue;

            rows.Add(new ProcessNetworkMetrics
            {
                ProcessId = pid,
                Name = totals.Name,
                ExecutablePath = null,
                DownloadBytesPerSecond = down,
                UploadBytesPerSecond = up,
                SessionDownloadBytes = totals.In,
                SessionUploadBytes = totals.Out,

                // macOS does not attribute a socket to a process anywhere an
                // unprivileged reader can see it, short of running lsof over the whole
                // machine every second. The page shows no per process connection
                // column rather than a column of zeroes.
                ConnectionCount = 0,

                IsSelf = pid == _selfPid
            });
        }

        rows.Sort((a, b) =>
        {
            int byRate = (b.DownloadBytesPerSecond + b.UploadBytesPerSecond)
                .CompareTo(a.DownloadBytesPerSecond + a.UploadBytesPerSecond);

            return byRate != 0
                ? byRate
                : (b.SessionDownloadBytes + b.SessionUploadBytes)
                    .CompareTo(a.SessionDownloadBytes + a.SessionUploadBytes);
        });

        _lastProcessRows = rows;
        _lastDownloadRate = downloadPerSecond;
        _lastUploadRate = uploadPerSecond;

        return rows;
    }

    /// <summary>
    /// The last reading taken from a tick that actually had a nettop block in it, so a
    /// tick that drains nothing repeats it instead of reporting zero.
    /// </summary>
    private IReadOnlyList<ProcessNetworkMetrics> _lastProcessRows = Array.Empty<ProcessNetworkMetrics>();

    private double _lastDownloadRate;
    private double _lastUploadRate;
}

/// <summary>
/// Reads a long-lived <c>nettop</c> in delta mode and hands out whatever has arrived.
///
/// nettop is run once, in CSV logging mode, and streams one block of rows per second
/// for as long as the page is open. Running it per sample instead would cost a process
/// launch every second and would report cumulative rather than incremental bytes, both
/// of which a monitor can do without.
///
/// The first block is discarded: in delta mode it carries each process' total since it
/// started, not the last second, and folding that into the session totals would open
/// the page with a fabricated spike.
/// </summary>
internal sealed class NettopReader : IDisposable
{
    private const string NettopPath = "/usr/bin/nettop";

    private readonly object _gate = new();
    private readonly List<(int Pid, string Name, long In, long Out)> _pending = new();

    private Process? _process;
    private bool _firstBlockSeen;

    /// <summary>Delta blocks parsed since the last Drain. One block is one second.</summary>
    private int _pendingBlocks;

    /// <summary>True once a real delta block has been parsed.</summary>
    public bool HasData { get; private set; }

    public void Start()
    {
        if (!File.Exists(NettopPath))
        {
            Log.Info("nettop is not present; per process network figures are unavailable.");
            return;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = NettopPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // -P  aggregate per process rather than per connection
            // -x  machine readable output
            // -d  report the difference since the previous sample
            // -J  only the two columns that are needed
            // -L 0 stream for ever, one block per second
            foreach (var argument in new[] { "-P", "-x", "-d", "-J", "bytes_in,bytes_out", "-L", "0" })
                info.ArgumentList.Add(argument);

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => OnLine(e.Data);

            if (!_process.Start())
            {
                Log.Warn("nettop could not be started; falling back to interface counters.");
                _process = null;
                return;
            }

            _process.BeginOutputReadLine();
            Log.Debug("nettop started for per process network figures.");
        }
        catch (Exception ex)
        {
            Log.Warn($"nettop could not be started: {ex.Message}");
            _process = null;
        }
    }

    /// <summary>
    /// Everything parsed since the last call, and how many one-second blocks it spans.
    ///
    /// The block count is what a rate must be divided by. nettop emits one block per
    /// second on its own free-running clock and the sampler ticks on another; when the
    /// two drift past each other, one tick drains no blocks and the next drains two.
    /// Dividing by the sampler's tick gap makes a steady transfer read as nothing and
    /// then as double, once per drift cycle.
    /// </summary>
    public (IReadOnlyList<(int Pid, string Name, long In, long Out)> Rows, int Blocks) Drain()
    {
        lock (_gate)
        {
            int blocks = _pendingBlocks;
            _pendingBlocks = 0;

            if (_pending.Count == 0)
                return (Array.Empty<(int, string, long, long)>(), blocks);

            var copy = _pending.ToArray();
            _pending.Clear();
            return (copy, blocks);
        }
    }

    /// <summary>
    /// Parses one CSV line: "time,name.pid,bytes_in,bytes_out,".
    /// The header line that opens each block has an empty second field.
    /// </summary>
    private void OnLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var fields = line.Split(',');
        if (fields.Length < 4) return;

        var identity = fields[1];

        if (identity.Length == 0)
        {
            // Block header. The first one closes the cumulative opening block.
            if (_firstBlockSeen)
            {
                HasData = true;
                lock (_gate) _pendingBlocks++;
            }

            _firstBlockSeen = true;
            return;
        }

        if (!_firstBlockSeen || !HasData) return;

        // "Microsoft Outlo.10234" - the name may itself contain dots, so the pid is
        // whatever follows the last one.
        int split = identity.LastIndexOf('.');
        if (split <= 0 || split == identity.Length - 1) return;

        if (!int.TryParse(identity[(split + 1)..], out int pid)) return;
        if (!long.TryParse(fields[2], out long bytesIn)) return;
        if (!long.TryParse(fields[3], out long bytesOut)) return;
        if (bytesIn == 0 && bytesOut == 0) return;

        lock (_gate)
        {
            _pending.Add((pid, identity[..split], bytesIn, bytesOut));
        }
    }

    public void Dispose()
    {
        var process = _process;
        _process = null;

        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // It may already have gone; there is nothing useful to do either way.
        }
        finally
        {
            process.Dispose();
        }
    }
}
