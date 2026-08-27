using System.Globalization;
using System.Text.RegularExpressions;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Platform;

namespace DrvNest.Core.Backup;

/// <summary>One entry from <c>pnputil /enum-drivers</c>: a third-party driver package.</summary>
public sealed class DriverStoreEntry
{
    /// <summary>The name inside the driver store, e.g. "oem42.inf".</summary>
    public string PublishedName { get; set; } = string.Empty;

    /// <summary>The name the vendor shipped, e.g. "nvhda.inf".</summary>
    public string OriginalName { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string ClassGuid { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string SignerName { get; set; } = string.Empty;

    public bool IsSigned => !string.IsNullOrWhiteSpace(SignerName);

    public override string ToString() => $"{PublishedName} ({Provider} {Version})";
}

/// <summary>
/// Wrapper around the in-box <c>pnputil.exe</c>.
///
/// pnputil is the supported way to add, remove and export driver packages, it ships
/// with Windows and needs no redistributable, which is exactly what the offline
/// post-format workflow requires.
/// </summary>
public static partial class PnpUtil
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Lists every third-party driver package in the driver store.</summary>
    public static async Task<List<DriverStoreEntry>> EnumerateDriversAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = new List<DriverStoreEntry>();

        var result = await ProcessRunner.RunAsync(
            "pnputil.exe", new[] { "/enum-drivers" }, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            Log.Warn($"pnputil /enum-drivers failed ({result.ExitCode}).");
            return entries;
        }

        DriverStoreEntry? current = null;

        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (current is not null && current.PublishedName.Length > 0) entries.Add(current);
                current = null;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // pnputil is localised, so match on the value shape as well as the key.
            if (value.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) &&
                value.StartsWith("oem", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && current.PublishedName.Length > 0) entries.Add(current);
                current = new DriverStoreEntry { PublishedName = value };
                continue;
            }

            if (current is null) continue;

            if (value.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                current.OriginalName = value;
            else if (IsGuid(value))
            {
                // Only the class GUID, not the extension GUID: an extension package
                // prints "Extension ID" after "Class GUID", and taking any GUID would
                // overwrite the class with it.
                if (key.Contains("Class", StringComparison.OrdinalIgnoreCase))
                    current.ClassGuid = value;
            }
            else if (VersionAndDate().IsMatch(value))
                ApplyVersionAndDate(current, value);
            else if (key.Contains("Class", StringComparison.OrdinalIgnoreCase))
                current.ClassName = value;
            else if (key.Contains("Provider", StringComparison.OrdinalIgnoreCase))
                current.Provider = value;
            else if (key.Contains("Signer", StringComparison.OrdinalIgnoreCase))
                current.SignerName = value;
        }

        if (current is not null && current.PublishedName.Length > 0) entries.Add(current);

        Log.Info($"Driver store contains {entries.Count} third-party package(s).");
        return entries;
    }

    /// <summary>Installs a single .inf package and binds it to matching devices.</summary>
    public static async Task<ProcessResult> AddDriverAsync(
        string infPath,
        bool install = true,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "/add-driver", infPath };
        if (install) arguments.Add("/install");

        return await ProcessRunner.RunAsync("pnputil.exe", arguments, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Installs every .inf under a folder. The bulk post-format path.</summary>
    public static async Task<ProcessResult> AddDriversFromFolderAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        var pattern = Path.Combine(folder, "*.inf");

        return await ProcessRunner.RunAsync(
            "pnputil.exe",
            new[] { "/add-driver", pattern, "/subdirs", "/install" },
            ExportTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports driver packages out of the driver store into a folder.
    /// Pass "*" to export everything - this is the pre-format backup.
    /// </summary>
    public static async Task<ProcessResult> ExportDriverAsync(
        string publishedNameOrStar,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationFolder);

        return await ProcessRunner.RunAsync(
            "pnputil.exe",
            new[] { "/export-driver", publishedNameOrStar, destinationFolder },
            ExportTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a package from the driver store. Used by rollback.</summary>
    public static async Task<ProcessResult> DeleteDriverAsync(
        string publishedName,
        bool uninstall = true,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "/delete-driver", publishedName };
        if (uninstall) arguments.Add("/uninstall");
        if (force) arguments.Add("/force");

        return await ProcessRunner.RunAsync("pnputil.exe", arguments, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks Windows to re-enumerate the device tree so newly added packages get bound
    /// without a restart. Only available on Windows 10 1903 and newer; failure is fine.
    /// </summary>
    public static async Task<bool> ScanDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "pnputil.exe", new[] { "/scan-devices" }, TimeSpan.FromMinutes(3), cancellationToken)
                .ConfigureAwait(false);

            return result.SucceededOrNeedsReboot;
        }
        catch (Exception ex)
        {
            Log.Debug($"pnputil /scan-devices unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>Turns a pnputil exit code into something a user can act on.</summary>
    public static string DescribeExitCode(int exitCode) => exitCode switch
    {
        0 => "Completed successfully.",
        3010 => "Completed successfully. A restart is required.",
        5 => "Access denied. Run DrvNest as administrator.",
        87 => "pnputil rejected the parameters (unsupported INF or path).",
        259 => "No matching driver packages were found.",
        _ => $"pnputil exited with code {exitCode}."
    };

    private static bool IsGuid(string value)
        => value.Length is 36 or 38 && Guid.TryParse(value, out _);

    private static void ApplyVersionAndDate(DriverStoreEntry entry, string value)
    {
        var match = VersionAndDate().Match(value);
        if (!match.Success) return;

        if (DateTime.TryParseExact(match.Groups["date"].Value,
                new[] { "MM/dd/yyyy", "M/d/yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            entry.Date = parsed;
        }

        entry.Version = match.Groups["version"].Value;
    }

    /// <summary>Matches the "09/21/2023 31.0.15.3623" shape pnputil prints for a version.</summary>
    [GeneratedRegex(@"(?<date>[\d]{1,4}[./-][\d]{1,2}[./-][\d]{1,4})\s+(?<version>[\d]+(\.[\d]+){1,3})",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionAndDate();
}
