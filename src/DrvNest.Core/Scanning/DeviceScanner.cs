using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Platform;
using Microsoft.Win32;
using static DrvNest.Core.Platform.NativeMethods;

namespace DrvNest.Core.Scanning;

/// <summary>
/// Enumerates every present PnP device and the driver bound to it.
///
/// Uses SetupAPI + CfgMgr32 only, which means it works on a machine that has just
/// been formatted, has no network, no .NET installed system-wide and possibly a
/// broken WMI repository. This is the foundation of the post-format workflow.
/// </summary>
public sealed class DeviceScanner
{
    private const string ClassKeyRoot = @"SYSTEM\CurrentControlSet\Control\Class";

    private static readonly string[] GenericProviders = { "Microsoft", "Microsoft Corporation" };

    private readonly Dictionary<Guid, string> _classDescriptionCache = new();

    /// <summary>Enumerates all devices that are physically present in the machine.</summary>
    public List<DeviceItem> Scan(CancellationToken cancellationToken = default)
    {
        var devices = new List<DeviceItem>(512);

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

        if (deviceInfoSet == INVALID_HANDLE_VALUE)
        {
            int error = Marshal.GetLastWin32Error();
            Log.Error($"SetupDiGetClassDevs failed with Win32 error {error}.");
            return devices;
        }

        try
        {
            var data = new SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
            };

            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var item = ReadDevice(deviceInfoSet, ref data);
                    if (item is not null) devices.Add(item);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad device must never abort the whole scan.
                    Log.Warn($"Skipping device at index {index}: {ex.Message}");
                }

                data.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        Log.Info($"Device scan finished: {devices.Count} devices, " +
                 $"{devices.Count(d => d.Health == DeviceHealth.DriverMissing)} missing drivers.");

        return devices
            .OrderBy(d => d.DeviceClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private DeviceItem? ReadDevice(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        string deviceId = GetDeviceInstanceId(data.DevInst);
        if (string.IsNullOrWhiteSpace(deviceId)) return null;

        var item = new DeviceItem
        {
            DeviceId = deviceId,
            Enumerator = deviceId.Split('\\').FirstOrDefault() ?? string.Empty,
            ClassGuid = FormatGuid(GetStringProperty(set, ref data, SPDRP_CLASSGUID), data.ClassGuid),
            ClassName = GetStringProperty(set, ref data, SPDRP_CLASS) ?? string.Empty,
            Manufacturer = GetStringProperty(set, ref data, SPDRP_MFG) ?? string.Empty,
            HardwareIds = GetMultiStringProperty(set, ref data, SPDRP_HARDWAREID),
            CompatibleIds = GetMultiStringProperty(set, ref data, SPDRP_COMPATIBLEIDS),
            DriverKey = GetStringProperty(set, ref data, SPDRP_DRIVER)
        };

        item.Name =
            GetStringProperty(set, ref data, SPDRP_FRIENDLYNAME)
            ?? GetStringProperty(set, ref data, SPDRP_DEVICEDESC)
            ?? deviceId;

        item.DeviceClass = ResolveClassDescription(data.ClassGuid, item.ClassName);

        ParseVendorAndProduct(item);
        ReadDriverRegistry(item);
        ReadStatus(data.DevInst, item);

        item.IsGenericMicrosoftDriver =
            !string.IsNullOrWhiteSpace(item.DriverProvider) &&
            GenericProviders.Contains(item.DriverProvider, StringComparer.OrdinalIgnoreCase);

        item.IsThirdPartyDriver =
            item.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true;

        return item;
    }

    // -----------------------------------------------------------------------------
    // Device instance id
    // -----------------------------------------------------------------------------

    private static string GetDeviceInstanceId(uint devInst)
    {
        if (CM_Get_Device_ID_Size(out int length, devInst, 0) != CR_SUCCESS || length <= 0)
            length = 512;

        var buffer = new StringBuilder(length + 1);
        return CM_Get_Device_ID(devInst, buffer, buffer.Capacity, 0) == CR_SUCCESS
            ? buffer.ToString()
            : string.Empty;
    }

    // -----------------------------------------------------------------------------
    // Device registry properties
    // -----------------------------------------------------------------------------

    private static byte[]? GetRawProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        // First call sizes the buffer; the property simply not existing is normal.
        SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, null, 0, out uint required);
        if (required == 0) return null;

        var buffer = new byte[required];
        return SetupDiGetDeviceRegistryProperty(
            set, ref data, property, out _, buffer, required, out _)
            ? buffer
            : null;
    }

    private static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        var raw = GetRawProperty(set, ref data, property);
        if (raw is null) return null;

        string value = Encoding.Unicode.GetString(raw).TrimEnd('\0').Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> GetMultiStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        var raw = GetRawProperty(set, ref data, property);
        if (raw is null) return new List<string>();

        return Encoding.Unicode.GetString(raw)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // -----------------------------------------------------------------------------
    // Class description
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The class name Windows itself uses, which is already in the user's language.
    /// Returns an empty string rather than an English placeholder when there is no class,
    /// so the UI can substitute a localised label.
    /// </summary>
    private string ResolveClassDescription(Guid classGuid, string className)
    {
        if (classGuid == Guid.Empty) return className;

        if (_classDescriptionCache.TryGetValue(classGuid, out var cached)) return cached;

        string description;
        var buffer = new StringBuilder(256);
        var guid = classGuid;

        if (SetupDiGetClassDescription(ref guid, buffer, (uint)buffer.Capacity, out _) &&
            buffer.Length > 0)
        {
            description = buffer.ToString();
        }
        else
        {
            description = className;
        }

        _classDescriptionCache[classGuid] = description;
        return description;
    }

    private static string FormatGuid(string? raw, Guid fallback)
    {
        if (!string.IsNullOrWhiteSpace(raw)) return raw!.ToLowerInvariant();
        return fallback == Guid.Empty ? string.Empty : fallback.ToString("B").ToLowerInvariant();
    }

    // -----------------------------------------------------------------------------
    // Installed driver details, read from the device's driver key
    // -----------------------------------------------------------------------------

    private static void ReadDriverRegistry(DeviceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DriverKey)) return;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ClassKeyRoot}\{item.DriverKey}");
            if (key is null) return;

            item.DriverVersion = key.GetValue("DriverVersion") as string;
            item.DriverProvider = key.GetValue("ProviderName") as string;
            item.InfName = Path.GetFileName(key.GetValue("InfPath") as string ?? string.Empty);
            if (string.IsNullOrWhiteSpace(item.InfName)) item.InfName = null;

            if (key.GetValue("DriverDate") is string dateText &&
                DateTime.TryParse(dateText, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                item.DriverDate = parsed;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read driver key for {item.DeviceId}: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------------
    // Health
    // -----------------------------------------------------------------------------

    private static void ReadStatus(uint devInst, DeviceItem item)
    {
        if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != CR_SUCCESS)
        {
            // A node we cannot query is usually a ghost; treat it as healthy rather than alarming.
            item.Health = DeviceHealth.Healthy;
            return;
        }

        bool hasProblem = (status & DN_HAS_PROBLEM) != 0;
        item.ProblemCode = hasProblem ? (int)problem : 0;
        item.ProblemText = hasProblem ? DescribeProblem(problem) : null;

        if (!hasProblem)
        {
            // Windows is the authority here, and it says this device is fine.
            //
            // An earlier version inferred "driver missing" from the absence of a driver
            // key. That was wrong: the device tree root (HTREE\ROOT\0), software PDOs
            // (SWD\...) and vendor raw PDOs legitimately have no driver and never need
            // one. On a healthy laptop that inference invented seven broken devices out
            // of nothing - and a driver tool that cries wolf is worse than useless.
            item.Health = DeviceHealth.Healthy;
            item.ProblemText = null;
            return;
        }

        item.Health = problem switch
        {
            CM_PROB_FAILED_INSTALL or CM_PROB_NOT_CONFIGURED or CM_PROB_REINSTALL
                => DeviceHealth.DriverMissing,
            CM_PROB_DISABLED or CM_PROB_HARDWARE_DISABLED or CM_PROB_DISABLED_SERVICE
                => DeviceHealth.Disabled,
            CM_PROB_NEED_RESTART
                => DeviceHealth.RestartPending,
            _ => DeviceHealth.Faulty
        };
    }

    // -----------------------------------------------------------------------------
    // Vendor / product parsing
    // -----------------------------------------------------------------------------

    private static void ParseVendorAndProduct(DeviceItem item)
    {
        foreach (var id in item.HardwareIds.Concat(new[] { item.DeviceId }))
        {
            item.VendorId ??= ExtractToken(id, "VEN_") ?? ExtractToken(id, "VID_");
            item.ProductId ??= ExtractToken(id, "DEV_") ?? ExtractToken(id, "PID_");
            if (item.VendorId is not null && item.ProductId is not null) break;
        }
    }

    private static string? ExtractToken(string source, string prefix)
    {
        int start = source.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += prefix.Length;
        int end = start;
        while (end < source.Length && Uri.IsHexDigit(source[end])) end++;

        return end > start ? source[start..end].ToUpperInvariant() : null;
    }
}
