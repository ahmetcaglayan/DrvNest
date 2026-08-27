using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using DrvNest.Core.Persistence;

namespace DrvNest.Core.Backup;

/// <summary>Metadata written next to an exported backup so it can be identified later.</summary>
public sealed class BackupManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public string? SystemManufacturer { get; set; }
    public string? SystemProductName { get; set; }
    public string? OsBuild { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public int PackageCount { get; set; }
    public long SizeBytes { get; set; }
    public string? Note { get; set; }

    /// <summary>Short summary of every exported package, for the restore preview.</summary>
    public List<BackupPackageInfo> Packages { get; set; } = new();
}

/// <summary>One package inside a backup.</summary>
public sealed class BackupPackageInfo
{
    public string InfName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// <summary>A backup found on disk, ready to be restored.</summary>
public sealed class BackupEntry
{
    public string Path { get; set; } = string.Empty;
    public bool IsArchive { get; set; }
    public BackupManifest? Manifest { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedLocal { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string SizeDisplay => Formatting.HumanBytes(SizeBytes);
    public int PackageCount => Manifest?.PackageCount ?? 0;
}

/// <summary>
/// Exports and restores third-party driver packages.
///
/// This is the answer to the chicken-and-egg problem of a fresh format: run a backup
/// before wiping the machine, keep the folder (or ZIP) on a USB stick next to
/// DrvNest.exe, and every driver comes back without needing a network connection.
/// </summary>
public sealed class DriverBackupService
{
    private const string ManifestFileName = "drvnest-backup.json";

    private readonly Func<string> _backupRootProvider;

    public DriverBackupService(Func<string>? backupRootProvider = null)
        => _backupRootProvider = backupRootProvider ?? (() => AppPaths.BackupsDirectory);

    public string BackupRoot => _backupRootProvider();

    // =====================================================================================
    // Export
    // =====================================================================================

    /// <summary>
    /// Exports every third-party driver package in the driver store.
    /// In-box Microsoft drivers are deliberately not exported: Windows reinstalls
    /// those itself, and they would triple the backup size for no benefit.
    /// </summary>
    public async Task<BackupEntry> ExportAllAsync(
        string? destinationFolder = null,
        string? note = null,
        bool compress = false,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var folder = destinationFolder ?? AppPaths.NewBackupFolder("drivers");
        Directory.CreateDirectory(folder);

        status?.Report("Reading the driver store...");
        var entries = await PnpUtil.EnumerateDriversAsync(cancellationToken).ConfigureAwait(false);

        status?.Report($"Exporting {entries.Count} package(s)...");
        var result = await PnpUtil.ExportDriverAsync("*", folder, cancellationToken).ConfigureAwait(false);

        if (!result.SucceededOrNeedsReboot)
        {
            var message = PnpUtil.DescribeExitCode(result.ExitCode);
            Log.Error($"Driver export failed: {message}");
            throw new InvalidOperationException(message);
        }

        var snapshot = Platform.SystemInfo.Current;

        var manifest = new BackupManifest
        {
            SystemManufacturer = snapshot.SystemManufacturer,
            SystemProductName = snapshot.SystemProductName,
            OsBuild = snapshot.FullBuild,
            Architecture = snapshot.Architecture,
            Note = note,
            PackageCount = entries.Count,
            SizeBytes = MeasureFolder(folder),
            Packages = entries.Select(e => new BackupPackageInfo
            {
                InfName = e.OriginalName.Length > 0 ? e.OriginalName : e.PublishedName,
                Provider = e.Provider,
                ClassName = e.ClassName,
                Version = e.Version
            }).ToList()
        };

        WriteManifest(folder, manifest);
        Log.Info($"Exported {entries.Count} driver package(s) to {folder}.");

        if (!compress)
        {
            return new BackupEntry
            {
                Path = folder,
                IsArchive = false,
                Manifest = manifest,
                SizeBytes = manifest.SizeBytes,
                CreatedLocal = DateTime.Now
            };
        }

        status?.Report("Compressing...");
        var archivePath = folder + ".zip";

        if (File.Exists(archivePath)) File.Delete(archivePath);
        ZipFile.CreateFromDirectory(folder, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the staging folder: {ex.Message}");
        }

        Log.Info($"Backup archived to {archivePath}.");

        return new BackupEntry
        {
            Path = archivePath,
            IsArchive = true,
            Manifest = manifest,
            SizeBytes = new FileInfo(archivePath).Length,
            CreatedLocal = DateTime.Now
        };
    }

    /// <summary>Exports one package, used before replacing a driver so rollback is possible.</summary>
    public async Task<string?> ExportSinglePackageAsync(
        string publishedInfName,
        string? destinationFolder = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publishedInfName)) return null;

        var folder = destinationFolder ??
                     Path.Combine(AppPaths.BackupsDirectory, "rollback",
                         $"{Path.GetFileNameWithoutExtension(publishedInfName)}-{DateTime.Now:yyyyMMddHHmmss}");

        try
        {
            Directory.CreateDirectory(folder);

            var result = await PnpUtil
                .ExportDriverAsync(publishedInfName, folder, cancellationToken)
                .ConfigureAwait(false);

            if (result.SucceededOrNeedsReboot)
            {
                Log.Info($"Backed up {publishedInfName} to {folder}.");
                return folder;
            }

            Log.Warn($"Could not back up {publishedInfName}: {PnpUtil.DescribeExitCode(result.ExitCode)}");
            TryDelete(folder);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Backup of {publishedInfName} failed: {ex.Message}");
            TryDelete(folder);
            return null;
        }
    }

    // =====================================================================================
    // Restore
    // =====================================================================================

    /// <summary>
    /// Installs every package in a backup folder or ZIP. This is the one-click
    /// post-format restore.
    /// </summary>
    public async Task<(bool Success, bool RebootRequired, string Message)> RestoreAsync(
        string backupPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        string folder = backupPath;
        string? temporary = null;

        try
        {
            if (File.Exists(backupPath) &&
                backupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                status?.Report("Extracting the archive...");
                temporary = Path.Combine(AppPaths.CacheDirectory, "restore",
                    Path.GetFileNameWithoutExtension(backupPath));

                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
                Directory.CreateDirectory(temporary);

                ZipFile.ExtractToDirectory(backupPath, temporary, overwriteFiles: true);
                folder = temporary;
            }

            if (!Directory.Exists(folder))
                return (false, false, $"Backup not found: {backupPath}");

            status?.Report("Installing driver packages...");
            var result = await PnpUtil.AddDriversFromFolderAsync(folder, cancellationToken)
                .ConfigureAwait(false);

            if (!result.SucceededOrNeedsReboot)
                return (false, false, PnpUtil.DescribeExitCode(result.ExitCode));

            status?.Report("Re-scanning devices...");
            await PnpUtil.ScanDevicesAsync(cancellationToken).ConfigureAwait(false);

            var message = result.RebootRequired
                ? "Drivers restored. A restart is required to finish."
                : "Drivers restored successfully.";

            Log.Info(message);
            return (true, result.RebootRequired, message);
        }
        catch (Exception ex)
        {
            Log.Error("Restore failed", ex);
            return (false, false, ex.Message);
        }
        finally
        {
            if (temporary is not null) TryDelete(temporary);
        }
    }

    // =====================================================================================
    // Discovery
    // =====================================================================================

    /// <summary>Lists backups found under the backup root plus any extra folders.</summary>
    public List<BackupEntry> ListBackups(IEnumerable<string>? extraFolders = null)
    {
        var results = new List<BackupEntry>();
        var roots = new List<string> { BackupRoot };

        if (extraFolders is not null) roots.AddRange(extraFolders);

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    var manifest = ReadManifest(directory);
                    if (manifest is null && !Directory.EnumerateFiles(
                            directory, "*.inf", SearchOption.AllDirectories).Any())
                        continue;

                    results.Add(new BackupEntry
                    {
                        Path = directory,
                        IsArchive = false,
                        Manifest = manifest,
                        SizeBytes = manifest?.SizeBytes ?? MeasureFolder(directory),
                        CreatedLocal = manifest?.CreatedUtc.ToLocalTime() ??
                                       Directory.GetCreationTime(directory)
                    });
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.zip"))
                {
                    var info = new FileInfo(file);
                    results.Add(new BackupEntry
                    {
                        Path = file,
                        IsArchive = true,
                        Manifest = ReadManifestFromArchive(file),
                        SizeBytes = info.Length,
                        CreatedLocal = info.CreationTime
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not list backups in {root}: {ex.Message}");
            }
        }

        return results.OrderByDescending(b => b.CreatedLocal).ToList();
    }

    public void Delete(BackupEntry entry)
    {
        try
        {
            if (entry.IsArchive && File.Exists(entry.Path)) File.Delete(entry.Path);
            else if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, recursive: true);

            Log.Info($"Deleted backup {entry.Name}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete {entry.Path}: {ex.Message}");
        }
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static void WriteManifest(string folder, BackupManifest manifest)
    {
        try
        {
            var path = Path.Combine(folder, ManifestFileName);
            File.WriteAllText(path,
                JsonSerializer.Serialize(manifest, JsonStore.Options),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the backup manifest: {ex.Message}");
        }
    }

    private static BackupManifest? ReadManifest(string folder)
        => JsonStore.Read<BackupManifest>(Path.Combine(folder, ManifestFileName));

    private static BackupManifest? ReadManifestFromArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry(ManifestFileName);
            if (entry is null) return null;

            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, JsonStore.Options);
        }
        catch
        {
            return null;
        }
    }

    private static long MeasureFolder(string folder)
    {
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Best effort only.
        }
    }
}
