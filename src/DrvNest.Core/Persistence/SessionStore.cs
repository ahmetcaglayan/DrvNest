using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;

namespace DrvNest.Core.Persistence;

/// <summary>
/// Owns the reboot-surviving session file.
///
/// Saves are debounced: the job engine reports progress many times a second and we
/// do not want to hammer the disk, but we must never be more than a second behind
/// reality when the machine restarts.
/// </summary>
public sealed class SessionStore : IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Timer _timer;

    private SessionState? _session;
    private bool _dirty;
    private bool _disposed;

    public SessionStore()
    {
        _timer = new Timer(_ => FlushIfDirty(), null, SaveInterval, SaveInterval);
    }

    /// <summary>The session currently in memory, if any.</summary>
    public SessionState? Current
    {
        get { lock (_gate) return _session; }
    }

    /// <summary>True when a session file exists on disk that could be resumed.</summary>
    public static bool HasPersistedSession() => File.Exists(AppPaths.SessionFile);

    /// <summary>
    /// Loads the persisted session if it is valid and belongs to this machine.
    /// Returns null when there is nothing sensible to resume.
    /// </summary>
    public SessionState? Load()
    {
        var loaded = JsonStore.Read<SessionState>(AppPaths.SessionFile);
        if (loaded is null) return null;

        if (loaded.SchemaVersion != SessionState.CurrentSchemaVersion)
        {
            Log.Warn($"Ignoring session with schema version {loaded.SchemaVersion}.");
            Delete();
            return null;
        }

        if (!string.Equals(loaded.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Ignoring session created on '{loaded.MachineName}'.");
            Delete();
            return null;
        }

        if (loaded.RebootCount > SessionState.MaxRebootCount)
        {
            Log.Warn($"Session exceeded {SessionState.MaxRebootCount} restarts; abandoning it.");
            Delete();
            return null;
        }

        if (loaded.Jobs.Count == 0 || loaded.IsFinished)
        {
            Log.Info("Persisted session has nothing left to do.");
            Delete();
            return null;
        }

        lock (_gate) _session = loaded;

        Log.Info($"Loaded session {loaded.SessionId} with {loaded.PendingCount} pending job(s), " +
                 $"{loaded.RebootCount} restart(s) so far.");
        return loaded;
    }

    /// <summary>Starts tracking a new session and writes it immediately.</summary>
    public void Begin(SessionState session)
    {
        lock (_gate)
        {
            _session = session;
            _dirty = true;
        }
        Flush();
        Log.Info($"Session {session.SessionId} started with {session.Jobs.Count} job(s).");
    }

    /// <summary>Marks the in-memory session as needing a write on the next tick.</summary>
    public void Touch()
    {
        lock (_gate)
        {
            if (_session is null) return;
            _session.UpdatedUtc = DateTime.UtcNow;
            _dirty = true;
        }
    }

    /// <summary>Writes the session to disk right now. Called before a restart.</summary>
    public void Flush()
    {
        SessionState? snapshot;
        lock (_gate)
        {
            if (_session is null) return;
            _session.UpdatedUtc = DateTime.UtcNow;
            _dirty = false;
            snapshot = _session;
        }

        JsonStore.Write(AppPaths.SessionFile, snapshot);
    }

    /// <summary>Removes the session file; the queue is finished or was abandoned.</summary>
    public void Delete()
    {
        lock (_gate)
        {
            _session = null;
            _dirty = false;
        }

        try
        {
            if (File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            Log.Info("Session file removed.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete session file: {ex.Message}");
        }
    }

    private void FlushIfDirty()
    {
        bool needed;
        lock (_gate) needed = _dirty && _session is not null;
        if (needed) Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        FlushIfDirty();
    }
}
