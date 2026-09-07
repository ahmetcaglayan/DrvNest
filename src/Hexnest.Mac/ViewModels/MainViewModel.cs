using System.Collections.ObjectModel;
using Avalonia.Controls;
using Hexnest.Core;
using Hexnest.Core.Localization;
using Hexnest.Core.Platform;
using Hexnest.Mac.Services;
using Hexnest.Mac.Views;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The shell: navigation and the status bar.
///
/// Deliberately thinner than the Windows shell, which also owns the driver scan that
/// four of its pages consume. There is nothing to scan on a Mac, so this class does
/// only the two things a shell should do.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private NavItem? _selectedNav;
    private Control? _currentView;
    private string _statusText = string.Empty;

    public MainViewModel()
    {
        BuildNavigation();

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavItem item) SelectedNav = item;
            else if (parameter is string key) Navigate(key);
        });

        AppEvents.NavigationRequested += key => OnUi(() => Navigate(key));
        AppEvents.StatusChanged += message => OnUi(() => StatusText = message);

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

            // PageTitle is computed from SelectedNav.Label, and the SelectedNav setter
            // is the only other place that raises it - which does not help here,
            // because the selection has not changed. Without this the header strip
            // keeps the previous language until a *different* page is opened.
            RaiseAll(nameof(AccountText), nameof(MachineSummary), nameof(PageTitle));
            AppEvents.RaiseLanguageChanged();
        });

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
            Raise(nameof(PageTitle));
        }
    }

    public Control? CurrentView
    {
        get => _currentView;
        private set => Set(ref _currentView, value);
    }

    public string PageTitle => SelectedNav?.Label ?? AppInfo.ProductName;

    /// <summary>
    /// The whole menu, in order.
    ///
    /// Five of the Windows build's thirteen pages are missing, and every one of them is
    /// missing for the same reason: it is about the driver store, and macOS does not
    /// have one. Devices, Updates, Queue, Backup and History all exist to find, install,
    /// queue, back up and record driver packages. There is no honest Mac equivalent to
    /// put in their place - Apple ships drivers inside the operating system and there is
    /// nothing for a third party to scan or update - so they are left out rather than
    /// shown as empty pages that never fill in.
    ///
    /// What remains is everything Hexnest does that is not about drivers, and all of it
    /// works here properly rather than approximately.
    /// </summary>
    private void BuildNavigation()
    {
        NavItems.Add(new NavItem("dashboard", "nav.dashboard", "Icon.Dashboard", () => new DashboardView()));
        NavItems.Add(new NavItem("system",    "nav.system",    "Icon.System",    () => new SystemMonitorView()));
        NavItems.Add(new NavItem("network",   "nav.network",   "Icon.Network",   () => new NetworkMonitorView()));
        NavItems.Add(new NavItem("startup",   "nav.startup",   "Icon.Startup",   () => new StartupView()));
        NavItems.Add(new NavItem("clean",     "nav.clean",     "Icon.Clean",     () => new CleanupView()));
        NavItems.Add(new NavItem("logs",      "nav.logs",      "Icon.Logs",      () => new LogsView()));
        NavItems.Add(new NavItem("settings",  "nav.settings",  "Icon.Settings",  () => new SettingsView()));
        NavItems.Add(new NavItem("about",     "nav.about",     "Icon.About",     () => new AboutView()));
    }

    public void Navigate(string key)
    {
        var target = NavItems.FirstOrDefault(n =>
            n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (target is not null) SelectedNav = target;
    }

    public RelayCommand NavigateCommand { get; }

    // =====================================================================================
    // Shell chrome
    // =====================================================================================

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public string Title => AppInfo.ProductName;

    public string VersionDisplay => "v" + AppInfo.Version;

    /// <summary>
    /// What the sidebar footer says about privileges.
    ///
    /// The Windows shell shows "Administrator" or "Not administrator", because there it
    /// decides whether a driver can be installed at all. Here it would always read "not
    /// administrator", since the Mac build never runs as root, and a permanent warning
    /// about a thing that is deliberate would be noise. It names the account instead,
    /// and says whether that account could authorise a system change if one were ever
    /// needed.
    /// </summary>
    public string AccountText => Elevation.IsAdministrator
        ? Loc.T("status.admin")
        : $"{SystemInfo.Current.UserName}"
          + (Elevation.IsAdministratorAccount ? $"  ·  {Loc.T("mac.adminAccount")}" : string.Empty);

    public string MachineSummary =>
        $"{SystemInfo.Current.MachineDisplay}  ·  {SystemInfo.Current.OsDisplay}";
}
