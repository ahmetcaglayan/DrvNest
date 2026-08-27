using System.Text.Json.Serialization;

namespace DrvNest.Core.Models;

/// <summary>
/// One PnP device present in the system, together with the driver currently bound to it.
/// Populated entirely from SetupAPI/CfgMgr32 plus the device's driver registry key,
/// so it works on a freshly installed Windows with no WMI dependency.
/// </summary>
public sealed class DeviceItem
{
    /// <summary>Device instance id, e.g. <c>PCI\VEN_10DE&amp;DEV_2484&amp;SUBSYS_...\4&amp;1a2b&amp;0&amp;0008</c>.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Friendly name if available, otherwise the device description.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human readable class as Windows reports it, e.g. "Display adapters" - already
    /// localised by the OS. Empty when the device has no setup class; the UI
    /// substitutes its own localised "Other devices" in that case rather than this
    /// layer inventing an English string.
    /// </summary>
    public string DeviceClass { get; set; } = string.Empty;

    /// <summary>Short class name, e.g. "Display".</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Setup class GUID; used to pick an icon in the UI.</summary>
    public string ClassGuid { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Installed driver version, or null when no driver is bound.</summary>
    public string? DriverVersion { get; set; }

    public DateTime? DriverDate { get; set; }

    /// <summary>Company that published the installed driver (Microsoft, Intel, NVIDIA...).</summary>
    public string? DriverProvider { get; set; }

    /// <summary>INF the driver came from, e.g. "oem42.inf".</summary>
    public string? InfName { get; set; }

    /// <summary>Driver key suffix under Control\Class, e.g. "{4d36e968-...}\0000".</summary>
    public string? DriverKey { get; set; }

    /// <summary>Hardware ids, most specific first. The primary matching key for driver packages.</summary>
    public List<string> HardwareIds { get; set; } = new();

    /// <summary>Compatible ids, used when no hardware id matches.</summary>
    public List<string> CompatibleIds { get; set; } = new();

    /// <summary>Configuration Manager problem code. 0 means no problem.</summary>
    public int ProblemCode { get; set; }

    /// <summary>Localised explanation of <see cref="ProblemCode"/>.</summary>
    public string? ProblemText { get; set; }

    public DeviceHealth Health { get; set; } = DeviceHealth.Healthy;

    /// <summary>True when Windows fell back to its own in-box driver; a vendor driver is usually better.</summary>
    public bool IsGenericMicrosoftDriver { get; set; }

    /// <summary>True when the bound driver came from a third party package in the driver store.</summary>
    public bool IsThirdPartyDriver { get; set; }

    public string? SignerName { get; set; }

    /// <summary>PCI/USB vendor id parsed out of the hardware id, e.g. "10DE".</summary>
    public string? VendorId { get; set; }

    /// <summary>PCI/USB device id parsed out of the hardware id, e.g. "2484".</summary>
    public string? ProductId { get; set; }

    /// <summary>Bus enumerator: PCI, USB, ACPI, HDAUDIO, SWD...</summary>
    public string Enumerator { get; set; } = string.Empty;

    [JsonIgnore]
    public string PrimaryHardwareId => HardwareIds.Count > 0 ? HardwareIds[0] : DeviceId;

    [JsonIgnore]
    public string VersionDisplay => string.IsNullOrWhiteSpace(DriverVersion) ? "-" : DriverVersion!;

    [JsonIgnore]
    public bool NeedsAttention => Health is DeviceHealth.DriverMissing or DeviceHealth.Faulty;

    /// <summary>
    /// True when this device is a network interface. Used to warn the user that
    /// an offline recovery path is required before Windows Update can be reached.
    /// </summary>
    [JsonIgnore]
    public bool IsNetworkDevice =>
        ClassName.Equals("Net", StringComparison.OrdinalIgnoreCase) ||
        ClassGuid.Equals("{4d36e972-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} [{DeviceClass}] v{VersionDisplay}";
}
