using DrvNest.Core;

namespace DrvNest.Cli;

/// <summary>What the user asked the CLI to do.</summary>
public enum Command
{
    None,
    Scan,
    Install,
    Backup,
    Restore,
    Resume,
    History,
    Report
}

/// <summary>Parsed command line.</summary>
public sealed class CliOptions
{
    public Command Command { get; set; } = Command.None;

    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool Verbose { get; set; }

    /// <summary>Never contact Windows Update; use local folders only.</summary>
    public bool Offline { get; set; }

    /// <summary>Skip optional driver offers.</summary>
    public bool RecommendedOnly { get; set; }

    /// <summary>Install only devices that currently have no driver at all.</summary>
    public bool MissingOnly { get; set; }

    /// <summary>List what would happen without touching anything.</summary>
    public bool DryRun { get; set; }

    /// <summary>Restart automatically when the queue asks for it.</summary>
    public bool AutoReboot { get; set; }

    public int RebootDelaySeconds { get; set; } = 60;

    /// <summary>Compress a backup into a ZIP.</summary>
    public bool Compress { get; set; }

    /// <summary>Destination or source path, depending on the command.</summary>
    public string? Path { get; set; }

    /// <summary>Substring match against the device name, class or package title.</summary>
    public string? DeviceFilter { get; set; }

    /// <summary>Extra offline driver folders to search.</summary>
    public List<string> RepositoryPaths { get; } = new();

    /// <summary>How many history records to print.</summary>
    public int Limit { get; set; } = 50;
}

/// <summary>
/// Hand-written argument parser.
///
/// System.CommandLine would be nicer, but it is a NuGet package, and this project's
/// whole promise is a single file with nothing else to install. The grammar here is
/// small enough that parsing it by hand costs less than the dependency.
/// </summary>
public static class CommandLine
{
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        if (args.Length == 0)
        {
            options.ShowHelp = true;
            return options;
        }

        int index = 0;

        // The first non-flag token is the verb.
        if (!args[0].StartsWith('-'))
        {
            options.Command = args[0].ToLowerInvariant() switch
            {
                "scan" => Command.Scan,
                "install" or "update" => Command.Install,
                "backup" or "export" => Command.Backup,
                "restore" or "import" => Command.Restore,
                "resume" or "continue" => Command.Resume,
                "history" or "log" => Command.History,
                "report" => Command.Report,
                _ => Command.None
            };

            if (options.Command == Command.None)
            {
                options.ShowHelp = true;
                return options;
            }

            index = 1;
        }

        for (; index < args.Length; index++)
        {
            var argument = args[index];
            var name = argument.TrimStart('-', '/').ToLowerInvariant();

            switch (name)
            {
                case "h":
                case "?":
                case "help":
                    options.ShowHelp = true;
                    break;

                case "v":
                case "version":
                    options.ShowVersion = true;
                    break;

                case "verbose":
                    options.Verbose = true;
                    break;

                case "offline":
                case "rescue":
                    options.Offline = true;
                    break;

                case "recommended":
                    options.RecommendedOnly = true;
                    break;

                case "missing":
                case "missing-only":
                    options.MissingOnly = true;
                    break;

                case "dry-run":
                case "whatif":
                    options.DryRun = true;
                    break;

                case "reboot":
                    options.AutoReboot = true;
                    break;

                case "compress":
                case "zip":
                    options.Compress = true;
                    break;

                case "path":
                case "out":
                case "output":
                    options.Path = Next(args, ref index);
                    break;

                case "device":
                case "filter":
                    options.DeviceFilter = Next(args, ref index);
                    break;

                case "repo":
                case "drivers":
                    if (Next(args, ref index) is { Length: > 0 } folder)
                        options.RepositoryPaths.Add(folder);
                    break;

                case "delay":
                    if (int.TryParse(Next(args, ref index), out int delay))
                        options.RebootDelaySeconds = Math.Clamp(delay, 5, 3600);
                    break;

                case "limit":
                    if (int.TryParse(Next(args, ref index), out int limit))
                        options.Limit = Math.Clamp(limit, 1, 10000);
                    break;

                default:
                    // An unknown flag is a typo, and silently ignoring it on a tool that
                    // installs drivers is not a kindness.
                    Console.Error.WriteLine($"Unknown option: {argument}");
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }

    private static string? Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length) return null;

        index++;
        return args[index];
    }

    public static void PrintHelp()
    {
        Console.WriteLine($"""
            {AppInfo.ProductName} {AppInfo.Version} - {AppInfo.Tagline}

            USAGE
              drvnest <command> [options]

            COMMANDS
              scan                 List devices, missing drivers and available updates.
              install              Install everything that applies. Requires administrator.
              backup               Export every third-party driver package.
              restore              Install every package from a backup folder or ZIP.
              resume               Continue a session interrupted by a restart.
              history              Print or export the update history.
              report               Write a hardware report with every hardware ID.

            OPTIONS
              --offline            Never contact Windows Update; use local folders only.
                                   Alias: --rescue
              --repo <folder>      Add an offline driver folder. May be repeated.
              --missing            Only handle devices that have no driver at all.
              --device <text>      Only handle devices matching this text.
              --recommended        Skip optional driver offers.
              --dry-run            Show what would happen and change nothing.
              --reboot             Restart automatically when a restart is required.
              --delay <seconds>    Restart countdown. Default 60.
              --path <path>        Destination or source path, depending on the command.
              --compress           Write the backup as a ZIP archive.
              --limit <n>          History records to print. Default 50.
              --verbose            Print debug detail.
              --version            Print the version and exit.
              --help               Print this help.

            EXIT CODES
              0     Success.
              1     One or more operations failed.
              2     Bad usage.
              5     Administrator rights are required.
              130   Cancelled with Ctrl+C.
              3010  Success, but Windows must restart to finish.

            EXAMPLES
              drvnest scan
              drvnest install --missing --reboot
              drvnest install --device "Realtek" --dry-run
              drvnest backup --path D:\DriverBackup --compress
              drvnest restore --path D:\DriverBackup.zip
              drvnest scan --offline --repo D:\Drivers
              drvnest history --path C:\Temp\history.csv

            A "Drivers" folder next to the executable is searched automatically, which is
            what makes a USB rescue stick work with no arguments at all.
            """);
    }
}
