using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using DrvNest.App.Services;
using DrvNest.Core.Models;
using DrvNest.Core.Monitoring;

namespace DrvNest.App.ViewModels;

/// <summary>One row in the process table. Updated in place so the list never flickers.</summary>
public sealed class ProcessRow : ViewModelBase
{
    private double _cpuPercent;
    private long _workingSet;
    private long _privateBytes;
    private double _memoryPercent;
    private double _diskRate;
    private int _threads;
    private bool _restricted;

    public ProcessRow(ProcessMetrics metrics)
    {
        ProcessId = metrics.ProcessId;
        Name = metrics.Name;
        ExecutablePath = metrics.ExecutablePath;
        IsSelf = metrics.IsSelf;
        StartedAt = metrics.StartedAt;
        Apply(metrics);
    }

    public int ProcessId { get; }
    public string Name { get; }
    public string? ExecutablePath { get; }
    public bool IsSelf { get; }
    public DateTime? StartedAt { get; }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set { if (Set(ref _cpuPercent, value)) Raise(nameof(CpuDisplay)); }
    }

    public long WorkingSetBytes
    {
        get => _workingSet;
        private set { if (Set(ref _workingSet, value)) Raise(nameof(MemoryDisplay)); }
    }

    public long PrivateBytes
    {
        get => _privateBytes;
        private set { if (Set(ref _privateBytes, value)) Raise(nameof(PrivateDisplay)); }
    }

    public double MemoryPercent
    {
        get => _memoryPercent;
        private set => Set(ref _memoryPercent, value);
    }

    public double DiskBytesPerSecond
    {
        get => _diskRate;
        private set { if (Set(ref _diskRate, value)) Raise(nameof(DiskDisplay)); }
    }

    public int ThreadCount
    {
        get => _threads;
        private set => Set(ref _threads, value);
    }

    public bool IsRestricted
    {
        get => _restricted;
        private set => Set(ref _restricted, value);
    }

    public string CpuDisplay => CpuPercent < 0.05 ? "0" : CpuPercent.ToString("0.0");
    public string MemoryDisplay => Formatting.HumanBytes(WorkingSetBytes);
    public string PrivateDisplay => Formatting.HumanBytes(PrivateBytes);
    public string DiskDisplay => DiskBytesPerSecond < 1024 ? "-" : Formatting.HumanSpeed(DiskBytesPerSecond);

    public string Tooltip => ExecutablePath ?? Name;

    public void Apply(ProcessMetrics metrics)
    {
        CpuPercent = metrics.CpuPercent;
        WorkingSetBytes = metrics.WorkingSetBytes;
        PrivateBytes = metrics.PrivateBytes;
        MemoryPercent = metrics.MemoryPercent;
        DiskBytesPerSecond = metrics.DiskBytesPerSecond;
        ThreadCount = metrics.ThreadCount;
        IsRestricted = metrics.IsRestricted;
    }
}

/// <summary>How the process table is ordered.</summary>
public enum ProcessSort
{
    Cpu = 0,
    Memory = 1,
    Disk = 2,
    Name = 3
}

/// <summary>
/// The live machine panel: processor, memory, temperature, storage and what every
/// running program is costing.
///
/// The monitor is started when this page is first shown and stopped when the page goes
/// away, so DrvNest is not quietly sampling the machine for someone who only came to
/// install a driver. That is the same promise the rest of the application makes: no
/// background service, nothing running when you are not looking at it.
/// </summary>
public sealed class SystemMonitorViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many rows the table shows before "show everything" is turned on.</summary>
    private const int DefaultRowLimit = 40;

    private readonly SystemMonitor _monitor = new();
    private readonly ProcessMonitor _processes = new();
    private readonly Dictionary<int, ProcessRow> _rows = new();

    private SystemMetrics? _metrics;
    private int _sweepRunning;
    private bool _disposed;

    private string _search = string.Empty;
    private ProcessSort _sort = ProcessSort.Cpu;
    private bool _isPaused;
    private bool _showAll;
    private int _totalProcessRows;

    public SystemMonitorViewModel()
    {
        _monitor.Sampled += OnSampled;

        PauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        SortCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProcessSort sort) Sort = sort;
            else if (parameter is string name && Enum.TryParse<ProcessSort>(name, true, out var parsed))
                Sort = parsed;
        });

        ShowAllCommand = new RelayCommand(() => ShowAll = !ShowAll);
        OpenTaskManagerCommand = new RelayCommand(OpenTaskManager);

        AppEvents.LanguageChanged += () => OnUi(RefreshTexts);
    }

    /// <summary>
    /// Starts sampling. Called when the page becomes visible.
    ///
    /// Navigation caches its pages, so the same view model is reused every time the user
    /// comes back here; starting on load and stopping on unload is what keeps the promise
    /// that nothing samples the machine while the page is closed.
    /// </summary>
    public void Activate()
    {
        if (_disposed) return;

        // The processor deltas from before the page was closed would produce one absurd
        // first reading, so the baseline is dropped rather than reused.
        _processes.Reset();
        _monitor.Start();
    }

    /// <summary>Stops sampling. Called when the page is navigated away from.</summary>
    public void Deactivate() => _monitor.Stop();

    // =====================================================================================
    // Commands
    // =====================================================================================

    public RelayCommand PauseCommand { get; }
    public RelayCommand SortCommand { get; }
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand OpenTaskManagerCommand { get; }

    public ObservableCollection<ProcessRow> Processes { get; } = new();

    /// <summary>Per logical processor load, for the core strip.</summary>
    public ObservableCollection<CoreRow> Cores { get; } = new();

    public ObservableCollection<ThermalRow> Temperatures { get; } = new();

    public ObservableCollection<DiskRow> Disks { get; } = new();

    // =====================================================================================
    // Live numbers
    // =====================================================================================

    public double CpuPercent => _metrics?.CpuPercent ?? 0;
    public string CpuDisplay => $"{CpuPercent:0}%";

    public string CpuName => _metrics?.CpuName ?? Loc.T("common.loading");

    public string CpuDetail
    {
        get
        {
            if (_metrics is null) return string.Empty;

            var parts = new List<string>(3)
            {
                Loc.T("mon.coresValue", _metrics.PhysicalCores?.ToString() ?? "?", _metrics.LogicalProcessors)
            };

            if (_metrics.CpuMhz is > 0)
            {
                parts.Add(_metrics.CpuMhz.Value >= 1000
                    ? $"{_metrics.CpuMhz.Value / 1000.0:0.00} GHz"
                    : $"{_metrics.CpuMhz.Value} MHz");
            }

            return string.Join("  ·  ", parts);
        }
    }

    public double MemoryPercent => _metrics?.MemoryPercent ?? 0;
    public string MemoryDisplay => $"{MemoryPercent:0}%";

    public string MemoryUsedDisplay => _metrics is null
        ? "-"
        : $"{Formatting.HumanBytes(_metrics.MemoryUsedBytes)} / {Formatting.HumanBytes(_metrics.MemoryTotalBytes)}";

    public string MemoryAvailableDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.MemoryAvailableBytes);

    public string MemoryCachedDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.MemoryCachedBytes);

    public string CommitDisplay => _metrics is null
        ? "-"
        : $"{Formatting.HumanBytes(_metrics.CommitUsedBytes)} / {Formatting.HumanBytes(_metrics.CommitLimitBytes)}";

    public string KernelPoolDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.KernelPoolBytes);

    public string ProcessCountDisplay => _metrics?.ProcessCount.ToString() ?? "-";
    public string ThreadCountDisplay => _metrics?.ThreadCount.ToString() ?? "-";
    public string HandleCountDisplay => _metrics?.HandleCount.ToString("N0") ?? "-";

    public string UptimeDisplay
    {
        get
        {
            if (_metrics is null) return "-";

            var up = _metrics.Uptime;

            return up.TotalDays >= 1
                ? Loc.T("mon.uptimeDays", (int)up.TotalDays, up.Hours, up.Minutes)
                : Loc.T("mon.uptimeHours", (int)up.TotalHours, up.Minutes);
        }
    }

    // --- temperature -------------------------------------------------------------

    public bool HasTemperature => Temperatures.Count > 0;

    public double HottestCelsius => _metrics?.HottestTemperature?.Celsius ?? 0;

    public string TemperatureDisplay =>
        _metrics?.HottestTemperature is { } hottest ? $"{hottest.Celsius:0}°C" : "-";

    /// <summary>
    /// Explains an empty thermal panel instead of leaving a blank card.
    /// Most desktops genuinely have no sensor Windows can read without a kernel driver.
    /// </summary>
    public string TemperatureNotice => Loc.T("mon.noThermal");

    // --- disk activity -----------------------------------------------------------

    public double DiskBytesPerSecond => Disks.Sum(d => d.ReadRate + d.WriteRate);

    public string DiskActivityDisplay =>
        DiskBytesPerSecond < 1024 ? "-" : Formatting.HumanSpeed(DiskBytesPerSecond);

    // --- battery -----------------------------------------------------------------

    public bool HasBattery => _metrics?.Battery is not null;

    public double BatteryPercent => _metrics?.Battery?.Percent ?? 0;

    public string BatteryDisplay => _metrics?.Battery is not { } battery
        ? "-"
        : battery.Percent is { } percent ? $"{percent}%" : "-";

    public string BatteryStateDisplay
    {
        get
        {
            if (_metrics?.Battery is not { } battery) return string.Empty;

            if (battery.IsCharging) return Loc.T("mon.batteryCharging");
            if (battery.IsOnMains) return Loc.T("mon.batteryMains");

            return battery.Remaining is { } left
                ? Loc.T("mon.batteryRemaining", (int)left.TotalHours, left.Minutes)
                : Loc.T("mon.batteryOnBattery");
        }
    }

    // --- machine -----------------------------------------------------------------

    public string MachineDisplay => _metrics?.MachineName ?? string.Empty;
    public string OsDisplay => _metrics?.OsName ?? string.Empty;

    // =====================================================================================
    // Table controls
    // =====================================================================================

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) RebuildRows(); }
    }

    public ProcessSort Sort
    {
        get => _sort;
        set
        {
            if (!Set(ref _sort, value)) return;

            RaiseAll(nameof(IsSortCpu), nameof(IsSortMemory), nameof(IsSortDisk), nameof(IsSortName));
            RebuildRows();
        }
    }

    public bool IsSortCpu => Sort == ProcessSort.Cpu;
    public bool IsSortMemory => Sort == ProcessSort.Memory;
    public bool IsSortDisk => Sort == ProcessSort.Disk;
    public bool IsSortName => Sort == ProcessSort.Name;

    /// <summary>Freezes the table so a row can be read without it moving underneath.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set { if (Set(ref _isPaused, value)) Raise(nameof(PauseLabel)); }
    }

    public string PauseLabel => Loc.T(IsPaused ? "mon.resume" : "mon.pause");

    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (!Set(ref _showAll, value)) return;

            Raise(nameof(ShowAllLabel));
            RebuildRows();
        }
    }

    public string ShowAllLabel =>
        Loc.T(ShowAll ? "mon.showTop" : "mon.showAll", DefaultRowLimit);

    /// <summary>"Showing 40 of 473", so a truncated table never looks like the whole truth.</summary>
    public string RowCountDisplay =>
        Loc.T("mon.showing", Processes.Count, _totalProcessRows);

    // =====================================================================================
    // Sampling
    // =====================================================================================

    private void OnSampled(SystemMetrics metrics)
    {
        // The process sweep is far more expensive than the machine sample, so it runs on
        // its own task and is never allowed to overlap with itself.
        if (Interlocked.CompareExchange(ref _sweepRunning, 1, 0) == 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var rows = _processes.Sample(metrics.MemoryTotalBytes);
                    OnUi(() => ApplyProcesses(rows));
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.Log.Debug($"Process sweep failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _sweepRunning, 0);
                }
            });
        }

        OnUi(() => Apply(metrics));
    }

    private void Apply(SystemMetrics metrics)
    {
        if (_disposed) return;

        _metrics = metrics;

        SyncCores(metrics.CoreLoads);
        SyncTemperatures(metrics.Temperatures);
        SyncDisks(metrics.Disks);

        RefreshTexts();
    }

    private void ApplyProcesses(IReadOnlyList<ProcessMetrics> sampled)
    {
        if (_disposed || IsPaused) return;

        var alive = new HashSet<int>(sampled.Count);

        foreach (var metrics in sampled)
        {
            alive.Add(metrics.ProcessId);

            if (_rows.TryGetValue(metrics.ProcessId, out var row)) row.Apply(metrics);
            else _rows[metrics.ProcessId] = new ProcessRow(metrics);
        }

        foreach (var pid in _rows.Keys.Where(pid => !alive.Contains(pid)).ToList())
            _rows.Remove(pid);

        _totalProcessRows = _rows.Count;
        RebuildRows();
    }

    /// <summary>Applies the search, the sort and the row cap, then patches the collection.</summary>
    private void RebuildRows()
    {
        if (_disposed) return;

        IEnumerable<ProcessRow> query = _rows.Values;

        var term = Search.Trim();

        if (term.Length > 0)
        {
            query = query.Where(row =>
                row.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.ProcessId.ToString().Contains(term, StringComparison.Ordinal) ||
                (row.ExecutablePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = Sort switch
        {
            ProcessSort.Memory => query.OrderByDescending(r => r.WorkingSetBytes),
            ProcessSort.Disk => query.OrderByDescending(r => r.DiskBytesPerSecond)
                                     .ThenByDescending(r => r.CpuPercent),
            ProcessSort.Name => query.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                                     .ThenBy(r => r.ProcessId),
            _ => query.OrderByDescending(r => r.CpuPercent)
                      .ThenByDescending(r => r.WorkingSetBytes)
        };

        if (!ShowAll) query = query.Take(DefaultRowLimit);

        ListSync.Apply(Processes, query.ToList());

        Raise(nameof(RowCountDisplay));
    }

    private void SyncCores(IReadOnlyList<double> loads)
    {
        // The core count cannot change while Windows is running, so the strip is built
        // once and only its values are updated afterwards.
        if (Cores.Count != loads.Count)
        {
            Cores.Clear();
            for (int i = 0; i < loads.Count; i++) Cores.Add(new CoreRow(i));
        }

        for (int i = 0; i < loads.Count; i++) Cores[i].Percent = loads[i];
    }

    private void SyncTemperatures(IReadOnlyList<ThermalReading> readings)
    {
        if (Temperatures.Count != readings.Count)
        {
            Temperatures.Clear();
            foreach (var reading in readings) Temperatures.Add(new ThermalRow(reading));
        }
        else
        {
            for (int i = 0; i < readings.Count; i++) Temperatures[i].Apply(readings[i]);
        }

        Raise(nameof(HasTemperature));
    }

    private void SyncDisks(IReadOnlyList<DiskMetrics> disks)
    {
        if (Disks.Count != disks.Count)
        {
            Disks.Clear();
            foreach (var disk in disks) Disks.Add(new DiskRow(disk));
        }
        else
        {
            for (int i = 0; i < disks.Count; i++) Disks[i].Apply(disks[i]);
        }
    }

    private void RefreshTexts() => RaiseAll(
        nameof(CpuPercent), nameof(CpuDisplay), nameof(CpuName), nameof(CpuDetail),
        nameof(MemoryPercent), nameof(MemoryDisplay), nameof(MemoryUsedDisplay),
        nameof(MemoryAvailableDisplay), nameof(MemoryCachedDisplay),
        nameof(CommitDisplay), nameof(KernelPoolDisplay),
        nameof(ProcessCountDisplay), nameof(ThreadCountDisplay), nameof(HandleCountDisplay),
        nameof(UptimeDisplay),
        nameof(HasTemperature), nameof(HottestCelsius), nameof(TemperatureDisplay),
        nameof(TemperatureNotice),
        nameof(DiskBytesPerSecond), nameof(DiskActivityDisplay),
        nameof(HasBattery), nameof(BatteryPercent), nameof(BatteryDisplay), nameof(BatteryStateDisplay),
        nameof(MachineDisplay), nameof(OsDisplay),
        nameof(PauseLabel), nameof(ShowAllLabel), nameof(RowCountDisplay));

    private static void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open Task Manager: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.Sampled -= OnSampled;
        _monitor.Dispose();
    }
}

/// <summary>One logical processor in the core strip.</summary>
public sealed class CoreRow : ViewModelBase
{
    private double _percent;

    public CoreRow(int index) => Index = index;

    public int Index { get; }

    public string Label => $"{Index}";

    public double Percent
    {
        get => _percent;
        set { if (Set(ref _percent, value)) Raise(nameof(Display)); }
    }

    public string Display => $"{Loc.T("mon.core")} {Index}: {Percent:0}%";
}

/// <summary>One temperature sensor row.</summary>
public sealed class ThermalRow : ViewModelBase
{
    private double _celsius;
    private ThermalBand _band;

    public ThermalRow(ThermalReading reading)
    {
        Name = reading.Name;
        Source = reading.Source;
        Apply(reading);
    }

    public string Name { get; }
    public string Source { get; }

    public double Celsius
    {
        get => _celsius;
        private set { if (Set(ref _celsius, value)) Raise(nameof(Display)); }
    }

    public ThermalBand Band
    {
        get => _band;
        private set => Set(ref _band, value);
    }

    public string Display => $"{Celsius:0.0} °C";

    /// <summary>0-100 for the bar, mapping 20-100 °C onto the full width.</summary>
    public double BarPercent => Math.Clamp((Celsius - 20) / 80 * 100, 0, 100);

    public void Apply(ThermalReading reading)
    {
        Celsius = reading.Celsius;
        Band = reading.Band;
        Raise(nameof(BarPercent));
    }
}

/// <summary>One fixed drive row.</summary>
public sealed class DiskRow : ViewModelBase
{
    private long _free;
    private double _readRate;
    private double _writeRate;

    public DiskRow(DiskMetrics metrics)
    {
        Name = metrics.Name;
        Label = metrics.Label;
        Format = metrics.Format;
        TotalBytes = metrics.TotalBytes;
        Apply(metrics);
    }

    public string Name { get; }
    public string? Label { get; }
    public string? Format { get; }
    public long TotalBytes { get; }

    public double ReadRate
    {
        get => _readRate;
        private set { if (Set(ref _readRate, value)) Raise(nameof(ThroughputDisplay)); }
    }

    public double WriteRate
    {
        get => _writeRate;
        private set { if (Set(ref _writeRate, value)) Raise(nameof(ThroughputDisplay)); }
    }

    public long FreeBytes
    {
        get => _free;
        private set
        {
            if (Set(ref _free, value))
                RaiseAll(nameof(UsedPercent), nameof(CapacityDisplay), nameof(FreeDisplay));
        }
    }

    public string Title => string.IsNullOrWhiteSpace(Label) ? Name : $"{Name}  {Label}";

    public double UsedPercent =>
        TotalBytes > 0 ? (TotalBytes - FreeBytes) * 100.0 / TotalBytes : 0;

    public string CapacityDisplay =>
        $"{Formatting.HumanBytes(TotalBytes - FreeBytes)} / {Formatting.HumanBytes(TotalBytes)}";

    public string FreeDisplay => Loc.T("mon.freeSpace", Formatting.HumanBytes(FreeBytes));

    public string ThroughputDisplay
    {
        get
        {
            if (ReadRate < 1024 && WriteRate < 1024) return "-";

            return $"↓ {Formatting.HumanSpeed(ReadRate)}   ↑ {Formatting.HumanSpeed(WriteRate)}";
        }
    }

    public void Apply(DiskMetrics metrics)
    {
        FreeBytes = metrics.FreeBytes;
        ReadRate = metrics.ReadBytesPerSecond;
        WriteRate = metrics.WriteBytesPerSecond;
    }
}
