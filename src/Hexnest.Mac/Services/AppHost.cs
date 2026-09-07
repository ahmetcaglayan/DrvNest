using Hexnest.Core.Cleanup;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Persistence;
using Hexnest.Core.Startup;

namespace Hexnest.Mac.Services;

/// <summary>
/// Composition root.
///
/// A hand-rolled container rather than Microsoft.Extensions.DependencyInjection, for
/// the same reason as on Windows: there are six services, they are all singletons, and
/// every package left out is weight removed from what somebody downloads.
///
/// The monitors are created here but never started here. Sampling begins when a page
/// that needs it is opened and stops when it is closed, so a Mac left sitting on the
/// dashboard is doing no work at all.
/// </summary>
public static class AppHost
{
    private static bool _initialized;

    public static SettingsStore Settings { get; private set; } = null!;
    public static HistoryStore History { get; private set; } = null!;
    public static StartupService Startup { get; private set; } = null!;
    public static CleanupScanner CleanupScanner { get; private set; } = null!;
    public static CleanupService Cleanup { get; private set; } = null!;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        AppPaths.EnsureCreated();
        Log.Initialize(AppPaths.LogFile, LogLevel.Info);

        Log.Info($"{Core.AppInfo.VersionDisplay} starting on macOS.");
        Log.Info($"Machine: {Core.Platform.SystemInfo.Current.MachineDisplay}");
        Log.Info($"System : {Core.Platform.SystemInfo.Current.OsDisplay} " +
                 $"({Core.Platform.SystemInfo.Current.Architecture})");

        Settings = new SettingsStore();
        History = new HistoryStore();
        Startup = new StartupService();
        CleanupScanner = new CleanupScanner();
        Cleanup = new CleanupService();

        var settings = Settings.Current;

        if (settings.HistoryRetentionDays > 0) History.Prune(settings.HistoryRetentionDays);

        Loc.SetLanguage(settings.Language);
        ThemeManager.Apply(settings.Theme);
    }

    public static void Shutdown()
    {
        try
        {
            Log.Info("Hexnest closed.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Shutdown was not clean: {ex.Message}");
        }
    }
}
