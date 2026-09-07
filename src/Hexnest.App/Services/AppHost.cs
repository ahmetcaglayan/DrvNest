using System.Net.Http;
using Hexnest.Core.Abstractions;
using Hexnest.Core.Backup;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Jobs;
using Hexnest.Core.Models;
using Hexnest.Core.Persistence;
using Hexnest.Core.Providers;
using Hexnest.Core.Scanning;
using Hexnest.Core.Updating;

namespace Hexnest.App.Services;

/// <summary>
/// Composition root.
///
/// A hand-rolled container rather than Microsoft.Extensions.DependencyInjection:
/// there are nine services, they are all singletons, and every NuGet package left
/// out is weight removed from the single-file executable that people download onto
/// a freshly formatted machine.
/// </summary>
public static class AppHost
{
    private static bool _initialized;

    public static SettingsStore Settings { get; private set; } = null!;
    public static HistoryStore History { get; private set; } = null!;
    public static SessionStore Sessions { get; private set; } = null!;
    public static DriverBackupService Backup { get; private set; } = null!;
    public static ScanService Scanner { get; private set; } = null!;
    public static JobEngine Jobs { get; private set; } = null!;
    public static SelfUpdateService Updater { get; private set; } = null!;

    /// <summary>Runs the daily update check and stages an automatic install.</summary>
    public static UpdateCoordinator Updates { get; private set; } = null!;

    /// <summary>UI-facing owner of the running queue; created after the job engine.</summary>
    public static QueueService Queue { get; private set; } = null!;

    public static WindowsUpdateProvider WindowsUpdate { get; private set; } = null!;
    public static LocalRepositoryProvider LocalRepository { get; private set; } = null!;

    public static IReadOnlyList<IDriverProvider> Providers { get; private set; } = Array.Empty<IDriverProvider>();

    /// <summary>How this process was started, which decides the startup flow.</summary>
    public static LaunchMode LaunchMode { get; private set; } = LaunchMode.Normal;

    /// <summary>The most recent scan, shared by every view.</summary>
    public static ScanResult? LastScan { get; set; }

    public static DateTime? LastScanAt { get; set; }

    public static void Initialize(LaunchMode launchMode)
    {
        if (_initialized) return;
        _initialized = true;

        LaunchMode = launchMode;

        AppPaths.EnsureCreated();
        Log.Initialize(AppPaths.LogFile, LogLevel.Info);
        Log.Info($"{Core.AppInfo.VersionDisplay} starting in {launchMode} mode.");
        Log.Info($"Machine: {Core.Platform.SystemInfo.Current.MachineDisplay}");
        Log.Info($"System : {Core.Platform.SystemInfo.Current.OsDisplay} " +
                 $"({Core.Platform.SystemInfo.Current.Architecture})");
        Log.Info($"Elevated: {Core.Platform.Elevation.IsAdministrator}");

        Settings = new SettingsStore();
        History = new HistoryStore();
        Sessions = new SessionStore();
        Backup = new DriverBackupService(() =>
            string.IsNullOrWhiteSpace(Settings.Current.BackupRoot)
                ? AppPaths.BackupsDirectory
                : Settings.Current.BackupRoot!);

        var settings = Settings.Current;

        // Rescue mode is offline by definition: it exists for the machine that has no
        // working network driver yet.
        bool offline = settings.OfflineMode || launchMode == LaunchMode.Rescue;

        WindowsUpdate = new WindowsUpdateProvider
        {
            Enabled = !offline,
            IncludeOptional = settings.IncludeOptionalDrivers
        };

        LocalRepository = new LocalRepositoryProvider(() => Settings.Current.LocalRepositoryPaths);

        Providers = new IDriverProvider[] { LocalRepository, WindowsUpdate };

        Scanner = new ScanService(Providers);
        Jobs = new JobEngine(Sessions, History, Settings, Backup, Providers);
        Updater = new SelfUpdateService(new HttpClient { Timeout = TimeSpan.FromMinutes(15) });

        // Must come after Jobs: it subscribes to the engine's events in its constructor.
        Queue = new QueueService();

        Updates = new UpdateCoordinator(Updater, new SettingsStoreAccessor(
            () => Settings.Current,
            change =>
            {
                // The store hands out the live instance, so a change has to be applied to
                // a copy and saved, or a failed write would still have mutated memory.
                var copy = Settings.Current.Clone();
                change(copy);
                Settings.Save(copy);
            }));

        Settings.Changed += OnSettingsChanged;

        if (settings.HistoryRetentionDays > 0) History.Prune(settings.HistoryRetentionDays);

        Loc.SetLanguage(settings.Language);
        ThemeManager.Apply(settings.Theme);
    }

    private static void OnSettingsChanged(AppSettings settings)
    {
        WindowsUpdate.Enabled = !(settings.OfflineMode || LaunchMode == LaunchMode.Rescue);
        WindowsUpdate.IncludeOptional = settings.IncludeOptionalDrivers;
    }

    public static void Shutdown()
    {
        try
        {
            // Before anything else is torn down: swapping the executable is the last
            // thing Hexnest does, and only ever on the way out.
            Updates?.ApplyOnExit();
            Updates?.Dispose();

            Jobs?.Dispose();
            Sessions?.Dispose();
            Log.Info("Hexnest closed.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Shutdown was not clean: {ex.Message}");
        }
    }
}
