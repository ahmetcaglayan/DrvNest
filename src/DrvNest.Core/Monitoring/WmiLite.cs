using System.Globalization;
using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Monitoring;

/// <summary>
/// The smallest possible WMI client.
///
/// System.Management would be the obvious choice, but it is a NuGet package, and this
/// project ships as one self-contained executable that someone downloads onto a machine
/// they formatted five minutes ago - every avoided dependency is weight removed from
/// that download. WbemScripting.SWbemLocator is a COM class that has been part of
/// Windows since Windows 2000, and it is reached through IDispatch with <c>dynamic</c>,
/// exactly the way <see cref="Providers.WindowsUpdateProvider"/> already talks to the
/// Windows Update Agent.
///
/// Every method here is best effort by design. WMI is the single flakiest interface in
/// Windows: the repository can be corrupt, the service can be disabled, a query can
/// block for thirty seconds on a broken WMI provider. Nothing DrvNest shows depends on
/// it, so a failure is logged at debug level and produces an empty result.
/// </summary>
internal static class WmiLite
{
    private const string LocatorProgId = "WbemScripting.SWbemLocator";

    /// <summary>
    /// Set once a query has failed in a way that will not fix itself, so the monitor
    /// stops paying for a call it knows cannot work.
    /// </summary>
    private static bool _unavailable;

    public static bool IsUnavailable => _unavailable;

    /// <summary>
    /// Runs a WQL query and hands each row to <paramref name="read"/> as a dynamic
    /// SWbemObject. Returns false when the query could not be run at all.
    /// </summary>
    public static bool Query(string wqlNamespace, string wql, Action<dynamic> read)
    {
        if (_unavailable) return false;

        var type = Type.GetTypeFromProgID(LocatorProgId);
        if (type is null)
        {
            _unavailable = true;
            Log.Debug("WbemScripting.SWbemLocator is not registered; WMI readings are unavailable.");
            return false;
        }

        object? locator = null;

        try
        {
            locator = Activator.CreateInstance(type);
            if (locator is null) return false;

            dynamic services = ((dynamic)locator).ConnectServer(".", wqlNamespace);

            // ReturnImmediately (16) | ForwardOnly (32) keeps the enumerator lazy, which
            // matters because a thermal query on a broken provider can take seconds.
            dynamic rows = services.ExecQuery(wql, "WQL", 48);

            foreach (var row in rows) read(row);

            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"WMI query failed ({wql}): {ex.Message}");
            return false;
        }
        finally
        {
            if (locator is not null && System.Runtime.InteropServices.Marshal.IsComObject(locator))
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(locator); }
                catch { /* Releasing a COM object can never be worth an exception here. */ }
            }
        }
    }

    /// <summary>Reads a property, returning null when it is missing or empty.</summary>
    public static string? String(dynamic row, string property)
    {
        try
        {
            object? value = ((dynamic)row).Properties_[property].Value;
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a numeric property, returning null when it is missing.</summary>
    public static double? Number(dynamic row, string property)
    {
        try
        {
            object? value = ((dynamic)row).Properties_[property].Value;
            if (value is null) return null;

            return value is IConvertible convertible
                ? convertible.ToDouble(CultureInfo.InvariantCulture)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Stops trying, after a failure that will not recover.</summary>
    public static void MarkUnavailable() => _unavailable = true;
}
