using Microsoft.Win32;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Persistence;

namespace DrvNest.Core.Cleanup;

/// <summary>
/// Works out what can be cleaned and how much space it would free.
///
/// The whole design of this class is the answer to one question: how do you write a disk
/// cleaner that cannot destroy somebody's machine?
///
///   * Every target owns a fixed list of roots, computed from the well-known folder APIs
///     rather than from strings. Nothing is ever deleted outside them.
///   * Reparse points are never followed. %LOCALAPPDATA% is full of junctions, and a scan
///     that walks into one is how "clear the cache" ends up deleting someone's documents.
///   * Anything that is a user's own file - the Downloads folder, folders left behind by
///     software that was uninstalled - is never a bulk target. Those are listed item by
///     item with a size, an age and a reason, and each has to be ticked individually.
///   * Nothing is ticked by default. Not one target. The user chooses.
///
/// Sizes are measured, not estimated. That makes a full scan slower than the tools that
/// guess, and it means the number next to the checkbox is the number of bytes that will
/// actually come back.
/// </summary>
public sealed class CleanupScanner
{
    /// <summary>
    /// A file in Downloads has to be at least this old before it is offered. Someone who
    /// downloaded an installer this morning is still using it.
    /// </summary>
    private const int DownloadsMinimumAgeDays = 30;

    /// <summary>
    /// A leftover folder has to be untouched for this long. Plenty of applications write
    /// to their data folder only occasionally, so a short window would offer folders that
    /// are still in use.
    /// </summary>
    private const int LeftoverMinimumAgeDays = 180;

    /// <summary>Extensions worth offering in Downloads: big, and re-downloadable.</summary>
    private static readonly string[] DownloadExtensions =
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".img",
        ".exe", ".msi", ".msix", ".appx", ".dmg", ".pkg", ".cab"
    };

    /// <summary>
    /// Folder names under %APPDATA% and friends that belong to Windows or to something
    /// that has no uninstall entry, and must never be offered as a leftover.
    /// </summary>
    private static readonly HashSet<string> NeverLeftovers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "WindowsApps", "Packages", "Temp", "TempState",
        "Programs", "CrashDumps", "ConnectedDevicesPlatform", "Comms", "D3DSCache",
        "IconCache.db", "PlaceholderTileLogoFolder", "VirtualStore", "ElevatedDiagnostics",
        "Google", "Mozilla", "Apple Computer", "Adobe", "NVIDIA", "NVIDIA Corporation",
        "Intel", "AMD", "Package Cache", "Application Data", "History", "Publishers",
        "DrvNest"
    };

    /// <summary>Browser cache folders, relative to %LOCALAPPDATA% or %APPDATA%.</summary>
    private static readonly (string Local, string Relative, string Browser)[] BrowserCaches =
    {
        ("local", @"Google\Chrome\User Data\Default\Cache",              "Chrome"),
        ("local", @"Google\Chrome\User Data\Default\Code Cache",         "Chrome"),
        ("local", @"Google\Chrome\User Data\Default\GPUCache",           "Chrome"),
        ("local", @"Microsoft\Edge\User Data\Default\Cache",             "Edge"),
        ("local", @"Microsoft\Edge\User Data\Default\Code Cache",        "Edge"),
        ("local", @"Microsoft\Edge\User Data\Default\GPUCache",          "Edge"),
        ("local", @"BraveSoftware\Brave-Browser\User Data\Default\Cache", "Brave"),
        ("local", @"Vivaldi\User Data\Default\Cache",                    "Vivaldi"),
        ("local", @"Opera Software\Opera Stable\Cache",                  "Opera"),
        ("local", @"Mozilla\Firefox\Profiles",                           "Firefox"),
        ("local", @"Yandex\YandexBrowser\User Data\Default\Cache",       "Yandex"),
    };

    /// <summary>
    /// Scans every target. Long-running: it measures rather than estimates.
    /// </summary>
    public IReadOnlyList<CleanupTarget> Scan(
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<CleanupTarget>(16);

        foreach (var target in BuildTargets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(target.Id);

            try
            {
                Measure(target, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                target.ScanWarning = ex.Message;
                Log.Debug($"Cleanup scan of '{target.Id}' failed: {ex.Message}");
            }

            targets.Add(target);
        }

        long total = targets.Sum(t => t.Bytes);
        Log.Info($"Cleanup scan: {targets.Count} targets, {Models.Formatting.HumanBytes(total)} reclaimable.");

        return targets;
    }

    // =====================================================================================
    // Target definitions
    // =====================================================================================

    private IEnumerable<CleanupTarget> BuildTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return new CleanupTarget
        {
            Id = "temp",
            TitleKey = "clean.temp",
            DescriptionKey = "clean.tempDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(Path.GetTempPath(), Path.Combine(windows, "Temp")),
            RequiresElevation = true
        };

        yield return new CleanupTarget
        {
            Id = "thumbnails",
            TitleKey = "clean.thumbnails",
            DescriptionKey = "clean.thumbnailsDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(Path.Combine(local, @"Microsoft\Windows\Explorer")),
            NoteKey = "clean.thumbnailsNote"
        };

        yield return new CleanupTarget
        {
            Id = "browser",
            TitleKey = "clean.browser",
            DescriptionKey = "clean.browserDesc",
            Risk = CleanupRisk.Moderate,
            Roots = BrowserCacheRoots(local, roaming),
            NoteKey = "clean.browserNote"
        };

        yield return new CleanupTarget
        {
            Id = "wincache",
            TitleKey = "clean.windowsUpdate",
            DescriptionKey = "clean.windowsUpdateDesc",
            Risk = CleanupRisk.Moderate,
            Roots = Existing(Path.Combine(windows, @"SoftwareDistribution\Download")),
            RequiresElevation = true,
            NoteKey = "clean.windowsUpdateNote"
        };

        yield return new CleanupTarget
        {
            Id = "delivery",
            TitleKey = "clean.delivery",
            DescriptionKey = "clean.deliveryDesc",
            Risk = CleanupRisk.Safe,
            // Which of the two Windows uses depends on the build, so both are listed
            // and Existing() keeps whichever is actually there.
            Roots = Existing(
                Path.Combine(windows, @"SoftwareDistribution\DeliveryOptimization"),
                Path.Combine(windows,
                    @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization")),
            RequiresElevation = true
        };

        yield return new CleanupTarget
        {
            Id = "dumps",
            TitleKey = "clean.dumps",
            DescriptionKey = "clean.dumpsDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(
                Path.Combine(local, "CrashDumps"),
                Path.Combine(windows, "Minidump")),
            RequiresElevation = true
        };

        yield return new CleanupTarget
        {
            Id = "wer",
            TitleKey = "clean.wer",
            DescriptionKey = "clean.werDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(
                Path.Combine(local, @"Microsoft\Windows\WER"),
                Path.Combine(programData, @"Microsoft\Windows\WER")),
            RequiresElevation = true
        };

        yield return new CleanupTarget
        {
            Id = "shader",
            TitleKey = "clean.shader",
            DescriptionKey = "clean.shaderDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, @"NVIDIA\DXCache"),
                Path.Combine(local, @"NVIDIA\GLCache"),
                Path.Combine(local, @"AMD\DxCache"),
                Path.Combine(local, @"Intel\ShaderCache"))
        };

        yield return new CleanupTarget
        {
            Id = "logs",
            TitleKey = "clean.logs",
            DescriptionKey = "clean.logsDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(
                Path.Combine(windows, "Logs", "CBS"),
                Path.Combine(windows, "Logs", "DISM"),
                Path.Combine(windows, "Logs", "WindowsUpdate")),
            RequiresElevation = true
        };

        yield return new CleanupTarget
        {
            Id = "drvnest",
            TitleKey = "clean.drvnest",
            DescriptionKey = "clean.drvnestDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(AppPaths.CacheDirectory)
        };

        yield return new CleanupTarget
        {
            Id = "recyclebin",
            TitleKey = "clean.recycleBin",
            DescriptionKey = "clean.recycleBinDesc",
            Risk = CleanupRisk.Moderate,
            NoteKey = "clean.recycleBinNote"
        };

        // ---- the two review targets --------------------------------------------------

        yield return new CleanupTarget
        {
            Id = "downloads",
            TitleKey = "clean.downloads",
            DescriptionKey = "clean.downloadsDesc",
            Risk = CleanupRisk.Review,
            Roots = Existing(DownloadsFolder(profile)),
            UseRecycleBin = true,
            NoteKey = "clean.downloadsNote"
        };

        yield return new CleanupTarget
        {
            Id = "leftovers",
            TitleKey = "clean.leftovers",
            DescriptionKey = "clean.leftoversDesc",
            Risk = CleanupRisk.Review,
            Roots = Existing(local, roaming),
            UseRecycleBin = true,
            NoteKey = "clean.leftoversNote"
        };
    }

    private static IReadOnlyList<string> Existing(params string[] paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static IReadOnlyList<string> BrowserCacheRoots(string local, string roaming)
    {
        var roots = new List<string>(8);

        foreach (var (which, relative, _) in BrowserCaches)
        {
            var full = Path.Combine(which == "local" ? local : roaming, relative);

            // Firefox keeps its cache under a randomly named profile folder.
            if (relative.EndsWith("Profiles", StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(full)) continue;

                foreach (var profile in SafeDirectories(full))
                {
                    var cache = Path.Combine(profile, "cache2");
                    if (Directory.Exists(cache)) roots.Add(cache);
                }

                continue;
            }

            if (Directory.Exists(full)) roots.Add(full);
        }

        return roots;
    }

    private static string DownloadsFolder(string profile)
    {
        // The Downloads folder can be redirected, and there is no SpecialFolder for it.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");

            if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string raw &&
                !string.IsNullOrWhiteSpace(raw))
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw);
                if (Directory.Exists(expanded)) return expanded;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read the Downloads folder location: {ex.Message}");
        }

        return Path.Combine(profile, "Downloads");
    }

    // =====================================================================================
    // Measuring
    // =====================================================================================

    private void Measure(CleanupTarget target, CancellationToken cancellationToken)
    {
        switch (target.Id)
        {
            case "recyclebin":
                MeasureRecycleBin(target);
                return;

            case "downloads":
                target.Items = FindOldDownloads(target, cancellationToken);
                break;

            case "leftovers":
                target.Items = FindLeftovers(target, cancellationToken);
                break;

            case "thumbnails":
                // Only the cache databases, not the whole Explorer folder, which holds
                // real settings alongside them.
                MeasureFiles(target, "thumbcache_*.db", cancellationToken);
                MeasureFiles(target, "iconcache_*.db", cancellationToken);
                return;

            default:
                MeasureTree(target, cancellationToken);
                return;
        }

        target.Bytes = target.Items.Sum(i => i.Bytes);
        target.FileCount = target.Items.Count;
    }

    private static void MeasureRecycleBin(CleanupTarget target)
    {
        var info = new CleanupInterop.ShQueryRbInfo
        {
            StructSize = System.Runtime.InteropServices.Marshal.SizeOf<CleanupInterop.ShQueryRbInfo>()
        };

        if (CleanupInterop.SHQueryRecycleBin(null, ref info) == 0)
        {
            target.Bytes = info.BinSize;
            target.FileCount = (int)Math.Min(int.MaxValue, info.ItemCount);
        }
    }

    private void MeasureFiles(CleanupTarget target, string pattern, CancellationToken cancellationToken)
    {
        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in SafeFiles(root, pattern))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;

                    target.Bytes += info.Length;
                    target.FileCount++;
                }
                catch
                {
                    // A file that cannot be measured also cannot be deleted; skip it.
                }
            }
        }
    }

    private void MeasureTree(CleanupTarget target, CancellationToken cancellationToken)
    {
        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (bytes, count, _) = MeasureDirectory(root, cancellationToken, depth: 0);

            target.Bytes += bytes;
            target.FileCount += count;
        }
    }

    /// <summary>
    /// Recursive size of a directory, never following a reparse point and never going
    /// deeper than a sane limit - a junction loop would otherwise recurse forever.
    /// </summary>
    private static (long Bytes, int Count, DateTime Newest) MeasureDirectory(
        string path, CancellationToken cancellationToken, int depth)
    {
        const int MaxDepth = 24;

        if (depth > MaxDepth) return (0, 0, DateTime.MinValue);

        long bytes = 0;
        int count = 0;
        var newest = DateTime.MinValue;

        try
        {
            var info = new DirectoryInfo(path);

            if (!info.Exists || info.Attributes.HasFlag(CleanupInterop.ReparsePoint))
                return (0, 0, DateTime.MinValue);

            foreach (var file in info.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (file.Attributes.HasFlag(CleanupInterop.ReparsePoint)) continue;

                    bytes += file.Length;
                    count++;

                    if (file.LastWriteTime > newest) newest = file.LastWriteTime;
                }
                catch
                {
                    // Deleted between the enumeration and the read; nothing to count.
                }
            }

            foreach (var child in info.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (childBytes, childCount, childNewest) =
                    MeasureDirectory(child.FullName, cancellationToken, depth + 1);

                bytes += childBytes;
                count += childCount;

                if (childNewest > newest) newest = childNewest;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Access denied on a subtree is normal and not worth a log line per folder.
        }

        return (bytes, count, newest);
    }

    // =====================================================================================
    // Review targets
    // =====================================================================================

    private IReadOnlyList<CleanupItem> FindOldDownloads(
        CleanupTarget target, CancellationToken cancellationToken)
    {
        var items = new List<CleanupItem>(32);
        var cutoff = DateTime.Now.AddDays(-DownloadsMinimumAgeDays);

        foreach (var root in target.Roots)
        {
            foreach (var file in SafeFiles(root, "*"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);

                    if (!info.Exists) continue;
                    if (info.Attributes.HasFlag(CleanupInterop.ReparsePoint)) continue;
                    if (info.LastWriteTime > cutoff) continue;

                    if (!DownloadExtensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    items.Add(new CleanupItem
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Bytes = info.Length,
                        LastWriteTime = info.LastWriteTime,
                        IsDirectory = false
                    });
                }
                catch
                {
                    // Unreadable file; not a candidate.
                }
            }
        }

        items.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return items;
    }

    /// <summary>
    /// Folders under %LOCALAPPDATA% and %APPDATA% that look like they belong to software
    /// that is no longer installed.
    ///
    /// "Look like" is doing real work in that sentence, which is why these are never
    /// bulk-selected. The test is deliberately conservative: the folder must not match any
    /// installed program's name or publisher, must not be one of the folders Windows owns,
    /// must not have been written to in six months, and must not contain a running
    /// program's executable. Even then it is offered, not deleted, with its age and size
    /// on the row - and it goes to the Recycle Bin, not into the void.
    /// </summary>
    private IReadOnlyList<CleanupItem> FindLeftovers(
        CleanupTarget target, CancellationToken cancellationToken)
    {
        var installed = InstalledProgramNames();
        var cutoff = DateTime.Now.AddDays(-LeftoverMinimumAgeDays);

        var items = new List<CleanupItem>(16);

        foreach (var root in target.Roots)
        {
            foreach (var directory in SafeDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new DirectoryInfo(directory);

                    if (info.Attributes.HasFlag(CleanupInterop.ReparsePoint)) continue;
                    if (NeverLeftovers.Contains(info.Name)) continue;

                    // A folder whose name matches any installed program, publisher or
                    // running process belongs to software that is still here.
                    var normalized = Normalize(info.Name);
                    if (normalized.Length < 3) continue;
                    if (installed.Any(name => Matches(normalized, name))) continue;

                    var (bytes, count, newest) = MeasureDirectory(directory, cancellationToken, depth: 0);

                    // The folder's own timestamp does not move when a file several levels
                    // down is written, so the age test uses the newest file in the tree.
                    var lastTouched = newest > info.LastWriteTime ? newest : info.LastWriteTime;
                    if (lastTouched > cutoff) continue;

                    // Not worth a row, and not worth the risk, for a few kilobytes.
                    if (bytes < 5L * 1024 * 1024 || count == 0) continue;

                    items.Add(new CleanupItem
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Bytes = bytes,
                        LastWriteTime = lastTouched,
                        IsDirectory = true
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Unreadable folder; not a candidate.
                }
            }
        }

        items.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return items;
    }

    /// <summary>
    /// Strips everything that is not a letter or a digit and lower-cases the rest, so
    /// "riot-client-ux", "Riot Client" and "RiotClient" all reduce to the same shape.
    /// </summary>
    private static string Normalize(string value)
    {
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        int length = 0;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// True when a normalised folder name looks like it belongs to <paramref name="program"/>.
    ///
    /// Prefix matching in both directions, which is what actually happens in practice:
    /// "riotclientux" starts with the installed "riotclient", and "notion" is a prefix of
    /// the installed "notion310". A four character floor stops a short program name from
    /// matching half the folders on the disk.
    /// </summary>
    private static bool Matches(string normalizedFolder, string program)
    {
        var normalizedProgram = Normalize(program);

        if (normalizedProgram.Length < 4) return false;

        return normalizedFolder.StartsWith(normalizedProgram, StringComparison.Ordinal) ||
               normalizedProgram.StartsWith(normalizedFolder, StringComparison.Ordinal) ||
               normalizedProgram.Contains(normalizedFolder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every installed program's display name and publisher, from the three uninstall
    /// keys Windows actually uses.
    /// </summary>
    private static IReadOnlyList<string> InstalledProgramNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sources = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, path) in sources)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key is null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub is null) continue;

                    foreach (var value in new[] { "DisplayName", "Publisher" })
                    {
                        if (sub.GetValue(value) is string text && text.Trim().Length > 2)
                            names.Add(text.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not read {path}: {ex.Message}");
            }
        }

        // A running program is installed, whatever the registry says. This catches
        // launchers and updaters that never register an uninstall entry at all.
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                try { names.Add(process.ProcessName); }
                catch { /* Exited between the enumeration and the read. */ }
                finally { process.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not enumerate processes for the leftover check: {ex.Message}");
        }

        // Program Files folder names catch portable software with no uninstall entry.
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(
                         Environment.SpecialFolder.LocalApplicationData), "Programs")
                 })
        {
            foreach (var directory in SafeDirectories(folder))
                names.Add(Path.GetFileName(directory));
        }

        return names.ToList();
    }

    // =====================================================================================
    // Enumeration that never throws
    // =====================================================================================

    internal static IEnumerable<string> SafeDirectories(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Array.Empty<string>();

        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static IEnumerable<string> SafeFiles(string root, string pattern)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
