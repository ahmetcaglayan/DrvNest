using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>
/// A permanent entry in the update history. Stored one JSON object per line in
/// <c>%ProgramData%\DrvNest\history.jsonl</c> so the file is append-only, crash
/// safe and trivially exportable.
/// </summary>
public sealed class HistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Groups every record produced by one batch run.</summary>
    public string? SessionId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceClass { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public ProviderKind Provider { get; set; }

    public HistoryOutcome Outcome { get; set; }

    public string? ErrorMessage { get; set; }

    public int ResultCode { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Download plus install time.</summary>
    public double DurationSeconds { get; set; }

    public bool RebootRequired { get; set; }

    /// <summary>Backup folder captured before the install, used by the rollback action.</summary>
    public string? BackupPath { get; set; }

    public string MachineName { get; set; } = Environment.MachineName;

    public string? OsBuild { get; set; }

    /// <summary>True when this was a first-time install rather than an upgrade.</summary>
    public bool WasMissingDriver { get; set; }

    [JsonIgnore]
    public string VersionTransition =>
        string.IsNullOrWhiteSpace(FromVersion)
            ? $"new  →  {ToVersion}"
            : $"{FromVersion}  →  {ToVersion}";

    /// <summary>The timestamp in the machine's own time zone, for display.</summary>
    [JsonIgnore]
    public DateTime TimestampLocal => TimestampUtc.ToLocalTime();

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

    /// <summary>True when a backup folder still exists and rollback is worth offering.</summary>
    [JsonIgnore]
    public bool CanRollback =>
        Outcome == HistoryOutcome.Success &&
        !string.IsNullOrWhiteSpace(BackupPath) &&
        Directory.Exists(BackupPath);
}
