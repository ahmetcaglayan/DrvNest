using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;
using Microsoft.Win32;

namespace Hexnest.Core.Resume;

/// <summary>
/// Restart handling.
///
/// Uses shutdown.exe rather than ExitWindowsEx so the user gets the standard Windows
/// countdown they can cancel, and so Hexnest never needs SeShutdownPrivilege plumbing.
/// </summary>
public static class RebootService
{
    private const string PendingFileRenameKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string CbsRebootPendingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WindowsUpdateRebootKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    /// <summary>
    /// Asks Windows whether something already requires a restart.
    /// Checked before a driver install because Windows Update refuses to install while
    /// a restart is outstanding.
    /// </summary>
    public static bool IsRebootPending()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(CbsRebootPendingKey))
                if (key is not null) return true;

            using (var key = Registry.LocalMachine.OpenSubKey(WindowsUpdateRebootKey))
                if (key is not null) return true;

            using (var key = Registry.LocalMachine.OpenSubKey(PendingFileRenameKey))
            {
                if (key?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 })
                    return true;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not determine reboot state: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Schedules a restart with a visible countdown.
    /// The user can cancel it from Hexnest or with <c>shutdown /a</c>.
    /// </summary>
    public static async Task<bool> RestartAsync(
        int delaySeconds = 60,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        delaySeconds = Math.Clamp(delaySeconds, 0, 315360000);

        var arguments = new List<string>
        {
            "/r",
            "/t", delaySeconds.ToString(),
            "/c", comment ?? "Hexnest is restarting Windows to finish installing drivers."
        };

        try
        {
            var result = await ProcessRunner.RunAsync(
                "shutdown.exe", arguments, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                Log.Info($"Restart scheduled in {delaySeconds} second(s).");
                return true;
            }

            Log.Error($"shutdown.exe failed ({result.ExitCode}): {result.CombinedOutput.Trim()}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not schedule a restart: {ex.Message}");
            return false;
        }
    }

    /// <summary>Cancels a restart that has not fired yet.</summary>
    public static async Task<bool> CancelRestartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "shutdown.exe", new[] { "/a" }, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);

            if (result.Success) Log.Info("Pending restart cancelled.");
            return result.Success;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not cancel the restart: {ex.Message}");
            return false;
        }
    }
}
