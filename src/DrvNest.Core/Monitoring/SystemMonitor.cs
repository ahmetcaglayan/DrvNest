using System.Diagnostics;
using System.Runtime.InteropServices;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Platform;

namespace DrvNest.Core.Monitoring;

/// <summary>
/// Samples the machine on a timer and raises <see cref="Sampled"/> with the result.
///
/// Sampling only runs while something is subscribed and <see cref="Start"/> has been
/// called, so a user who never opens the monitor page pays nothing at all. DrvNest has
/// no background service and this does not become one: the timer stops the moment the
/// page is closed.
///
/// Every reading comes from a system call rather than a performance counter handle.
/// PDH would give the same numbers while adding its own measurable cost to DrvNest's
/// row in its own process table, which would be a slightly absurd thing for a monitor
/// to do.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    /// <summary>How often thermal zones are re-read, in samples.</summary>
    private const int ThermalEveryNSamples = 3;

    /// <summary>How often the processor clock is re-read, in samples.</summary>
    private const int ClockEveryNSamples = 2;

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private System.Threading.Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _running;
    private bool _disposed;
    private int _sampleIndex;

    // Previous processor times, for the deltas.
    private ulong _prevIdle, _prevKernel, _prevUser;
    private long[]? _prevCoreIdle, _prevCoreBusy;
    private double _prevElapsedSeconds;

    // Cached between samples because they are expensive and change slowly.
    private IReadOnlyList<ThermalReading> _temperatures = Array.Empty<ThermalReading>();
    private int? _cpuMhz;
    private int? _cpuBaseMhz;
    private readonly int? _physicalCores = MonitorInterop.CountPhysicalCores();

    private readonly Dictionary<string, (long Read, long Write)> _diskCounters = new(StringComparer.OrdinalIgnoreCase);

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
            _prevIdle = _prevKernel = _prevUser = 0;
            _prevCoreIdle = _prevCoreBusy = null;

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
        // A slow WMI thermal read must never let two samples overlap.
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

        double cpu = ReadTotalCpu();
        var cores = ReadCoreLoads();

        ReadMemory(
            out long totalPhys, out long availPhys,
            out long commitUsed, out long commitLimit,
            out long cached, out long pool,
            out int processes, out int threads, out int handles);

        if (index % ThermalEveryNSamples == 0) _temperatures = ThermalReader.Read();

        if (index % ClockEveryNSamples == 0) ReadClocks(out _cpuMhz, out _cpuBaseMhz);

        return new SystemMetrics
        {
            IntervalSeconds = interval,

            CpuPercent = cpu,
            CoreLoads = cores,
            CpuMhz = _cpuMhz,
            CpuBaseMhz = _cpuBaseMhz,
            ProcessCount = processes,
            ThreadCount = threads,
            HandleCount = handles,

            MemoryTotalBytes = totalPhys,
            MemoryAvailableBytes = availPhys,
            CommitUsedBytes = commitUsed,
            CommitLimitBytes = commitLimit,
            MemoryCachedBytes = cached,
            KernelPoolBytes = pool,

            Disks = ReadDisks(interval),
            Temperatures = _temperatures,
            Battery = ReadBattery(),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),

            CpuName = SystemInfo.Current.ProcessorName ?? "-",
            LogicalProcessors = Environment.ProcessorCount,
            PhysicalCores = _physicalCores,
            OsName = SystemInfo.Current.OsDisplay,
            MachineName = SystemInfo.Current.MachineDisplay
        };
    }

    // =====================================================================================
    // Processor
    // =====================================================================================

    /// <summary>
    /// Total load from the difference in system idle, kernel and user time.
    ///
    /// GetSystemTimes reports kernel time *including* idle time, which is the classic
    /// trap in this calculation: busy = (kernel - idle) + user.
    /// </summary>
    private double ReadTotalCpu()
    {
        if (!MonitorInterop.GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        ulong idleTicks = idle.Ticks;
        ulong kernelTicks = kernel.Ticks;
        ulong userTicks = user.Ticks;

        if (_prevKernel == 0 && _prevUser == 0)
        {
            _prevIdle = idleTicks;
            _prevKernel = kernelTicks;
            _prevUser = userTicks;
            return 0;
        }

        double idleDelta = idleTicks - _prevIdle;
        double kernelDelta = kernelTicks - _prevKernel;
        double userDelta = userTicks - _prevUser;

        _prevIdle = idleTicks;
        _prevKernel = kernelTicks;
        _prevUser = userTicks;

        double total = kernelDelta + userDelta;
        if (total <= 0) return 0;

        return Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
    }

    /// <summary>
    /// Per logical processor load from NtQuerySystemInformation.
    ///
    /// Documented as "may be altered or unavailable in future versions", and it has
    /// been the way every Windows task manager has done this since NT 3.1. A failure
    /// returns an empty list and the per-core strip simply does not render.
    /// </summary>
    private IReadOnlyList<double> ReadCoreLoads()
    {
        int count = Environment.ProcessorCount;
        int size = Marshal.SizeOf<MonitorInterop.SystemProcessorPerformanceInformation>();
        var buffer = Marshal.AllocHGlobal(size * count);

        try
        {
            int status = MonitorInterop.NtQuerySystemInformation(
                MonitorInterop.SystemProcessorPerformanceInformationClass,
                buffer, size * count, out _);

            if (status != 0) return Array.Empty<double>();

            var idle = new long[count];
            var busy = new long[count];

            for (int i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<MonitorInterop.SystemProcessorPerformanceInformation>(
                    buffer + i * size);

                idle[i] = entry.IdleTime;
                busy[i] = entry.KernelTime + entry.UserTime; // KernelTime already contains idle
            }

            if (_prevCoreIdle is null || _prevCoreBusy is null || _prevCoreIdle.Length != count)
            {
                _prevCoreIdle = idle;
                _prevCoreBusy = busy;
                return Array.Empty<double>();
            }

            var loads = new double[count];

            for (int i = 0; i < count; i++)
            {
                double totalDelta = busy[i] - _prevCoreBusy[i];
                double idleDelta = idle[i] - _prevCoreIdle[i];

                loads[i] = totalDelta <= 0
                    ? 0
                    : Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100);
            }

            _prevCoreIdle = idle;
            _prevCoreBusy = busy;

            return loads;
        }
        catch (Exception ex)
        {
            Log.Debug($"Per-core load unavailable: {ex.Message}");
            return Array.Empty<double>();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Current and nominal clock, averaged across the logical processors.</summary>
    private static void ReadClocks(out int? current, out int? nominal)
    {
        current = null;
        nominal = null;

        int count = Environment.ProcessorCount;
        int size = Marshal.SizeOf<MonitorInterop.ProcessorPowerInformation>();
        var buffer = Marshal.AllocHGlobal(size * count);

        try
        {
            if (MonitorInterop.CallNtPowerInformation(
                    MonitorInterop.ProcessorInformation,
                    IntPtr.Zero, 0, buffer, (uint)(size * count)) != 0)
            {
                return;
            }

            long currentSum = 0, maxSum = 0;

            for (int i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<MonitorInterop.ProcessorPowerInformation>(
                    buffer + i * size);

                currentSum += entry.CurrentMhz;
                maxSum += entry.MaxMhz;
            }

            if (currentSum > 0) current = (int)(currentSum / count);
            if (maxSum > 0) nominal = (int)(maxSum / count);
        }
        catch (Exception ex)
        {
            Log.Debug($"Processor clock unavailable: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // =====================================================================================
    // Memory
    // =====================================================================================

    private static void ReadMemory(
        out long totalPhys, out long availPhys,
        out long commitUsed, out long commitLimit,
        out long cached, out long pool,
        out int processes, out int threads, out int handles)
    {
        totalPhys = availPhys = commitUsed = commitLimit = cached = pool = 0;
        processes = threads = handles = 0;

        var status = new MonitorInterop.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MonitorInterop.MemoryStatusEx>()
        };

        if (MonitorInterop.GlobalMemoryStatusEx(ref status))
        {
            totalPhys = (long)status.TotalPhys;
            availPhys = (long)status.AvailPhys;
        }

        int size = Marshal.SizeOf<MonitorInterop.PerformanceInformation>();

        if (MonitorInterop.GetPerformanceInfo(out var info, size))
        {
            long pageSize = info.PageSize.ToInt64();
            if (pageSize <= 0) pageSize = 4096;

            commitUsed = info.CommitTotal.ToInt64() * pageSize;
            commitLimit = info.CommitLimit.ToInt64() * pageSize;
            cached = info.SystemCache.ToInt64() * pageSize;
            pool = info.KernelTotal.ToInt64() * pageSize;

            processes = info.ProcessCount;
            threads = info.ThreadCount;
            handles = info.HandleCount;
        }
    }

    // =====================================================================================
    // Storage
    // =====================================================================================

    /// <summary>
    /// Capacity and throughput for every fixed drive.
    ///
    /// Throughput comes from IOCTL_DISK_PERFORMANCE on the volume, which is the counter
    /// the "Disk" column in Task Manager is built from. It needs no access rights on the
    /// handle, only that the disk performance counters are enabled - they are, by
    /// default, on every Windows since Vista. When the ioctl is refused the rate columns
    /// stay at zero instead of showing an invented number.
    /// </summary>
    private IReadOnlyList<DiskMetrics> ReadDisks(double interval)
    {
        var disks = new List<DiskMetrics>(4);

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var name = drive.Name.TrimEnd('\\');

                double readRate = 0, writeRate = 0;

                if (TryReadVolumeCounters(name, out long read, out long written))
                {
                    if (interval > 0 && _diskCounters.TryGetValue(name, out var previous))
                    {
                        // A counter that went backwards means the volume was remounted;
                        // treat it as a fresh baseline rather than reporting a huge spike.
                        if (read >= previous.Read) readRate = (read - previous.Read) / interval;
                        if (written >= previous.Write) writeRate = (written - previous.Write) / interval;
                    }

                    _diskCounters[name] = (read, written);
                }

                disks.Add(new DiskMetrics
                {
                    Name = name,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    Format = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    ReadBytesPerSecond = readRate,
                    WriteBytesPerSecond = writeRate
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Drive enumeration failed: {ex.Message}");
        }

        return disks;
    }

    /// <summary>Reads the cumulative byte counters for one volume, e.g. "C:".</summary>
    private static bool TryReadVolumeCounters(string volume, out long read, out long written)
    {
        read = written = 0;

        IntPtr buffer = IntPtr.Zero;

        try
        {
            using var handle = MonitorInterop.CreateFile(
                $@"\\.\{volume}",
                0, // No access rights are needed for a metadata ioctl.
                MonitorInterop.FileShareRead | MonitorInterop.FileShareWrite,
                IntPtr.Zero,
                MonitorInterop.OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid) return false;

            int size = Marshal.SizeOf<MonitorInterop.DiskPerformance>();
            buffer = Marshal.AllocHGlobal(size);

            if (!MonitorInterop.DeviceIoControl(
                    handle, MonitorInterop.IoctlDiskPerformance,
                    IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero))
            {
                return false;
            }

            var performance = Marshal.PtrToStructure<MonitorInterop.DiskPerformance>(buffer);

            read = performance.BytesRead;
            written = performance.BytesWritten;
            return true;
        }
        catch
        {
            // A volume that cannot be opened simply has no throughput figure.
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    // =====================================================================================
    // Power
    // =====================================================================================

    private static BatteryMetrics? ReadBattery()
    {
        if (!MonitorInterop.GetSystemPowerStatus(out var status)) return null;

        const byte NoBattery = 128;
        const byte Unknown = 255;
        const byte Charging = 8;

        // A desktop reports "no system battery"; there is nothing to show.
        if ((status.BatteryFlag & NoBattery) != 0) return null;

        return new BatteryMetrics
        {
            Percent = status.BatteryLifePercent == Unknown ? null : status.BatteryLifePercent,
            IsCharging = (status.BatteryFlag & Charging) != 0,
            IsOnMains = status.AcLineStatus == 1,
            Remaining = status.BatteryLifeTime > 0
                ? TimeSpan.FromSeconds(status.BatteryLifeTime)
                : null
        };
    }
}
