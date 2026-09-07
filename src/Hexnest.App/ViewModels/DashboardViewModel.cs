using System.Diagnostics;
using System.Windows;
using Hexnest.App.Services;
using Hexnest.Core.Models;
using Hexnest.Core.Platform;
using Hexnest.Core.Scanning;

namespace Hexnest.App.ViewModels;

/// <summary>The landing page: headline numbers, warnings and the quick actions.</summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private ScanResult? _scan;
    private string? _notice;
    private bool _isBusy;

    public DashboardViewModel()
    {
        ScanCommand = new AsyncRelayCommand(
            () => Shell?.ScanAsync() ?? Task.CompletedTask,
            () => Shell is null or { IsScanning: false });

        FullRecoveryCommand = new AsyncRelayCommand(FullRecoveryAsync, () => MissingCandidateCount > 0);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllAsync, () => UpdateCount > 0);
        BackupCommand = new RelayCommand(() => AppEvents.RequestNavigation("backup"));
        ExportReportCommand = new RelayCommand(ExportReport, () => _scan is not null);
        OpenUpdatesCommand = new RelayCommand(() => AppEvents.RequestNavigation("updates"));
        OpenDevicesCommand = new RelayCommand(() => AppEvents.RequestNavigation("devices"));

        AppEvents.ScanCompleted += result => OnUi(() => Apply(result));
        AppEvents.LanguageChanged += () => OnUi(RefreshTexts);

        if (AppHost.LastScan is not null) Apply(AppHost.LastScan);
    }

    /// <summary>
    /// The shell owns scanning; the dashboard only triggers it. Resolved lazily and
    /// defensively because CommandManager can evaluate CanExecute before the main
    /// window has finished wiring up its data context.
    /// </summary>
    private static MainViewModel? Shell =>
        Application.Current?.MainWindow?.DataContext as MainViewModel;

    // =====================================================================================
    // Numbers
    // =====================================================================================

    public int DeviceCount => _scan?.Devices.Count ?? 0;

    /// <summary>
    /// Devices with no driver at all - not the number of packages found for them.
    /// Those are different numbers, and the honest one to headline is the hardware
    /// that is not working: a machine can have seven dead devices and zero available
    /// packages, and reporting that as "0 missing" would be a lie.
    /// </summary>
    public int MissingCount => _scan?.MissingDriverCount ?? 0;

    public int UpdateCount => _scan?.UpdateCount ?? 0;
    public int ProblemCount => _scan?.ProblemDeviceCount ?? 0;

    /// <summary>Packages actually available for the missing devices; drives the button.</summary>
    public int MissingCandidateCount => _scan?.NewInstallCount ?? 0;

    public bool HasScan => _scan is not null;

    public bool IsHealthy => HasScan && MissingCount == 0 && UpdateCount == 0 && ProblemCount == 0;

    public string LastScanText =>
        AppHost.LastScanAt is { } at
            ? $"{Loc.T("dash.lastScan")}: {at:HH:mm:ss}"
            : Loc.T("dash.never");

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    /// <summary>Warning banner text; null hides the banner.</summary>
    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    // System summary
    public string OsText => SystemInfo.Current.OsDisplay;
    public string MachineText => SystemInfo.Current.MachineDisplay;
    public string CpuText => SystemInfo.Current.ProcessorName ?? "-";
    public string ArchText => SystemInfo.Current.Architecture;
    public string BiosText => SystemInfo.Current.BiosVersion ?? "-";

    /// <summary>The display adapter, read once at startup.</summary>
    public string GraphicsText => SystemInfo.Current.GraphicsName ?? "-";

    /// <summary>Installed physical memory.</summary>
    public string MemoryText => SystemInfo.Current.MemoryTotalBytes > 0
        ? Core.Models.Formatting.HumanBytes(SystemInfo.Current.MemoryTotalBytes)
        : "-";

    // =====================================================================================
    // Commands
    // =====================================================================================

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand FullRecoveryCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand BackupCommand { get; }
    public RelayCommand ExportReportCommand { get; }
    public RelayCommand OpenUpdatesCommand { get; }
    public RelayCommand OpenDevicesCommand { get; }

    /// <summary>
    /// The post-format button: install everything that is missing, newest first,
    /// and keep going across restarts.
    /// </summary>
    private async Task FullRecoveryAsync()
    {
        if (_scan is null) return;

        var missing = _scan.Candidates.Where(c => c.IsMissingDriver).ToList();
        if (missing.Count == 0) return;

        await AppHost.Queue.StartAsync(missing, fullRecovery: true).ConfigureAwait(true);
    }

    private async Task UpdateAllAsync()
    {
        if (_scan is null) return;

        var all = _scan.Candidates.ToList();
        if (all.Count == 0) return;

        await AppHost.Queue.StartAsync(all).ConfigureAwait(true);
    }

    private void ExportReport()
    {
        if (_scan is null) return;

        try
        {
            IsBusy = true;
            var path = ScanService.ExportHardwareReport(_scan);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });

            Notice = path;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Could not export the hardware report", ex);
            Notice = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =====================================================================================

    private void Apply(ScanResult result)
    {
        _scan = result;

        Notice = result.HasNoWorkingNetworkDriver
            ? Loc.T("warn.noNetwork")
            : result.Warnings.FirstOrDefault();

        RefreshTexts();
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void RefreshTexts() => RaiseAll(
        nameof(DeviceCount), nameof(MissingCount), nameof(UpdateCount), nameof(ProblemCount),
        nameof(MissingCandidateCount),
        nameof(HasScan), nameof(IsHealthy), nameof(LastScanText),
        nameof(OsText), nameof(MachineText), nameof(CpuText), nameof(ArchText), nameof(BiosText),
        nameof(GraphicsText), nameof(MemoryText));
}
