namespace Hexnest.Core.Models;

/// <summary>User preferences, persisted to <c>%ProgramData%\Hexnest\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>How many drivers may download and install at the same time (1-8).</summary>
    public int MaxParallelJobs { get; set; } = 3;

    /// <summary>Create a System Restore point before the first install of a session.</summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>Export the current driver package before replacing it, enabling rollback.</summary>
    public bool BackupBeforeUpdate { get; set; } = true;

    /// <summary>Restart automatically when the queue finishes and a restart is required.</summary>
    public bool AutoReboot { get; set; }

    /// <summary>Grace period before an automatic restart.</summary>
    public int AutoRebootDelaySeconds { get; set; } = 60;

    /// <summary>Register a scheduled task so the queue continues after a restart.</summary>
    public bool ResumeAfterReboot { get; set; } = true;

    /// <summary>Include optional (non-critical) driver offers from Microsoft Update.</summary>
    public bool IncludeOptionalDrivers { get; set; } = true;

    /// <summary>Highlight devices still running an in-box Microsoft driver.</summary>
    public bool PreferVendorOverGeneric { get; set; } = true;

    /// <summary>Automatic retries for a failed job before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Offline driver folders searched in Rescue Mode (USB stick, network share).</summary>
    public List<string> LocalRepositoryPaths { get; set; } = new();

    /// <summary>Where backups are written. Null uses the default under %ProgramData%.</summary>
    public string? BackupRoot { get; set; }

    /// <summary>Run a scan as soon as the app opens.</summary>
    public bool ScanOnStartup { get; set; } = true;

    /// <summary>Never contact Windows Update; only local sources are used.</summary>
    public bool OfflineMode { get; set; }

    /// <summary>UI theme: "dark" or "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>
    /// UI language preference.
    ///
    /// "auto" (the default) follows the Windows display language and falls back to
    /// English when no language pack matches it. Anything else is a language code
    /// such as "en" or "tr"; unknown codes fall back to English at runtime, so a
    /// settings file written by a newer build never breaks an older one.
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>Hardware ids the user chose to stop being nagged about.</summary>
    public List<string> IgnoredHardwareIds { get; set; } = new();

    /// <summary>Provider ids the user hid from the update list.</summary>
    public List<string> HiddenUpdateIds { get; set; } = new();

    /// <summary>Keep this many days of history; 0 keeps everything.</summary>
    public int HistoryRetentionDays { get; set; }

    // =====================================================================================
    // Self update
    //
    // Checking is on by default and installing is not. Hexnest replaces an executable
    // that installs drivers with an elevated token, so "we quietly swapped your driver
    // installer while you were not looking" is not a default anyone should have to opt
    // out of. Telling the user a new version exists costs one HTTPS request a day.
    // =====================================================================================

    /// <summary>Ask GitHub once per <see cref="UpdateCheckIntervalHours"/> whether a newer release exists.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>
    /// Download the verified release automatically and swap it in when Hexnest closes.
    /// Off by default; the check still only ever reports, never installs, without this.
    /// </summary>
    public bool AutoInstallUpdates { get; set; }

    /// <summary>Offer beta builds as well as stable releases.</summary>
    public bool IncludePrereleaseUpdates { get; set; }

    /// <summary>Hours between automatic checks. Clamped to between 1 and 720.</summary>
    public int UpdateCheckIntervalHours { get; set; } = 24;

    /// <summary>When the last automatic check ran, so a restart does not re-check immediately.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// A downloaded and checksum-verified executable waiting to be swapped in on exit.
    /// Null once it has been applied, or when the file has gone.
    /// </summary>
    public string? PendingUpdateFile { get; set; }

    /// <summary>The version <see cref="PendingUpdateFile"/> contains, for the notice.</summary>
    public string? PendingUpdateVersion { get; set; }

    /// <summary>A version the user chose not to be reminded about again.</summary>
    public string? SkippedUpdateVersion { get; set; }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.LocalRepositoryPaths = new List<string>(LocalRepositoryPaths);
        copy.IgnoredHardwareIds = new List<string>(IgnoredHardwareIds);
        copy.HiddenUpdateIds = new List<string>(HiddenUpdateIds);
        return copy;
    }

    /// <summary>Clamps everything into a safe range after loading untrusted JSON.</summary>
    public void Normalize()
    {
        MaxParallelJobs = Math.Clamp(MaxParallelJobs, 1, 8);
        MaxRetryAttempts = Math.Clamp(MaxRetryAttempts, 0, 5);
        AutoRebootDelaySeconds = Math.Clamp(AutoRebootDelaySeconds, 5, 3600);
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 0, 3650);
        UpdateCheckIntervalHours = Math.Clamp(UpdateCheckIntervalHours, 1, 720);
        LocalRepositoryPaths ??= new List<string>();
        IgnoredHardwareIds ??= new List<string>();
        HiddenUpdateIds ??= new List<string>();
        // "system" follows the desktop's own light/dark setting. Only the Mac build
        // offers it, but it has to be accepted here too: Normalize runs on every save,
        // and rejecting the value would silently rewrite the Mac user's choice to
        // "dark" the moment they picked it.
        if (Theme is not ("dark" or "light" or "system")) Theme = "dark";

        // Language codes are validated by the UI layer, which is the only place that
        // knows which packs exist. Here we only reject obvious junk.
        if (string.IsNullOrWhiteSpace(Language) || Language.Length > 12) Language = "auto";
        else Language = Language.Trim().ToLowerInvariant();
    }
}
