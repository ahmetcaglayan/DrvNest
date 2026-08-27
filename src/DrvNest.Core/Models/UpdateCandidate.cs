using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>
/// An installable driver package. Provider agnostic: Windows Update, a local
/// repository folder and a DrvNest backup all project onto this shape so the
/// job engine and the UI never care where a package came from.
/// </summary>
public sealed class UpdateCandidate
{
    /// <summary>Stable id used by the queue and by the resume session file.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Provider specific id: WU UpdateID, or the absolute .inf path for local packages.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>WU revision number; the same UpdateID can be re-issued with a new revision.</summary>
    public int ProviderRevision { get; set; }

    public ProviderKind Provider { get; set; } = ProviderKind.Unknown;

    /// <summary>Package title, e.g. "NVIDIA - Display - 31.0.15.3623".</summary>
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Target device name shown to the user.</summary>
    public string DeviceName { get; set; } = string.Empty;

    public string DeviceClass { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Version currently installed. Null means the device has no driver at all.</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>Version this package will install.</summary>
    public string? NewVersion { get; set; }

    public DateTime? ReleaseDate { get; set; }

    /// <summary>Download size in bytes; 0 when unknown (local packages).</summary>
    public long SizeBytes { get; set; }

    public UpdateSeverity Severity { get; set; } = UpdateSeverity.Optional;

    /// <summary>The provider already knows a reboot will be required.</summary>
    public bool RebootRequired { get; set; }

    /// <summary>Hardware ids this package declares support for.</summary>
    public List<string> TargetHardwareIds { get; set; } = new();

    /// <summary>Device instance id this candidate was matched to, when a match was found.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Absolute .inf path for <see cref="ProviderKind.LocalRepository"/> packages.</summary>
    public string? LocalInfPath { get; set; }

    /// <summary>Vendor support or KB article link.</summary>
    public string? MoreInfoUrl { get; set; }

    /// <summary>Selected in the UI. Persisted so a resumed session keeps the user's choices.</summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Why this package is being offered.
    ///
    /// Matters for presentation: replacing Microsoft's in-box driver with the vendor's
    /// own is an improvement even though the version number goes *down*, and showing a
    /// bare "10.0.26100.1 -> 1.41.1420.0" arrow makes it look like a downgrade.
    /// </summary>
    public OfferReason Reason { get; set; } = OfferReason.NewerVersion;

    /// <summary>True when nothing is installed yet, i.e. a fresh install rather than an upgrade.</summary>
    [JsonIgnore]
    public bool IsMissingDriver => string.IsNullOrWhiteSpace(CurrentVersion);

    [JsonIgnore]
    public string SizeDisplay => Formatting.HumanBytes(SizeBytes);

    [JsonIgnore]
    public string VersionTransition =>
        IsMissingDriver ? (NewVersion ?? "?") : $"{CurrentVersion}  →  {NewVersion ?? "?"}";

    /// <summary>
    /// De-duplication key. Two providers can offer the same package; we keep the
    /// highest ranked one only.
    /// </summary>
    [JsonIgnore]
    public string DedupeKey =>
        $"{Formatting.NormalizeHardwareId(TargetHardwareIds.FirstOrDefault() ?? DeviceId ?? Title)}|{NewVersion}";

    public override string ToString() => Title;
}
