using System.IO;
using DrvNest.Core;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Updating;

namespace DrvNest.App.Services;

/// <summary>
/// Runs the automatic half of the built-in updater.
///
/// Before this existed, DrvNest only ever learned about a new release when somebody
/// opened the About page and pressed a button - which meant most installs stayed on
/// whatever version they were first downloaded as, forever. Now it asks GitHub once a
/// day, in the background, well after startup.
///
/// What it deliberately does not do:
///
///   * Nothing is downloaded unless the user turned that on. The default is a check
///     that reports and stops.
///   * Nothing is ever installed while DrvNest is running. Even with automatic
///     installs enabled, the verified file is only swapped in as the application
///     closes, so an update can never land in the middle of a driver queue.
///   * The check is skipped entirely in offline and rescue mode. A machine that was
///     formatted five minutes ago has no network, and rescue mode promises not to
///     touch one even if it does.
///
/// The swap itself, and the SHA-256 verification that guards it, live in
/// <see cref="SelfUpdateService"/>; this class only decides when to call them.
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    /// <summary>
    /// How long to wait after startup before the first check.
    ///
    /// Long enough that it never competes with the opening scan for the network, and
    /// long enough that someone who opens DrvNest to fix one driver and closes it
    /// again is never charged for a request they did not need.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    private readonly SettingsStoreAccessor _settings;
    private readonly SelfUpdateService _updater;

    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    /// <summary>The release found by the last automatic check, if it is still newer.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>A downloaded, verified file waiting for the swap on exit.</summary>
    public string? PendingFile { get; private set; }

    public string? PendingVersion { get; private set; }

    public UpdateCoordinator(SelfUpdateService updater, SettingsStoreAccessor settings)
    {
        _updater = updater;
        _settings = settings;
    }

    /// <summary>
    /// Schedules the background check. Returns immediately; nothing blocks startup.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        var settings = _settings.Current;

        // Recover a download that was verified during the previous run but never applied,
        // e.g. because the machine lost power. The file is only trusted if it is still
        // where it was left; the checksum was verified before it was recorded.
        if (!string.IsNullOrWhiteSpace(settings.PendingUpdateFile) &&
            File.Exists(settings.PendingUpdateFile))
        {
            PendingFile = settings.PendingUpdateFile;
            PendingVersion = settings.PendingUpdateVersion;

            Log.Info($"An update to {PendingVersion} is staged and will be applied on exit.");
        }
        else if (settings.PendingUpdateFile is not null)
        {
            ClearPending();
        }

        if (!settings.AutoCheckUpdates)
        {
            Log.Debug("Automatic update checks are turned off.");
            return;
        }

        if (AppHost.LaunchMode == LaunchMode.Rescue || settings.OfflineMode)
        {
            Log.Debug("Offline: skipping the automatic update check.");
            return;
        }

        if (!AppInfo.IsRepositoryConfigured) return;

        if (!IsDue(settings))
        {
            Log.Debug("The automatic update check is not due yet.");
            return;
        }

        _cancellation = new CancellationTokenSource();
        _ = RunAsync(_cancellation.Token);
    }

    /// <summary>True when enough time has passed since the last check.</summary>
    private static bool IsDue(AppSettings settings)
    {
        if (settings.LastUpdateCheckUtc is not { } last) return true;

        // A clock that jumped backwards - a fresh install often has one until it syncs -
        // must not park the check a month into the future.
        if (last > DateTime.UtcNow) return true;

        return DateTime.UtcNow - last >= TimeSpan.FromHours(settings.UpdateCheckIntervalHours);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(StartupDelay, cancellationToken).ConfigureAwait(false);

            var settings = _settings.Current;

            var result = await _updater
                .CheckAsync(settings.IncludePrereleaseUpdates, cancellationToken)
                .ConfigureAwait(false);

            // Only a completed request counts as a check; a failed one should be retried
            // on the next launch rather than suppressed for a day.
            if (result.Status is UpdateCheckStatus.NetworkError or UpdateCheckStatus.Error)
            {
                Log.Debug($"Automatic update check did not complete: {result.Message}");
                return;
            }

            _settings.Update(s => s.LastUpdateCheckUtc = DateTime.UtcNow);

            if (!result.UpdateAvailable || result.Release is null) return;

            var release = result.Release;

            if (string.Equals(settings.SkippedUpdateVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"Version {release.Version} is available but was skipped by the user.");
                return;
            }

            Available = release;
            AppEvents.RaiseUpdateAvailable(release);

            Log.Info($"Automatic check found DrvNest {release.Version}.");

            if (settings.AutoInstallUpdates) await StageAsync(release, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The application is closing; nothing to report.
        }
        catch (Exception ex)
        {
            Log.Debug($"Automatic update check failed: {ex.Message}");
        }
    }

    /// <summary>Downloads and verifies the release, then records it for the swap on exit.</summary>
    private async Task StageAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var file = await _updater.DownloadAsync(release, null, cancellationToken).ConfigureAwait(false);

        // DownloadAsync returns null when the checksum did not match, or when the release
        // published none at all. Either way there is nothing here worth installing.
        if (file is null)
        {
            Log.Warn($"The automatic download of {release.Version} was not installable.");
            return;
        }

        PendingFile = file;
        PendingVersion = release.Version;

        _settings.Update(s =>
        {
            s.PendingUpdateFile = file;
            s.PendingUpdateVersion = release.Version;
        });

        AppEvents.RaiseUpdateStaged(release.Version);
        Log.Info($"DrvNest {release.Version} is staged and will be applied when the application closes.");
    }

    /// <summary>
    /// Applies a staged update as the application shuts down.
    ///
    /// Called from the exit path rather than at startup on purpose: swapping the file on
    /// the way out means the user gets the new version the next time they open DrvNest,
    /// with no surprise relaunch and no chance of the executable changing underneath a
    /// running driver queue.
    /// </summary>
    public void ApplyOnExit()
    {
        var staged = PendingFile;

        if (staged is null || !File.Exists(staged)) return;

        try
        {
            if (_updater.Apply(staged, relaunch: false))
            {
                Log.Info($"DrvNest was updated to {PendingVersion} on exit.");
                ClearPending();
            }
        }
        catch (Exception ex)
        {
            // A failed swap leaves the original executable in place, which is the
            // correct outcome; the pending file stays recorded so the next exit retries.
            Log.Warn($"Could not apply the staged update: {ex.Message}");
        }
    }

    /// <summary>Stops reminding the user about this version.</summary>
    public void Skip(string version)
    {
        _settings.Update(s => s.SkippedUpdateVersion = version);
        Available = null;
    }

    private void ClearPending()
    {
        PendingFile = null;
        PendingVersion = null;

        _settings.Update(s =>
        {
            s.PendingUpdateFile = null;
            s.PendingUpdateVersion = null;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }
}

/// <summary>
/// The narrow slice of the settings store the coordinator needs.
///
/// An interface-shaped seam rather than the store itself, so the update logic can be
/// reasoned about - and, if it ever matters, tested - without dragging in the whole
/// persistence layer.
/// </summary>
public sealed class SettingsStoreAccessor
{
    private readonly Func<AppSettings> _read;
    private readonly Action<Action<AppSettings>> _write;

    public SettingsStoreAccessor(Func<AppSettings> read, Action<Action<AppSettings>> write)
    {
        _read = read;
        _write = write;
    }

    public AppSettings Current => _read();

    public void Update(Action<AppSettings> change) => _write(change);
}
