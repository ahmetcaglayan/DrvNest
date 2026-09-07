namespace Hexnest.Core.Persistence;

/// <summary>
/// Every path the application writes to.
///
/// On Windows, state lives under %ProgramData% rather than %AppData% on purpose: the
/// resume scheduled task may run as SYSTEM or as a different administrator account
/// after a restart, and it must still find the same session file.
///
/// macOS has neither that problem nor a writable equivalent of %ProgramData%, and an
/// application that scattered files outside <c>~/Library</c> would be a bad citizen
/// there. The Mac build therefore keeps its state in the two folders the platform
/// expects: <c>~/Library/Application Support/Hexnest</c> and
/// <c>~/Library/Logs/Hexnest</c>, the second of which is where Console.app looks.
/// </summary>
public static class AppPaths
{
    public const string AppName = "Hexnest";

    private static string? _rootOverride;

    /// <summary>Redirects all state to another folder. Used by the portable mode and by tests.</summary>
    public static void OverrideRoot(string? path) => _rootOverride = path;

    /// <summary>%ProgramData%\Hexnest, or ~/Library/Application Support/Hexnest.</summary>
    public static string Root => _rootOverride ?? DefaultRoot;

    private static string DefaultRoot => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName)
        : Path.Combine(HomeDirectory, "Library", "Application Support", AppName);

    /// <summary>
    /// The user's home folder. <c>Environment.SpecialFolder.UserProfile</c> is empty in
    /// a few sandboxed launch contexts, so <c>$HOME</c> is the fallback.
    /// </summary>
    private static string HomeDirectory
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile)) return profile;

            return Environment.GetEnvironmentVariable("HOME") ?? "/tmp";
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string SessionFile => Path.Combine(Root, "session.json");

    public static string HistoryFile => Path.Combine(Root, "history.jsonl");

    public static string LogFile => Path.Combine(LogsDirectory, "hexnest.log");

    public static string LogsDirectory => OperatingSystem.IsWindows() || _rootOverride is not null
        ? Path.Combine(Root, "logs")
        : Path.Combine(HomeDirectory, "Library", "Logs", AppName);

    /// <summary>Default destination for exported driver backups.</summary>
    public static string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>Scratch space for downloaded packages that are not managed by Windows Update.</summary>
    public static string CacheDirectory => Path.Combine(Root, "cache");

    /// <summary>Hardware and scan reports the user exports.</summary>
    public static string ReportsDirectory => Path.Combine(Root, "reports");

    /// <summary>
    /// A "Drivers" folder next to the executable. On a USB rescue stick this lets the
    /// user drop INF packages beside Hexnest.exe and have them picked up automatically.
    /// </summary>
    public static string PortableRepositoryDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Drivers");

    /// <summary>Creates every directory the app expects. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        foreach (var directory in new[]
                 {
                     Root, LogsDirectory, BackupsDirectory, CacheDirectory, ReportsDirectory
                 })
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception)
            {
                // Falls back to the user profile below if ProgramData is not writable.
            }
        }

        if (Directory.Exists(Root)) return;

        // Last resort: a non-elevated or locked-down machine.
        _rootOverride = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ReportsDirectory);
    }

    /// <summary>Builds a timestamped backup folder name, e.g. "backup-2026-08-27_1930".</summary>
    public static string NewBackupFolder(string? label = null)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var name = string.IsNullOrWhiteSpace(label) ? $"backup-{stamp}" : $"{Sanitize(label!)}-{stamp}";
        return Path.Combine(BackupsDirectory, name);
    }

    /// <summary>Strips characters that are illegal in a Windows file name.</summary>
    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim().Trim('.');
    }
}
