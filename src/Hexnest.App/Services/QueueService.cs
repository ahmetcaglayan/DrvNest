using System.Collections.ObjectModel;
using System.Windows;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Jobs;
using Hexnest.Core.Models;

namespace Hexnest.App.Services;

/// <summary>
/// UI-facing owner of the running queue.
///
/// The job engine is deliberately UI-free, so this is the one place that knows about
/// the dispatcher. It also holds the observable job list, which means the Updates
/// page and the Activity page look at exactly the same objects rather than two
/// copies that can drift apart.
/// </summary>
public sealed class QueueService
{
    private CancellationTokenSource? _cancellation;

    public QueueService()
    {
        AppHost.Jobs.JobChanged += _ => Notify();
        AppHost.Jobs.Completed += OnCompleted;
    }

    /// <summary>Jobs of the current session, in queue order.</summary>
    public ObservableCollection<DriverJob> Jobs { get; } = new();

    public bool IsRunning => AppHost.Jobs.IsRunning;

    public bool RebootRequired { get; private set; }

    /// <summary>
    /// A session found on disk that has not been continued yet.
    ///
    /// Populated when Hexnest starts and discovers an interrupted queue. It is only
    /// resumed automatically when the process was launched by the resume task; a
    /// manual launch shows it with a Continue button instead of silently installing
    /// drivers the moment the window opens.
    /// </summary>
    public SessionState? Pending { get; private set; }

    public bool HasPendingSession => Pending is not null && !IsRunning;

    /// <summary>Shows an interrupted session in the UI without starting it.</summary>
    public void LoadPending(SessionState session)
    {
        Pending = session;
        Replace(session.Jobs);
        Log.Info($"Loaded interrupted session with {session.PendingCount} job(s) still to do.");
    }

    /// <summary>Continues the session discovered at startup.</summary>
    public async Task ContinuePendingAsync()
    {
        var session = Pending;
        if (session is null) return;

        Pending = null;
        await ResumeAsync(session).ConfigureAwait(true);
    }

    /// <summary>Raised on the UI thread whenever anything about the queue changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once when the queue finishes, with the summary.</summary>
    public event Action<QueueCompletedEventArgs>? Completed;

    // =====================================================================================

    /// <summary>Builds a session from the selected candidates and starts it.</summary>
    public async Task StartAsync(IEnumerable<UpdateCandidate> candidates, bool fullRecovery = false)
    {
        if (IsRunning)
        {
            Log.Warn("A queue is already running; ignoring the new request.");
            return;
        }

        var session = AppHost.Jobs.CreateSession(candidates, fullRecovery);
        if (session.Jobs.Count == 0) return;

        Replace(session.Jobs);
        RebootRequired = false;

        _cancellation = new CancellationTokenSource();
        AppEvents.RequestNavigation("queue");

        try
        {
            await AppHost.Jobs.RunAsync(session, _cancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            Notify();
        }
    }

    /// <summary>Continues a session that survived a restart.</summary>
    public async Task ResumeAsync(SessionState session)
    {
        if (IsRunning) return;

        Replace(session.Jobs);
        RebootRequired = false;

        _cancellation = new CancellationTokenSource();
        AppEvents.RequestNavigation("queue");

        try
        {
            await AppHost.Jobs.ResumeAsync(session, _cancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            Notify();
        }
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        AppHost.Jobs.Cancel();
    }

    /// <summary>Re-queues everything that failed, as a fresh session.</summary>
    public async Task RetryFailedAsync()
    {
        var failed = Jobs.Where(j => j.IsRetryable).Select(j => j.Candidate).ToList();
        if (failed.Count == 0) return;

        await StartAsync(failed).ConfigureAwait(true);
    }

    // =====================================================================================

    private void Replace(IEnumerable<DriverJob> jobs)
    {
        var ordered = jobs.OrderBy(j => j.Order).ToList();

        OnUi(() =>
        {
            Jobs.Clear();
            foreach (var job in ordered) Jobs.Add(job);
        });

        Notify();
    }

    private void OnCompleted(QueueCompletedEventArgs args)
    {
        RebootRequired = args.RebootRequired;
        OnUi(() => Completed?.Invoke(args));
        Notify();
    }

    private void Notify() => OnUi(() =>
    {
        Changed?.Invoke();
        AppEvents.RaiseQueueChanged();
    });

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
