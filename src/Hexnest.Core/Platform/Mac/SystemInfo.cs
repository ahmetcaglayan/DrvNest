using System.Runtime.InteropServices;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Platform;

/// <summary>
/// Read-only facts about the machine, shown on the dashboard and stored in history.
///
/// The macOS counterpart of the registry reads in <c>Platform/SystemInfo.cs</c>. Every
/// value comes from a sysctl, so the whole snapshot costs about a millisecond and needs
/// no privileges, no helper tool and no network.
/// </summary>
public static class SystemInfo
{
    private static readonly Lazy<SystemSnapshot> Cached = new(Capture, isThreadSafe: true);

    public static SystemSnapshot Current => Cached.Value;

    private static SystemSnapshot Capture()
    {
        var snapshot = new SystemSnapshot
        {
            MachineName = SafeMachineName(),
            UserName = Environment.UserName,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsDescription = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            IsElevated = Elevation.IsAdministrator
        };

        try
        {
            snapshot.ProductName = "macOS";
            snapshot.DisplayVersion = MacNative.SysctlString("kern.osproductversion");
            snapshot.BuildNumber = MacNative.SysctlString("kern.osversion");
            snapshot.EditionId = MacNative.SysctlString("kern.ostype");
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the OS version sysctls: {ex.Message}");
        }

        try
        {
            snapshot.SystemManufacturer = "Apple";
            snapshot.ModelIdentifier = MacNative.SysctlString("hw.model");
            snapshot.SystemProductName = FriendlyModel(snapshot.ModelIdentifier);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the hardware model: {ex.Message}");
        }

        try
        {
            snapshot.ProcessorName = MacNative.SysctlString("machdep.cpu.brand_string")?.Trim();
            snapshot.PhysicalCores = (int?)MacNative.SysctlInt64("hw.physicalcpu");

            // Apple silicon splits its cores into performance and efficiency clusters.
            // Reporting "8P + 2E" is the only description of an M-series chip that is
            // actually true; a single core count hides half of what the machine is.
            int? performance = (int?)MacNative.SysctlInt64("hw.perflevel0.physicalcpu");
            int? efficiency = (int?)MacNative.SysctlInt64("hw.perflevel1.physicalcpu");

            if (performance is > 0 && efficiency is > 0)
            {
                snapshot.PerformanceCores = performance;
                snapshot.EfficiencyCores = efficiency;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the processor sysctls: {ex.Message}");
        }

        try
        {
            snapshot.MemoryTotalBytes = MacNative.SysctlInt64("hw.memsize") ?? 0;
            ReadGraphics(snapshot);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the hardware summary: {ex.Message}");
        }

        return snapshot;
    }

    /// <summary>
    /// The graphics processor, from the IOKit registry.
    ///
    /// <c>system_profiler SPDisplaysDataType</c> is the documented way to ask this and
    /// takes the better part of a second, which is far too long for something the
    /// dashboard reads at startup. The same two values are in the accelerator's own
    /// registry entry and <c>ioreg</c> returns them in about twenty milliseconds.
    ///
    /// On Apple silicon the GPU is part of the SoC, so its name matches the chip and
    /// the core count is the number worth showing. On an Intel Mac the entry names the
    /// discrete or integrated part instead.
    /// </summary>
    private static void ReadGraphics(SystemSnapshot snapshot)
    {
        try
        {
            var result = ProcessRunner
                .RunAsync("/usr/sbin/ioreg", new[] { "-rc", "AGXAccelerator", "-d", "1" },
                    TimeSpan.FromSeconds(5))
                .GetAwaiter().GetResult();

            if (!result.Success) return;

            foreach (var line in result.StandardOutput.Split('\n'))
            {
                var trimmed = line.Trim();

                if (snapshot.GraphicsName is null && trimmed.StartsWith("\"model\" = ", StringComparison.Ordinal))
                    snapshot.GraphicsName = trimmed[(trimmed.IndexOf('=') + 1)..].Trim().Trim('"', '<', '>');

                if (snapshot.GraphicsCores is null &&
                    trimmed.StartsWith("\"gpu-core-count\" = ", StringComparison.Ordinal) &&
                    int.TryParse(trimmed[(trimmed.IndexOf('=') + 1)..].Trim(), out int cores))
                {
                    snapshot.GraphicsCores = cores;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Graphics information unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// <see cref="Environment.MachineName"/> throws on a Mac whose sharing name contains
    /// a character the resolver does not like, which is not a reason to fail to start.
    /// </summary>
    private static string SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return MacNative.SysctlString("kern.hostname") ?? "Mac";
        }
    }

    /// <summary>
    /// Turns a model identifier such as "MacBookPro18,1" into "MacBook Pro".
    ///
    /// Apple's marketing names are not exposed by any public API; they live in a
    /// private framework's localised property list, which is not something to depend
    /// on. The family prefix is the part that is actually stable, and the full
    /// identifier is still shown beside it, so nothing is lost.
    /// </summary>
    private static string? FriendlyModel(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        // Longest prefix first: "MacBookPro" has to win over "MacBook", and
        // "iMacPro" over "iMac".
        (string Prefix, string Name)[] families =
        {
            ("MacBookPro", "MacBook Pro"),
            ("MacBookAir", "MacBook Air"),
            ("MacBook",    "MacBook"),
            ("iMacPro",    "iMac Pro"),
            ("iMac",       "iMac"),
            ("Macmini",    "Mac mini"),
            ("MacStudio",  "Mac Studio"),
            ("MacPro",     "Mac Pro"),
        };

        foreach (var (prefix, name) in families)
        {
            if (identifier.StartsWith(prefix, StringComparison.Ordinal)) return name;
        }

        // Apple silicon desktops report a bare "Mac16,3" with no family in it. There is
        // nothing to expand, so the identifier is shown as it is.
        return identifier;
    }
}

/// <summary>Immutable-ish machine description.</summary>
public sealed class SystemSnapshot
{
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ProductName { get; set; } = "macOS";
    public string? EditionId { get; set; }
    public string? DisplayVersion { get; set; }
    public string? BuildNumber { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string? SystemManufacturer { get; set; }
    public string? SystemProductName { get; set; }

    /// <summary>The raw model identifier, e.g. "MacBookPro18,1".</summary>
    public string? ModelIdentifier { get; set; }

    public string? ProcessorName { get; set; }
    public int ProcessorCount { get; set; }
    public int? PhysicalCores { get; set; }

    /// <summary>Performance cores on an Apple silicon chip; null on Intel.</summary>
    public int? PerformanceCores { get; set; }

    /// <summary>Efficiency cores on an Apple silicon chip; null on Intel.</summary>
    public int? EfficiencyCores { get; set; }

    public bool IsElevated { get; set; }

    /// <summary>Installed physical memory.</summary>
    public long MemoryTotalBytes { get; set; }

    /// <summary>The graphics processor, or null when it could not be read.</summary>
    public string? GraphicsName { get; set; }

    /// <summary>GPU core count on Apple silicon; null on Intel.</summary>
    public int? GraphicsCores { get; set; }

    /// <summary>e.g. "Apple M1 Pro  ·  16 cores", or just the name.</summary>
    public string GraphicsDisplay => GraphicsName is null
        ? "-"
        : GraphicsCores is > 0 ? $"{GraphicsName} · {GraphicsCores}" : GraphicsName;

    /// <summary>e.g. "26.6.2 (25G83)".</summary>
    public string FullBuild =>
        string.IsNullOrWhiteSpace(BuildNumber) ? DisplayVersion ?? "?" : BuildNumber!;

    /// <summary>e.g. "macOS 26.6.2 (25G83)".</summary>
    public string OsDisplay =>
        $"{ProductName}{(string.IsNullOrWhiteSpace(DisplayVersion) ? "" : " " + DisplayVersion)} ({FullBuild})";

    /// <summary>e.g. "Apple MacBook Pro".</summary>
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

    /// <summary>e.g. "10 cores (8P + 2E)", or "10 cores" on a chip with one cluster.</summary>
    public string CoreDisplay
    {
        get
        {
            int cores = PhysicalCores ?? ProcessorCount;

            return PerformanceCores is > 0 && EfficiencyCores is > 0
                ? $"{cores} ({PerformanceCores}P + {EfficiencyCores}E)"
                : cores.ToString();
        }
    }
}
