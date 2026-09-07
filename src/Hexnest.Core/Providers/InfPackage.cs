using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Models;

namespace Hexnest.Core.Providers;

/// <summary>A driver package discovered on disk, parsed out of its .inf file.</summary>
public sealed class InfPackage
{
    public string InfPath { get; init; } = string.Empty;
    public string? Provider { get; init; }
    public string? Class { get; init; }
    public string? ClassGuid { get; init; }
    public string? Version { get; init; }
    public DateTime? Date { get; init; }

    /// <summary>Every hardware id the INF declares support for, upper-cased.</summary>
    public List<string> HardwareIds { get; init; } = new();

    /// <summary>Total size of the package folder, used for the UI size column.</summary>
    public long SizeBytes { get; init; }

    public string FileName => Path.GetFileName(InfPath);
}

/// <summary>
/// Minimal INF reader.
///
/// It only needs four things - DriverVer, Provider, Class and the hardware ids in the
/// [Manufacturer]/model sections - so it deliberately does not try to be a general
/// INF parser. Being tolerant matters more than being complete: a package that fails
/// to parse is skipped, never fatal.
/// </summary>
public static partial class InfParser
{
    private static readonly string[] Encodings = { "utf-8", "utf-16", "windows-1252" };

    /// <summary>Recursively finds and parses every .inf under a folder.</summary>
    public static List<InfPackage> ScanFolder(string folder, CancellationToken cancellationToken = default)
    {
        var packages = new List<InfPackage>();

        if (!Directory.Exists(folder))
        {
            Log.Warn($"Driver folder not found: {folder}");
            return packages;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.inf", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 12
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not enumerate {folder}: {ex.Message}");
            return packages;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var package = Parse(file);
                if (package is not null && package.HardwareIds.Count > 0) packages.Add(package);
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipping {file}: {ex.Message}");
            }
        }

        Log.Info($"Found {packages.Count} usable driver package(s) in {folder}.");
        return packages;
    }

    /// <summary>Parses a single .inf file. Returns null when it is not a driver INF.</summary>
    public static InfPackage? Parse(string infPath)
    {
        var text = ReadText(infPath);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Split('\n');

        string? provider = null, driverClass = null, classGuid = null, version = null;
        DateTime? date = null;

        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hardwareIds = new List<string>();
        var section = string.Empty;

        // Pass 1: headers, [Strings] and every hardware id we can see.
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine);
            if (line.Length == 0) continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0) continue;

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();

            if (section.Equals("Strings", StringComparison.OrdinalIgnoreCase))
            {
                strings[key] = Unquote(value);
                continue;
            }

            if (section.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Provider", StringComparison.OrdinalIgnoreCase)) provider = value;
                else if (key.Equals("Class", StringComparison.OrdinalIgnoreCase)) driverClass = value;
                else if (key.Equals("ClassGuid", StringComparison.OrdinalIgnoreCase)) classGuid = value;
                else if (key.Equals("DriverVer", StringComparison.OrdinalIgnoreCase))
                    (date, version) = ParseDriverVer(value);
                continue;
            }

            // Model lines look like:  %Desc% = Install_Section, PCI\VEN_8086&DEV_15F3
            foreach (var token in value.Split(',', StringSplitOptions.TrimEntries))
            {
                if (LooksLikeHardwareId(token)) hardwareIds.Add(token.ToUpperInvariant());
            }
        }

        if (hardwareIds.Count == 0) return null;

        return new InfPackage
        {
            InfPath = infPath,
            Provider = Resolve(provider, strings),
            Class = Resolve(driverClass, strings),
            ClassGuid = classGuid?.Trim(),
            Version = version,
            Date = date,
            HardwareIds = hardwareIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SizeBytes = MeasurePackage(infPath)
        };
    }

    /// <summary>
    /// DriverVer looks like <c>DriverVer=09/21/2023,31.0.15.3623</c>.
    /// The date is always MM/DD/YYYY regardless of locale.
    /// </summary>
    private static (DateTime? Date, string? Version) ParseDriverVer(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        DateTime? date = null;

        if (parts.Length > 0 && DateTime.TryParseExact(
                parts[0], new[] { "MM/dd/yyyy", "M/d/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }

        var version = parts.Length > 1 ? parts[1].Trim() : null;
        return (date, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    /// <summary>Hardware ids always carry a bus prefix and a backslash.</summary>
    private static bool LooksLikeHardwareId(string token)
    {
        if (token.Length < 6 || !token.Contains('\\')) return false;
        if (token.Contains('%') || token.Contains('"')) return false;

        var prefix = token.Split('\\')[0];
        return prefix.Length is >= 2 and <= 16 &&
               prefix.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>Expands a %Token% reference using the [Strings] section.</summary>
    private static string? Resolve(string? value, Dictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        value = Unquote(value.Trim());
        if (value.Length > 2 && value[0] == '%' && value[^1] == '%')
        {
            var key = value[1..^1];
            if (strings.TryGetValue(key, out var expanded)) return expanded;
        }

        return value;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    private static string StripComment(string line)
    {
        int comment = line.IndexOf(';');
        if (comment >= 0) line = line[..comment];
        return line.Trim().Trim('\r');
    }

    /// <summary>INF files come in ANSI, UTF-8 and UTF-16; try each until one is sane.</summary>
    private static string? ReadText(string path)
    {
        foreach (var name in Encodings)
        {
            try
            {
                var text = File.ReadAllText(path, Encoding.GetEncoding(name));

                // A wrong encoding shows up as a wall of NUL characters.
                if (!text.Contains('\0')) return text;
            }
            catch
            {
                // Try the next encoding.
            }
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Approximates package size by summing the files next to the INF.</summary>
    private static long MeasurePackage(string infPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(infPath);
            if (string.IsNullOrEmpty(directory)) return 0;

            return new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
