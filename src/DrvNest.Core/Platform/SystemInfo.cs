using System.Runtime.InteropServices;
using DrvNest.Core.Diagnostics;
using Microsoft.Win32;

namespace DrvNest.Core.Platform;

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

        return snapshot;
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
