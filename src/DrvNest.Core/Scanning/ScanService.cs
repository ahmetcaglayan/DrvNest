using System.Text;
using DrvNest.Core.Abstractions;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Persistence;

namespace DrvNest.Core.Scanning;

/// <summary>
/// Ties the device scan and every provider together into one operation.
///
/// The device scan always runs, even with no network: knowing which devices have no
/// driver is useful on its own, and it is what drives the post-format recovery flow.
/// Provider searches run in parallel and a provider that fails only contributes a
/// warning, never an exception.
/// </summary>
public sealed class ScanService
{
    private readonly DeviceScanner _deviceScanner = new();
    private readonly IReadOnlyList<IDriverProvider> _providers;

    public ScanService(IEnumerable<IDriverProvider> providers)
        => _providers = providers.ToList();

    /// <summary>Diagnostic messages, in English, for the log and the CLI.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>
    /// Structured progress for the UI to localise and render as a bar.
    /// Raised roughly once a second while a long step is running, so the status bar
    /// can show elapsed time instead of appearing frozen.
    /// </summary>
    public event Action<ScanProgress>? ProgressChanged;

    // Phase weights. The device scan is quick; the source queries dominate.
    private const double DevicesStart = 4;
    private const double DevicesEnd = 22;
    private const double SourcesEnd = 94;

    public async Task<ScanResult> ScanAsync(
        IReadOnlyList<string>? ignoredHardwareIds = null,
        IReadOnlyList<string>? hiddenUpdateIds = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScanResult();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // ---- Devices -------------------------------------------------------------------
        Publish(new ScanProgress(ScanPhase.Devices, DevicesStart, 0, 1, clock.Elapsed.TotalSeconds));
        Report("Scanning devices...");

        result.Devices = await Task
            .Run(() => _deviceScanner.Scan(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        Publish(new ScanProgress(ScanPhase.Devices, DevicesEnd, 1, 1,
            clock.Elapsed.TotalSeconds, result.Devices.Count.ToString()));

        Report($"{result.Devices.Count} device(s) found, " +
               $"{result.MissingDriverCount} without a driver.");

        if (result.HasNoWorkingNetworkDriver)
        {
            result.Warnings.Add(
                "No network adapter has a working driver, so Windows Update cannot be reached. " +
                "Use Rescue Mode with a local driver folder or a DrvNest backup.");
        }

        // ---- Providers -------------------------------------------------------------------
        int total = Math.Max(_providers.Count, 1);
        int completed = 0;

        Publish(new ScanProgress(ScanPhase.Sources, DevicesEnd, 0, total, clock.Elapsed.TotalSeconds));

        var searches = _providers.Select(provider => SearchSafelyAsync(
            provider, result.Devices, result.Warnings, cancellationToken,
            () =>
            {
                int done = Interlocked.Increment(ref completed);
                Publish(new ScanProgress(
                    ScanPhase.Sources,
                    DevicesEnd + (SourcesEnd - DevicesEnd) * done / total,
                    done, total, clock.Elapsed.TotalSeconds));
            })).ToList();

        var all = Task.WhenAll(searches);

        // A Windows Update query is a single opaque COM call that can take a minute and
        // reports nothing while it runs. Ticking once a second keeps the status bar
        // showing elapsed time instead of looking frozen.
        while (!all.IsCompleted)
        {
            var tick = await Task.WhenAny(all, Task.Delay(1000, cancellationToken)).ConfigureAwait(false);
            if (tick == all) break;

            int done = Volatile.Read(ref completed);
            Publish(new ScanProgress(
                ScanPhase.Sources,
                DevicesEnd + (SourcesEnd - DevicesEnd) * done / total,
                done, total, clock.Elapsed.TotalSeconds));
        }

        var batches = await all.ConfigureAwait(false);

        var candidates = batches.SelectMany(b => b).ToList();

        // Only providers that were actually usable count. If none were, the UI must not
        // tell the user their drivers are current - nothing was checked.
        result.AvailableSourceCount = _providers.Count(p => p.IsAvailable);

        // ---- Filtering -------------------------------------------------------------------
        Publish(new ScanProgress(ScanPhase.Finalizing, SourcesEnd, total, total,
            clock.Elapsed.TotalSeconds));

        var ignored = new HashSet<string>(
            (ignoredHardwareIds ?? Array.Empty<string>()).Select(Formatting.NormalizeHardwareId),
            StringComparer.OrdinalIgnoreCase);

        var hidden = new HashSet<string>(
            hiddenUpdateIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        result.Candidates = Deduplicate(candidates)
            .Where(c => !hidden.Contains(c.ProviderId))
            .Where(c => !c.TargetHardwareIds
                .Select(Formatting.NormalizeHardwareId)
                .Any(ignored.Contains))
            .OrderByDescending(c => c.IsMissingDriver)
            .ThenByDescending(c => c.Severity)
            .ThenBy(c => c.DeviceClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        result.FinishedUtc = DateTime.UtcNow;

        Publish(new ScanProgress(ScanPhase.Done, 100, total, total,
            clock.Elapsed.TotalSeconds, result.Candidates.Count.ToString()));

        Report($"Scan complete: {result.NewInstallCount} missing driver(s), " +
               $"{result.UpdateCount} update(s) in {result.Duration.TotalSeconds:0.0}s.");

        return result;
    }

    private async Task<IReadOnlyList<UpdateCandidate>> SearchSafelyAsync(
        IDriverProvider provider,
        IReadOnlyList<DeviceItem> devices,
        List<string> warnings,
        CancellationToken cancellationToken,
        Action onFinished)
    {
        try
        {
            Report($"Querying {provider.DisplayName}...");

            var found = await provider.SearchAsync(devices, cancellationToken).ConfigureAwait(false);

            if (!provider.IsAvailable && provider.UnavailableReason is { Length: > 0 } reason)
            {
                lock (warnings) warnings.Add($"{provider.DisplayName}: {reason}");
            }

            Report($"{provider.DisplayName}: {found.Count} package(s).");
            return found;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"{provider.DisplayName} search failed", ex);
            lock (warnings) warnings.Add($"{provider.DisplayName}: {ex.Message}");
            return Array.Empty<UpdateCandidate>();
        }
        finally
        {
            try { onFinished(); } catch { /* progress reporting must not fail a scan */ }
        }
    }

    private void Publish(ScanProgress progress)
    {
        try { ProgressChanged?.Invoke(progress); } catch { /* UI handler */ }
    }

    /// <summary>
    /// Two providers can offer the same driver. Keep one per device+version, preferring
    /// the local repository: it is already on disk, so it installs without a network.
    /// </summary>
    private static IEnumerable<UpdateCandidate> Deduplicate(IEnumerable<UpdateCandidate> candidates)
        => candidates
            .GroupBy(c => c.DedupeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(c => c.Provider == ProviderKind.LocalRepository ? 0 : 1)
                .ThenByDescending(c => c.NewVersion, Comparer<string?>.Create(Formatting.CompareVersions))
                .First());

    /// <summary>
    /// Writes a plain-text hardware report.
    ///
    /// The intended use is deliberately low tech: on a machine with no network, export
    /// this to a USB stick, open it on a working computer, and you have every hardware
    /// id needed to fetch the right drivers by hand.
    /// </summary>
    public static string ExportHardwareReport(ScanResult scan, string? targetPath = null)
    {
        var path = targetPath ?? Path.Combine(
            AppPaths.ReportsDirectory,
            $"drvnest-hardware-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd_HHmm}.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var snapshot = Platform.SystemInfo.Current;
        var builder = new StringBuilder();

        builder.AppendLine("DrvNest hardware report");
        builder.AppendLine("=======================");
        builder.AppendLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Machine   : {snapshot.MachineDisplay}");
        builder.AppendLine($"System    : {snapshot.OsDisplay} ({snapshot.Architecture})");
        builder.AppendLine($"CPU       : {snapshot.ProcessorName}");
        builder.AppendLine($"BIOS      : {snapshot.BiosVersion}");
        builder.AppendLine();
        builder.AppendLine($"Devices   : {scan.Devices.Count}");
        builder.AppendLine($"No driver : {scan.MissingDriverCount}");
        builder.AppendLine($"Problems  : {scan.ProblemDeviceCount}");
        builder.AppendLine();

        var missing = scan.Devices.Where(d => d.NeedsAttention).ToList();
        if (missing.Count > 0)
        {
            builder.AppendLine("DEVICES NEEDING A DRIVER");
            builder.AppendLine("------------------------");

            foreach (var device in missing)
            {
                builder.AppendLine($"  {device.Name}");
                builder.AppendLine($"    Class      : {device.DeviceClass}");
                builder.AppendLine($"    Status     : {device.Health} - {device.ProblemText}");
                builder.AppendLine($"    Hardware ID: {device.PrimaryHardwareId}");
                foreach (var id in device.HardwareIds.Skip(1).Take(4))
                    builder.AppendLine($"                 {id}");
                builder.AppendLine();
            }
        }

        builder.AppendLine("ALL DEVICES");
        builder.AppendLine("-----------");

        foreach (var group in scan.Devices.GroupBy(d => d.DeviceClass).OrderBy(g => g.Key))
        {
            builder.AppendLine($"[{group.Key}]");
            foreach (var device in group.OrderBy(d => d.Name))
            {
                builder.AppendLine($"  {device.Name}");
                builder.AppendLine($"    Driver : {device.DriverProvider ?? "-"} " +
                                   $"{device.VersionDisplay} ({device.DriverDate:yyyy-MM-dd})");
                builder.AppendLine($"    HWID   : {device.PrimaryHardwareId}");
            }
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        Log.Info($"Hardware report written to {path}");
        return path;
    }

    private void Report(string message)
    {
        Log.Info(message);
        try { StatusChanged?.Invoke(message); } catch { /* UI handler */ }
    }
}
