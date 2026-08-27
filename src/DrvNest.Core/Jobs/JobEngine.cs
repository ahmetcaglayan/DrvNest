using System.Diagnostics;
using DrvNest.Core.Abstractions;
using DrvNest.Core.Backup;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Persistence;
using DrvNest.Core.Resume;
using DrvNest.Core.Safety;

namespace DrvNest.Core.Jobs;

/// <summary>Raised when the queue finishes, so the UI can show the right summary.</summary>
public sealed record QueueCompletedEventArgs(
    int Succeeded,
    int Failed,
    int Cancelled,
    bool RebootRequired,
    bool WillAutoReboot);

/// <summary>
/// Runs the download + install queue.
///
/// Concurrency model, and the reason for it:
///   * Downloads run in parallel, up to <see cref="AppSettings.MaxParallelJobs"/>.
///     They are network bound and genuinely benefit from overlapping.
///   * Installs are serialised behind a single lock. This is not a simplification:
///     Windows Update returns WU_E_OPERATIONINPROGRESS if a second install starts,
///     and the PnP subsystem serialises pnputil anyway. Pretending otherwise would
///     produce a prettier progress screen and a pile of spurious failures.
///
/// Every state transition is written to the session file, so pulling the power cord
/// mid-install costs at most the job that was running.
/// </summary>
public sealed class JobEngine : IDisposable
{
    private readonly SessionStore _sessions;
    private readonly HistoryStore _history;
    private readonly SettingsStore _settings;
    private readonly DriverBackupService _backup;
    private readonly IReadOnlyDictionary<ProviderKind, IDriverProvider> _providers;

    /// <summary>Windows installs one driver at a time; so do we.</summary>
    private readonly SemaphoreSlim _installLock = new(1, 1);

    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public JobEngine(
        SessionStore sessions,
        HistoryStore history,
        SettingsStore settings,
        DriverBackupService backup,
        IEnumerable<IDriverProvider> providers)
    {
        _sessions = sessions;
        _history = history;
        _settings = settings;
        _backup = backup;
        _providers = providers.ToDictionary(p => p.Kind);
    }

    /// <summary>Fires whenever a job changes state, for status text and counters.</summary>
    public event Action<DriverJob>? JobChanged;

    /// <summary>Human readable progress for the status bar.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Fires once when the whole queue is done.</summary>
    public event Action<QueueCompletedEventArgs>? Completed;

    /// <summary>True while a queue is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Set when at least one installed driver needs a restart.</summary>
    public bool RebootRequired { get; private set; }

    // =====================================================================================
    // Queue construction
    // =====================================================================================

    /// <summary>Builds a fresh session from the candidates the user selected.</summary>
    public SessionState CreateSession(IEnumerable<UpdateCandidate> candidates, bool fullRecovery = false)
    {
        var ordered = candidates
            // Missing drivers first: a machine with no network driver must fix that before
            // anything else has a chance of working.
            .OrderByDescending(c => c.IsMissingDriver)
            .ThenByDescending(c => c.Severity)
            .ThenBy(c => c.DeviceClass)
            .ToList();

        var session = new SessionState { IsFullRecoveryRun = fullRecovery };

        for (int i = 0; i < ordered.Count; i++)
        {
            session.Jobs.Add(new DriverJob
            {
                Candidate = ordered[i],
                Order = i,
                BytesTotal = ordered[i].SizeBytes,
                State = JobState.Queued
            });
        }

        return session;
    }

    // =====================================================================================
    // Execution
    // =====================================================================================

    /// <summary>Starts a new queue. Returns when every job reaches a terminal state.</summary>
    public async Task RunAsync(SessionState session, CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("A queue is already running.");

        _sessions.Begin(session);
        await RunInternalAsync(session, isResume: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Continues a session that was interrupted by a restart.</summary>
    public async Task ResumeAsync(SessionState session, CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("A queue is already running.");

        session.MarkResumed();
        _sessions.Begin(session);

        Log.Info($"Resuming session {session.SessionId} " +
                 $"({session.PendingCount} job(s) left, restart #{session.RebootCount}).");

        await RunInternalAsync(session, isResume: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunInternalAsync(
        SessionState session,
        bool isResume,
        CancellationToken cancellationToken)
    {
        IsRunning = true;
        RebootRequired = false;

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cancellation.Token;

        try
        {
            await PreflightAsync(session, isResume, token).ConfigureAwait(false);

            var settings = _settings.Current;
            using var slots = new SemaphoreSlim(settings.MaxParallelJobs, settings.MaxParallelJobs);

            var pending = session.Pending.ToList();
            Report($"Starting {pending.Count} job(s), {settings.MaxParallelJobs} at a time.");

            var running = pending.Select(job => ProcessJobAsync(job, session, slots, token)).ToList();
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Info("Queue cancelled by the user.");
            foreach (var job in session.Jobs.Where(j => !j.IsTerminal))
            {
                job.State = JobState.Cancelled;
                job.StatusText = "Cancelled";
                Notify(job);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Queue failed unexpectedly", ex);
        }
        finally
        {
            IsRunning = false;
            await FinishAsync(session).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the queue. Downloads abort; an install already in flight finishes.</summary>
    public void Cancel()
    {
        Report("Cancelling...");
        _cancellation?.Cancel();
    }

    // =====================================================================================
    // Preflight
    // =====================================================================================

    private async Task PreflightAsync(SessionState session, bool isResume, CancellationToken token)
    {
        var settings = _settings.Current;

        // The resume hook goes in first: if the machine dies during preflight we still
        // come back on the next logon.
        if (settings.ResumeAfterReboot)
            await ResumeManager.EnableAsync(token).ConfigureAwait(false);

        if (isResume) return;

        if (RebootService.IsRebootPending())
        {
            Report("Windows already has a restart pending; some installs may be deferred.");
            Log.Warn("A restart was already pending before the queue started.");
        }

        if (settings.CreateRestorePoint && session.RestorePointDescription is null)
        {
            Report("Creating a system restore point...");

            var description = $"DrvNest - before {session.Jobs.Count} driver update(s)";
            var result = await RestorePointService.CreateAsync(description, token).ConfigureAwait(false);

            session.RestorePointDescription = result.Created ? description : null;
            Report(result.Message);
            _sessions.Touch();
        }
    }

    // =====================================================================================
    // One job
    // =====================================================================================

    private async Task ProcessJobAsync(
        DriverJob job,
        SessionState session,
        SemaphoreSlim slots,
        CancellationToken token)
    {
        await slots.WaitAsync(token).ConfigureAwait(false);

        try
        {
            int maxAttempts = _settings.Current.MaxRetryAttempts + 1;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                job.Attempt = attempt;
                if (attempt > 1)
                {
                    job.ResetForRetry();
                    job.Attempt = attempt;
                    Report($"Retrying {job.Candidate.DeviceName} ({attempt}/{maxAttempts})...");
                    await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                }

                var outcome = await RunOnceAsync(job, session, token).ConfigureAwait(false);
                if (outcome || attempt == maxAttempts) break;
            }
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Cancelled;
            job.StatusText = "Cancelled";
            job.FinishedUtc = DateTime.UtcNow;
            Notify(job);
        }
        catch (Exception ex)
        {
            Fail(job, -1, ex.Message);
            Log.Error($"Job {job.Candidate.Title} threw", ex);
        }
        finally
        {
            RecordHistory(job, session);
            _sessions.Touch();
            slots.Release();
        }
    }

    /// <summary>One download+install attempt. Returns true when the job succeeded.</summary>
    private async Task<bool> RunOnceAsync(DriverJob job, SessionState session, CancellationToken token)
    {
        if (!_providers.TryGetValue(job.Candidate.Provider, out var provider))
        {
            Fail(job, -1, $"No provider is registered for {job.Candidate.Provider}.");
            return false;
        }

        job.StartedUtc ??= DateTime.UtcNow;

        // ---- Download ------------------------------------------------------------------
        job.State = JobState.Downloading;
        job.StatusText = "Downloading";
        Notify(job);

        var meter = new SpeedMeter();

        var download = await provider.DownloadAsync(job, report =>
        {
            job.DownloadPercent = report.Percent;
            if (report.BytesTotal > 0) job.BytesTotal = report.BytesTotal;
            job.BytesDownloaded = report.BytesTransferred;
            job.SpeedBytesPerSecond = meter.Update(report.BytesTransferred);
            if (report.StatusText is not null) job.StatusText = report.StatusText;
            Notify(job);
        }, token).ConfigureAwait(false);

        if (!download.Success)
        {
            Fail(job, download.ResultCode, download.Message ?? "Download failed.");
            return false;
        }

        job.DownloadPercent = 100;
        job.SpeedBytesPerSecond = 0;
        job.State = JobState.Downloaded;
        job.StatusText = "Waiting to install";
        Notify(job);
        _sessions.Touch();

        // ---- Install (serialised) --------------------------------------------------------
        await _installLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();

            await BackupExistingDriverAsync(job, token).ConfigureAwait(false);

            job.State = JobState.Installing;
            job.StatusText = "Installing";
            Notify(job);
            Report($"Installing {job.Candidate.DeviceName}...");

            var install = await provider.InstallAsync(job, report =>
            {
                job.InstallPercent = report.Percent;
                if (report.StatusText is not null) job.StatusText = report.StatusText;
                Notify(job);
            }, token).ConfigureAwait(false);

            if (!install.Success)
            {
                // A provider can report "reboot first" without it being a real failure.
                if (install.RebootRequired)
                {
                    job.State = JobState.PendingResume;
                    job.StatusText = install.Message ?? "Restart required before installing";
                    RebootRequired = true;
                    session.RebootPending = true;
                    Notify(job);
                    return false;
                }

                Fail(job, install.ResultCode, install.Message ?? "Install failed.");
                return false;
            }

            job.InstallPercent = 100;
            job.ResultCode = 0;
            job.FinishedUtc = DateTime.UtcNow;
            job.State = install.RebootRequired ? JobState.RebootRequired : JobState.Succeeded;
            job.StatusText = install.RebootRequired ? "Installed - restart required" : "Installed";

            if (install.RebootRequired)
            {
                RebootRequired = true;
                session.RebootPending = true;
            }

            Notify(job);
            Report($"{job.Candidate.DeviceName}: {job.StatusText}");
            return true;
        }
        finally
        {
            _installLock.Release();
        }
    }

    /// <summary>
    /// Exports the driver that is about to be replaced, so the history view can offer a
    /// rollback. Only meaningful for a real upgrade; a device with no driver has nothing
    /// to back up.
    /// </summary>
    private async Task BackupExistingDriverAsync(DriverJob job, CancellationToken token)
    {
        if (!_settings.Current.BackupBeforeUpdate) return;
        if (job.Candidate.IsMissingDriver) return;
        if (job.BackupPath is not null) return;

        try
        {
            var entries = await PnpUtil.EnumerateDriversAsync(token).ConfigureAwait(false);

            var match = entries.FirstOrDefault(e =>
                !string.IsNullOrWhiteSpace(job.Candidate.Manufacturer) &&
                e.Provider.Contains(job.Candidate.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Version, job.Candidate.CurrentVersion, StringComparison.OrdinalIgnoreCase));

            if (match is null) return;

            job.StatusText = "Backing up the current driver";
            Notify(job);

            job.BackupPath = await _backup
                .ExportSinglePackageAsync(match.PublishedName, cancellationToken: token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed backup must never block the update the user asked for.
            Log.Warn($"Pre-update backup skipped for {job.Candidate.DeviceName}: {ex.Message}");
        }
    }

    // =====================================================================================
    // Completion
    // =====================================================================================

    private async Task FinishAsync(SessionState session)
    {
        var settings = _settings.Current;

        int succeeded = session.Jobs.Count(j => j.State is JobState.Succeeded or JobState.RebootRequired);
        int failed = session.Jobs.Count(j => j.State == JobState.Failed);
        int cancelled = session.Jobs.Count(j => j.State == JobState.Cancelled);
        bool hasPending = session.Jobs.Any(j => j.State == JobState.PendingResume);

        if (RebootRequired || hasPending)
        {
            session.MarkRebooting();
            _sessions.Flush();

            if (settings.ResumeAfterReboot) await ResumeManager.EnableAsync().ConfigureAwait(false);

            Report(hasPending
                ? "Restart required to continue with the remaining drivers."
                : "Restart required to activate the installed drivers.");
        }
        else
        {
            // Nothing left to do: clean up after ourselves.
            _sessions.Delete();
            await ResumeManager.DisableAsync().ConfigureAwait(false);
            Report($"Finished. {succeeded} succeeded, {failed} failed.");
        }

        bool willAutoReboot = settings.AutoReboot && (RebootRequired || hasPending);

        Completed?.Invoke(new QueueCompletedEventArgs(
            succeeded, failed, cancelled, RebootRequired || hasPending, willAutoReboot));

        if (willAutoReboot)
        {
            Report($"Restarting in {settings.AutoRebootDelaySeconds} seconds...");
            await RebootService.RestartAsync(settings.AutoRebootDelaySeconds).ConfigureAwait(false);
        }
    }

    private void RecordHistory(DriverJob job, SessionState session)
    {
        if (job.State is not (JobState.Succeeded or JobState.RebootRequired
            or JobState.Failed or JobState.Cancelled))
            return;

        try
        {
            _history.Add(job.ToHistoryRecord(session.SessionId, Platform.SystemInfo.Current.FullBuild));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not record history for {job.Candidate.Title}: {ex.Message}");
        }
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private void Fail(DriverJob job, int code, string message)
    {
        job.State = JobState.Failed;
        job.ResultCode = code;
        job.ErrorMessage = message;
        job.StatusText = "Failed";
        job.FinishedUtc = DateTime.UtcNow;
        job.SpeedBytesPerSecond = 0;

        Log.Warn($"{job.Candidate.DeviceName}: {message}");
        Notify(job);
    }

    private void Notify(DriverJob job)
    {
        try { JobChanged?.Invoke(job); } catch { /* a UI handler must not break the queue */ }
    }

    private void Report(string message)
    {
        Log.Info(message);
        try { StatusChanged?.Invoke(message); } catch { /* same */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _installLock.Dispose();
    }

    /// <summary>Smooths raw byte counts into a readable transfer rate.</summary>
    private sealed class SpeedMeter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastBytes;
        private TimeSpan _lastAt = TimeSpan.Zero;
        private double _smoothed;

        public double Update(long totalBytes)
        {
            var now = _clock.Elapsed;
            var elapsed = (now - _lastAt).TotalSeconds;
            if (elapsed < 0.25) return _smoothed;

            long delta = totalBytes - _lastBytes;
            _lastBytes = totalBytes;
            _lastAt = now;

            if (delta <= 0) return _smoothed;

            double instant = delta / elapsed;

            // Exponential moving average keeps the number from jittering every tick.
            _smoothed = _smoothed <= 0 ? instant : _smoothed * 0.7 + instant * 0.3;
            return _smoothed;
        }
    }
}
