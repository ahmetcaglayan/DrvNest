namespace DrvNest.Core.Persistence;

/// <summary>
/// Every path the application writes to.
///
/// State lives under %ProgramData% rather than %AppData% on purpose: the resume
/// scheduled task may run as SYSTEM or as a different administrator account after a
/// restart, and it must still find the same session file.
/// </summary>
public static class AppPaths
{
    public const string AppName = "DrvNest";

    private static string? _rootOverride;

    /// <summary>Redirects all state to another folder. Used by the portable mode and by tests.</summary>
    public static void OverrideRoot(string? path) => _rootOverride = path;

    /// <summary>%ProgramData%\DrvNest</summary>
    public static string Root =>
        _rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string SessionFile => Path.Combine(Root, "session.json");

    public static string HistoryFile => Path.Combine(Root, "history.jsonl");

    public static string LogFile => Path.Combine(LogsDirectory, "drvnest.log");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Default destination for exported driver backups.</summary>
    public static string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>Scratch space for downloaded packages that are not managed by Windows Update.</summary>
    public static string CacheDirectory => Path.Combine(Root, "cache");

    /// <summary>Hardware and scan reports the user exports.</summary>
    public static string ReportsDirectory => Path.Combine(Root, "reports");

    /// <summary>
    /// A "Drivers" folder next to the executable. On a USB rescue stick this lets the
    /// user drop INF packages beside DrvNest.exe and have them picked up automatically.
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
