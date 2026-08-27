using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>
/// The unit of work that survives reboots. Persisted to
/// <c>%ProgramData%\DrvNest\session.json</c> after every state change so that a
/// crash or a restart never loses the queue. Launching with <c>--resume</c>
/// reloads this file and continues exactly where it stopped.
/// </summary>
public sealed class SessionState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Guards against resuming a session that belongs to another machine.</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>Every job, including finished ones, so the summary screen stays accurate.</summary>
    public List<DriverJob> Jobs { get; set; } = new();

    /// <summary>How many times the machine restarted while this session was running.</summary>
    public int RebootCount { get; set; }

    /// <summary>Set when at least one finished job asked for a restart.</summary>
    public bool RebootPending { get; set; }

    /// <summary>Description of the restore point created before the first install.</summary>
    public string? RestorePointDescription { get; set; }

    /// <summary>Folder holding the full driver backup taken before the session started.</summary>
    public string? PreflightBackupPath { get; set; }

    /// <summary>True for the "install everything missing" post-format flow.</summary>
    public bool IsFullRecoveryRun { get; set; }

    /// <summary>Safety valve: stop auto-resuming after this many restarts.</summary>
    public const int MaxRebootCount = 10;

    [JsonIgnore]
    public bool IsFinished => Jobs.Count > 0 && Jobs.All(j => j.IsTerminal);

    [JsonIgnore]
    public IEnumerable<DriverJob> Pending => Jobs.Where(j => !j.IsTerminal).OrderBy(j => j.Order);

    [JsonIgnore]
    public int SucceededCount => Jobs.Count(j => j.State == JobState.Succeeded);
    [JsonIgnore]
    public int FailedCount => Jobs.Count(j => j.State == JobState.Failed);
    [JsonIgnore]
    public int PendingCount => Jobs.Count(j => !j.IsTerminal);
    [JsonIgnore]
    public int TotalCount => Jobs.Count;

    [JsonIgnore]
    public double OverallPercent =>
        Jobs.Count == 0 ? 0 : Jobs.Sum(j => j.OverallPercent) / Jobs.Count;

    /// <summary>Marks every in-flight job as resumable and bumps the reboot counter.</summary>
    public void MarkRebooting()
    {
        foreach (var job in Jobs) job.PrepareForResume();
        RebootPending = true;
        RebootCount++;
        UpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>Turns parked jobs back into queued jobs after a successful restart.</summary>
    public void MarkResumed()
    {
        foreach (var job in Jobs.Where(j => j.State == JobState.PendingResume))
        {
            job.State = JobState.Queued;
            job.StatusText = "Resumed after restart";
        }
        RebootPending = false;
        UpdatedUtc = DateTime.UtcNow;
    }
}
