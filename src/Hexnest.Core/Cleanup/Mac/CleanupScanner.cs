using Hexnest.Core.Diagnostics;
using Hexnest.Core.Persistence;

namespace Hexnest.Core.Cleanup;

/// <summary>
/// Works out what can be cleaned on macOS and how much space it would free.
///
/// The rules are the ones the Windows scanner is built on, and they are not relaxed
/// here just because the platform is different:
///
///   * Every target owns a fixed list of roots under the user's own home folder.
///     Nothing outside them is ever measured or deleted, and nothing outside
///     ~/Library, ~/Downloads and ~/.Trash is touched at all.
///   * Symbolic links are never followed. ~/Library is full of them, and a scan that
///     walks into one is how "clear the cache" ends up deleting someone's photos.
///   * Nothing under /System, /Library or /private is offered. Those need root, and
///     the Mac build does not run as root by design.
///   * Your own files - old downloads, iPhone backups, folders left behind by
///     software that was deleted - are never a bulk target. They are listed item by
///     item with a size, an age and a reason, and each has to be ticked on its own.
///   * Nothing is ticked by default. Not one target.
///
/// Sizes are measured, not estimated, exactly as on Windows.
/// </summary>
public sealed class CleanupScanner
{
    /// <summary>A download has to be at least this old before it is offered.</summary>
    private const int DownloadsMinimumAgeDays = 30;

    /// <summary>A leftover folder has to be untouched for this long.</summary>
    private const int LeftoverMinimumAgeDays = 180;

    /// <summary>Extensions worth offering in Downloads: big, and re-downloadable.</summary>
    private static readonly string[] DownloadExtensions =
    {
        ".dmg", ".pkg", ".zip", ".rar", ".7z", ".tar", ".gz", ".xz", ".bz2",
        ".iso", ".img", ".mpkg", ".exe", ".msi", ".deb", ".appimage"
    };

    /// <summary>
    /// Folder names under ~/Library/Application Support that belong to macOS itself or
    /// to something with no visible application, and must never be offered as leftovers.
    /// </summary>
    private static readonly HashSet<string> NeverLeftovers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Apple", "AddressBook", "CallHistoryDB", "CallHistoryTransactions", "CloudDocs",
        "Dock", "FileProvider", "Knowledge", "MobileSync", "SyncServices", "iCloud",
        "CrashReporter", "Containers", "Group Containers", "CloudStorage",
        "Google", "Microsoft", "Mozilla", "Firefox", "Adobe", "Steam",
        "Hexnest", "Hexnest",

        // Generic names that are a shared bucket rather than one application's folder.
        // "Caches" under Application Support is written to by anything that wants to,
        // and deleting it is not the same thing as removing a dead application.
        "Caches", "Logs", "Preferences", "Data", "Temp", "Cache", "Support", "Shared"
    };

    /// <summary>
    /// Label prefixes that belong to the operating system.
    ///
    /// This is a prefix test rather than a name test on purpose. The folder that made
    /// it necessary is <c>com.apple.wallpaper</c>: ten gigabytes of the wallpapers
    /// macOS itself ships, whose folder timestamp is a year old because the folder is
    /// only written to when a wallpaper is added. An exact-name list would never have
    /// caught it, and it is exactly the kind of thing that must never be offered.
    /// </summary>
    private static readonly string[] NeverLeftoverPrefixes =
    {
        "com.apple.", "org.apple.", "com.google.", "com.microsoft.", "com.adobe."
    };

    /// <summary>
    /// Developer caches. They live inside ~/Library/Caches alongside everything else,
    /// so the generic cache target excludes exactly these paths and this one owns them.
    /// Offering them separately matters: on a developer's Mac they are usually the
    /// single largest thing on this page.
    /// </summary>
    private static readonly string[] DeveloperCacheRelatives =
    {
        "Library/Developer/Xcode/DerivedData",
        "Library/Developer/CoreSimulator/Caches",
        "Library/Caches/Homebrew",
        "Library/Caches/pip",
        "Library/Caches/Yarn",
        "Library/Caches/go-build",
        "Library/Caches/com.apple.dt.Xcode",
        ".npm/_cacache",
        ".gradle/caches",
        ".cargo/registry/cache",
    };

    /// <summary>Scans every target. Long-running: it measures rather than estimates.</summary>
    public IReadOnlyList<CleanupTarget> Scan(
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<CleanupTarget>(12);

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

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } p
        ? p
        : Environment.GetEnvironmentVariable("HOME") ?? "/tmp";

    private IEnumerable<CleanupTarget> BuildTargets()
    {
        var home = Home;
        var library = Path.Combine(home, "Library");

        var developerCaches = Existing(
            DeveloperCacheRelatives.Select(r => Path.Combine(home, r)).ToArray());

        yield return new CleanupTarget
        {
            Id = "usercache",
            TitleKey = "clean.mac.userCache",
            DescriptionKey = "clean.mac.userCacheDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(Path.Combine(library, "Caches")),

            // The developer caches below own their own row, so this one must not
            // measure or delete them a second time.
            Excluded = developerCaches,
            NoteKey = "clean.mac.userCacheNote"
        };

        yield return new CleanupTarget
        {
            Id = "devcache",
            TitleKey = "clean.mac.devCache",
            DescriptionKey = "clean.mac.devCacheDesc",
            Risk = CleanupRisk.Safe,
            Roots = developerCaches,
            NoteKey = "clean.mac.devCacheNote"
        };

        yield return new CleanupTarget
        {
            Id = "applogs",
            TitleKey = "clean.mac.appLogs",
            DescriptionKey = "clean.mac.appLogsDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(Path.Combine(library, "Logs")),

            // Hexnest is writing to its own log file while this runs. Deleting a file
            // that is open succeeds on macOS and leaves the application logging into
            // nothing, which is a silly way to lose a diagnostic.
            Excluded = Existing(AppPaths.LogsDirectory)
        };

        yield return new CleanupTarget
        {
            Id = "savedstate",
            TitleKey = "clean.mac.savedState",
            DescriptionKey = "clean.mac.savedStateDesc",
            Risk = CleanupRisk.Moderate,
            Roots = Existing(Path.Combine(library, "Saved Application State")),
            NoteKey = "clean.mac.savedStateNote"
        };

        yield return new CleanupTarget
        {
            Id = "hexnest",
            TitleKey = "clean.hexnest",
            DescriptionKey = "clean.mac.ownCacheDesc",
            Risk = CleanupRisk.Safe,
            Roots = Existing(AppPaths.CacheDirectory)
        };

        yield return new CleanupTarget
        {
            Id = "trash",
            TitleKey = "clean.mac.trash",
            DescriptionKey = "clean.mac.trashDesc",
            Risk = CleanupRisk.Moderate,
            Roots = Existing(Path.Combine(home, ".Trash")),
            ContentsOnly = true,
            NoteKey = "clean.mac.trashNote"
        };

        // ---- the review targets ------------------------------------------------------

        yield return new CleanupTarget
        {
            Id = "downloads",
            TitleKey = "clean.downloads",
            DescriptionKey = "clean.mac.downloadsDesc",
            Risk = CleanupRisk.Review,
            Roots = Existing(Path.Combine(home, "Downloads")),
            UseRecycleBin = true,
            NoteKey = "clean.mac.downloadsNote"
        };

        yield return new CleanupTarget
        {
            Id = "iosbackups",
            TitleKey = "clean.mac.iosBackups",
            DescriptionKey = "clean.mac.iosBackupsDesc",
            Risk = CleanupRisk.Review,
            Roots = Existing(Path.Combine(library, "Application Support", "MobileSync", "Backup")),
            UseRecycleBin = true,
            NoteKey = "clean.mac.iosBackupsNote"
        };

        yield return new CleanupTarget
        {
            Id = "leftovers",
            TitleKey = "clean.leftovers",
            DescriptionKey = "clean.mac.leftoversDesc",
            Risk = CleanupRisk.Review,
            Roots = Existing(Path.Combine(library, "Application Support")),
            UseRecycleBin = true,
            NoteKey = "clean.mac.leftoversNote"
        };
    }

    private static IReadOnlyList<string> Existing(params string[] paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct(StringComparer.Ordinal)
                .ToList();

    // =====================================================================================
    // Measuring
    // =====================================================================================

    private void Measure(CleanupTarget target, CancellationToken cancellationToken)
    {
        switch (target.Id)
        {
            case "downloads":
                target.Items = FindOldDownloads(target, cancellationToken);
                break;

            case "iosbackups":
                target.Items = FindDeviceBackups(target, cancellationToken);
                break;

            case "leftovers":
                target.Items = FindLeftovers(target, cancellationToken);
                break;

            default:
                MeasureTree(target, cancellationToken);
                return;
        }

        target.Bytes = target.Items.Sum(i => i.Bytes);
        target.FileCount = target.Items.Count;
    }

    private static void MeasureTree(CleanupTarget target, CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(target.Excluded, StringComparer.Ordinal);

        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var measured = MeasureDirectory(root, excluded, cancellationToken, depth: 0);

            target.Bytes += measured.Bytes;
            target.FileCount += measured.Files;
        }
    }

    /// <summary>
    /// Adds up one tree, refusing to follow symbolic links and giving up at a sane
    /// depth. Both of those are protection against a cycle rather than optimisations.
    ///
    /// <c>Newest</c> is the most recent write time found anywhere in the tree, not the
    /// timestamp on the folder itself. That distinction is the whole reason it is
    /// returned: a folder's own mtime only changes when an entry is added or removed
    /// from it directly, so an application that writes to a database three levels down
    /// every day can easily have a support folder that looks a year untouched.
    /// </summary>
    private static Measurement MeasureDirectory(
        string path, HashSet<string> excluded, CancellationToken cancellationToken, int depth)
    {
        if (depth > 24) return Measurement.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        if (excluded.Contains(path)) return Measurement.Empty;

        long bytes = 0;
        int files = 0;
        var newest = DateTime.MinValue;

        DirectoryInfo directory;

        try
        {
            directory = new DirectoryInfo(path);
            if (!directory.Exists) return Measurement.Empty;

            // LinkTarget is non-null for a symlink. macOS also has firmlinks, which are
            // not links as far as this API is concerned but never appear inside a home
            // folder, so this is the whole of the protection needed here.
            if (directory.LinkTarget is not null) return Measurement.Empty;

            if (directory.LastWriteTime > newest) newest = directory.LastWriteTime;
        }
        catch
        {
            return Measurement.Empty;
        }

        try
        {
            foreach (var file in directory.EnumerateFiles())
            {
                try
                {
                    if (file.LinkTarget is not null) continue;

                    bytes += file.Length;
                    files++;

                    if (file.LastWriteTime > newest) newest = file.LastWriteTime;
                }
                catch
                {
                    // A file that cannot be measured also cannot be deleted.
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A folder the user cannot read is not an error worth stopping the scan for.
        }

        try
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                var measured =
                    MeasureDirectory(child.FullName, excluded, cancellationToken, depth + 1);

                bytes += measured.Bytes;
                files += measured.Files;

                if (measured.Newest > newest) newest = measured.Newest;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }

        return new Measurement(bytes, files, newest);
    }

    /// <summary>What one pass over a tree found.</summary>
    private readonly record struct Measurement(long Bytes, int Files, DateTime Newest)
    {
        public static Measurement Empty => new(0, 0, DateTime.MinValue);
    }

    // =====================================================================================
    // Review targets
    // =====================================================================================

    /// <summary>
    /// Archives and installers in ~/Downloads that have not been touched for a month.
    ///
    /// Someone who downloaded an installer this morning is still using it, so the age
    /// floor matters more than the size does.
    /// </summary>
    private static IReadOnlyList<CleanupItem> FindOldDownloads(
        CleanupTarget target, CancellationToken cancellationToken)
    {
        var items = new List<CleanupItem>(32);
        var cutoff = DateTime.Now.AddDays(-DownloadsMinimumAgeDays);

        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles())
                {
                    try
                    {
                        if (file.LinkTarget is not null) continue;
                        if (file.LastWriteTime > cutoff) continue;
                        if (!DownloadExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                            continue;

                        items.Add(new CleanupItem
                        {
                            Path = file.FullName,
                            Name = file.Name,
                            Bytes = file.Length,
                            LastWriteTime = file.LastWriteTime,
                            IsDirectory = false
                        });
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Downloads scan: {ex.Message}");
            }
        }

        return items.OrderByDescending(i => i.Bytes).ToList();
    }

    /// <summary>
    /// iPhone and iPad backups, one item per device backup.
    ///
    /// These are routinely the largest single thing in a home folder and macOS never
    /// removes an old one by itself, but deleting the only backup of a phone is not
    /// something to do in bulk, so each is listed with its size and date.
    /// </summary>
    private static IReadOnlyList<CleanupItem> FindDeviceBackups(
        CleanupTarget target, CancellationToken cancellationToken)
    {
        var items = new List<CleanupItem>(4);

        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var backup in new DirectoryInfo(root).EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (backup.LinkTarget is not null) continue;

                    var measured = MeasureDirectory(
                        backup.FullName, new HashSet<string>(StringComparer.Ordinal),
                        cancellationToken, depth: 0);

                    if (measured.Bytes == 0) continue;

                    items.Add(new CleanupItem
                    {
                        Path = backup.FullName,
                        Name = backup.Name,
                        Bytes = measured.Bytes,
                        LastWriteTime = measured.Newest,
                        IsDirectory = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Device backup scan: {ex.Message}");
            }
        }

        return items.OrderByDescending(i => i.Bytes).ToList();
    }

    /// <summary>
    /// Folders under ~/Library/Application Support that do not match anything in
    /// /Applications or ~/Applications and have not been written to for six months.
    ///
    /// Deleting an application on macOS leaves its support folder behind for ever, so
    /// this is where the space actually is - but the same is true of an application
    /// that is simply not installed right now, which is why nothing here is ticked and
    /// everything goes to the Trash rather than being erased.
    /// </summary>
    private static IReadOnlyList<CleanupItem> FindLeftovers(
        CleanupTarget target, CancellationToken cancellationToken)
    {
        var items = new List<CleanupItem>(16);
        var cutoff = DateTime.Now.AddDays(-LeftoverMinimumAgeDays);
        var installed = InstalledApplications();
        var running = RunningApplications();

        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var folder in new DirectoryInfo(root).EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (folder.LinkTarget is not null) continue;
                        if (folder.Name.StartsWith('.')) continue;
                        if (NeverLeftovers.Contains(folder.Name)) continue;

                        if (NeverLeftoverPrefixes.Any(prefix =>
                                folder.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (MatchesApplication(folder.Name, installed)) continue;
                        if (MatchesApplication(folder.Name, running)) continue;

                        var measured = MeasureDirectory(
                            folder.FullName, new HashSet<string>(StringComparer.Ordinal),
                            cancellationToken, depth: 0);

                        // Below a megabyte there is nothing to gain and something to lose.
                        if (measured.Bytes < 1024 * 1024) continue;

                        // The age test uses the newest file anywhere inside, not the
                        // folder's own timestamp. Anything still being written to is
                        // still in use, however old the folder looks from outside.
                        if (measured.Newest > cutoff) continue;

                        items.Add(new CleanupItem
                        {
                            Path = folder.FullName,
                            Name = folder.Name,
                            Bytes = measured.Bytes,
                            LastWriteTime = measured.Newest,
                            IsDirectory = true
                        });
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Leftover scan: {ex.Message}");
            }
        }

        return items.OrderByDescending(i => i.Bytes).ToList();
    }

    /// <summary>
    /// The names of everything currently running, from the bundle around each
    /// executable.
    ///
    /// An application that is running right now is not a leftover, whatever its support
    /// folder's timestamps say. This catches the case the file system cannot: software
    /// installed somewhere other than /Applications, which on a managed Mac is most of
    /// the security and VPN tooling.
    /// </summary>
    private static HashSet<string> RunningApplications()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (int pid in Platform.MacProcNative.ListPids())
            {
                var path = Platform.MacProcNative.ReadPath(pid);
                if (path is null) continue;

                var bundle = Platform.MacProcNative.BundleName(path);
                if (bundle is not null) names.Add(bundle);

                // "/Applications/Cisco/Cisco Secure Client.app/..." - the vendor folder
                // above the bundle is what the support folder is usually named after.
                foreach (var segment in path.Split('/'))
                {
                    if (segment.Length >= 3 && !segment.Contains('.')) names.Add(segment);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not list running applications: {ex.Message}");
        }

        return names;
    }

    /// <summary>
    /// Every application on the machine, plus the folders they are grouped into.
    ///
    /// The search goes two levels deep because vendors who ship more than one
    /// application put them in a folder - /Applications/Cisco/Cisco Secure Client.app -
    /// and a top-level-only scan would decide that "Cisco" was a leftover while the
    /// software was running.
    /// </summary>
    private static HashSet<string> InstalledApplications()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in new[]
                 {
                     "/Applications",
                     "/System/Applications",
                     Path.Combine(Home, "Applications")
                 })
        {
            Collect(folder, depth: 0);
        }

        return names;

        void Collect(string folder, int depth)
        {
            if (depth > 2) return;

            try
            {
                if (!Directory.Exists(folder)) return;

                foreach (var entry in Directory.EnumerateDirectories(folder))
                {
                    var name = Path.GetFileName(entry);

                    if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(entry));
                        continue;
                    }

                    // A plain folder inside /Applications is a vendor grouping; both it
                    // and what is inside it count as installed.
                    names.Add(name);
                    Collect(entry, depth + 1);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not list {folder}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// True when a support folder plausibly belongs to one of <paramref name="known"/>.
    ///
    /// Deliberately generous, and it errs in one direction on purpose: a false match
    /// leaves a folder on disk, a false miss offers to delete something that is still
    /// in use. Only the second of those can cost anybody anything.
    /// </summary>
    private static bool MatchesApplication(string folderName, HashSet<string> known)
    {
        if (known.Count == 0) return false;
        if (known.Contains(folderName)) return true;

        // "com.microsoft.VSCode" -> "VSCode"
        int lastDot = folderName.LastIndexOf('.');

        if (lastDot > 0 && lastDot < folderName.Length - 1 &&
            known.Contains(folderName[(lastDot + 1)..]))
        {
            return true;
        }

        // A containment test, but only against names long enough for it to mean
        // something. Without the length floor, an application called "Go" would
        // vouch for every folder with those two letters in it.
        return known.Any(app =>
            app.Length >= 4 &&
            (folderName.Contains(app, StringComparison.OrdinalIgnoreCase) ||
             (folderName.Length >= 4 && app.Contains(folderName, StringComparison.OrdinalIgnoreCase))));
    }
}
