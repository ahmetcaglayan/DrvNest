using System.Diagnostics;
using System.Text;
using Hexnest.Core.Diagnostics;
using Microsoft.Win32;

namespace Hexnest.Core.Startup;

/// <summary>
/// Reads and toggles the programs that start with Windows.
///
/// Disabling works exactly the way Task Manager's Startup tab works, which matters more
/// than it sounds: Windows keeps a separate approval key,
///
///     ...\CurrentVersion\Explorer\StartupApproved\Run
///     ...\CurrentVersion\Explorer\StartupApproved\Run32
///     ...\CurrentVersion\Explorer\StartupApproved\StartupFolder
///
/// holding a small binary value per entry whose first byte carries the enabled flag. The
/// original Run value, or the shortcut in the Startup folder, is never touched.
///
/// Three things follow from that, and all three are the reason it is done this way:
///
///   * Nothing is deleted, so re-enabling is exact - the command line comes back byte for
///     byte, because it never went anywhere.
///   * Task Manager and Hexnest agree. Disable something here and Task Manager shows it as
///     disabled, and the other way round.
///   * A user who later uninstalls Hexnest is not left with a machine missing half its
///     startup programs, because the entries are all still there.
///
/// The alternative - deleting the value and remembering it in Hexnest's own settings - is
/// easier to write and quietly makes Hexnest load-bearing for someone else's software.
/// </summary>
public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32Path = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";

    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private const string ApprovedRun = ApprovedRoot + @"\Run";
    private const string ApprovedRun32 = ApprovedRoot + @"\Run32";
    private const string ApprovedFolder = ApprovedRoot + @"\StartupFolder";

    /// <summary>
    /// The approval value is 12 bytes. Bit 0 of the first byte is the disabled flag; the
    /// last eight are a FILETIME of when it was switched off. Windows writes 0x02 for
    /// enabled and 0x03 for disabled, and 0x06 / 0x07 after a round trip - so the flag is
    /// read as a bit rather than compared against a list of magic numbers.
    /// </summary>
    private const byte EnabledFlag = 0x02;

    private const byte DisabledFlag = 0x03;
    private const int ApprovalValueLength = 12;

    /// <summary>Names whose absence is worth a warning before the user switches them off.</summary>
    private static readonly string[] SecurityHints =
    {
        "defender", "antivirus", "antimalware", "avast", "avg", "avira", "bitdefender",
        "eset", "kaspersky", "malwarebytes", "mcafee", "norton", "sophos", "trendmicro",
        "webroot", "firewall", "vpn", "bitlocker"
    };

    /// <summary>Vendor and platform utilities: usually optional, occasionally load-bearing.</summary>
    private static readonly string[] VendorHints =
    {
        "microsoft", "intel", "amd", "nvidia", "realtek", "synaptics", "elan", "asus",
        "acer", "dell", "hp inc", "hewlett", "lenovo", "msi", "gigabyte", "logitech",
        "razer", "corsair", "qualcomm", "broadcom", "conexant", "cirrus", "waves audio",
        "dolby", "ftdi", "mediatek"
    };

    // =====================================================================================
    // Reading
    // =====================================================================================

    /// <summary>
    /// Every autostart entry Hexnest can see and safely toggle.
    ///
    /// Deliberately not exhaustive. Windows starts programs from at least a dozen places -
    /// logon scheduled tasks, services, shell extensions, per-app Store startup tasks, the
    /// Winlogon keys - and most of them cannot be turned off without breaking something.
    /// This covers the four locations Task Manager offers, which is what people mean by
    /// "startup programs", and says nothing about the rest rather than pretending to.
    /// </summary>
    public IReadOnlyList<StartupEntry> Scan()
    {
        var entries = new List<StartupEntry>(32);

        ReadRunKey(Registry.CurrentUser, RunPath, StartupLocation.CurrentUserRun, entries);
        ReadRunKey(Registry.LocalMachine, RunPath, StartupLocation.LocalMachineRun, entries);
        ReadRunKey(Registry.LocalMachine, Run32Path, StartupLocation.LocalMachineRun32, entries);

        ReadRunKey(Registry.CurrentUser, RunOncePath, StartupLocation.RunOnce, entries);
        ReadRunKey(Registry.LocalMachine, RunOncePath, StartupLocation.RunOnce, entries);

        ReadStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            StartupLocation.CurrentUserStartupFolder, entries);

        ReadStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            StartupLocation.CommonStartupFolder, entries);

        entries.Sort((a, b) =>
        {
            // Broken entries first: they are pure waste and the easiest decision on the page.
            int byBroken = b.IsBroken.CompareTo(a.IsBroken);
            if (byBroken != 0) return byBroken;

            int byEnabled = b.IsEnabled.CompareTo(a.IsEnabled);
            if (byEnabled != 0) return byEnabled;

            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });

        Log.Info($"Startup: {entries.Count} entries, {entries.Count(e => !e.IsEnabled)} disabled.");
        return entries;
    }

    private void ReadRunKey(RegistryKey hive, string path, StartupLocation location,
                            List<StartupEntry> into)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;

            var approved = location == StartupLocation.LocalMachineRun32
                ? ApprovedRun32
                : ApprovedRun;

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var command = key.GetValue(name) as string;
                if (string.IsNullOrWhiteSpace(command)) continue;

                into.Add(Describe(name, command!, location,
                    // RunOnce has no approval key - it runs and deletes itself either way.
                    location == StartupLocation.RunOnce || IsApproved(hive, approved, name)));
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read {path}: {ex.Message}");
        }
    }

    private void ReadStartupFolder(string folder, StartupLocation location, List<StartupEntry> into)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        var hive = location == StartupLocation.CommonStartupFolder
            ? Registry.LocalMachine
            : Registry.CurrentUser;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);

                // Windows puts one of these in every Startup folder and it is not a program.
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var target = ResolveShortcut(file) ?? file;

                into.Add(Describe(
                    Path.GetFileNameWithoutExtension(file),
                    target,
                    location,
                    IsApproved(hive, ApprovedFolder, fileName),
                    sourcePath: file));
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the startup folder {folder}: {ex.Message}");
        }
    }

    /// <summary>Builds the entry, resolving the executable and its version information.</summary>
    private StartupEntry Describe(string name, string command, StartupLocation location,
                                  bool enabled, string? sourcePath = null)
    {
        var executable = ExtractExecutable(command);

        string? publisher = null;
        string? description = null;
        long size = 0;
        bool broken = false;

        if (executable is not null)
        {
            try
            {
                var info = new FileInfo(executable);

                if (info.Exists)
                {
                    size = info.Length;

                    var version = FileVersionInfo.GetVersionInfo(executable);
                    publisher = Clean(version.CompanyName);
                    description = Clean(version.FileDescription);
                }
                else
                {
                    broken = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not inspect {executable}: {ex.Message}");
            }
        }

        return new StartupEntry
        {
            Id = $"{location}|{sourcePath ?? name}",
            Name = name,
            Command = command,
            ExecutablePath = executable,
            Publisher = publisher,
            Description = description,
            Location = location,
            Caution = Classify(name, publisher, description, executable),
            IsEnabled = enabled,
            IsBroken = broken,
            ExecutableSize = size
        };
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StartupCaution Classify(string name, string? publisher, string? description,
                                           string? executable)
    {
        var haystack = string.Join(' ', new[] { name, publisher, description, executable }
            .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        if (SecurityHints.Any(hint => haystack.Contains(hint, StringComparison.Ordinal)))
            return StartupCaution.Security;

        if (VendorHints.Any(hint => haystack.Contains(hint, StringComparison.Ordinal)))
            return StartupCaution.SystemOrVendor;

        return StartupCaution.Normal;
    }

    // =====================================================================================
    // Toggling
    // =====================================================================================

    /// <summary>
    /// Turns an entry on or off by writing the approval value Windows itself reads.
    /// The entry's own command line is never modified or removed.
    /// </summary>
    public StartupChangeResult SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.IsRunOnce)
            return StartupChangeResult.Failed("RunOnce entries cannot be disabled; they delete themselves.");

        try
        {
            var (hive, path, key) = ApprovalTarget(entry);

            using var approval = hive.CreateSubKey(path, writable: true);

            if (approval is null)
                return StartupChangeResult.Failed($"Could not open {path} for writing.");

            approval.SetValue(key, BuildApprovalValue(enabled), RegistryValueKind.Binary);

            Log.Info($"Startup: {(enabled ? "enabled" : "disabled")} '{entry.Name}' ({entry.Location}).");
            return StartupChangeResult.Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return StartupChangeResult.Failed(
                "Access denied. Changing an all-users entry needs administrator rights.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not change the startup entry '{entry.Name}': {ex.Message}");
            return StartupChangeResult.Failed(ex.Message);
        }
    }

    private static (RegistryKey Hive, string Path, string Key) ApprovalTarget(StartupEntry entry)
        => entry.Location switch
        {
            StartupLocation.CurrentUserRun =>
                (Registry.CurrentUser, ApprovedRun, entry.Name),

            StartupLocation.LocalMachineRun =>
                (Registry.LocalMachine, ApprovedRun, entry.Name),

            StartupLocation.LocalMachineRun32 =>
                (Registry.LocalMachine, ApprovedRun32, entry.Name),

            // The folder approval is keyed by the shortcut's file name, extension included.
            StartupLocation.CurrentUserStartupFolder =>
                (Registry.CurrentUser, ApprovedFolder, FileNameOf(entry)),

            StartupLocation.CommonStartupFolder =>
                (Registry.LocalMachine, ApprovedFolder, FileNameOf(entry)),

            _ => throw new InvalidOperationException($"{entry.Location} has no approval key.")
        };

    private static string FileNameOf(StartupEntry entry)
    {
        // Id is "<location>|<full path of the shortcut>" for folder entries.
        int bar = entry.Id.IndexOf('|');
        var source = bar >= 0 ? entry.Id[(bar + 1)..] : entry.Name;

        return Path.GetFileName(source);
    }

    /// <summary>
    /// 12 bytes: the flag, three bytes of padding, then the FILETIME of the change.
    /// Windows leaves the timestamp zero for an enabled entry.
    /// </summary>
    private static byte[] BuildApprovalValue(bool enabled)
    {
        var value = new byte[ApprovalValueLength];
        value[0] = enabled ? EnabledFlag : DisabledFlag;

        if (!enabled)
        {
            long stamp = DateTime.UtcNow.ToFileTimeUtc();
            BitConverter.GetBytes(stamp).CopyTo(value, 4);
        }

        return value;
    }

    /// <summary>
    /// Reads the approval flag. An entry with no approval value has never been touched,
    /// which means it is enabled - that is the default Windows itself assumes.
    /// </summary>
    private static bool IsApproved(RegistryKey hive, string path, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(path);

            if (key?.GetValue(valueName) is not byte[] { Length: > 0 } value) return true;

            // Bit 0 of the first byte is the disabled flag. Windows writes 0x02 / 0x03 and
            // 0x06 / 0x07, so testing the bit is right where a magic-number comparison is
            // wrong half the time.
            return (value[0] & 1) == 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the approval for '{valueName}': {ex.Message}");
            return true;
        }
    }

    // =====================================================================================
    // Command line parsing
    // =====================================================================================

    /// <summary>
    /// Pulls the executable out of a Run command line.
    ///
    /// These are not well-formed: some are quoted, some are not, some have arguments with
    /// spaces and no quotes at all, and plenty point at a path with a space in it without
    /// quoting. The unquoted case is resolved the way Windows does it - try progressively
    /// longer prefixes until one names a file that exists.
    /// </summary>
    internal static string? ExtractExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());

        if (text.Length == 0) return null;

        if (text[0] == '"')
        {
            int end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        // rundll32 and friends: the first token is the real executable.
        int space = text.IndexOf(' ');
        if (space < 0) return text;

        var firstToken = text[..space];

        // A path with no spaces: done.
        if (File.Exists(firstToken)) return firstToken;

        // Otherwise walk the spaces, longest match wins, exactly like CreateProcess.
        var builder = new StringBuilder(firstToken);
        int index = space;

        while (index >= 0 && index < text.Length)
        {
            int next = text.IndexOf(' ', index + 1);
            var candidate = next < 0 ? text : text[..next];

            if (File.Exists(candidate)) return candidate;
            if (File.Exists(candidate + ".exe")) return candidate + ".exe";

            if (next < 0) break;
            index = next;
        }

        // Nothing on disk matched, which normally means the entry is broken. Returning the
        // first token would report "C:\Program" as the executable for
        // `C:\Program Files\Gone\app.exe -x`, so instead cut at the first token that looks
        // like a switch - everything before it is the path the entry meant.
        var tokens = text.Split(' ');
        var path = new StringBuilder();

        foreach (var token in tokens)
        {
            if (path.Length > 0 && (token.StartsWith('-') || token.StartsWith('/'))) break;
            if (path.Length > 0) path.Append(' ');
            path.Append(token);
        }

        return path.Length > 0 ? path.ToString() : firstToken;
    }

    /// <summary>
    /// Resolves a .lnk to its target through the shell, with <c>dynamic</c> over IDispatch
    /// rather than a COM interop assembly - the same approach the Windows Update provider
    /// uses, and for the same reason: no NuGet package.
    /// Returns null for anything that is not a shortcut, or when the shell refuses.
    /// </summary>
    internal static string? ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

        object? shell = null;

        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;

            shell = Activator.CreateInstance(type);
            if (shell is null) return null;

            dynamic link = ((dynamic)shell).CreateShortcut(path);

            string target = link.TargetPath;
            string arguments = link.Arguments;

            if (string.IsNullOrWhiteSpace(target)) return null;

            return string.IsNullOrWhiteSpace(arguments) ? target : $"\"{target}\" {arguments}";
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not resolve the shortcut {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
        finally
        {
            if (shell is not null &&
                System.Runtime.InteropServices.Marshal.IsComObject(shell))
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                catch { /* Releasing a COM object is never worth an exception here. */ }
            }
        }
    }
}
