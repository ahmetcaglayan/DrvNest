namespace DrvNest.Core.Startup;

/// <summary>Where an autostart entry lives. Also decides how it is turned off.</summary>
public enum StartupLocation
{
    /// <summary>HKCU\...\CurrentVersion\Run - starts for this user only.</summary>
    CurrentUserRun = 0,

    /// <summary>HKLM\...\CurrentVersion\Run - starts for everyone.</summary>
    LocalMachineRun = 1,

    /// <summary>HKLM\...\Wow6432Node\...\Run - the 32-bit registry view.</summary>
    LocalMachineRun32 = 2,

    /// <summary>The user's own Startup folder.</summary>
    CurrentUserStartupFolder = 3,

    /// <summary>The all-users Startup folder.</summary>
    CommonStartupFolder = 4,

    /// <summary>HKCU or HKLM RunOnce - runs once and deletes itself.</summary>
    RunOnce = 5
}

/// <summary>How confident DrvNest is that turning an entry off is harmless.</summary>
public enum StartupCaution
{
    /// <summary>An ordinary application. Turning it off only means it will not start itself.</summary>
    Normal = 0,

    /// <summary>
    /// Ships with the machine's hardware or with Windows: a touchpad utility, an audio
    /// panel, a graphics control app. Usually safe, occasionally the thing that makes a
    /// function key work.
    /// </summary>
    SystemOrVendor = 1,

    /// <summary>
    /// Security software, or something whose absence could leave the machine unprotected.
    /// DrvNest will still turn it off if asked, but it says so first.
    /// </summary>
    Security = 2
}

/// <summary>
/// One program that starts with Windows.
///
/// The <see cref="IsEnabled"/> flag is not a property of the entry itself: Windows keeps
/// it in a separate "StartupApproved" key, which is exactly how Task Manager's Startup tab
/// works. Disabling therefore never deletes the entry - the command stays where it is and
/// re-enabling puts it straight back.
/// </summary>
public sealed class StartupEntry
{
    /// <summary>Stable identity: the location plus the value or file name.</summary>
    public required string Id { get; init; }

    /// <summary>The registry value name, or the shortcut's file name without its extension.</summary>
    public required string Name { get; init; }

    /// <summary>The full command line as Windows will run it.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>The executable the command resolves to, when it could be resolved.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Company name from the executable's version resource.</summary>
    public string? Publisher { get; init; }

    /// <summary>File description from the version resource, which is usually the real name.</summary>
    public string? Description { get; init; }

    public StartupLocation Location { get; init; }

    public StartupCaution Caution { get; init; }

    public bool IsEnabled { get; init; }

    /// <summary>
    /// True when the command points at a file that is no longer there. Those are pure
    /// leftovers: Windows tries to run them at every logon and fails silently.
    /// </summary>
    public bool IsBroken { get; init; }

    /// <summary>Size of the executable, for the detail line. 0 when unknown.</summary>
    public long ExecutableSize { get; init; }

    /// <summary>True for the two RunOnce keys, which delete themselves after one run.</summary>
    public bool IsRunOnce => Location == StartupLocation.RunOnce;

    /// <summary>True when changing this entry needs an elevated token.</summary>
    public bool RequiresElevation =>
        Location is StartupLocation.LocalMachineRun
                 or StartupLocation.LocalMachineRun32
                 or StartupLocation.CommonStartupFolder;

    /// <summary>What to show as the entry's title: the file description when there is one.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Description) ? Name : Description!;

    public override string ToString() => $"{DisplayName} ({Location})";
}

/// <summary>What happened when an entry was toggled.</summary>
/// <param name="Success">False when Windows refused the change.</param>
/// <param name="Error">English diagnostic text for the log; the UI localises its own message.</param>
public readonly record struct StartupChangeResult(bool Success, string? Error)
{
    public static StartupChangeResult Ok() => new(true, null);

    public static StartupChangeResult Failed(string error) => new(false, error);
}
