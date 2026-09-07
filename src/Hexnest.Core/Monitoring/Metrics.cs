namespace Hexnest.Core.Monitoring;

/// <summary>
/// One sample of the whole machine, taken by <see cref="SystemMonitor"/>.
///
/// Every field is either measured or null. Nothing here is estimated, interpolated
/// or carried over from an earlier sample: a value the machine will not report is
/// reported as "not available" rather than as a plausible looking number.
/// </summary>
public sealed class SystemMetrics
{
    /// <summary>When the sample was taken, local time.</summary>
    public DateTime TakenAt { get; init; } = DateTime.Now;

    /// <summary>Seconds since the previous sample; 0 on the very first one.</summary>
    public double IntervalSeconds { get; init; }

    // --- processor ---------------------------------------------------------------

    /// <summary>Total processor load, 0-100.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Per logical processor load, 0-100, in Windows' own core order.</summary>
    public IReadOnlyList<double> CoreLoads { get; init; } = Array.Empty<double>();

    /// <summary>Current clock in MHz when the machine reports one.</summary>
    public int? CpuMhz { get; init; }

    /// <summary>Nominal clock in MHz from the firmware.</summary>
    public int? CpuBaseMhz { get; init; }

    /// <summary>Live process count.</summary>
    public int ProcessCount { get; init; }

    /// <summary>Live thread count across all processes.</summary>
    public int ThreadCount { get; init; }

    /// <summary>Open kernel handle count across all processes.</summary>
    public int HandleCount { get; init; }

    // --- memory ------------------------------------------------------------------

    public long MemoryTotalBytes { get; init; }
    public long MemoryAvailableBytes { get; init; }
    public long MemoryUsedBytes => Math.Max(0, MemoryTotalBytes - MemoryAvailableBytes);

    public double MemoryPercent =>
        MemoryTotalBytes > 0 ? MemoryUsedBytes * 100.0 / MemoryTotalBytes : 0;

    /// <summary>Committed bytes, i.e. the commit charge Task Manager calls "Committed".</summary>
    public long CommitUsedBytes { get; init; }

    public long CommitLimitBytes { get; init; }

    /// <summary>Standby plus modified pages: what Task Manager labels "Cached".</summary>
    public long MemoryCachedBytes { get; init; }

    /// <summary>Non-paged plus paged pool.</summary>
    public long KernelPoolBytes { get; init; }

    // --- storage -----------------------------------------------------------------

    public IReadOnlyList<DiskMetrics> Disks { get; init; } = Array.Empty<DiskMetrics>();

    // --- thermal and power -------------------------------------------------------

    /// <summary>
    /// Every temperature sensor the machine actually exposes. Empty on the many
    /// desktops whose firmware publishes no ACPI thermal zone at all.
    /// </summary>
    public IReadOnlyList<ThermalReading> Temperatures { get; init; } = Array.Empty<ThermalReading>();

    /// <summary>The hottest reading, or null when there is no sensor.</summary>
    public ThermalReading? HottestTemperature =>
        Temperatures.Count == 0 ? null : Temperatures.MaxBy(t => t.Celsius);

    public BatteryMetrics? Battery { get; init; }

    /// <summary>How long the machine has been running.</summary>
    public TimeSpan Uptime { get; init; }

    // --- machine identity, copied once so the view needs one binding source ------

    public string CpuName { get; init; } = string.Empty;
    public int LogicalProcessors { get; init; }
    public int? PhysicalCores { get; init; }
    public string OsName { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
}

/// <summary>One temperature sensor.</summary>
/// <param name="Name">Sensor name as the firmware reports it, e.g. "THRM" or "TZ00".</param>
/// <param name="Celsius">Temperature in degrees Celsius.</param>
/// <param name="Source">Where the reading came from, for the tooltip.</param>
public sealed record ThermalReading(string Name, double Celsius, string Source)
{
    public double Fahrenheit => Celsius * 9 / 5 + 32;

    /// <summary>
    /// A coarse band the UI colours by. The thresholds are deliberately generous:
    /// modern silicon is designed to sit at 80 C under load and throttles itself
    /// long before anything is at risk.
    /// </summary>
    public ThermalBand Band => Celsius switch
    {
        < 60 => ThermalBand.Cool,
        < 80 => ThermalBand.Warm,
        < 90 => ThermalBand.Hot,
        _ => ThermalBand.Critical
    };
}

public enum ThermalBand
{
    Cool = 0,
    Warm = 1,
    Hot = 2,
    Critical = 3
}

/// <summary>One fixed drive.</summary>
public sealed class DiskMetrics
{
    public string Name { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Format { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100.0 / TotalBytes : 0;

    /// <summary>Bytes read per second since the previous sample, when measurable.</summary>
    public double ReadBytesPerSecond { get; init; }

    /// <summary>Bytes written per second since the previous sample, when measurable.</summary>
    public double WriteBytesPerSecond { get; init; }
}

/// <summary>Battery state, or null on a machine that has no battery.</summary>
public sealed class BatteryMetrics
{
    /// <summary>Charge level 0-100, or null when the firmware will not say.</summary>
    public int? Percent { get; init; }

    public bool IsCharging { get; init; }
    public bool IsOnMains { get; init; }

    /// <summary>Estimated runtime left, or null when Windows has no estimate yet.</summary>
    public TimeSpan? Remaining { get; init; }
}

// =====================================================================================
// Per process
// =====================================================================================

/// <summary>
/// What one running process is costing the machine.
///
/// CPU is measured the only honest way available without a kernel driver: the
/// difference in the process' own kernel + user time between two samples, divided by
/// the wall clock time that elapsed and by the number of logical processors. That is
/// the same definition Task Manager's "CPU" column uses.
/// </summary>
public sealed class ProcessMetrics
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Window title or file description when one could be read.</summary>
    public string? Description { get; init; }

    /// <summary>Full image path, or null when the process could not be opened.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>0-100 across the whole machine, exactly like Task Manager's column.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Physical memory currently held by the process.</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Committed private bytes: the memory this process alone is responsible for.</summary>
    public long PrivateBytes { get; init; }

    public double MemoryPercent { get; init; }

    public int ThreadCount { get; init; }

    /// <summary>Disk read+write bytes per second, when the process could be opened.</summary>
    public double DiskBytesPerSecond { get; init; }

    /// <summary>True for a process Hexnest could not open, so its numbers are partial.</summary>
    public bool IsRestricted { get; init; }

    /// <summary>True when this is Hexnest itself, so the UI can mark the row.</summary>
    public bool IsSelf { get; init; }

    public DateTime? StartedAt { get; init; }
}

// =====================================================================================
// Network
// =====================================================================================

/// <summary>One sample of the machine's network usage.</summary>
public sealed class NetworkMetrics
{
    public DateTime TakenAt { get; init; } = DateTime.Now;
    public double IntervalSeconds { get; init; }

    /// <summary>Bytes received per second across every non-loopback interface.</summary>
    public double DownloadBytesPerSecond { get; init; }

    /// <summary>Bytes sent per second across every non-loopback interface.</summary>
    public double UploadBytesPerSecond { get; init; }

    /// <summary>Bytes received since Hexnest started watching.</summary>
    public long SessionDownloadBytes { get; init; }

    /// <summary>Bytes sent since Hexnest started watching.</summary>
    public long SessionUploadBytes { get; init; }

    /// <summary>Bytes received since Windows started, as the adapters count them.</summary>
    public long TotalDownloadBytes { get; init; }

    public long TotalUploadBytes { get; init; }

    /// <summary>How long Hexnest has been watching, for the session totals.</summary>
    public TimeSpan SessionDuration { get; init; }

    public IReadOnlyList<NetworkAdapterMetrics> Adapters { get; init; } =
        Array.Empty<NetworkAdapterMetrics>();

    /// <summary>Open TCP connections, grouped by owning process.</summary>
    public int ConnectionCount { get; init; }
}

/// <summary>One network adapter.</summary>
public sealed class NetworkAdapterMetrics
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>Ethernet, Wireless80211, and so on.</summary>
    public string Kind { get; init; } = string.Empty;

    public bool IsUp { get; init; }

    /// <summary>Negotiated link speed in bits per second, or null when unknown.</summary>
    public long? LinkSpeedBps { get; init; }

    public string? IpAddress { get; init; }
    public string? MacAddress { get; init; }

    public double DownloadBytesPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }

    public long TotalDownloadBytes { get; init; }
    public long TotalUploadBytes { get; init; }
}

/// <summary>
/// What one process is doing on the network.
///
/// Windows exposes per connection byte counters through TCP ESTATS, so these numbers
/// cover TCP only - which is essentially all of the traffic a desktop generates, but
/// not UDP video calls, DNS or QUIC. <see cref="IsTcpOnly"/> is always true today and
/// exists so the UI can say so rather than quietly under-reporting.
/// </summary>
public sealed class ProcessNetworkMetrics
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }

    public double DownloadBytesPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }

    /// <summary>Bytes received by this process since Hexnest started watching.</summary>
    public long SessionDownloadBytes { get; init; }

    public long SessionUploadBytes { get; init; }

    /// <summary>Established TCP connections owned by the process.</summary>
    public int ConnectionCount { get; init; }

    /// <summary>The remote endpoints, for the detail panel. Capped to keep the UI sane.</summary>
    public IReadOnlyList<string> RemoteEndpoints { get; init; } = Array.Empty<string>();

    public bool IsTcpOnly => true;

    public bool IsSelf { get; init; }
}
