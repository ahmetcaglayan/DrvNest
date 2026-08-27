using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>Everything one scan pass produced.</summary>
public sealed class ScanResult
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Every PnP device currently present.</summary>
    public List<DeviceItem> Devices { get; set; } = new();

    /// <summary>Installable packages: upgrades and first-time installs.</summary>
    public List<UpdateCandidate> Candidates { get; set; } = new();

    /// <summary>Non-fatal problems, e.g. "Windows Update service unreachable".</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// How many providers actually answered. Zero means nothing could be checked, and
    /// the UI must say "not checked" rather than "up to date" - claiming a driver is
    /// current when no source was reachable would be the worst kind of wrong.
    /// </summary>
    public int AvailableSourceCount { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => FinishedUtc - StartedUtc;

    [JsonIgnore]
    public int MissingDriverCount => Devices.Count(d => d.Health == DeviceHealth.DriverMissing);
    [JsonIgnore]
    public int ProblemDeviceCount => Devices.Count(d => d.NeedsAttention);
    [JsonIgnore]
    public int UpdateCount => Candidates.Count(c => !c.IsMissingDriver);
    [JsonIgnore]
    public int NewInstallCount => Candidates.Count(c => c.IsMissingDriver);

    /// <summary>
    /// True when no network adapter has a working driver. In that state Windows Update
    /// cannot be reached and the user must be pushed towards the offline recovery path.
    /// </summary>
    /// <summary>True when at least one driver source answered, so "up to date" is meaningful.</summary>
    [JsonIgnore]
    public bool HasUsableSource => AvailableSourceCount > 0;

    [JsonIgnore]
    public bool HasNoWorkingNetworkDriver =>
        Devices.Any(d => d.IsNetworkDevice) &&
        !Devices.Any(d => d.IsNetworkDevice && d.Health == DeviceHealth.Healthy);
}
