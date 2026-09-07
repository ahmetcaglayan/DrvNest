using System.Globalization;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;

namespace Hexnest.Core.Monitoring.Mac;

/// <summary>
/// What the battery is, rather than how full it is, and what is stopping the Mac
/// going to sleep.
///
/// The System Monitor already reports charge level and time remaining, which is the
/// question you ask several times an hour. This answers the two you ask a few times a
/// year and cannot currently answer without leaving the application: is this battery
/// wearing out, and why does this Mac keep waking up.
///
/// Sources, all first party and none needing any privilege:
///
///   ioreg -rc AppleSmartBattery   cycle count, capacities and the battery's own
///                                 temperature sensor. About 25 ms.
///   system_profiler SPPowerDataType
///                                 condition and the "Maximum Capacity" percentage
///                                 macOS itself shows. About 120 ms, so it is read
///                                 when the page opens and on refresh, never on a
///                                 timer.
///   pmset -g assertions           who is holding the machine awake, by process.
///                                 About 20 ms.
///
/// The same reasoning as the rest of the Mac build: IOKit would answer most of this
/// without a child process, but only through a pile of Core Foundation calls whose
/// failure modes are hard to test on a desktop Mac with no battery at all.
/// </summary>
public static class PowerMonitor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // =====================================================================================
    // Battery health
    // =====================================================================================

    /// <summary>
    /// Everything about the battery that does not change minute to minute.
    ///
    /// Returns null on a Mac with no battery, which is what the page expects.
    /// </summary>
    public static async Task<BatteryHealth?> ReadHealthAsync(CancellationToken cancellationToken = default)
    {
        var registry = await ReadIoregAsync(cancellationToken).ConfigureAwait(false);

        if (registry is null) return null;

        var profile = await ReadPowerProfileAsync(cancellationToken).ConfigureAwait(false);

        int? design = registry.GetValueOrDefault("DesignCapacity");
        int? nominal = registry.GetValueOrDefault("NominalChargeCapacity");

        // Apple's own "Maximum Capacity" is preferred over anything computed here.
        //
        // It is not a plain ratio: on this machine NominalChargeCapacity/DesignCapacity
        // is 88.8% and AppleRawMaxCapacity/DesignCapacity is 86.0%, while System
        // Settings says 90%. Whatever smoothing Apple applies, a second number that
        // disagrees with the one in System Settings would read as a bug in Hexnest, so
        // the ratio is only used when macOS does not offer a figure of its own - and it
        // is labelled differently when it is.
        int? maximum = profile.MaximumCapacityPercent;
        bool derived = false;

        if (maximum is null && design is > 0 && nominal is > 0)
        {
            maximum = (int)Math.Round(nominal.Value * 100.0 / design.Value);
            derived = true;
        }

        return new BatteryHealth
        {
            CycleCount = registry.GetValueOrDefault("CycleCount"),
            DesignCapacityMah = design,
            FullChargeCapacityMah = nominal,
            MaximumCapacityPercent = maximum,
            MaximumCapacityIsDerived = derived,
            Condition = profile.Condition,
            AdapterWatts = profile.AdapterWatts,

            // IOKit reports the battery's temperature in hundredths of a degree. This is
            // the battery, not the processor: the Mac exposes no processor temperature
            // without a signed kernel driver, and the System Monitor's temperature card
            // says so rather than showing this number in its place.
            TemperatureCelsius = registry.TryGetValue("Temperature", out int raw) && raw > 0
                ? raw / 100.0
                : null,
        };
    }

    /// <summary>
    /// The numeric properties of the AppleSmartBattery registry entry.
    ///
    /// Only the flat "key" = number lines are read. The entry also carries a large
    /// BatteryData dictionary on one line; it is deliberately not parsed, because its
    /// contents are undocumented and change between models.
    /// </summary>
    private static async Task<Dictionary<string, int>?> ReadIoregAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner
                .RunAsync("/usr/sbin/ioreg", new[] { "-rc", "AppleSmartBattery" }, Timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success || result.StandardOutput.Length == 0) return null;

            var values = ParseRegistry(result.StandardOutput);

            // A Mac with no battery still has the class but reports nothing useful.
            return values.ContainsKey("DesignCapacity") || values.ContainsKey("CycleCount")
                ? values
                : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the battery registry entry: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pulls the flat <c>"key" = number</c> lines out of an ioreg dump.
    ///
    /// Separate from the async method that calls it because it slices spans, and a ref
    /// struct cannot live across an await.
    /// </summary>
    private static Dictionary<string, int> ParseRegistry(string output)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n'))
        {
            // Lines look like:  "CycleCount" = 304
            var span = line.AsSpan().Trim();

            if (span.Length < 5 || span[0] != '"') continue;

            int closing = span[1..].IndexOf('"');
            if (closing <= 0) continue;

            var rest = span[(closing + 2)..].TrimStart();

            if (rest.Length < 2 || rest[0] != '=') continue;

            rest = rest[1..].Trim();

            if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                values[span.Slice(1, closing).ToString()] = number;
        }

        return values;
    }

    private readonly record struct PowerProfile(int? MaximumCapacityPercent, string? Condition, int? AdapterWatts);

    /// <summary>
    /// The two figures macOS shows the user itself, so Hexnest can show the same ones.
    /// </summary>
    private static async Task<PowerProfile> ReadPowerProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner
                .RunAsync("/usr/sbin/system_profiler", new[] { "SPPowerDataType" }, Timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success) return default;

            int? maximum = null;
            string? condition = null;
            int? watts = null;

            foreach (var line in result.StandardOutput.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();

                if (value.Length == 0) continue;

                switch (key)
                {
                    // Localised installs print the label in the user's language, so the
                    // value is taken from the English key only. macOS ships the key in
                    // English even when the value is not; where it is not, the field is
                    // simply left empty rather than guessed at.
                    case "Maximum Capacity":
                        maximum = ParsePercent(value);
                        break;

                    case "Condition":
                        condition = value;
                        break;

                    case "Wattage (W)":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
                            watts = w;
                        break;
                }
            }

            return new PowerProfile(maximum, condition, watts);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the power profile: {ex.Message}");
            return default;
        }
    }

    /// <summary>"90%", "%90" and "90 %" all appear depending on the display language.</summary>
    private static int? ParsePercent(string value)
    {
        Span<char> digits = stackalloc char[4];
        int length = 0;

        foreach (char c in value)
        {
            if (char.IsDigit(c) && length < digits.Length) digits[length++] = c;
            else if (length > 0) break;
        }

        return length > 0 && int.TryParse(digits[..length], NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out int percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    // =====================================================================================
    // Sleep
    // =====================================================================================

    /// <summary>
    /// Who is currently stopping the Mac, or its display, from going to sleep.
    ///
    /// This is the question Activity Monitor's "Preventing Sleep" column gestures at
    /// without answering: it shows a tick, not what the assertion is or how long it has
    /// been held. pmset names both.
    /// </summary>
    public static async Task<IReadOnlyList<PowerAssertion>> ReadAssertionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner
                .RunAsync("/usr/bin/pmset", new[] { "-g", "assertions" }, Timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success) return Array.Empty<PowerAssertion>();

            var assertions = new List<PowerAssertion>();
            bool inList = false;

            foreach (var raw in result.StandardOutput.Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("Listed by owning process", StringComparison.Ordinal))
                {
                    inList = true;
                    continue;
                }

                // The process list is the last section; anything that is not indented
                // under it ends it.
                if (!inList || !line.StartsWith("pid ", StringComparison.Ordinal)) continue;

                if (Parse(line) is { } assertion) assertions.Add(assertion);
            }

            return assertions;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the power assertions: {ex.Message}");
            return Array.Empty<PowerAssertion>();
        }
    }

    /// <summary>
    /// One line of <c>pmset -g assertions</c>, which looks like:
    ///
    ///   pid 1152(Claude): [0x00026e1e0001986a] 00:01:02 NoIdleSleepAssertion named: "Electron"
    /// </summary>
    private static PowerAssertion? Parse(string line)
    {
        int open = line.IndexOf('(');
        int close = open > 0 ? line.IndexOf(')', open) : -1;

        if (open < 4 || close < 0) return null;

        if (!int.TryParse(line[4..open], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
            return null;

        var process = line.Substring(open + 1, close - open - 1);

        // Everything after the assertion id in brackets.
        int bracket = line.IndexOf(']', close);
        if (bracket < 0) return null;

        var rest = line[(bracket + 1)..].Trim();

        var parts = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var held = ParseDuration(parts[0]);

        var kind = parts[1];

        string? name = null;
        int named = rest.IndexOf("named:", StringComparison.Ordinal);

        if (named >= 0)
        {
            var tail = rest[(named + 6)..].Trim().Trim('"').Trim();
            if (tail.Length > 0) name = tail;
        }

        return new PowerAssertion
        {
            ProcessId = pid,
            ProcessName = process,
            Kind = kind,
            Name = name,
            Held = held,

            // powerd holds an assertion whenever the display is on and WindowServer
            // holds one for every keystroke. Both are the machine working normally, and
            // burying the one application that is actually keeping the Mac awake among
            // them is how this page would become useless.
            IsSystem = process is "powerd" or "WindowServer" or "coreaudiod" or "kernel_task",
        };
    }

    /// <summary>
    /// The "H:MM:SS" pmset prints, where the hours are not clamped to a day.
    ///
    /// TimeSpan.TryParseExact with "c" refuses these outright: it reads the first field
    /// as hours 00-23, so an assertion powerd has held since the machine booted -
    /// "162:52:08" is a real line from this machine - parses as nothing at all and the
    /// column silently empties on exactly the entries worth looking at.
    /// </summary>
    private static TimeSpan? ParseDuration(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3) return null;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
            return null;

        if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59) return null;

        return new TimeSpan(hours, minutes, seconds);
    }
}
