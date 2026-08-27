using DrvNest.Core.Abstractions;
using DrvNest.Core.Backup;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Persistence;

namespace DrvNest.Core.Providers;

/// <summary>
/// Driver packages from folders on disk: a USB stick, a network share, a DrvNest
/// backup, or the "Drivers" folder next to the executable.
///
/// This is the provider that makes the post-format story work at all. When the
/// network adapter has no driver there is no internet, so Windows Update cannot
/// help; a local repository can.
/// </summary>
public sealed class LocalRepositoryProvider : IDriverProvider
{
    private readonly Func<IReadOnlyList<string>> _folderProvider;
    private readonly List<InfPackage> _packages = new();
    private readonly object _gate = new();

    private string? _unavailableReason;

    public LocalRepositoryProvider(Func<IReadOnlyList<string>> folderProvider)
        => _folderProvider = folderProvider;

    public ProviderKind Kind => ProviderKind.LocalRepository;

    public string DisplayName => "Local repository";

    public bool IsAvailable => _folderProvider().Count > 0;

    public string? UnavailableReason => _unavailableReason;

    // =====================================================================================
    // Search
    // =====================================================================================

    public Task<IReadOnlyList<UpdateCandidate>> SearchAsync(
        IReadOnlyList<DeviceItem> devices,
        CancellationToken cancellationToken)
        => Task.Run<IReadOnlyList<UpdateCandidate>>(() => SearchCore(devices, cancellationToken),
            cancellationToken);

    private List<UpdateCandidate> SearchCore(
        IReadOnlyList<DeviceItem> devices,
        CancellationToken cancellationToken)
    {
        var folders = _folderProvider();
        var candidates = new List<UpdateCandidate>();

        if (folders.Count == 0)
        {
            // Having no offline folders is a configuration state, not a problem, so it
            // deliberately produces no warning: telling every online user that they have
            // not set up a USB driver repository would be noise on every single scan.
            _unavailableReason = null;
            return candidates;
        }

        var packages = new List<InfPackage>();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                packages.AddRange(InfParser.ScanFolder(folder, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not scan {folder}: {ex.Message}");
            }
        }

        lock (_gate)
        {
            _packages.Clear();
            _packages.AddRange(packages);
        }

        if (packages.Count == 0)
        {
            _unavailableReason = "No .inf packages were found in the configured folders.";
            return candidates;
        }

        _unavailableReason = null;

        // Index packages by hardware id so each device gets its best match in one pass.
        var byHardwareId = new Dictionary<string, List<InfPackage>>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            foreach (var id in package.HardwareIds)
            {
                var key = Formatting.NormalizeHardwareId(id);
                if (key.Length == 0) continue;

                if (!byHardwareId.TryGetValue(key, out var list))
                    byHardwareId[key] = list = new List<InfPackage>();

                list.Add(package);
            }
        }

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var best = FindBestPackage(device, byHardwareId);
            if (best is null) continue;

            // Not a raw version comparison: see DriverComparison for why comparing an
            // Intel package's 1.41.1420.0 against Microsoft's generic 10.0.26100.1
            // throws away exactly the driver the user needs.
            var reason = DriverComparison.Evaluate(device, best.Provider, best.Version);
            if (reason == OfferReason.None) continue;

            candidates.Add(new UpdateCandidate
            {
                Provider = ProviderKind.LocalRepository,
                Reason = reason,
                ProviderId = best.InfPath,
                LocalInfPath = best.InfPath,
                Title = $"{best.Provider ?? "Local"} - {best.Class ?? device.DeviceClass} - {best.Version ?? "?"}",
                Description = $"Offline package: {best.FileName}",
                DeviceName = device.Name,
                DeviceClass = device.DeviceClass,
                DeviceId = device.DeviceId,
                Manufacturer = best.Provider ?? device.Manufacturer,
                CurrentVersion = device.DriverVersion,
                NewVersion = best.Version,
                ReleaseDate = best.Date,
                SizeBytes = best.SizeBytes,
                Severity = reason switch
                {
                    OfferReason.MissingDriver => UpdateSeverity.Important,
                    OfferReason.ReplacesGeneric => UpdateSeverity.Recommended,
                    _ => UpdateSeverity.Recommended
                },
                TargetHardwareIds = best.HardwareIds.Take(8).ToList()
            });
        }

        Log.Info($"Local repository matched {candidates.Count} device(s) from {packages.Count} package(s).");
        return candidates;
    }

    /// <summary>
    /// Prefers an exact hardware id match over a compatible id match, and the newest
    /// version within each tier. This mirrors how Windows itself ranks drivers.
    /// </summary>
    private static InfPackage? FindBestPackage(
        DeviceItem device,
        Dictionary<string, List<InfPackage>> index)
    {
        foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
        {
            var key = Formatting.NormalizeHardwareId(id);
            if (key.Length == 0 || !index.TryGetValue(key, out var matches)) continue;

            return matches
                .OrderByDescending(p => p.Version, Comparer<string?>.Create(Formatting.CompareVersions))
                .ThenByDescending(p => p.Date ?? DateTime.MinValue)
                .First();
        }

        return null;
    }

    // =====================================================================================
    // Download - stage the package into the local cache
    // =====================================================================================

    /// <summary>
    /// Copies the package next to the app before installing it. Staging means an
    /// install cannot fail half way because someone unplugged the USB stick, and it
    /// gives the UI a real byte-level progress bar for offline packages too.
    /// </summary>
    public Task<ProviderResult> DownloadAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
        => Task.Run(() => StageCore(job, progress, cancellationToken), cancellationToken);

    private static ProviderResult StageCore(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var infPath = job.Candidate.LocalInfPath;
            if (string.IsNullOrWhiteSpace(infPath) || !File.Exists(infPath))
                return ProviderResult.Fail(-1, $"Package not found: {infPath}");

            var sourceFolder = Path.GetDirectoryName(infPath)!;
            var targetFolder = Path.Combine(AppPaths.CacheDirectory, "staged", job.Id);

            if (Directory.Exists(targetFolder)) Directory.Delete(targetFolder, recursive: true);
            Directory.CreateDirectory(targetFolder);

            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            long total = files.Sum(f => new FileInfo(f).Length);
            long copied = 0;

            progress(new JobProgress(0, 0, total, "Staging package"));

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(sourceFolder, file);
                var destination = Path.Combine(targetFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                File.Copy(file, destination, overwrite: true);

                copied += new FileInfo(file).Length;
                double percent = total > 0 ? copied * 100.0 / total : 100;
                progress(new JobProgress(percent, copied, total, "Staging package"));
            }

            // Point the install step at the staged copy.
            job.Candidate.LocalInfPath = Path.Combine(targetFolder, Path.GetFileName(infPath));

            progress(new JobProgress(100, total, total, "Ready to install"));
            return ProviderResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return ProviderResult.Fail(-2, "Cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error($"Staging failed for {job.Candidate.Title}", ex);
            return ProviderResult.Fail(-1, ex.Message);
        }
    }

    // =====================================================================================
    // Install
    // =====================================================================================

    public async Task<ProviderResult> InstallAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        var infPath = job.Candidate.LocalInfPath;
        if (string.IsNullOrWhiteSpace(infPath) || !File.Exists(infPath))
            return ProviderResult.Fail(-1, $"Package not found: {infPath}");

        // pnputil reports no progress, so the UI shows coarse milestones instead of
        // pretending to know a percentage it cannot know.
        progress(new JobProgress(15, 0, 0, "Adding package to the driver store"));

        var result = await PnpUtil.AddDriverAsync(infPath, install: true, cancellationToken)
            .ConfigureAwait(false);

        if (!result.SucceededOrNeedsReboot)
        {
            var message = PnpUtil.DescribeExitCode(result.ExitCode);
            Log.Warn($"pnputil failed for {infPath}: {message}");
            return ProviderResult.Fail(result.ExitCode, message);
        }

        progress(new JobProgress(85, 0, 0, "Binding the driver to the device"));
        await PnpUtil.ScanDevicesAsync(cancellationToken).ConfigureAwait(false);

        progress(new JobProgress(100, 0, 0, result.RebootRequired ? "Restart required" : "Installed"));
        return ProviderResult.Ok(result.RebootRequired);
    }
}
