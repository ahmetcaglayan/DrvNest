using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Monitoring;

/// <summary>
/// Reads whatever temperature sensors this machine is willing to publish.
///
/// The honest summary: most desktops publish nothing. Windows has no general purpose
/// temperature API. What exists is the ACPI thermal zone the firmware declares for the
/// operating system's own fan control, surfaced as
/// <c>root\WMI:MSAcpi_ThermalZoneTemperature</c>, and most desktop motherboards declare
/// either a single vague zone or none at all. Laptops usually do better.
///
/// Real per-core and per-GPU temperatures come from a vendor's Super I/O chip over an
/// SMBus, which needs a signed kernel driver - the reason tools like HWiNFO and Open
/// Hardware Monitor install one. DrvNest will not install a kernel driver to draw a
/// number on a page, so when the machine has no thermal zone the panel says the sensor
/// is unavailable rather than inventing a plausible 45 C.
/// </summary>
internal static class ThermalReader
{
    private const string Namespace = @"root\WMI";
    private const string Wql = "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature";

    /// <summary>Tenths of a Kelvin, which is what ACPI reports.</summary>
    private const double KelvinOffsetDeciKelvin = 2731.5;

    /// <summary>Anything outside this range is a broken provider, not a temperature.</summary>
    private const double MinPlausibleCelsius = -20;
    private const double MaxPlausibleCelsius = 130;

    private static bool _loggedUnavailable;

    /// <summary>Every plausible sensor reading, or an empty list when there are none.</summary>
    public static IReadOnlyList<ThermalReading> Read()
    {
        var readings = new List<ThermalReading>(4);

        bool ran = WmiLite.Query(Namespace, Wql, row =>
        {
            var raw = WmiLite.Number(row, "CurrentTemperature");
            if (raw is null or <= 0) return;

            double celsius = raw.Value / 10.0 - KelvinOffsetDeciKelvin / 10.0;

            if (celsius is < MinPlausibleCelsius or > MaxPlausibleCelsius) return;

            readings.Add(new ThermalReading(
                Friendly(WmiLite.String(row, "InstanceName")),
                Math.Round(celsius, 1),
                "ACPI thermal zone"));
        });

        if (!ran && !_loggedUnavailable)
        {
            _loggedUnavailable = true;
            Log.Debug("No ACPI thermal zone is available on this machine.");
        }

        return readings;
    }

    /// <summary>
    /// Turns "ACPI\ThermalZone\THM0_0" into "THM0". The instance name is a device path
    /// and showing it raw would be noise.
    /// </summary>
    private static string Friendly(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return "CPU";

        var last = instanceName.Split('\\', StringSplitOptions.RemoveEmptyEntries)[^1];

        // The enumerator suffix "_0" is an instance counter, not part of the name.
        int underscore = last.LastIndexOf('_');
        if (underscore > 0 && underscore == last.Length - 2 && char.IsDigit(last[^1]))
            last = last[..underscore];

        return last.Length == 0 ? "CPU" : last;
    }
}
