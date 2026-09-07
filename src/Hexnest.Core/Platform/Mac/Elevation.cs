using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Platform;

/// <summary>
/// Privilege checks on macOS.
///
/// The Windows build self-elevates through UAC because installing a driver genuinely
/// requires it. The Mac build deliberately does the opposite: it never asks to become
/// root, and it is not designed to be run with <c>sudo</c>.
///
/// That is a decision, not a limitation. Everything Hexnest for Mac does - reading the
/// monitors, emptying the user's own caches, turning the user's own login items on and
/// off - lives inside the account that launched it. A GUI that runs as root can delete
/// anything on the machine by accident, and there is nothing on this page that would be
/// worth that. Items outside the user's own domain are listed and clearly marked, and
/// they are not touched.
/// </summary>
public static class Elevation
{
    private static bool? _isRoot;
    private static bool? _isAdminUser;

    /// <summary>True only when the process is actually running as root.</summary>
    public static bool IsAdministrator
    {
        get
        {
            if (_isRoot.HasValue) return _isRoot.Value;

            try
            {
                _isRoot = MacNative.IsRoot();
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not determine the effective user: {ex.Message}");
                _isRoot = false;
            }

            return _isRoot.Value;
        }
    }

    /// <summary>
    /// True when the logged in account is a member of the <c>admin</c> group, which is
    /// what macOS calls an administrator. Says nothing about the current process, which
    /// still runs unprivileged; it only decides whether asking for a password would
    /// have any chance of succeeding.
    /// </summary>
    public static bool IsAdministratorAccount
    {
        get
        {
            if (_isAdminUser.HasValue) return _isAdminUser.Value;

            _isAdminUser = false;

            try
            {
                // `id -Gn` is the portable answer and, unlike dscl, needs no directory
                // service round trip.
                var groups = ProcessRunner
                    .RunAsync("/usr/bin/id", new[] { "-Gn" }, TimeSpan.FromSeconds(5))
                    .GetAwaiter().GetResult();

                if (groups.Success)
                {
                    _isAdminUser = groups.StandardOutput
                        .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(g => g.Equals("admin", StringComparison.Ordinal));
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not read the group list: {ex.Message}");
            }

            return _isAdminUser.Value;
        }
    }

    /// <summary>
    /// Always false on macOS: Hexnest does not relaunch itself as root.
    ///
    /// Kept so that code shared with the Windows build compiles unchanged and gets the
    /// same "carry on read-only" answer it would get from a user who declined a UAC
    /// prompt.
    /// </summary>
    public static bool RelaunchElevated(params string[] arguments)
    {
        Log.Info("Elevation was requested and refused: the Mac build never runs as root.");
        return false;
    }
}
