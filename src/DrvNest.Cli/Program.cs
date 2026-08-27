using System.Globalization;
using DrvNest.Cli;
using DrvNest.Core;
using DrvNest.Core.Abstractions;
using DrvNest.Core.Backup;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Jobs;
using DrvNest.Core.Models;
using DrvNest.Core.Persistence;
using DrvNest.Core.Platform;
using DrvNest.Core.Providers;
using DrvNest.Core.Resume;
using DrvNest.Core.Scanning;

// =============================================================================================
// DrvNest CLI
//
// The headless half of the project. It exists for the cases a window cannot serve:
// deployment scripts, WinPE and rescue media, and remote sessions. It shares every
// line of logic with the GUI - the only thing that differs is the front end.
// =============================================================================================

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

AppPaths.EnsureCreated();
Log.Initialize(AppPaths.LogFile);

var options = CommandLine.Parse(args);

if (options.ShowHelp)
{
    CommandLine.PrintHelp();
    return 0;
}

if (options.ShowVersion)
{
    Console.WriteLine(AppInfo.VersionDisplay);
    return 0;
}

// Only force UTF-8 for a real console. When output is redirected, Windows PowerShell
// decodes the stream with the console code page, and forcing UTF-8 there turns every
// non-ASCII character into mojibake in log files and CI output.
if (!Console.IsOutputRedirected) Console.OutputEncoding = System.Text.Encoding.UTF8;
Output.Verbose = options.Verbose;

Output.Banner();

var settings = new SettingsStore();
var history = new HistoryStore();
var sessions = new SessionStore();
var backup = new DriverBackupService();

// Offline mode covers the machine this tool exists for: freshly formatted, no network
// driver yet, running from a USB stick.
var windowsUpdate = new WindowsUpdateProvider
{
    Enabled = !options.Offline,
    IncludeOptional = !options.RecommendedOnly
};

var repositoryPaths = new List<string>(settings.Current.LocalRepositoryPaths);
repositoryPaths.AddRange(options.RepositoryPaths);

var localRepository = new LocalRepositoryProvider(() => repositoryPaths);

// Local first: an offline package installs without a network and without waiting on
// the Windows Update service.
var providers = new IDriverProvider[] { localRepository, windowsUpdate };

var scanner = new ScanService(providers);
scanner.StatusChanged += Output.Status;

using var engine = new JobEngine(sessions, history, settings, backup, providers);
engine.StatusChanged += Output.Status;
engine.JobChanged += Output.Job;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Output.Warn("Cancelling...");
    cancellation.Cancel();
    engine.Cancel();
};

try
{
    return options.Command switch
    {
        Command.Scan => await RunScanAsync(),
        Command.Install => await RunInstallAsync(),
        Command.Backup => await RunBackupAsync(),
        Command.Restore => await RunRestoreAsync(),
        Command.Resume => await RunResumeAsync(),
        Command.History => RunHistory(),
        Command.Report => await RunReportAsync(),
        _ => Help()
    };
}
catch (OperationCanceledException)
{
    Output.Warn("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Log.Error("CLI failed", ex);
    Output.Error(ex.Message);
    return 1;
}

// =============================================================================================
// Commands
// =============================================================================================

int Help()
{
    CommandLine.PrintHelp();
    return 2;
}

async Task<ScanResult> ScanAsync()
{
    var current = settings.Current;

    return await scanner.ScanAsync(
        current.IgnoredHardwareIds,
        current.HiddenUpdateIds,
        cancellation.Token).ConfigureAwait(false);
}

async Task<int> RunScanAsync()
{
    var result = await ScanAsync().ConfigureAwait(false);

    Output.Section("Summary");
    Output.KeyValue("Devices", result.Devices.Count.ToString());
    Output.KeyValue("Missing drivers", result.MissingDriverCount.ToString());
    Output.KeyValue("Problem devices", result.ProblemDeviceCount.ToString());
    Output.KeyValue("Available updates", result.UpdateCount.ToString());
    Output.KeyValue("New installs", result.NewInstallCount.ToString());

    foreach (var warning in result.Warnings) Output.Warn(warning);

    if (result.Candidates.Count > 0)
    {
        Output.Section("Installable packages");

        foreach (var candidate in result.Candidates)
        {
            var tag = candidate.IsMissingDriver ? "[MISSING]" : "[UPDATE] ";
            Output.Line($"  {tag} {candidate.DeviceName}");
            Output.Dim($"           {candidate.Title}");
            Output.Dim("           " + candidate.VersionTransition + Output.Dot +
                       candidate.SizeDisplay + Output.Dot + candidate.Provider);
        }
    }

    if (result.ProblemDeviceCount > 0)
    {
        Output.Section("Devices needing attention");

        foreach (var device in result.Devices.Where(d => d.NeedsAttention))
        {
            Output.Line($"  {device.Name}");
            Output.Dim("    " + device.DeviceClass + Output.Dot + device.Health + Output.Dot + device.ProblemText);
            Output.Dim($"    {device.PrimaryHardwareId}");
        }
    }

    return 0;
}

async Task<int> RunInstallAsync()
{
    if (!Elevation.IsAdministrator)
    {
        Output.Error("Installing drivers requires an elevated prompt. Run as administrator.");
        return 5;
    }

    var result = await ScanAsync().ConfigureAwait(false);

    var selected = options.MissingOnly
        ? result.Candidates.Where(c => c.IsMissingDriver).ToList()
        : result.Candidates.ToList();

    if (options.DeviceFilter is { Length: > 0 } filter)
    {
        selected = selected
            .Where(c => c.DeviceName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || c.DeviceClass.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || c.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    if (selected.Count == 0)
    {
        Output.Ok("Nothing to install.");
        return 0;
    }

    Output.Section($"Installing {selected.Count} package(s)");
    foreach (var candidate in selected)
        Output.Line("  " + candidate.DeviceName + Output.Dot + candidate.VersionTransition);

    if (options.DryRun)
    {
        Output.Warn("Dry run: nothing was installed.");
        return 0;
    }

    var session = engine.CreateSession(selected, options.MissingOnly);
    await engine.RunAsync(session, cancellation.Token).ConfigureAwait(false);

    Output.Section("Result");
    Output.KeyValue("Succeeded", session.SucceededCount.ToString());
    Output.KeyValue("Failed", session.FailedCount.ToString());

    foreach (var job in session.Jobs.Where(j => j.State == JobState.Failed))
        Output.Error($"  {job.Candidate.DeviceName}: {job.ErrorMessage}");

    if (engine.RebootRequired)
    {
        Output.Warn("A restart is required to finish.");

        if (options.AutoReboot)
        {
            Output.Warn($"Restarting in {options.RebootDelaySeconds} seconds...");
            await RebootService.RestartAsync(options.RebootDelaySeconds).ConfigureAwait(false);
        }
        else
        {
            Output.Line("Run 'drvnest resume' after the restart, or let the scheduled task do it.");
        }

        return 3010;
    }

    return session.FailedCount > 0 ? 1 : 0;
}

async Task<int> RunResumeAsync()
{
    var session = sessions.Load();

    if (session is null)
    {
        Output.Ok("There is no interrupted session to continue.");
        return 0;
    }

    Output.Section($"Resuming {session.PendingCount} job(s)");
    await engine.ResumeAsync(session, cancellation.Token).ConfigureAwait(false);

    Output.KeyValue("Succeeded", session.SucceededCount.ToString());
    Output.KeyValue("Failed", session.FailedCount.ToString());

    return engine.RebootRequired ? 3010 : session.FailedCount > 0 ? 1 : 0;
}

async Task<int> RunBackupAsync()
{
    if (!Elevation.IsAdministrator)
    {
        Output.Error("Exporting the driver store requires an elevated prompt.");
        return 5;
    }

    Output.Section("Exporting drivers");

    var progress = new Progress<string>(Output.Status);

    var entry = await backup.ExportAllAsync(
        destinationFolder: options.Path,
        note: null,
        compress: options.Compress,
        status: progress,
        cancellationToken: cancellation.Token).ConfigureAwait(false);

    Output.Ok($"Backup written to {entry.Path}");
    Output.KeyValue("Packages", entry.PackageCount.ToString());
    Output.KeyValue("Size", entry.SizeDisplay);

    return 0;
}

async Task<int> RunRestoreAsync()
{
    if (!Elevation.IsAdministrator)
    {
        Output.Error("Restoring drivers requires an elevated prompt.");
        return 5;
    }

    if (string.IsNullOrWhiteSpace(options.Path))
    {
        Output.Error("Specify the backup folder or ZIP with --path.");
        return 2;
    }

    Output.Section("Restoring drivers");

    var progress = new Progress<string>(Output.Status);

    var (success, rebootRequired, message) = await backup
        .RestoreAsync(options.Path!, progress, cancellation.Token)
        .ConfigureAwait(false);

    if (!success)
    {
        Output.Error(message);
        return 1;
    }

    Output.Ok(message);
    return rebootRequired ? 3010 : 0;
}

int RunHistory()
{
    var records = history.All();

    if (options.Path is { Length: > 0 })
    {
        var exported = history.ExportCsv(options.Path);
        Output.Ok($"History exported to {exported}");
        return 0;
    }

    if (records.Count == 0)
    {
        Output.Line("No history yet.");
        return 0;
    }

    Output.Section($"History ({records.Count} record(s))");

    foreach (var record in records.Take(options.Limit))
    {
        Output.Line($"  {record.TimestampLocal:yyyy-MM-dd HH:mm}  {record.Outcome,-9}  {record.DeviceName}");
        Output.Dim("    " + record.VersionTransition + Output.Dot + record.Provider);

        if (!string.IsNullOrWhiteSpace(record.ErrorMessage))
            Output.Dim($"    {record.ErrorMessage}");
    }

    return 0;
}

async Task<int> RunReportAsync()
{
    var result = await ScanAsync().ConfigureAwait(false);
    var path = ScanService.ExportHardwareReport(result, options.Path);

    Output.Ok($"Hardware report written to {path}");
    return 0;
}
