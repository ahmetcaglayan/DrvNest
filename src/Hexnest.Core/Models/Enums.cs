namespace Hexnest.Core.Models;

/// <summary>Where a driver package comes from. Add new values as providers are added.</summary>
public enum ProviderKind
{
    Unknown = 0,

    /// <summary>Microsoft Update / Windows Update driver catalog (requires internet).</summary>
    WindowsUpdate = 1,

    /// <summary>A folder or ZIP of .inf packages (USB stick, offline repository).</summary>
    LocalRepository = 2,

    /// <summary>A Hexnest backup archive produced by this machine or another one.</summary>
    DriverBackup = 3
}

/// <summary>Driver health of a single PnP device.</summary>
public enum DeviceHealth
{
    /// <summary>Driver installed, device reports no problem.</summary>
    Healthy = 0,

    /// <summary>No driver installed at all (CM problem 28) - the classic post-format state.</summary>
    DriverMissing = 1,

    /// <summary>Driver present but the device is in an error state (CM problem 1/10/39/43...).</summary>
    Faulty = 2,

    /// <summary>Device is disabled by the user or by policy (CM problem 22).</summary>
    Disabled = 3,

    /// <summary>Device needs a restart to finish configuration (CM problem 14).</summary>
    RestartPending = 4
}

/// <summary>How strongly an update should be recommended to the user.</summary>
public enum UpdateSeverity
{
    Optional = 0,
    Recommended = 1,
    Important = 2,
    Critical = 3
}

/// <summary>Lifecycle of one queued download + install job.</summary>
public enum JobState
{
    Queued = 0,
    Downloading = 1,
    Downloaded = 2,
    Installing = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,

    /// <summary>Installed successfully but Windows must restart to activate it.</summary>
    RebootRequired = 7,

    /// <summary>Interrupted by a restart; will be picked up again on next launch.</summary>
    PendingResume = 8
}

/// <summary>Final outcome recorded in the persistent history log.</summary>
public enum HistoryOutcome
{
    Success = 0,
    Failed = 1,
    Cancelled = 2,
    RolledBack = 3
}

/// <summary>Severity of a diagnostic log entry.</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

/// <summary>How the application process was launched.</summary>
public enum LaunchMode
{
    /// <summary>Normal interactive launch.</summary>
    Normal = 0,

    /// <summary>Relaunched by the resume task after a reboot.</summary>
    Resume = 1,

    /// <summary>Offline-only mode: no Windows Update calls at all.</summary>
    Rescue = 2
}
