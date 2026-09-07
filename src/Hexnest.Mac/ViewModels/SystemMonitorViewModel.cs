using System.Collections.ObjectModel;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Monitoring;
using Hexnest.Core.Platform;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The system monitor page.
///
/// Owns both samplers and starts them only while the page is on screen, which is the
/// whole reason Hexnest can call itself a monitor without becoming a background
/// service. <see cref="Stop"/> is called when the page is detached from the visual
/// tree, so switching to another page really does stop the work.
/// </summary>
public sealed class SystemMonitorViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many readings the sparklines keep. At one a second, two minutes.</summary>
    private const int HistoryLength = 120;

    /// <summary>
    /// How often the process table is rebuilt, in samples. Walking eight hundred
    /// processes every second would be the most expensive thing on the page, and a
    /// table that reorders itself once a second is unreadable anyway.
    /// </summary>
    private const int ProcessEveryNSamples = 2;

    private readonly SystemMonitor _system = new();
    private readonly ProcessMonitor _processes = new();

    private readonly List<double> _cpuHistory = new(HistoryLength);
    private readonly List<double> _memoryHistory = new(HistoryLength);

    private int _sampleIndex;
    private bool _paused;
    private bool _showAllProcesses;
    private bool _disposed;

    public SystemMonitorViewModel()
    {
        PauseCommand = new RelayCommand(() =>
        {
            IsPaused = !IsPaused;
            if (IsPaused) _system.Stop();
            else _system.Start();
        });

        ToggleShowAllCommand = new RelayCommand(() => ShowAllProcesses = !ShowAllProcesses);

        _system.Sampled += OnSampled;
    }

    // =====================================================================================
    // Lifetime
    // =====================================================================================

    public void Start()
    {
        if (_disposed || IsPaused) return;
        _processes.Reset();
        _system.Start();
    }

    public void Stop() => _system.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _system.Sampled -= OnSampled;
        _system.Dispose();
    }

    // =====================================================================================
    // Sampling
    // =====================================================================================

    private void OnSampled(SystemMetrics metrics)
    {
        // The process walk happens off the UI thread, before the hop, so the only work
        // done on the dispatcher is assigning already-computed values.
        IReadOnlyList<ProcessMetrics>? processes = null;

        if (_sampleIndex % ProcessEveryNSamples == 0)
        {
            try
            {
                processes = _processes.Sample(metrics.MemoryTotalBytes);
            }
            catch
            {
                // A failed pass is one missing table refresh, not a broken page.
            }
        }

        _sampleIndex++;

        OnUi(() => Apply(metrics, processes));
    }

    private void Apply(SystemMetrics metrics, IReadOnlyList<ProcessMetrics>? processes)
    {
        Latest = metrics;

        Push(_cpuHistory, metrics.CpuPercent);
        Push(_memoryHistory, metrics.MemoryPercent);

        CpuHistory = _cpuHistory.ToArray();
        MemoryHistory = _memoryHistory.ToArray();
        CoreLoads = metrics.CoreLoads;

        RaiseAll(
            nameof(Latest), nameof(CpuPercent), nameof(CpuText), nameof(CpuDetail),
            nameof(MemoryPercent), nameof(MemoryText), nameof(MemoryDetail),
            nameof(SwapText), nameof(CachedText),
            nameof(ProcessCountText), nameof(ThreadCountText), nameof(UptimeText),
            nameof(BatteryText), nameof(HasBattery), nameof(BatteryPercent),
            nameof(CpuHistory), nameof(MemoryHistory), nameof(CoreLoads), nameof(HasCoreLoads));

        SyncDisks(metrics.Disks);

        if (processes is not null) SyncProcesses(processes);
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength) history.RemoveAt(0);
    }

    // =====================================================================================
    // Processor and memory
    // =====================================================================================

    public SystemMetrics? Latest { get; private set; }

    public double CpuPercent => Latest?.CpuPercent ?? 0;

    public string CpuText => Latest is null ? "-" : $"{Latest.CpuPercent:0}%";

    /// <summary>
    /// The chip and its core layout. On Apple silicon this says "8P + 2E", which is the
    /// only description of an M-series processor that is actually true.
    /// </summary>
    public string CpuDetail
    {
        get
        {
            var identity = SystemInfo.Current;
            var cores = Loc.T("mac.cores", identity.CoreDisplay, identity.ProcessorCount);
            return $"{identity.ProcessorName}  ·  {cores}";
        }
    }

    public double MemoryPercent => Latest?.MemoryPercent ?? 0;

    public string MemoryText => Latest is null ? "-" : $"{Latest.MemoryPercent:0}%";

    public string MemoryDetail => Latest is null
        ? "-"
        : Loc.T("mac.memoryUsed",
            Formatting.HumanBytes(Latest.MemoryUsedBytes),
            Formatting.HumanBytes(Latest.MemoryTotalBytes));

    public string CachedText =>
        Latest is null ? "-" : Formatting.HumanBytes(Latest.MemoryCachedBytes);

    /// <summary>
    /// Swap in use against the swap file's size. macOS has no commit charge, and this
    /// is the honest local equivalent: how much the machine has had to push to disk.
    /// </summary>
    public string SwapText => Latest is null || Latest.CommitLimitBytes == 0
        ? Loc.T("common.none")
        : $"{Formatting.HumanBytes(Latest.CommitUsedBytes)} / {Formatting.HumanBytes(Latest.CommitLimitBytes)}";

    public IReadOnlyList<double> CpuHistory { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<double> MemoryHistory { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<double> CoreLoads { get; private set; } = Array.Empty<double>();

    public bool HasCoreLoads => CoreLoads.Count > 0;

    public string ProcessCountText => Latest is null ? "-" : Latest.ProcessCount.ToString("N0");

    public string ThreadCountText => Latest is null ? "-" : Latest.ThreadCount.ToString("N0");

    public string UptimeText
    {
        get
        {
            if (Latest is null) return "-";

            var uptime = Latest.Uptime;

            return uptime.TotalDays >= 1
                ? Loc.T("mon.uptimeDays", (int)uptime.TotalDays, uptime.Hours, uptime.Minutes)
                : Loc.T("mon.uptimeHours", (int)uptime.TotalHours, uptime.Minutes);
        }
    }

    // =====================================================================================
    // Battery
    // =====================================================================================

    public bool HasBattery => Latest?.Battery is not null;

    public double BatteryPercent => Latest?.Battery?.Percent ?? 0;

    public string BatteryText
    {
        get
        {
            var battery = Latest?.Battery;
            if (battery is null) return "-";

            var parts = new List<string>(3);

            if (battery.Percent.HasValue) parts.Add($"{battery.Percent}%");

            parts.Add(battery.IsCharging
                ? Loc.T("mon.batteryCharging")
                : battery.IsOnMains
                    ? Loc.T("mon.batteryMains")
                    : Loc.T("mon.batteryOnBattery"));

            if (battery.Remaining is { } remaining && remaining > TimeSpan.Zero)
                parts.Add(Loc.T("mon.batteryRemaining", (int)remaining.TotalHours, remaining.Minutes));

            return string.Join("  ·  ", parts);
        }
    }

    // =====================================================================================
    // Storage
    // =====================================================================================

    public ObservableCollection<DiskRow> Disks { get; } = new();

    private void SyncDisks(IReadOnlyList<DiskMetrics> disks)
    {
        // Volumes are mounted and unmounted rarely, so the list is only rebuilt when it
        // actually changed; otherwise the rows are updated in place and the selection
        // and scroll position survive.
        if (Disks.Count != disks.Count)
        {
            Disks.Clear();
            foreach (var disk in disks) Disks.Add(new DiskRow(disk));
            return;
        }

        for (int i = 0; i < disks.Count; i++) Disks[i].Update(disks[i]);
    }

    // =====================================================================================
    // Processes
    // =====================================================================================

    public ObservableCollection<ProcessRow> Processes { get; } = new();

    /// <summary>How many rows the table shows when it is not expanded.</summary>
    private const int TopProcessCount = 12;

    public bool ShowAllProcesses
    {
        get => _showAllProcesses;
        set
        {
            if (!Set(ref _showAllProcesses, value)) return;

            Raise(nameof(ShowAllLabel));
            SyncProcesses(_lastProcesses);
        }
    }

    public string ShowAllLabel => ShowAllProcesses
        ? Loc.T("mon.showTop", TopProcessCount)
        : Loc.T("mon.showAll");

    public string ProcessCaption => Loc.T("mon.showing", Processes.Count, _lastProcesses.Count);

    private IReadOnlyList<ProcessMetrics> _lastProcesses = Array.Empty<ProcessMetrics>();

    private void SyncProcesses(IReadOnlyList<ProcessMetrics> processes)
    {
        _lastProcesses = processes;

        // Restricted rows carry a name and nothing else, so they would sort to the
        // bottom of a table ordered by usage and add several hundred empty lines. They
        // are counted in the caption instead, and shown when the table is expanded.
        var visible = ShowAllProcesses
            ? processes
            : processes.Where(p => !p.IsRestricted).Take(TopProcessCount).ToList();

        if (Processes.Count != visible.Count)
        {
            Processes.Clear();
            foreach (var process in visible) Processes.Add(new ProcessRow(process));
        }
        else
        {
            for (int i = 0; i < visible.Count; i++) Processes[i].Update(visible[i]);
        }

        Raise(nameof(ProcessCaption));
    }

    // =====================================================================================
    // Commands
    // =====================================================================================

    public RelayCommand PauseCommand { get; }

    public RelayCommand ToggleShowAllCommand { get; }

    public bool IsPaused
    {
        get => _paused;
        private set { if (Set(ref _paused, value)) Raise(nameof(PauseLabel)); }
    }

    public string PauseLabel => IsPaused ? Loc.T("mon.resume") : Loc.T("mon.pause");

    public string Title => Loc.T("mon.title");

    public string Subtitle => Loc.T("mon.subtitle");

    /// <summary>
    /// The honesty note at the foot of the page, with the two macOS caveats appended.
    /// A monitor that quietly omits what it cannot see is worse than one that says so.
    /// </summary>
    public string Footnote => Loc.T("mac.monitorFootnote") + "  " + Loc.T("mac.monitorNote");
}

/// <summary>One volume, updated in place so the row does not flicker.</summary>
public sealed class DiskRow : ViewModelBase
{
    public DiskRow(DiskMetrics disk) => Update(disk);

    public string Name { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public double UsedPercent { get; private set; }
    public string SizeText { get; private set; } = string.Empty;
    public string FreeText { get; private set; } = string.Empty;

    public void Update(DiskMetrics disk)
    {
        Name = string.IsNullOrWhiteSpace(disk.Label) ? disk.Name : disk.Label!;
        Detail = $"{disk.Name}  ·  {disk.Format}";
        UsedPercent = disk.UsedPercent;
        SizeText = $"{Formatting.HumanBytes(disk.UsedBytes)} / {Formatting.HumanBytes(disk.TotalBytes)}";
        FreeText = Loc.T("mon.freeSpace", Formatting.HumanBytes(disk.FreeBytes));

        RaiseAll(nameof(Name), nameof(Detail), nameof(UsedPercent), nameof(SizeText), nameof(FreeText));
    }
}

/// <summary>One process, updated in place so the table does not flicker.</summary>
public sealed class ProcessRow : ViewModelBase
{
    public ProcessRow(ProcessMetrics process) => Update(process);

    public int ProcessId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CpuText { get; private set; } = string.Empty;
    public double CpuPercent { get; private set; }
    public string MemoryText { get; private set; } = string.Empty;
    public string ThreadText { get; private set; } = string.Empty;
    public bool IsRestricted { get; private set; }
    public bool IsSelf { get; private set; }

    public void Update(ProcessMetrics process)
    {
        ProcessId = process.ProcessId;
        Name = process.Name;
        IsRestricted = process.IsRestricted;
        IsSelf = process.IsSelf;

        // A restricted process is listed with a name and dashes rather than zeroes:
        // "0.0%" would be a claim, and "-" is the truth.
        CpuPercent = process.CpuPercent;
        CpuText = process.IsRestricted ? "-" : $"{process.CpuPercent:0.0}%";
        MemoryText = process.IsRestricted ? "-" : Formatting.HumanBytes(process.WorkingSetBytes);
        ThreadText = process.IsRestricted ? "-" : process.ThreadCount.ToString();

        RaiseAll(nameof(ProcessId), nameof(Name), nameof(CpuText), nameof(CpuPercent),
            nameof(MemoryText), nameof(ThreadText), nameof(IsRestricted), nameof(IsSelf));
    }
}
