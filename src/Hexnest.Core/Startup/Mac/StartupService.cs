using System.Xml.Linq;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;

namespace Hexnest.Core.Startup;

/// <summary>
/// Lists what starts with macOS, and turns entries on and off.
///
/// launchd is the whole answer on a Mac. Everything that starts by itself - a cloud
/// drive, an updater, a VPN client, a database somebody installed with Homebrew - does
/// it by dropping a property list into one of three folders:
///
///   ~/Library/LaunchAgents    this user, at login
///   /Library/LaunchAgents     every user, at login
///   /Library/LaunchDaemons    the machine, at boot, as root
///
/// Only the first is the user's own. The other two are listed because leaving them out
/// would make the page a lie - four of the five things slowing a managed Mac's login
/// live there - but they are marked as needing an administrator and Hexnest will not
/// touch them, because the Mac build never runs as root.
///
/// Disabling is done through <c>launchctl disable</c>, which writes to launchd's own
/// override database. That is the same mechanism macOS itself uses, and it is why
/// turning something off here never deletes the property list: the file stays exactly
/// where the application put it, and turning it back on puts it straight back into
/// service.
///
/// Login Items - the list in System Settings - are deliberately not shown. Since
/// macOS 13 they live in a database that only a privileged tool may read, and the
/// AppleScript route needs an Automation permission prompt to return a list that is
/// then still incomplete. A page that showed some of them would be worse than one
/// that is clear about showing launchd only.
/// </summary>
public sealed class StartupService
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(20);

    private const string LaunchCtl = "/bin/launchctl";
    private const string PlUtil = "/usr/bin/plutil";

    /// <summary>
    /// Vendors whose agents are part of how the machine works, so turning one off is
    /// more likely to break a function key or a corporate login than to save a second.
    /// </summary>
    private static readonly string[] SystemPrefixes =
    {
        "com.apple.", "com.oracle.java", "org.mozilla.updater"
    };

    /// <summary>
    /// Security software. Hexnest will still turn these off if asked, but it says so
    /// first, and on a managed Mac it is usually not allowed to anyway.
    /// </summary>
    private static readonly string[] SecurityMarkers =
    {
        "vpn", "antivirus", "endpoint", "sophos", "mcafee", "symantec", "crowdstrike",
        "sentinelone", "carbonblack", "cylance", "eset", "kaspersky", "trendmicro",
        "forcepoint", "netskope", "zscaler", "paloaltonetworks", "cisco.secureclient",
        "cisco.anyconnect", "jamf", "falcon", "defender", "bitdefender", "webroot"
    };

    /// <summary>
    /// Everything launchd would start, in the order the folders are listed above.
    /// </summary>
    public IReadOnlyList<StartupEntry> Scan()
    {
        var entries = new List<StartupEntry>(48);

        // launchd keeps a separate override database per domain. A daemon disabled
        // with `launchctl disable system/<label>` never appears in the gui listing,
        // so asking only the gui domain reports every disabled daemon as enabled.
        var userOverrides = ReadDisabledLabels($"gui/{UserId()}");
        var systemOverrides = ReadDisabledLabels("system");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME") ?? "/";

        ReadFolder(Path.Combine(home, "Library", "LaunchAgents"),
            StartupLocation.UserLaunchAgent, userOverrides, entries);

        // A global agent still runs in each user's gui domain, so it is the gui
        // override that decides whether this user gets it. Only daemons are system.
        ReadFolder("/Library/LaunchAgents", StartupLocation.GlobalLaunchAgent, userOverrides, entries);
        ReadFolder("/Library/LaunchDaemons", StartupLocation.LaunchDaemon, systemOverrides, entries);

        Log.Info($"Startup scan: {entries.Count} launchd entries, " +
                 $"{entries.Count(e => !e.IsEnabled)} disabled.");

        return entries;
    }

    private static void ReadFolder(
        string folder,
        StartupLocation location,
        IReadOnlyDictionary<string, bool> disabled,
        List<StartupEntry> entries)
    {
        try
        {
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.EnumerateFiles(folder, "*.plist"))
            {
                try
                {
                    var entry = ReadPlist(file, location, disabled);
                    if (entry is not null) entries.Add(entry);
                }
                catch (Exception ex)
                {
                    Log.Debug($"Could not read '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not list '{folder}': {ex.Message}");
        }
    }

    // =====================================================================================
    // Property lists
    // =====================================================================================

    private static StartupEntry? ReadPlist(
        string file,
        StartupLocation location,
        IReadOnlyDictionary<string, bool> disabled)
    {
        var document = LoadPlist(file);
        if (document is null) return null;

        var root = document.Root?.Element("dict");
        if (root is null) return null;

        var values = ReadDictionary(root);

        // A plist with no Label is not a job launchd will ever run, and it must not be
        // listed. Falling back to the file name produced a row with a live switch whose
        // service name does not exist - and `launchctl disable` accepts an unknown
        // service name happily, writing a junk override and reporting success, so the
        // user would be told a program had been turned off when nothing had changed.
        if (!values.TryGetValue("Label", out var label) || string.IsNullOrWhiteSpace(label))
        {
            Log.Debug($"Ignoring '{file}': a launchd job with no Label is never run.");
            return null;
        }

        var command = BuildCommand(root, values);
        var executable = FirstExistingExecutable(root, values);

        // A job with neither RunAtLoad nor KeepAlive is started on demand - by a socket,
        // a folder changing, a calendar interval. It is still something that starts by
        // itself, so it belongs on the page, but it is not what makes a login slow.
        bool runsAtLogin =
            values.TryGetValue("RunAtLoad", out var runAtLoad) && runAtLoad == "true" ||
            root.Elements("key").Any(k => k.Value == "KeepAlive");

        // launchd's override database wins; the plist's own Disabled key is the initial
        // state and is what applies when there is no override.
        bool enabled = disabled.TryGetValue(label!, out bool overridden)
            ? overridden
            : !(values.TryGetValue("Disabled", out var self) && self == "true");

        long size = 0;

        try
        {
            if (executable is not null && File.Exists(executable)) size = new FileInfo(executable).Length;
        }
        catch
        {
            // A size is a detail line, not a reason to drop the row.
        }

        return new StartupEntry
        {
            Id = $"{location}|{label}",
            Name = label!,
            Command = command,
            ExecutablePath = executable,
            Publisher = PublisherFromLabel(label!),
            Description = DescribeJob(label!, runsAtLogin),
            Location = location,
            Caution = Classify(label!),
            IsEnabled = enabled,
            IsBroken = executable is not null && !File.Exists(executable),
            ExecutableSize = size
        };
    }

    /// <summary>
    /// Reads a property list as XML.
    ///
    /// Most of these files are already XML and are parsed directly. A binary plist is
    /// converted first by plutil, which ships with macOS - one child process for the
    /// handful of files that need it, rather than a binary plist reader in this
    /// project or one child process per file.
    /// </summary>
    private static XDocument? LoadPlist(string file)
    {
        try
        {
            using (var stream = File.OpenRead(file))
            {
                Span<byte> magic = stackalloc byte[8];
                int read = stream.Read(magic);

                bool isBinary = read == 8 &&
                                magic[0] == (byte)'b' && magic[1] == (byte)'p' &&
                                magic[2] == (byte)'l' && magic[3] == (byte)'i' &&
                                magic[4] == (byte)'s' && magic[5] == (byte)'t';

                if (!isBinary)
                {
                    stream.Position = 0;
                    return XDocument.Load(stream);
                }
            }

            var result = ProcessRunner
                .RunAsync(PlUtil, new[] { "-convert", "xml1", "-o", "-", file }, ToolTimeout)
                .GetAwaiter().GetResult();

            return result.Success && result.StandardOutput.Length > 0
                ? XDocument.Parse(result.StandardOutput)
                : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Property list '{file}' could not be parsed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The scalar keys of a plist dictionary.
    ///
    /// A plist dict is a flat list of alternating &lt;key&gt; and value elements rather
    /// than nested pairs, so the value of a key is simply the element that follows it.
    /// Arrays and nested dictionaries are read separately by the callers that need them.
    /// </summary>
    private static Dictionary<string, string> ReadDictionary(XElement dictionary)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        XElement? key = null;

        foreach (var element in dictionary.Elements())
        {
            if (element.Name == "key")
            {
                key = element;
                continue;
            }

            if (key is null) continue;

            values[key.Value] = element.Name == "true" || element.Name == "false"
                ? element.Name.LocalName
                : element.Value;

            key = null;
        }

        return values;
    }

    /// <summary>Reads the string array that follows a key, e.g. ProgramArguments.</summary>
    private static IReadOnlyList<string> ReadArray(XElement dictionary, string name)
    {
        XElement? key = null;

        foreach (var element in dictionary.Elements())
        {
            if (element.Name == "key")
            {
                key = element;
                continue;
            }

            if (key?.Value == name && element.Name == "array")
                return element.Elements("string").Select(s => s.Value).ToList();

            key = null;
        }

        return Array.Empty<string>();
    }

    /// <summary>The command line as launchd will run it.</summary>
    private static string BuildCommand(XElement root, Dictionary<string, string> values)
    {
        var arguments = ReadArray(root, "ProgramArguments");
        if (arguments.Count > 0) return string.Join(' ', arguments);

        return values.TryGetValue("Program", out var program) ? program : string.Empty;
    }

    /// <summary>
    /// The executable the job runs. Program wins over ProgramArguments[0] because a
    /// plist that has both means the first argument is argv[0], not a path.
    /// </summary>
    private static string? FirstExistingExecutable(XElement root, Dictionary<string, string> values)
    {
        if (values.TryGetValue("Program", out var program) && !string.IsNullOrWhiteSpace(program))
            return program;

        var arguments = ReadArray(root, "ProgramArguments");
        return arguments.Count > 0 ? arguments[0] : null;
    }

    /// <summary>
    /// "com.google.keystone.agent" -> "google". The reverse-DNS label is the only
    /// publisher information a launchd job carries.
    /// </summary>
    private static string? PublisherFromLabel(string label)
    {
        var parts = label.Split('.');
        if (parts.Length < 2) return null;

        var vendor = parts[0] is "com" or "org" or "net" or "io" or "co" ? parts[1] : parts[0];

        return string.IsNullOrWhiteSpace(vendor)
            ? null
            : char.ToUpperInvariant(vendor[0]) + vendor[1..];
    }

    /// <summary>
    /// The last component of the label, which is usually the readable part, plus how
    /// the job is started.
    /// </summary>
    private static string DescribeJob(string label, bool runsAtLogin)
    {
        var parts = label.Split('.');
        var tail = parts.Length > 0 ? parts[^1] : label;

        return runsAtLogin ? tail : $"{tail} (on demand)";
    }

    private static StartupCaution Classify(string label)
    {
        var lowered = label.ToLowerInvariant();

        if (SecurityMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
            return StartupCaution.Security;

        if (SystemPrefixes.Any(prefix => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return StartupCaution.SystemOrVendor;

        return StartupCaution.Normal;
    }

    // =====================================================================================
    // Enabled state
    // =====================================================================================

    /// <summary>
    /// launchd's own override database for one domain, as
    /// <c>launchctl print-disabled &lt;domain&gt;</c> reports it.
    ///
    /// The domain matters: agents are overridden in <c>gui/&lt;uid&gt;</c> and daemons in
    /// <c>system</c>, and a label overridden in one never appears in the other's
    /// listing. Reading only the gui domain reports every disabled daemon as enabled.
    ///
    /// Only labels that have been overridden appear; everything else follows what its
    /// plist says. The output has used both "=> true" and "=> disabled" over the years,
    /// so both spellings are accepted.
    /// </summary>
    private static IReadOnlyDictionary<string, bool> ReadDisabledLabels(string domain)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);

        try
        {
            var result = ProcessRunner
                .RunAsync(LaunchCtl, new[] { "print-disabled", domain }, ToolTimeout)
                .GetAwaiter().GetResult();

            if (!result.Success) return states;

            foreach (var line in result.StandardOutput.Split('\n'))
            {
                var trimmed = line.Trim();

                int arrow = trimmed.IndexOf("=>", StringComparison.Ordinal);
                if (arrow <= 0) continue;

                var label = trimmed[..arrow].Trim().Trim('"');
                var state = trimmed[(arrow + 2)..].Trim();

                if (label.Length == 0) continue;

                states[label] = state is not ("true" or "disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"launchctl print-disabled {domain} failed: {ex.Message}");
        }

        return states;
    }

    private static string UserId()
    {
        try
        {
            var result = ProcessRunner
                .RunAsync("/usr/bin/id", new[] { "-u" }, TimeSpan.FromSeconds(5))
                .GetAwaiter().GetResult();

            var text = result.StandardOutput.Trim();
            if (result.Success && text.Length > 0) return text;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the user id: {ex.Message}");
        }

        return "501";
    }

    // =====================================================================================
    // Changing an entry
    // =====================================================================================

    /// <summary>
    /// Turns one entry on or off.
    ///
    /// The property list is never modified and never moved: <c>launchctl</c> records
    /// the override, which is how the change survives a reboot and how re-enabling puts
    /// the job back exactly as its author configured it.
    /// </summary>
    public StartupChangeResult SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.RequiresElevation)
        {
            // A system agent or daemon would need the whole application to be running
            // as root, which it never is. Refusing is the honest answer; the page shows
            // the row as read-only rather than offering a switch that would fail.
            return StartupChangeResult.Failed(
                "System-wide launchd jobs can only be changed by an administrator, " +
                "and Hexnest for Mac does not run as root.");
        }

        try
        {
            var target = $"gui/{UserId()}/{entry.Name}";
            var verb = enabled ? "enable" : "disable";

            var result = ProcessRunner
                .RunAsync(LaunchCtl, new[] { verb, target }, ToolTimeout)
                .GetAwaiter().GetResult();

            if (!result.Success)
            {
                var message = result.CombinedOutput.Trim();

                return StartupChangeResult.Failed(string.IsNullOrWhiteSpace(message)
                    ? $"launchctl {verb} exited with {result.ExitCode}."
                    : message);
            }

            // Disabling records the override but leaves a job that is already loaded
            // running until the next login. Booting it out makes the change take effect
            // now, and is a no-op when it was not loaded.
            if (!enabled)
            {
                ProcessRunner
                    .RunAsync(LaunchCtl, new[] { "bootout", target }, ToolTimeout)
                    .GetAwaiter().GetResult();
            }

            Log.Info($"Startup entry '{entry.Name}' {(enabled ? "enabled" : "disabled")}.");
            return StartupChangeResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not change '{entry.Name}': {ex.Message}");
            return StartupChangeResult.Failed(ex.Message);
        }
    }
}
