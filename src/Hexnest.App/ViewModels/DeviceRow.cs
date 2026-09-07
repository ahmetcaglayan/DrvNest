using Hexnest.App.Services;
using Hexnest.Core.Models;

namespace Hexnest.App.ViewModels;

/// <summary>What Hexnest can say about a device's driver being current.</summary>
public enum DeviceUpdateState
{
    /// <summary>Nothing was checked - no scan yet, or no source could be reached.</summary>
    Unknown = 0,

    /// <summary>No driver is installed at all.</summary>
    NoDriver = 1,

    /// <summary>A newer package is available and can be installed from here.</summary>
    UpdateAvailable = 2,

    /// <summary>Every reachable source was checked and had nothing newer.</summary>
    UpToDate = 3
}

/// <summary>
/// One row of the device list: the device joined to whatever the scan found for it.
///
/// The device scan and the driver search are separate operations, and keeping their
/// results separate in the UI was a mistake: "healthy" answers "is this device
/// working", but the question the user actually opened Hexnest to ask is "is this
/// driver current, and can I update it". This type answers both in one row.
/// </summary>
public sealed class DeviceRow : ViewModelBase
{
    public DeviceRow(DeviceItem device, UpdateCandidate? candidate, bool sourcesWereChecked)
    {
        Device = device;
        Candidate = candidate;

        State = ResolveState(device, candidate, sourcesWereChecked);

        InstallCommand = new AsyncRelayCommand(
            InstallAsync,
            () => Candidate is not null && !AppHost.Queue.IsRunning);

        // The honest next step when no source has a package: look the hardware id up.
        // It is what a technician does, and the id is the only thing that identifies the
        // part unambiguously.
        SearchOnlineCommand = new RelayCommand(SearchOnline);
    }

    public DeviceItem Device { get; }

    /// <summary>The package the scan matched to this device, if any.</summary>
    public UpdateCandidate? Candidate { get; }

    public DeviceUpdateState State { get; }

    public AsyncRelayCommand InstallCommand { get; }

    public RelayCommand SearchOnlineCommand { get; }

    // -- Passthroughs so the row can be bound directly ------------------------------------

    public string Name => Device.Name;

    /// <summary>
    /// Windows already returns the class name in the user's own language. It is empty
    /// for devices with no setup class, and the localised label is substituted here
    /// rather than in the scanner, which has no notion of language.
    /// </summary>
    public string DeviceClass => string.IsNullOrWhiteSpace(Device.DeviceClass)
        ? Loc.T("dev.otherDevices")
        : Device.DeviceClass;
    public string ClassGuid => Device.ClassGuid;
    public string? DriverProvider => Device.DriverProvider;
    public string? DriverVersion => Device.DriverVersion;
    public DateTime? DriverDate => Device.DriverDate;
    public string VersionDisplay => Device.VersionDisplay;
    public string PrimaryHardwareId => Device.PrimaryHardwareId;
    public DeviceHealth Health => Device.Health;
    public string? ProblemText => Device.ProblemText;
    public bool NeedsAttention => Device.NeedsAttention;
    public bool IsGenericMicrosoftDriver => Device.IsGenericMicrosoftDriver;

    // -- Presentation ---------------------------------------------------------------------

    public bool CanUpdate => Candidate is not null;

    /// <summary>True when the offer swaps Windows' in-box driver for the vendor's own.</summary>
    public bool ReplacesGeneric => Candidate?.Reason == OfferReason.ReplacesGeneric;

    /// <summary>
    /// "24.50.0.4  →  25.10.0.1", or the plain version when nothing is offered.
    ///
    /// For a generic-to-vendor swap the arrow is replaced by a description, because the
    /// version number legitimately goes down and the arrow would read as a downgrade.
    /// </summary>
    public string VersionSummary
    {
        get
        {
            if (ReplacesGeneric)
                return Loc.T("upd.genericToVendor", Candidate!.Manufacturer, Candidate.NewVersion ?? "?");

            return Candidate?.NewVersion is { Length: > 0 } newVersion
                ? $"{Device.VersionDisplay}  →  {newVersion}"
                : Device.VersionDisplay;
        }
    }

    public string StateText => State switch
    {
        DeviceUpdateState.NoDriver => Loc.T("state.noDriver"),
        DeviceUpdateState.UpdateAvailable when ReplacesGeneric => Loc.T("state.vendorAvailable"),
        DeviceUpdateState.UpdateAvailable => Loc.T("state.updateAvailable"),
        DeviceUpdateState.UpToDate => Loc.T("state.upToDate"),
        _ => Loc.T("state.unknown")
    };

    /// <summary>
    /// The tooltip is where the honesty lives: "up to date" only ever means "none of the
    /// sources Hexnest can reach has anything newer", not that the vendor has nothing.
    /// </summary>
    public string StateTooltip => State switch
    {
        DeviceUpdateState.NoDriver => Loc.T("state.noDriverHint"),
        DeviceUpdateState.UpdateAvailable => Candidate?.Title ?? Loc.T("state.updateAvailableHint"),
        DeviceUpdateState.UpToDate => Loc.T("state.upToDateHint"),
        _ => Loc.T("state.unknownHint")
    };

    private static DeviceUpdateState ResolveState(
        DeviceItem device,
        UpdateCandidate? candidate,
        bool sourcesWereChecked)
    {
        if (candidate is not null) return DeviceUpdateState.UpdateAvailable;
        if (device.Health == DeviceHealth.DriverMissing) return DeviceUpdateState.NoDriver;

        // Without a source that answered, silence is not evidence of being current.
        return sourcesWereChecked ? DeviceUpdateState.UpToDate : DeviceUpdateState.Unknown;
    }

    /// <summary>True when the row has nothing to install and the device still needs help.</summary>
    public bool NeedsManualHelp => Candidate is null && Device.NeedsAttention;

    private async Task InstallAsync()
    {
        if (Candidate is null) return;
        await AppHost.Queue.StartAsync(new[] { Candidate }).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a web search for the hardware id.
    ///
    /// Only the hardware id is sent - a model number like PCI\VEN_8086&amp;DEV_272B, not
    /// anything identifying the machine or its owner - and only when the user clicks.
    /// </summary>
    private void SearchOnline()
    {
        var id = Device.PrimaryHardwareId;
        if (string.IsNullOrWhiteSpace(id)) return;

        try
        {
            var query = Uri.EscapeDataString($"{id} driver");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://duckduckgo.com/?q={query}",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open a search for {id}: {ex.Message}");
        }
    }
}
