using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrvNest.Core.Diagnostics;

namespace DrvNest.App.Services;

/// <summary>
/// Device class icons, taken from Windows itself.
///
/// Every setup class registers an icon (HKLM\SYSTEM\CurrentControlSet\Control\Class\
/// {guid}\IconPath), and SetupDiLoadClassIcon hands back exactly the icon Device
/// Manager draws for that class. That is the right source for three reasons:
///
///   * It works offline. Fetching logos from vendor web sites would fail on the one
///     machine this application exists for - the freshly formatted one with no
///     network driver yet.
///   * It is complete. Every class is covered, including "Unknown device", which is
///     precisely the case a bundled sprite of well-known vendor logos would miss.
///   * It carries no trademark baggage, and users already recognise these icons.
///
/// Icons are cached per class GUID and frozen, so a list of several hundred devices
/// costs a handful of native calls in total.
/// </summary>
public static class DeviceIconProvider
{
    private static readonly ConcurrentDictionary<Guid, ImageSource?> Cache = new();

    /// <summary>The "Unknown device" setup class - the yellow question mark.</summary>
    private static readonly Guid UnknownClass = new("4d36e97e-e325-11ce-bfc1-08002be10318");

    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiLoadClassIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiLoadClassIcon(ref Guid classGuid, out IntPtr largeIcon, IntPtr miniIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>
    /// Returns the icon for a setup class GUID, or null when Windows has none.
    /// Callers fall back to the lettered avatar in that case.
    /// </summary>
    public static ImageSource? Get(string? classGuidText)
    {
        var classGuid = Parse(classGuidText);
        return Cache.GetOrAdd(classGuid, Load);
    }

    private static Guid Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return UnknownClass;
        return Guid.TryParse(text, out var parsed) ? parsed : UnknownClass;
    }

    private static ImageSource? Load(Guid classGuid)
    {
        IntPtr handle = IntPtr.Zero;

        try
        {
            if (!SetupDiLoadClassIcon(ref classGuid, out handle, IntPtr.Zero) || handle == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // Frozen so the same instance can be shared across threads and reused by
            // every row of the device list without re-marshalling the bitmap.
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not load the class icon for {classGuid}: {ex.Message}");
            return null;
        }
        finally
        {
            // The HICON is ours to release; CreateBitmapSourceFromHIcon copies the bits.
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }
    }
}
