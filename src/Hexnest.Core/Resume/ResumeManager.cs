using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;
using Microsoft.Win32;

namespace Hexnest.Core.Resume;

/// <summary>
/// Makes "continue where it left off after a restart" actually work.
///
/// Two mechanisms, in order of preference:
///  1. A scheduled task with the highest privileges, triggered at logon. This survives
///     several restarts, which matters because a batch of drivers can need more than one.
///  2. An HKLM RunOnce value, as a fallback when Task Scheduler is unavailable.
///
/// Both are removed as soon as the queue finishes, so Hexnest never leaves anything
/// behind on the machine.
/// </summary>
public static class ResumeManager
{
    private const string TaskName = "Hexnest\\ResumeSession";
    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceValue = "HexnestResume";
    private const string ResumeArgument = "--resume";

    /// <summary>Registers the resume hook. Returns false when neither mechanism worked.</summary>
    public static async Task<bool> EnableAsync(CancellationToken cancellationToken = default)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            Log.Error("Cannot register resume: the executable path is unknown.");
            return false;
        }

        if (await TryCreateScheduledTaskAsync(executable!, cancellationToken).ConfigureAwait(false))
            return true;

        Log.Warn("Falling back to RunOnce for resume.");
        return TryCreateRunOnce(executable!);
    }

    /// <summary>Removes both hooks. Safe to call when nothing was registered.</summary>
    public static async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await TryDeleteScheduledTaskAsync(cancellationToken).ConfigureAwait(false);
        TryDeleteRunOnce();
    }

    /// <summary>True when a resume hook is currently registered.</summary>
    public static async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                new[] { "/Query", "/TN", TaskName },
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            if (result.Success) return true;
        }
        catch
        {
            // Fall through to the registry check.
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceKey);
            return key?.GetValue(RunOnceValue) is not null;
        }
        catch
        {
            return false;
        }
    }

    // =====================================================================================
    // Scheduled task
    // =====================================================================================

    private static async Task<bool> TryCreateScheduledTaskAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        try
        {
            // schtasks wants the whole command as one quoted string.
            var command = $"\"{executable}\" {ResumeArgument}";

            var result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                new[]
                {
                    "/Create",
                    "/TN", TaskName,
                    "/TR", command,
                    "/SC", "ONLOGON",
                    "/RL", "HIGHEST",
                    "/F"
                },
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                Log.Info("Resume scheduled task registered.");
                return true;
            }

            Log.Warn($"schtasks /Create failed ({result.ExitCode}): {result.CombinedOutput.Trim()}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not create the resume task: {ex.Message}");
            return false;
        }
    }

    private static async Task TryDeleteScheduledTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                new[] { "/Delete", "/TN", TaskName, "/F" },
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            if (result.Success) Log.Info("Resume scheduled task removed.");
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not remove the resume task: {ex.Message}");
        }
    }

    // =====================================================================================
    // RunOnce fallback
    // =====================================================================================

    private static bool TryCreateRunOnce(string executable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RunOnceKey, writable: true);
            if (key is null) return false;

            key.SetValue(RunOnceValue, $"\"{executable}\" {ResumeArgument}", RegistryValueKind.String);
            Log.Info("Resume RunOnce entry registered.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not register RunOnce: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteRunOnce()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceKey, writable: true);
            if (key?.GetValue(RunOnceValue) is null) return;

            key.DeleteValue(RunOnceValue, throwOnMissingValue: false);
            Log.Info("Resume RunOnce entry removed.");
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not remove RunOnce: {ex.Message}");
        }
    }
}
