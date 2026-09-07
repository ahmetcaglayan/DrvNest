using System.Diagnostics;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;

namespace Hexnest.Core.Monitoring;

/// <summary>
/// Samples the machine on a timer and raises <see cref="Sampled"/> with the result.
///
/// The macOS counterpart of <c>Monitoring/SystemMonitor.cs</c>, with the same contract:
/// sampling only runs between <see cref="Start"/> and <see cref="Stop"/>, so a user who
/// never opens the monitor page pays nothing at all, and Hexnest has no background
/// service.
///
/// The rule the Windows monitor is built on holds here too: every field is measured or
/// it is null. macOS will not tell an unprivileged process its die temperature, so
/// <see cref="SystemMetrics.Temperatures"/> comes back empty rather than filled with a
/// number derived from the fan speed. The same goes for the processor clock, which
/// Apple silicon does not publish at all.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    /// <summary>How often the battery is re-read, in samples. It does not move quickly.</summary>
    private const int BatteryEveryNSamples = 15;

    /// <summary>How often the process and thread counts are re-walked, in samples.</summary>
    private const int ProcessCountEveryNSamples = 3;

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private System.Threading.Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _running;
    private bool _disposed;
    private int _sampleIndex;

    // Previous per core tick counters, for the deltas.
    private long[][]? _previousTicks;
    private double _prevElapsedSeconds;

    // Cached between samples because they are relatively expensive.
    private BatteryMetrics? _battery;
    private int _processCount;
    private int _threadCount;

    private readonly long _totalMemory = MacNative.SysctlInt64("hw.memsize") ?? 0;
    private readonly int? _physicalCores = (int?)MacNative.SysctlInt64("hw.physicalcpu");

    /// <summary>
    /// Intel Macs publish a nominal clock; Apple silicon does not publish one at all,
    /// and there is no honest number to put in its place.
    /// </summary>
    private readonly int? _cpuBaseMhz = MacNative.SysctlInt64("hw.cpufrequency") is > 0 and var hz
        ? (int)(hz / 1_000_000)
        : null;

    /// <summary>Raised on a thread pool thread every <see cref="Interval"/>.</summary>
    public event Action<SystemMetrics>? Sampled;

    /// <summary>The most recent sample, or null before the first one.</summary>
    public SystemMetrics? Latest { get; private set; }

    /// <summary>How often a sample is taken. Clamped to between half a second and a minute.</summary>
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
            _sampleIndex = 0;
            _prevElapsedSeconds = 0;
            _previousTicks = null;

            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, _interval);
        }

        Log.Debug($"System monitor started at {_interval.TotalMilliseconds:0} ms.");
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

        Log.Debug("System monitor stopped.");
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
        // A slow battery read must never let two samples overlap.
        if (!Monitor.TryEnter(_gate, 0)) return;

        try
        {
            if (!_running) return;
            var metrics = Sample();
            Latest = metrics;
            Sampled?.Invoke(metrics);
        }
        catch (Exception ex)
        {
            Log.Debug($"System sample failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private SystemMetrics Sample()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double interval = _prevElapsedSeconds > 0 ? now - _prevElapsedSeconds : 0;
        _prevElapsedSeconds = now;

        int index = _sampleIndex++;

        ReadCpu(out double cpu, out var cores);
        ReadMemory(out long available, out long cached, out long swapUsed, out long swapTotal);

        if (index % ProcessCountEveryNSamples == 0) ReadProcessCounts();
        if (index % BatteryEveryNSamples == 0) _battery = ReadBattery();

        var identity = SystemInfo.Current;

        return new SystemMetrics
        {
            IntervalSeconds = interval,

            CpuPercent = cpu,
            CoreLoads = cores,
            CpuMhz = null,
            CpuBaseMhz = _cpuBaseMhz,
            ProcessCount = _processCount,
            ThreadCount = _threadCount,

            // No macOS equivalent of a kernel handle count exists that can be read
            // without walking every file descriptor of every process. Left at zero,
            // and the page does not show a tile for it.
            HandleCount = 0,

            MemoryTotalBytes = _totalMemory,
            MemoryAvailableBytes = available,

            // macOS has no commit charge. Swap is the closest honest analogue: how much
            // the machine has had to push out to disk, against how much it may.
            CommitUsedBytes = swapUsed,
            CommitLimitBytes = swapTotal,
            MemoryCachedBytes = cached,
            KernelPoolBytes = 0,

            Disks = ReadDisks(),

            // Apple exposes no thermal sensor to an unprivileged process. An empty list
            // is what the page is built to show for a machine with no sensor.
            Temperatures = Array.Empty<ThermalReading>(),

            Battery = _battery,
            Uptime = MacNative.Uptime(),

            CpuName = identity.ProcessorName ?? "-",
            LogicalProcessors = Environment.ProcessorCount,
            PhysicalCores = _physicalCores,
            OsName = identity.OsDisplay,
            MachineName = identity.MachineDisplay
        };
    }

    // =====================================================================================
    // Processor
    // =====================================================================================

    /// <summary>
    /// Total and per core load from the difference in mach tick counters.
    ///
    /// busy = user + system + nice, and the denominator is busy + idle. Leaving nice
    /// out is the classic mistake here: on a Mac that is running a background build at
    /// a lowered priority, the missing category is most of the load.
    /// </summary>
    private void ReadCpu(out double total, out IReadOnlyList<double> cores)
    {
        total = 0;
        cores = Array.Empty<double>();

        var ticks = MacNative.ReadProcessorTicks();
        if (ticks is null || ticks.Length == 0) return;

        if (_previousTicks is null || _previousTicks.Length != ticks.Length)
        {
            // The first sample has nothing to subtract from, so it reports nothing
            // rather than reporting the average load since boot.
            _previousTicks = ticks;
            return;
        }

        var loads = new double[ticks.Length];
        double busySum = 0, totalSum = 0;

        for (int cpu = 0; cpu < ticks.Length; cpu++)
        {
            var current = ticks[cpu];
            var previous = _previousTicks[cpu];

            double busy =
                (current[MacNative.CpuStateUser] - previous[MacNative.CpuStateUser]) +
                (current[MacNative.CpuStateSystem] - previous[MacNative.CpuStateSystem]) +
                (current[MacNative.CpuStateNice] - previous[MacNative.CpuStateNice]);

            double idle = current[MacNative.CpuStateIdle] - previous[MacNative.CpuStateIdle];
            double sum = busy + idle;

            loads[cpu] = sum <= 0 ? 0 : Math.Clamp(busy * 100.0 / sum, 0, 100);

            busySum += busy;
            totalSum += sum;
        }

        _previousTicks = ticks;

        total = totalSum <= 0 ? 0 : Math.Clamp(busySum * 100.0 / totalSum, 0, 100);
        cores = loads;
    }

    // =====================================================================================
    // Memory
    // =====================================================================================

    /// <summary>
    /// Physical memory, using Activity Monitor's own definition so that the two agree.
    ///
    ///   used      = (internal - purgeable) + wired + compressed
    ///   cached    = external + purgeable
    ///
    /// "Free" on macOS is close to meaningless - the kernel deliberately keeps almost
    /// none - so what the page shows as available is total minus used, which is the
    /// memory a new application could actually have.
    /// </summary>
    private void ReadMemory(out long available, out long cached, out long swapUsed, out long swapTotal)
    {
        available = 0;
        cached = 0;

        var (total, used) = MacNative.SwapUsage();
        swapTotal = total;
        swapUsed = used;

        var vm = MacNative.ReadVmStatistics();
        if (vm is null) return;

        long page = MacNative.PageSize;
        var stats = vm.Value;

        long app = Math.Max(0, (long)stats.InternalPageCount - stats.PurgeableCount) * page;
        long wired = (long)stats.WireCount * page;
        long compressed = (long)stats.CompressorPageCount * page;

        long inUse = app + wired + compressed;

        cached = ((long)stats.ExternalPageCount + stats.PurgeableCount) * page;
        available = Math.Max(0, _totalMemory - inUse);
    }

    // =====================================================================================
    // Processes
    // =====================================================================================

    /// <summary>
    /// Process and thread counts.
    ///
    /// Walking every process is a few milliseconds, which is cheap enough at this
    /// cadence but not cheap enough to do at every sample. Processes Hexnest may not
    /// open still count towards the process total; their threads cannot be seen and
    /// are not guessed at.
    /// </summary>
    private void ReadProcessCounts()
    {
        try
        {
            var pids = MacProcNative.ListPids();
            _processCount = pids.Length;

            int threads = 0;

            foreach (int pid in pids)
            {
                var snapshot = MacProcNative.Read(pid);
                if (snapshot is { IsRestricted: false }) threads += snapshot.ThreadCount;
            }

            _threadCount = threads;
        }
        catch (Exception ex)
        {
            Log.Debug($"Process count unavailable: {ex.Message}");
        }
    }

    // =====================================================================================
    // Storage
    // =====================================================================================

    /// <summary>
    /// The volumes a person would recognise: the startup disk and anything mounted
    /// under /Volumes.
    ///
    /// macOS mounts a dozen internal APFS volumes - Preboot, Recovery, VM, xarts,
    /// iSCPreboot - that share one container and would otherwise be listed as a dozen
    /// identical 500 GB disks. None of them means anything to the person reading the
    /// page, so none of them is shown.
    ///
    /// Read and write rates are not reported: macOS keeps per-device I/O counters in
    /// IOKit rather than per volume, and there is no correct way to attribute them to
    /// a mount point. The page shows no throughput columns rather than wrong ones.
    /// </summary>
    private static IReadOnlyList<DiskMetrics> ReadDisks()
    {
        var disks = new List<DiskMetrics>(4);

        try
        {
            string? bootName = BootVolumeName();

            foreach (var drive in DriveInfo.GetDrives())
            {
                string name;

                try
                {
                    if (!drive.IsReady) continue;

                    name = drive.Name;

                    bool isBoot = name == "/";
                    bool isMounted = name.StartsWith("/Volumes/", StringComparison.Ordinal);

                    if (!isBoot && !isMounted) continue;

                    // A network or virtual mount has no size to report.
                    if (drive.TotalSize <= 0) continue;

                    string label = isBoot
                        ? bootName ?? "Macintosh HD"
                        : name["/Volumes/".Length..];

                    disks.Add(new DiskMetrics
                    {
                        Name = name,
                        Label = label,
                        Format = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug($"Volume skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Volume list unavailable: {ex.Message}");
        }

        return disks;
    }

    /// <summary>
    /// The startup disk's display name.
    ///
    /// macOS keeps a symbolic link at /Volumes/&lt;name&gt; pointing at "/" for exactly
    /// this purpose, which is cheaper and more reliable than asking diskutil.
    /// </summary>
    private static string? BootVolumeName()
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries("/Volumes"))
            {
                var info = new DirectoryInfo(entry);
                if (info.LinkTarget == "/") return info.Name;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Boot volume name unavailable: {ex.Message}");
        }

        return null;
    }

    // =====================================================================================
    // Battery
    // =====================================================================================

    /// <summary>
    /// Battery state from <c>pmset -g batt</c>.
    ///
    /// IOKit would answer this without a child process, but only through half a dozen
    /// Core Foundation calls whose failure modes are hard to test on a machine that has
    /// no battery. pmset is a first-party tool, it runs in a few milliseconds, and it is
    /// read once every fifteen samples rather than every one.
    ///
    /// Returns null on a desktop Mac, which is what the page expects for a machine with
    /// no battery.
    /// </summary>
    private static BatteryMetrics? ReadBattery()
    {
        try
        {
            var result = ProcessRunner
                .RunAsync("/usr/bin/pmset", new[] { "-g", "batt" }, TimeSpan.FromSeconds(5))
                .GetAwaiter().GetResult();

            if (!result.Success) return null;

            var text = result.StandardOutput;
            if (!text.Contains("InternalBattery", StringComparison.Ordinal)) return null;

            bool onMains = text.Contains("'AC Power'", StringComparison.Ordinal);

            int? percent = null;
            int marker = text.IndexOf('%');

            if (marker > 0)
            {
                int start = marker;
                while (start > 0 && char.IsDigit(text[start - 1])) start--;

                if (start < marker && int.TryParse(text[start..marker], out int value))
                    percent = Math.Clamp(value, 0, 100);
            }

            // pmset says "charging", "discharging", "charged" or "not charging"; the
            // last of those is a full battery on mains, not a fault.
            bool charging = text.Contains("; charging", StringComparison.OrdinalIgnoreCase);

            TimeSpan? remaining = null;

            foreach (var token in text.Split(new[] { ' ', '\t', '\n', '\r' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                // The estimate is printed as "1:47" and as "(no estimate)" until macOS
                // has watched the discharge for long enough to have one.
                if (token.Length is >= 3 and <= 5 && token.Contains(':') &&
                    TimeSpan.TryParse(token, out var parsed) && parsed > TimeSpan.Zero)
                {
                    remaining = parsed;
                    break;
                }
            }

            return new BatteryMetrics
            {
                Percent = percent,
                IsCharging = charging,
                IsOnMains = onMains,
                Remaining = remaining
            };
        }
        catch (Exception ex)
        {
            Log.Debug($"Battery state unavailable: {ex.Message}");
            return null;
        }
    }
}
