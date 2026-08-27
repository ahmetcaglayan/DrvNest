using System.Collections.ObjectModel;
using DrvNest.App.Services;
using DrvNest.Core.Jobs;
using DrvNest.Core.Models;
using DrvNest.Core.Resume;

namespace DrvNest.App.ViewModels;

/// <summary>
/// The activity page: live progress for every job, plus the restart prompt.
///
/// Deliberately shows download and install as two distinct phases, because that is
/// what actually happens: downloads overlap, installs do not.
/// </summary>
public sealed class QueueViewModel : ViewModelBase
{
    private string? _summary;
    private bool _rebootRequired;
    private bool _restartScheduled;

    public QueueViewModel()
    {
        CancelAllCommand = new RelayCommand(() => AppHost.Queue.Cancel(), () => IsRunning);
        RetryFailedCommand = new AsyncRelayCommand(
            () => AppHost.Queue.RetryFailedAsync(), () => FailedCount > 0 && !IsRunning);

        RestartNowCommand = new AsyncRelayCommand(RestartAsync, () => RebootRequired && !_restartScheduled);
        CancelRestartCommand = new AsyncRelayCommand(CancelRestartAsync, () => _restartScheduled);

        ContinueCommand = new AsyncRelayCommand(
            () => AppHost.Queue.ContinuePendingAsync(), () => AppHost.Queue.HasPendingSession);

        AppHost.Queue.Changed += () => OnUi(Refresh);
        AppHost.Queue.Completed += OnCompleted;
        AppEvents.LanguageChanged += () => OnUi(Refresh);

        Refresh();
    }

    public ObservableCollection<DriverJob> Jobs => AppHost.Queue.Jobs;

    public RelayCommand CancelAllCommand { get; }
    public AsyncRelayCommand RetryFailedCommand { get; }
    public AsyncRelayCommand RestartNowCommand { get; }
    public AsyncRelayCommand CancelRestartCommand { get; }
    public AsyncRelayCommand ContinueCommand { get; }

    /// <summary>True when an interrupted session is waiting for the user to continue it.</summary>
    public bool HasPendingSession => AppHost.Queue.HasPendingSession;

    public bool IsRunning => AppHost.Queue.IsRunning;

    public bool IsEmpty => Jobs.Count == 0;

    public int TotalCount => Jobs.Count;
    public int DoneCount => Jobs.Count(j => j.State is JobState.Succeeded or JobState.RebootRequired);
    public int FailedCount => Jobs.Count(j => j.State == JobState.Failed);
    public int ActiveCount => Jobs.Count(j => j.IsActive);

    public double OverallPercent =>
        Jobs.Count == 0 ? 0 : Jobs.Sum(j => j.OverallPercent) / Jobs.Count;

    public string ProgressText => $"{DoneCount}/{TotalCount}";

    public string? Summary
    {
        get => _summary;
        private set { if (Set(ref _summary, value)) Raise(nameof(HasSummary)); }
    }

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool RebootRequired
    {
        get => _rebootRequired;
        private set => Set(ref _rebootRequired, value);
    }

    public bool RestartScheduled => _restartScheduled;

    /// <summary>True when the queue still has work that will continue after a restart.</summary>
    public bool WillResume =>
        Jobs.Any(j => j.State == JobState.PendingResume) ||
        (AppHost.Sessions.Current?.PendingCount ?? 0) > 0;

    // =====================================================================================

    private void Refresh() => RaiseAll(
        nameof(IsRunning), nameof(IsEmpty), nameof(TotalCount), nameof(DoneCount),
        nameof(FailedCount), nameof(ActiveCount), nameof(OverallPercent),
        nameof(ProgressText), nameof(WillResume), nameof(HasPendingSession));

    private void OnCompleted(QueueCompletedEventArgs args) => OnUi(() =>
    {
        RebootRequired = args.RebootRequired;

        var parts = new List<string>
        {
            $"{args.Succeeded} {Loc.T("hist.success").ToLowerInvariant()}"
        };

        if (args.Failed > 0) parts.Add($"{args.Failed} {Loc.T("hist.failed").ToLowerInvariant()}");
        if (args.Cancelled > 0) parts.Add($"{args.Cancelled} {Loc.T("hist.cancelled").ToLowerInvariant()}");

        Summary = string.Join(" · ", parts);
        _restartScheduled = args.WillAutoReboot;

        Refresh();
        RelayCommand.RaiseCanExecuteChanged();
    });

    private async Task RestartAsync()
    {
        var delay = AppHost.Settings.Current.AutoRebootDelaySeconds;

        if (await RebootService.RestartAsync(delay).ConfigureAwait(true))
        {
            _restartScheduled = true;
            Summary = Loc.T("queue.willResume");
            Raise(nameof(RestartScheduled));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CancelRestartAsync()
    {
        if (await RebootService.CancelRestartAsync().ConfigureAwait(true))
        {
            _restartScheduled = false;
            Raise(nameof(RestartScheduled));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
