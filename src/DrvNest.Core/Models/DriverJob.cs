using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>
/// One queued download + install unit of work.
/// The same instance is bound directly by the UI (INotifyPropertyChanged) and
/// serialized into the resume session file, so a reboot never loses progress.
/// </summary>
public sealed class DriverJob : INotifyPropertyChanged
{
    private JobState _state = JobState.Queued;
    private double _downloadPercent;
    private double _installPercent;
    private long _bytesDownloaded;
    private long _bytesTotal;
    private double _speedBytesPerSecond;
    private string? _statusText;
    private string? _errorMessage;
    private DateTime? _startedUtc;
    private DateTime? _finishedUtc;
    private int _attempt;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public UpdateCandidate Candidate { get; set; } = new();

    /// <summary>Queue position. A resumed session replays jobs in this order.</summary>
    public int Order { get; set; }

    public JobState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnChanged(nameof(IsActive));
            OnChanged(nameof(IsTerminal));
            OnChanged(nameof(IsRetryable));
            OnChanged(nameof(OverallPercent));
        }
    }

    public double DownloadPercent
    {
        get => _downloadPercent;
        set { if (Set(ref _downloadPercent, value)) OnChanged(nameof(OverallPercent)); }
    }

    public double InstallPercent
    {
        get => _installPercent;
        set { if (Set(ref _installPercent, value)) OnChanged(nameof(OverallPercent)); }
    }

    public long BytesDownloaded
    {
        get => _bytesDownloaded;
        set { if (Set(ref _bytesDownloaded, value)) OnChanged(nameof(TransferDisplay)); }
    }

    public long BytesTotal
    {
        get => _bytesTotal;
        set { if (Set(ref _bytesTotal, value)) OnChanged(nameof(TransferDisplay)); }
    }

    /// <summary>Live throughput; never persisted because it is meaningless after a restart.</summary>
    [JsonIgnore]
    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set { if (Set(ref _speedBytesPerSecond, value)) OnChanged(nameof(SpeedDisplay)); }
    }

    public string? StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => Set(ref _errorMessage, value);
    }

    /// <summary>HRESULT returned by the provider. 0 means success.</summary>
    public int ResultCode { get; set; }

    public DateTime? StartedUtc
    {
        get => _startedUtc;
        set => Set(ref _startedUtc, value);
    }

    public DateTime? FinishedUtc
    {
        get => _finishedUtc;
        set => Set(ref _finishedUtc, value);
    }

    /// <summary>Retry counter used by the job engine.</summary>
    public int Attempt
    {
        get => _attempt;
        set => Set(ref _attempt, value);
    }

    /// <summary>True when this job was carried across a reboot.</summary>
    public bool ResumedFromReboot { get; set; }

    /// <summary>Folder holding the pre-update backup of the previous driver, for rollback.</summary>
    public string? BackupPath { get; set; }

    [JsonIgnore]
    public bool IsActive => State is JobState.Downloading or JobState.Downloaded or JobState.Installing;

    [JsonIgnore]
    public bool IsTerminal => State is JobState.Succeeded or JobState.Failed or JobState.Cancelled;

    [JsonIgnore]
    public bool IsRetryable => State is JobState.Failed or JobState.Cancelled;

    /// <summary>Weighted progress: download counts 60%, install 40%.</summary>
    [JsonIgnore]
    public double OverallPercent => State switch
    {
        JobState.Queued or JobState.PendingResume => 0,
        JobState.Downloading => Math.Clamp(DownloadPercent, 0, 100) * 0.6,
        JobState.Downloaded => 60,
        JobState.Installing => 60 + Math.Clamp(InstallPercent, 0, 100) * 0.4,
        JobState.Succeeded or JobState.RebootRequired => 100,
        _ => Math.Clamp(DownloadPercent, 0, 100) * 0.6
    };

    [JsonIgnore]
    public string TransferDisplay =>
        BytesTotal > 0
            ? $"{Formatting.HumanBytes(BytesDownloaded)} / {Formatting.HumanBytes(BytesTotal)}"
            : Formatting.HumanBytes(BytesDownloaded);

    [JsonIgnore]
    public string SpeedDisplay => Formatting.HumanSpeed(SpeedBytesPerSecond);

    [JsonIgnore]
    public TimeSpan? Duration =>
        StartedUtc is null ? null : (FinishedUtc ?? DateTime.UtcNow) - StartedUtc.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Called right before the machine restarts. Anything mid-flight is parked so the
    /// next launch can pick it up instead of reporting a bogus failure.
    /// </summary>
    public void PrepareForResume()
    {
        ResumedFromReboot = true;
        SpeedBytesPerSecond = 0;
        if (State is JobState.Downloading or JobState.Installing or JobState.Downloaded or JobState.Queued)
        {
            State = JobState.PendingResume;
            StatusText = "Will continue after restart";
        }
    }

    /// <summary>Re-arms a failed job for another attempt.</summary>
    public void ResetForRetry()
    {
        State = JobState.Queued;
        DownloadPercent = 0;
        InstallPercent = 0;
        BytesDownloaded = 0;
        SpeedBytesPerSecond = 0;
        ErrorMessage = null;
        ResultCode = 0;
        FinishedUtc = null;
        StatusText = null;
    }

    /// <summary>Builds the permanent history entry for this job.</summary>
    public HistoryRecord ToHistoryRecord(string? sessionId, string? osBuild)
        => new()
        {
            SessionId = sessionId,
            DeviceName = string.IsNullOrWhiteSpace(Candidate.DeviceName) ? Candidate.Title : Candidate.DeviceName,
            DeviceClass = Candidate.DeviceClass,
            Manufacturer = Candidate.Manufacturer,
            Title = Candidate.Title,
            FromVersion = Candidate.CurrentVersion,
            ToVersion = Candidate.NewVersion,
            Provider = Candidate.Provider,
            Outcome = State switch
            {
                JobState.Succeeded or JobState.RebootRequired => HistoryOutcome.Success,
                JobState.Cancelled => HistoryOutcome.Cancelled,
                _ => HistoryOutcome.Failed
            },
            ErrorMessage = ErrorMessage,
            ResultCode = ResultCode,
            SizeBytes = BytesTotal > 0 ? BytesTotal : Candidate.SizeBytes,
            DurationSeconds = Duration?.TotalSeconds ?? 0,
            RebootRequired = State == JobState.RebootRequired || Candidate.RebootRequired,
            BackupPath = BackupPath,
            WasMissingDriver = Candidate.IsMissingDriver,
            OsBuild = osBuild
        };
}
