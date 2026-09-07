using Hexnest.Core;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Monitoring;
using Hexnest.Core.Platform;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The dashboard.
///
/// On Windows this page is dominated by the driver scan, because that is the first
/// thing somebody who has just formatted a machine needs. There is nothing equivalent
/// to lead with on a Mac, so the page answers the question a Mac user actually opens a
/// utility to ask - what is this machine doing right now, and where has the disk gone -
/// and gets out of the way.
///
/// It takes one sample rather than running a monitor. A dashboard that sampled once a
/// second would mean Hexnest was measuring the machine from the moment it opened, which
/// is exactly what the rest of this application is careful not to do.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly SystemMonitor _monitor = new() { Interval = TimeSpan.FromSeconds(2) };

    private bool _disposed;

    public DashboardViewModel()
    {
        OpenSystemCommand = new RelayCommand(() => AppEvents.RequestNavigation("system"));
        OpenNetworkCommand = new RelayCommand(() => AppEvents.RequestNavigation("network"));
        OpenCleanCommand = new RelayCommand(() => AppEvents.RequestNavigation("clean"));
        OpenStartupCommand = new RelayCommand(() => AppEvents.RequestNavigation("startup"));

        _monitor.Sampled += OnSampled;
    }

    public void Start()
    {
        if (!_disposed) _monitor.Start();
    }

    public void Stop() => _monitor.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.Sampled -= OnSampled;
        _monitor.Dispose();
    }

    private void OnSampled(SystemMetrics metrics) => OnUi(() =>
    {
        Latest = metrics;

        RaiseAll(
            nameof(Latest), nameof(CpuText), nameof(CpuPercent),
            nameof(MemoryText), nameof(MemoryPercent), nameof(MemoryDetail),
            nameof(DiskText), nameof(DiskPercent), nameof(DiskDetail),
            nameof(UptimeText), nameof(ProcessCountText), nameof(HasReading),
            nameof(SpecStorage));
    });

    public SystemMetrics? Latest { get; private set; }

    public bool HasReading => Latest is not null;

    // =====================================================================================
    // Identity
    // =====================================================================================

    public string Title => Loc.T("dash.title");

    public string Subtitle => Loc.T("mac.dashSubtitle");

    public string Greeting => AppInfo.ProductName;

    public string MachineName => SystemInfo.Current.MachineDisplay;

    public string OsText => SystemInfo.Current.OsDisplay;

    public string ProcessorText =>
        $"{SystemInfo.Current.ProcessorName}  ·  {SystemInfo.Current.CoreDisplay}";

    public string UserText => SystemInfo.Current.UserName;

    // =====================================================================================
    // Hardware
    //
    // The facts that do not change while the application is open, read once. They are
    // at the foot of the dashboard rather than the top because somebody opening a
    // monitor wants to know what the machine is doing before they want to know what it
    // is - but "what is it" is the other question a utility like this gets asked, and
    // on a Mac the answer is otherwise four clicks deep in System Settings.
    // =====================================================================================

    public string SpecsTitle => Loc.T("spec.title");

    public string SpecModel => SystemInfo.Current.ModelIdentifier is { Length: > 0 } id
        ? $"{SystemInfo.Current.SystemProductName}  ({id})"
        : SystemInfo.Current.MachineDisplay;

    public string SpecChip =>
        $"{SystemInfo.Current.ProcessorName}  ·  {SystemInfo.Current.CoreDisplay}";

    public string SpecGraphics
    {
        get
        {
            var graphics = SystemInfo.Current;

            if (graphics.GraphicsName is null) return "-";

            return graphics.GraphicsCores is > 0
                ? $"{graphics.GraphicsName}  ·  {Loc.T("spec.gpuCores", graphics.GraphicsCores)}"
                : graphics.GraphicsName;
        }
    }

    public string SpecMemory => SystemInfo.Current.MemoryTotalBytes > 0
        ? Formatting.HumanBytes(SystemInfo.Current.MemoryTotalBytes)
        : "-";

    public string SpecStorage => BootDisk is null
        ? "-"
        : $"{Formatting.HumanBytes(BootDisk.TotalBytes)}  ·  " +
          $"{Formatting.HumanBytes(BootDisk.FreeBytes)} {Loc.T("mon.freeSpace", string.Empty).Trim()}";

    public string SpecSystem => SystemInfo.Current.OsDisplay;

    // =====================================================================================
    // Live figures
    // =====================================================================================

    public double CpuPercent => Latest?.CpuPercent ?? 0;

    public string CpuText => Latest is null ? "-" : $"{Latest.CpuPercent:0}%";

    public double MemoryPercent => Latest?.MemoryPercent ?? 0;

    public string MemoryText => Latest is null ? "-" : $"{Latest.MemoryPercent:0}%";

    public string MemoryDetail => Latest is null
        ? "-"
        : Loc.T("mac.memoryUsed",
            Formatting.HumanBytes(Latest.MemoryUsedBytes),
            Formatting.HumanBytes(Latest.MemoryTotalBytes));

    /// <summary>The startup disk. Any other volume is the storage list's business.</summary>
    private DiskMetrics? BootDisk => Latest?.Disks.FirstOrDefault(d => d.Name == "/");

    public double DiskPercent => BootDisk?.UsedPercent ?? 0;

    public string DiskText => BootDisk is null ? "-" : $"{BootDisk.UsedPercent:0}%";

    public string DiskDetail => BootDisk is null
        ? "-"
        : $"{Formatting.HumanBytes(BootDisk.UsedBytes)} / {Formatting.HumanBytes(BootDisk.TotalBytes)}";

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

    public string ProcessCountText => Latest is null ? "-" : Latest.ProcessCount.ToString("N0");

    // =====================================================================================
    // Quick actions
    // =====================================================================================

    public RelayCommand OpenSystemCommand { get; }
    public RelayCommand OpenNetworkCommand { get; }
    public RelayCommand OpenCleanCommand { get; }
    public RelayCommand OpenStartupCommand { get; }

    public string QuickActions => Loc.T("dash.quickActions");
}
