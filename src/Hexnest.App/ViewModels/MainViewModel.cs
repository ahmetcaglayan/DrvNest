using System.Collections.ObjectModel;
using System.Windows.Controls;
using Hexnest.App.Services;
using Hexnest.App.Views;
using Hexnest.Core;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Models;
using Hexnest.Core.Platform;

namespace Hexnest.App.ViewModels;

/// <summary>
/// The shell: navigation, the scan that every page consumes, and the status bar.
///
/// Scanning lives here rather than on the dashboard because four pages need the
/// result and only one of them should be allowed to trigger it.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private NavItem? _selectedNav;
    private UserControl? _currentView;
    private string _statusText = string.Empty;
    private bool _isScanning;
    private double _scanPercent;
    private bool _scanIndeterminate;
    private bool _updateAvailable;
    private CancellationTokenSource? _scanCancellation;

    public MainViewModel()
    {
        BuildNavigation();

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        CancelScanCommand = new RelayCommand(() => _scanCancellation?.Cancel(), () => IsScanning);

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavItem item) SelectedNav = item;
            else if (parameter is string key) Navigate(key);
        });

        AppEvents.NavigationRequested += key => OnUi(() => Navigate(key));
        AppEvents.StatusChanged += message => OnUi(() => StatusText = message);
        AppEvents.ScanCompleted += _ => OnUi(UpdateBadges);
        AppEvents.QueueChanged += () => OnUi(UpdateBadges);

        // The daily background check has no window of its own, so a new release is
        // announced the same way everything else is: a count on the menu entry that
        // leads to it, plus one line in the status bar. Nothing is installed by it.
        AppEvents.UpdateAvailable += release => OnUi(() =>
        {
            _updateAvailable = true;
            UpdateBadges();
            StatusText = Loc.T("about.autoNotice", release.Version);
        });

        AppEvents.UpdateStaged += version => OnUi(() =>
            StatusText = Loc.T("about.updateReady", version));

        Loc.LanguageChanged += () => OnUi(() =>
        {
            // Pages resolve their strings at parse time, so they are rebuilt rather
            // than re-bound. Cheap, and it removes a whole class of stale-text bugs.
            foreach (var item in NavItems)
            {
                item.RefreshLabel();
                item.ResetView();
            }

            if (SelectedNav is not null) CurrentView = SelectedNav.View;

            RaiseAll(nameof(ElevationText), nameof(StatusText));
            AppEvents.RaiseLanguageChanged();
        });

        // The scanner's StatusChanged is English diagnostics for the log; the status bar
        // is driven by the structured progress instead so it can be localised.
        AppHost.Scanner.ProgressChanged += progress => OnUi(() => ApplyProgress(progress));
        AppHost.Jobs.StatusChanged += message => OnUi(() => StatusText = message);

        StatusText = Loc.T("status.ready");
        Navigate("dashboard");
    }

    // =====================================================================================
    // Navigation
    // =====================================================================================

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!Set(ref _selectedNav, value) || value is null) return;

            foreach (var item in NavItems) item.IsSelected = ReferenceEquals(item, value);
            CurrentView = value.View;
        }
    }

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => Set(ref _currentView, value);
    }

    /// <summary>
    /// The whole menu, in order.
    ///
    /// Adding a page is one line here plus one view class; nothing else in the shell
    /// needs to change, which is what "more menus later" is meant to cost.
    /// </summary>
    private void BuildNavigation()
    {
        // Glyphs are Segoe MDL2 Assets code points written as \u escapes, so this file
        // stays plain ASCII and cannot be mangled by an editor, a diff or a patch tool.
        // Every one of them was rendered and visually checked against the shipped font.
        NavItems.Add(new NavItem("dashboard", "nav.dashboard", "\uE80F", () => new DashboardView()));
        NavItems.Add(new NavItem("devices",   "nav.devices",   "\uE772", () => new DevicesView()));
        NavItems.Add(new NavItem("updates",   "nav.updates",   "\uE777", () => new UpdatesView()));
        NavItems.Add(new NavItem("queue",     "nav.queue",     "\uE896", () => new QueueView()));
        NavItems.Add(new NavItem("backup",    "nav.backup",    "\uE74E", () => new BackupView()));
        NavItems.Add(new NavItem("history",   "nav.history",   "\uE81C", () => new HistoryView()));

        // The two monitor pages. They sit after the driver work rather than at the top
        // because that is still what Hexnest is for, and they sample nothing at all
        // until one of them is actually opened.
        NavItems.Add(new NavItem("system",    "nav.system",    "\uE950", () => new SystemMonitorView()));
        NavItems.Add(new NavItem("network",   "nav.network",   "\uE701", () => new NetworkMonitorView()));

        // The two maintenance pages. Both only ever act when the user presses something:
        // the startup page writes nothing until a switch is moved, and the cleaner opens
        // with every box unticked.
        NavItems.Add(new NavItem("startup",   "nav.startup",   "\uE7E8", () => new StartupView()));
        NavItems.Add(new NavItem("clean",     "nav.clean",     "\uE74D", () => new CleanupView()));

        NavItems.Add(new NavItem("logs",      "nav.logs",      "\uE7C3", () => new LogsView()));
        NavItems.Add(new NavItem("settings",  "nav.settings",  "\uE713", () => new SettingsView()));
        NavItems.Add(new NavItem("about",     "nav.about",     "\uE946", () => new AboutView()));
    }

    public void Navigate(string key)
    {
        var target = NavItems.FirstOrDefault(n =>
            n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (target is not null) SelectedNav = target;
    }

    /// <summary>Keeps the sidebar counters in sync with the last scan and the queue.</summary>
    private void UpdateBadges()
    {
        var scan = AppHost.LastScan;

        Find("devices").BadgeCount = scan?.ProblemDeviceCount ?? 0;
        Find("updates").BadgeCount = scan?.Candidates.Count ?? 0;

        var session = AppHost.Sessions.Current;
        Find("queue").BadgeCount = session?.PendingCount ?? 0;

        Find("about").BadgeCount = _updateAvailable ? 1 : 0;

        NavItem Find(string key) => NavItems.First(n => n.Key == key);
    }

    // =====================================================================================
    // Scanning
    // =====================================================================================

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand NavigateCommand { get; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>0-100 for the status bar progress bar.</summary>
    public double ScanPercent
    {
        get => _scanPercent;
        private set => Set(ref _scanPercent, value);
    }

    /// <summary>
    /// True while the running step genuinely cannot report a percentage, so the bar
    /// animates instead of sitting still and looking hung.
    /// </summary>
    public bool IsScanIndeterminate
    {
        get => _scanIndeterminate;
        private set => Set(ref _scanIndeterminate, value);
    }

    /// <summary>Turns structured scan progress into a localised status line.</summary>
    private void ApplyProgress(ScanProgress progress)
    {
        ScanPercent = progress.Percent;
        IsScanIndeterminate = progress.IsIndeterminate;

        int seconds = (int)Math.Round(progress.ElapsedSeconds);

        StatusText = progress.Phase switch
        {
            ScanPhase.Devices when progress.Completed == 0 => Loc.T("scan.devices"),
            ScanPhase.Devices => Loc.T("scan.devicesDone", progress.Detail ?? "0"),

            // Elapsed time is the only honest signal during a Windows Update query.
            ScanPhase.Sources => Loc.T("scan.sources", progress.Completed, progress.Total, seconds),

            ScanPhase.Finalizing => Loc.T("scan.finalizing"),
            ScanPhase.Done => StatusText,
            _ => Loc.T("dash.scanning")
        };
    }

    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanPercent = 0;
        IsScanIndeterminate = false;
        _scanCancellation = new CancellationTokenSource();

        try
        {
            var settings = AppHost.Settings.Current;

            var result = await AppHost.Scanner.ScanAsync(
                settings.IgnoredHardwareIds,
                settings.HiddenUpdateIds,
                _scanCancellation.Token).ConfigureAwait(true);

            AppHost.LastScan = result;
            AppHost.LastScanAt = DateTime.Now;

            AppEvents.RaiseScanCompleted(result);

            foreach (var warning in result.Warnings) Log.Warn(warning);

            StatusText = SummarizeScan(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("common.cancel");
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed", ex);
            StatusText = ex.Message;
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            IsScanning = false;
            IsScanIndeterminate = false;
            ScanPercent = 0;
        }
    }

    /// <summary>
    /// Builds the status line from what the scan actually found.
    ///
    /// Reports broken hardware even when no package is available for it: "everything is
    /// fine" next to seven dead devices would be the single most misleading thing this
    /// application could say.
    /// </summary>
    private static string SummarizeScan(ScanResult result)
    {
        var parts = new List<string>(3);

        if (result.MissingDriverCount > 0)
            parts.Add(Loc.T("status.summaryMissing", result.MissingDriverCount));

        int faulty = result.ProblemDeviceCount - result.MissingDriverCount;
        if (faulty > 0) parts.Add(Loc.T("status.summaryProblems", faulty));

        if (result.Candidates.Count > 0)
            parts.Add(Loc.T("status.summaryReady", result.Candidates.Count));

        return parts.Count == 0 ? Loc.T("dash.healthy") : string.Join("  ·  ", parts);
    }

    // =====================================================================================
    // Shell chrome
    // =====================================================================================

    public string Title => AppInfo.ProductName;

    public string VersionDisplay => "v" + AppInfo.Version;

    public bool IsElevated => Elevation.IsAdministrator;

    public string ElevationText => IsElevated ? Loc.T("status.admin") : Loc.T("status.notAdmin");

    public string MachineSummary =>
        $"{SystemInfo.Current.MachineDisplay}  ·  {SystemInfo.Current.OsDisplay}";

    public bool IsOffline =>
        AppHost.Settings.Current.OfflineMode || AppHost.LaunchMode == LaunchMode.Rescue;
}
