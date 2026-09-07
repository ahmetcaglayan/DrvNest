using System.Globalization;

namespace Hexnest.Core.Models;

/// <summary>Small display helpers shared by the UI and the CLI.</summary>
public static class Formatting
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string HumanBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", value, Units[unit])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, Units[unit]);
    }

    public static string HumanSpeed(double bytesPerSecond)
        => bytesPerSecond <= 1 ? "-" : HumanBytes((long)bytesPerSecond) + "/s";

    public static string HumanDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 1) return "<1s";
        if (span.TotalMinutes < 1) return $"{span.Seconds}s";
        if (span.TotalHours < 1) return $"{span.Minutes}m {span.Seconds}s";
        return $"{(int)span.TotalHours}h {span.Minutes}m";
    }

    /// <summary>
    /// Compares two dotted driver versions ("31.0.15.3623"). Returns &gt;0 when
    /// <paramref name="a"/> is newer. Missing or unparsable parts count as 0.
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
        if (string.IsNullOrWhiteSpace(a)) return -1;
        if (string.IsNullOrWhiteSpace(b)) return 1;

        var left = SplitVersion(a!);
        var right = SplitVersion(b!);
        int len = Math.Max(left.Length, right.Length);

        for (int i = 0; i < len; i++)
        {
            long l = i < left.Length ? left[i] : 0;
            long r = i < right.Length ? right[i] : 0;
            if (l != r) return l.CompareTo(r);
        }
        return 0;
    }

    private static long[] SplitVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            // Strip anything non numeric, e.g. "10.0.19041.1 (WinBuild)".
            var span = parts[i].AsSpan();
            int end = 0;
            while (end < span.Length && char.IsDigit(span[end])) end++;
            result[i] = end > 0 && long.TryParse(span[..end], out var v) ? v : 0;
        }
        return result;
    }

    /// <summary>
    /// Normalises a hardware id for comparison: upper case, trimmed, backslashes unified.
    /// </summary>
    public static string NormalizeHardwareId(string? id)
        => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToUpperInvariant().Replace('/', '\\');
}
