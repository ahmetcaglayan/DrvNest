using System.Windows;
using System.Windows.Threading;
using DrvNest.App.Services;
using DrvNest.App.ViewModels;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Platform;
using DrvNest.Core.Updating;

namespace DrvNest.App;

/// <summary>
/// Application entry point: argument parsing, single instance enforcement, crash
/// handling and the post-restart resume flow.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Global rather than per-session, because two copies installing drivers at the
    /// same time is genuinely destructive, not merely untidy.
    /// </summary>
    private const string GlobalMutexName = @"Global\DrvNest.SingleInstance.9f2c";

    /// <summary>
    /// Fallback for a non-elevated run: creating an object in the Global namespace
    /// needs SeCreateGlobalPrivilege, which a standard user does not have. Without
    /// this fallback DrvNest would refuse to start at all when run unelevated.
    /// </summary>
    private const string LocalMutexName = @"Local\DrvNest.SingleInstance.9f2c";

    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var launchMode = ParseLaunchMode(e.Args);

        if (!TryClaimSingleInstance())
        {
            MessageBox.Show(
                "DrvNest is already running.",
                "DrvNest",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        // Anything that escapes must be logged; a driver tool that vanishes without
        // a trace is impossible to support.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppHost.Initialize(launchMode);

        // Remove the previous executable if this launch is the result of a self-update.
        SelfUpdateService.CleanUpAfterUpdate();

        // Schedules the daily check. Returns immediately and waits 20 seconds before
        // touching the network, so it never competes with the opening scan.
        AppHost.Updates.Start();

        var window = new MainWindow { DataContext = new MainViewModel() };
        MainWindow = window;
        window.Show();

        // Capture mode replaces the normal startup flow: it drives the whole menu and
        // exits. See Services/CaptureRunner.cs; it is how the published screenshots are
        // regenerated after a UI change.
        var captureFolder = CaptureRunner.ParseFolder(e.Args);

        if (captureFolder is not null)
        {
            if (CaptureRunner.ParseLanguage(e.Args) is { } language) Loc.SetLanguage(language);

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(async () => await CaptureRunner.RunAsync(
                    window, captureFolder, scanFirst: launchMode != LaunchMode.Rescue)));

            return;
        }

        // Deferred so the window paints before any long running work starts.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(async () => await RunStartupFlowAsync(launchMode)));
    }

    // =====================================================================================
    // Startup flow
    // =====================================================================================

    private static async Task RunStartupFlowAsync(LaunchMode launchMode)
    {
        try
        {
            if (!Elevation.IsAdministrator)
            {
                Log.Warn("Not running elevated. Installing drivers will fail.");
                AppEvents.RaiseStatus(Loc.T("err.needAdmin"));
            }

            var settings = AppHost.Settings.Current;

            // ---- Interrupted session ---------------------------------------------------
            var session = AppHost.Sessions.Load();

            if (session is not null)
            {
                AppHost.Queue.LoadPending(session);
                AppEvents.RequestNavigation("queue");

                bool autoContinue = launchMode == LaunchMode.Resume && settings.ResumeAfterReboot;

                if (autoContinue)
                {
                    AppEvents.RaiseStatus(Loc.T("queue.resumed"));
                    await AppHost.Queue.ContinuePendingAsync().ConfigureAwait(true);
                    return;
                }

                // A manual launch shows the queue and waits for the user to press Continue.
                return;
            }

            // ---- Normal launch -----------------------------------------------------------
            if (settings.ScanOnStartup && Application.Current.MainWindow?.DataContext is MainViewModel shell)
                await shell.ScanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("Startup flow failed", ex);
        }
    }

    private static LaunchMode ParseLaunchMode(IReadOnlyList<string> args)
    {
        foreach (var argument in args)
        {
            var value = argument.TrimStart('-', '/').ToLowerInvariant();

            if (value is "resume") return LaunchMode.Resume;
            if (value is "rescue" or "offline") return LaunchMode.Rescue;
        }

        return LaunchMode.Normal;
    }

    // =====================================================================================
    // Single instance
    // =====================================================================================

    private bool TryClaimSingleInstance()
    {
        // Machine-wide first. Access denied here is ambiguous on its own: it can mean
        // an elevated instance already owns the mutex, or that this process lacks the
        // privilege to create objects in the Global namespace. Probing for the object
        // separates the two, and getting it wrong would mean refusing to start.
        try
        {
            _singleInstance = new Mutex(initiallyOwned: true, GlobalMutexName, out bool created);

            if (!created)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
            }

            return created;
        }
        catch (UnauthorizedAccessException)
        {
            if (MutexExists(GlobalMutexName))
            {
                Log.Info("Another DrvNest instance already holds the machine-wide lock.");
                return false;
            }

            Log.Debug("Cannot use the Global namespace; falling back to a per-session lock.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not claim the machine-wide lock: {ex.Message}");
        }

        try
        {
            _singleInstance = new Mutex(initiallyOwned: true, LocalMutexName, out bool created);

            if (!created)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
            }

            return created;
        }
        catch (Exception ex)
        {
            // Never block startup just because the lock could not be created.
            Log.Warn($"Single instance protection is unavailable: {ex.Message}");
            return true;
        }
    }

    /// <summary>True when a mutex with this name exists, whether or not we may open it.</summary>
    private static bool MutexExists(string name)
    {
        try
        {
            return Mutex.TryOpenExisting(name, out var existing) && Dispose(existing);
        }
        catch (UnauthorizedAccessException)
        {
            // It exists and belongs to a process we cannot touch.
            return true;
        }
        catch
        {
            return false;
        }

        static bool Dispose(Mutex mutex)
        {
            mutex.Dispose();
            return true;
        }
    }

    // =====================================================================================
    // Crash handling
    // =====================================================================================

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);

        var message =
            $"{e.Exception.Message}\n\n" +
            (Loc.Language == "tr"
                ? $"Ayrıntılar günlüğe yazıldı:\n{Log.FilePath}"
                : $"Details were written to the log:\n{Log.FilePath}");

        MessageBox.Show(message, "DrvNest", MessageBoxButton.OK, MessageBoxImage.Error);

        // Keep running: a failed view must not take a driver install down with it.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) Log.Error("Fatal exception", exception);
        else Log.Error($"Fatal exception: {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    // =====================================================================================

    protected override void OnExit(ExitEventArgs e)
    {
        AppHost.Shutdown();

        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        catch
        {
            // Nothing useful to do while exiting.
        }

        base.OnExit(e);
    }
}
