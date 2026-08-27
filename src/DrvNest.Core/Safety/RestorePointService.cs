using System.Runtime.InteropServices;
using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Safety;

/// <summary>
/// Creates System Restore points before touching drivers.
///
/// Uses srclient.dll directly rather than the WMI SystemRestore class, so it adds no
/// dependency and keeps working on a machine whose WMI repository is broken. Server
/// SKUs have no System Restore at all, so every failure here is non-fatal by design.
/// </summary>
public static class RestorePointService
{
    private const int MaxDescription = 256;

    // dwEventType
    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const int END_SYSTEM_CHANGE = 101;

    // dwRestorePtType
    private const int APPLICATION_INSTALL = 0;
    private const int DEVICE_DRIVER_INSTALL = 10;

    private const int ERROR_SERVICE_DISABLED = 1058;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDescription)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(
        ref RESTOREPOINTINFO restorePointSpec,
        out STATEMGRSTATUS status);

    /// <summary>Result of a restore point attempt.</summary>
    public sealed record RestorePointResult(bool Created, long SequenceNumber, string Message)
    {
        public static RestorePointResult Skipped(string reason) => new(false, 0, reason);
    }

    /// <summary>
    /// Creates a driver-install restore point.
    /// Returns a result rather than throwing: a machine with System Restore switched
    /// off must still be able to install drivers.
    /// </summary>
    public static Task<RestorePointResult> CreateAsync(
        string description,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Create(description), cancellationToken);

    public static RestorePointResult Create(string description)
    {
        if (description.Length >= MaxDescription)
            description = description[..(MaxDescription - 1)];

        var info = new RESTOREPOINTINFO
        {
            dwEventType = BEGIN_SYSTEM_CHANGE,
            dwRestorePtType = DEVICE_DRIVER_INSTALL,
            llSequenceNumber = 0,
            szDescription = description
        };

        try
        {
            if (!SRSetRestorePointW(ref info, out var status))
            {
                int error = Marshal.GetLastWin32Error();

                var message = error switch
                {
                    ERROR_SERVICE_DISABLED =>
                        "System Restore is turned off on this machine, so no restore point was created.",
                    _ => $"Windows refused to create a restore point (error {error})."
                };

                Log.Warn(message);
                return RestorePointResult.Skipped(message);
            }

            // Close the change window straight away; the drivers install afterwards and
            // any of them can be rolled back to this point.
            var end = new RESTOREPOINTINFO
            {
                dwEventType = END_SYSTEM_CHANGE,
                dwRestorePtType = DEVICE_DRIVER_INSTALL,
                llSequenceNumber = status.llSequenceNumber,
                szDescription = description
            };

            SRSetRestorePointW(ref end, out _);

            Log.Info($"Created restore point #{status.llSequenceNumber}: {description}");
            return new RestorePointResult(true, status.llSequenceNumber,
                $"Restore point created: {description}");
        }
        catch (DllNotFoundException)
        {
            const string message =
                "System Restore is not available on this Windows edition; continuing without a restore point.";
            Log.Warn(message);
            return RestorePointResult.Skipped(message);
        }
        catch (EntryPointNotFoundException)
        {
            const string message = "System Restore API is unavailable; continuing without a restore point.";
            Log.Warn(message);
            return RestorePointResult.Skipped(message);
        }
        catch (Exception ex)
        {
            Log.Warn($"Restore point failed: {ex.Message}");
            return RestorePointResult.Skipped(ex.Message);
        }
    }

    /// <summary>Opens the Windows System Restore wizard so the user can roll back.</summary>
    public static void OpenSystemRestoreUi()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "rstrui.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open System Restore: {ex.Message}");
        }
    }
}
