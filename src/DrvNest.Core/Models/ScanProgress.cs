namespace DrvNest.Core.Models;

/// <summary>Which stage of a scan is running.</summary>
public enum ScanPhase
{
    Starting = 0,

    /// <summary>Enumerating PnP devices with SetupAPI. Fast, a second or two.</summary>
    Devices = 1,

    /// <summary>Asking the providers what they have. Windows Update is the slow one.</summary>
    Sources = 2,

    /// <summary>Merging, de-duplicating and sorting the results.</summary>
    Finalizing = 3,

    Done = 4
}

/// <summary>
/// Structured scan progress.
///
/// Deliberately not a pre-formatted sentence: the UI localises this, and a string
/// built in the Core layer would arrive in the wrong language. It also carries the
/// numbers the status bar needs to show a bar that actually moves.
/// </summary>
public sealed record ScanProgress(
    ScanPhase Phase,
    double Percent,
    int Completed,
    int Total,
    double ElapsedSeconds,
    string? Detail = null)
{
    /// <summary>
    /// True while the running step cannot report a percentage of its own.
    ///
    /// The Windows Update query is a single opaque COM call that typically takes
    /// 20-60 seconds and reports nothing until it returns, so the honest thing is to
    /// say "still working, N seconds so far" rather than invent a percentage.
    /// </summary>
    public bool IsIndeterminate => Phase == ScanPhase.Sources && Completed < Total;

    public static ScanProgress Start() => new(ScanPhase.Starting, 0, 0, 0, 0);
}
