using System.Runtime.InteropServices;
using Hexnest.Core.Diagnostics;
using Microsoft.Win32;

namespace Hexnest.Core.Platform;

/// <summary>Read-only facts about the machine, shown on the dashboard and stored in history.</summary>
public static class SystemInfo
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    private static readonly Lazy<SystemSnapshot> Cached = new(Capture, isThreadSafe: true);

    public static SystemSnapshot Current => Cached.Value;

    private static SystemSnapshot Capture()
    {
        var snapshot = new SystemSnapshot
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsDescription = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            IsElevated = Elevation.IsAdministrator
        };

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
            if (key is not null)
            {
                snapshot.ProductName = key.GetValue("ProductName") as string ?? snapshot.ProductName;
                snapshot.DisplayVersion = key.GetValue("DisplayVersion") as string;
                snapshot.ReleaseId = key.GetValue("ReleaseId") as string;
                snapshot.BuildNumber = key.GetValue("CurrentBuildNumber") as string;
                snapshot.UpdateBuildRevision = key.GetValue("UBR") is int ubr ? ubr : null;
                snapshot.EditionId = key.GetValue("EditionID") as string;

                // Windows 11 still reports "Windows 10" in ProductName; build 22000+ is 11.
                if (int.TryParse(snapshot.BuildNumber, out int build) && build >= 22000 &&
                    snapshot.ProductName?.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) == true)
                {
                    snapshot.ProductName = snapshot.ProductName.Replace(
                        "Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read OS version registry: {ex.Message}");
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BiosKey);
            if (key is not null)
            {
                snapshot.SystemManufacturer = key.GetValue("SystemManufacturer") as string;
                snapshot.SystemProductName = key.GetValue("SystemProductName") as string;
                snapshot.BiosVersion = key.GetValue("BIOSVersion") is string[] bios
                    ? string.Join(' ', bios)
                    : key.GetValue("BIOSVersion") as string;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read BIOS registry: {ex.Message}");
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CpuKey);
            snapshot.ProcessorName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read CPU registry: {ex.Message}");
        }

        try
        {
            ReadMemory(snapshot);
            ReadGraphics(snapshot);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the hardware summary: {ex.Message}");
        }

        return snapshot;
    }

    /// <summary>Installed physical memory, for the dashboard's hardware summary.</summary>
    private static void ReadMemory(SystemSnapshot snapshot)
    {
        var status = new Monitoring.MonitorInterop.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<Monitoring.MonitorInterop.MemoryStatusEx>()
        };

        if (Monitoring.MonitorInterop.GlobalMemoryStatusEx(ref status))
            snapshot.MemoryTotalBytes = (long)status.TotalPhys;
    }

    /// <summary>
    /// The display adapter, from Win32_VideoController.
    ///
    /// Read once at startup and never again, which is what makes WMI acceptable here:
    /// it is the flakiest interface in Windows and far too slow to sample, but this is
    /// one query whose answer does not change while the application is open. A machine
    /// with more than one adapter reports the one with the most memory, which on a
    /// laptop is the discrete card rather than the integrated one.
    ///
    /// AdapterRAM is a 32-bit field and therefore wraps at 4 GB - it is used only to
    /// choose between adapters, never shown.
    /// </summary>
    private static void ReadGraphics(SystemSnapshot snapshot)
    {
        string? best = null;
        double bestMemory = -1;
        bool haveAny = false;

        bool ran = Monitoring.WmiLite.Query(
            @"root\cimv2",
            "SELECT Name, AdapterRAM FROM Win32_VideoController",
            row =>
            {
                var name = Monitoring.WmiLite.String(row, "Name");
                if (string.IsNullOrWhiteSpace(name)) return;

                // AdapterRAM is a CIM uint32, and the WMI scripting API hands anything
                // above 2^31-1 back through IDispatch as a *negative* VT_I4. Every card
                // with 2 GB or more therefore arrives negative, so comparing the raw
                // value would lose to the -1 sentinel and record no adapter at all.
                // Folding it back into the unsigned range restores the ordering.
                double raw = Monitoring.WmiLite.Number(row, "AdapterRAM") ?? 0;
                double memory = raw < 0 ? raw + 4294967296d : raw;

                // The first adapter always wins over "nothing", however little memory
                // it claims - some drivers report zero.
                if (!haveAny || memory > bestMemory)
                {
                    haveAny = true;
                    bestMemory = memory;
                    best = name!.Trim();
                }
            });

        if (ran && best is not null) snapshot.GraphicsName = best;
    }
}

/// <summary>Immutable-ish machine description.</summary>
public sealed class SystemSnapshot
{
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ProductName { get; set; } = "Windows";
    public string? EditionId { get; set; }
    public string? DisplayVersion { get; set; }
    public string? ReleaseId { get; set; }
    public string? BuildNumber { get; set; }
    public int? UpdateBuildRevision { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string? SystemManufacturer { get; set; }
    public string? SystemProductName { get; set; }
    public string? BiosVersion { get; set; }
    public string? ProcessorName { get; set; }
    public int ProcessorCount { get; set; }
    public bool IsElevated { get; set; }

    /// <summary>Installed physical memory.</summary>
    public long MemoryTotalBytes { get; set; }

    /// <summary>The display adapter, or null when it could not be read.</summary>
    public string? GraphicsName { get; set; }

    /// <summary>e.g. "22631.4317".</summary>
    public string FullBuild =>
        UpdateBuildRevision.HasValue ? $"{BuildNumber}.{UpdateBuildRevision}" : BuildNumber ?? "?";

    /// <summary>e.g. "Windows 11 Pro 23H2 (22631.4317)".</summary>
    public string OsDisplay =>
        $"{ProductName}{(string.IsNullOrWhiteSpace(DisplayVersion) ? "" : " " + DisplayVersion)} ({FullBuild})";

    /// <summary>e.g. "ASUS ROG STRIX B550-F".</summary>
    public string MachineDisplay
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(SystemManufacturer)) parts.Add(SystemManufacturer!.Trim());
            if (!string.IsNullOrWhiteSpace(SystemProductName)) parts.Add(SystemProductName!.Trim());
            return parts.Count > 0 ? string.Join(' ', parts) : MachineName;
        }
    }
}
