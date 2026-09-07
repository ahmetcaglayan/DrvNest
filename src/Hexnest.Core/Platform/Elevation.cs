using System.Diagnostics;
using System.Security.Principal;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Platform;

/// <summary>Administrator checks and self-elevation.</summary>
public static class Elevation
{
    private static bool? _isAdministrator;

    /// <summary>True when the current process runs with an elevated administrator token.</summary>
    public static bool IsAdministrator
    {
        get
        {
            if (_isAdministrator.HasValue) return _isAdministrator.Value;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                _isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not determine elevation state: {ex.Message}");
                _isAdministrator = false;
            }

            return _isAdministrator.Value;
        }
    }

    /// <summary>
    /// Restarts the current executable with a UAC prompt.
    /// Returns false when the user declines, so the caller can keep running read-only.
    /// </summary>
    public static bool RelaunchElevated(params string[] arguments)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return false;

            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            Process.Start(info);
            return true;
        }
        catch (Exception ex)
        {
            // 1223 == ERROR_CANCELLED, the user clicked No on the UAC prompt.
            Log.Warn($"Elevation declined or failed: {ex.Message}");
            return false;
        }
    }
}
