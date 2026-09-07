using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Cleanup;

/// <summary>
/// Deletes what the user ticked, and nothing else.
///
/// Every path this class touches comes from a <see cref="CleanupTarget"/> the scanner
/// built out of the user's own home folder. Nothing is constructed from a string here,
/// and <see cref="IsInsideRoots"/> re-checks every single deletion against the target's
/// own roots before it happens - a redundant check on purpose, because the cost of a
/// bug in this file is somebody's data.
///
/// Two macOS specifics are worth knowing about.
///
///   Unlike Windows, macOS will happily delete a file another process has open. That
///   removes the "file in use" safety net the Windows cleaner leans on, so the roots
///   are drawn tighter here instead: caches, logs, the Trash, and nothing else.
///
///   "Send to the Trash" is a move, not an API call. Finder does the same thing:
///   rename the item into ~/.Trash, adding a suffix if something of that name is
///   already there.
/// </summary>
public sealed class CleanupService
{
    /// <summary>
    /// Deletes the selected targets. <paramref name="selectedItems"/> carries the
    /// individually ticked paths for the review targets; a review target with no entry
    /// there is skipped entirely.
    /// </summary>
    public CleanupReport Clean(
        IReadOnlyList<CleanupTarget> targets,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? selectedItems = null,
        Action<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new CleanupReport();

        for (int i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = targets[i];

            try
            {
                if (target.Risk == CleanupRisk.Review)
                    CleanItems(target, selectedItems, report, cancellationToken);
                else
                    CleanTree(target, report, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Failures[target.Id] = ex.Message;
                Log.Warn($"Cleanup of '{target.Id}' failed: {ex.Message}");
            }

            progress?.Invoke(new CleanupProgress(target.Id, i + 1, targets.Count, report.BytesFreed));
        }

        Log.Info($"Cleanup finished: {Models.Formatting.HumanBytes(report.BytesFreed)} freed, " +
                 $"{report.FilesDeleted} deleted, {report.FilesSkipped} skipped.");

        return report;
    }

    // =====================================================================================
    // Bulk targets
    // =====================================================================================

    private static void CleanTree(CleanupTarget target, CleanupReport report, CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(target.Excluded, StringComparer.Ordinal);

        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ContentsOnly is the default and the safe answer: macOS recreates most of
            // these folders, but not all of them, and removing one the system expects
            // to exist is a way to break something quietly.
            DeleteContents(root, target, excluded, report, cancellationToken, depth: 0);
        }
    }

    private static void DeleteContents(
        string path,
        CleanupTarget target,
        HashSet<string> excluded,
        CleanupReport report,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 24) return;

        cancellationToken.ThrowIfCancellationRequested();

        if (excluded.Contains(path)) return;
        if (!IsInsideRoots(path, target)) return;

        DirectoryInfo directory;

        try
        {
            directory = new DirectoryInfo(path);
            if (!directory.Exists || directory.LinkTarget is not null) return;
        }
        catch
        {
            return;
        }

        try
        {
            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteFile(file, target, report);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            report.FilesSkipped++;
        }

        try
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (excluded.Contains(child.FullName)) continue;

                // A symlink is removed as a link; its target is somebody else's data.
                if (child.LinkTarget is not null)
                {
                    TryDeleteLink(child, report);
                    continue;
                }

                DeleteContents(child.FullName, target, excluded, report, cancellationToken, depth + 1);

                try
                {
                    // Only ever removes a folder this pass has just emptied.
                    if (!child.EnumerateFileSystemInfos().Any()) child.Delete(recursive: false);
                }
                catch
                {
                    report.FilesSkipped++;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            report.FilesSkipped++;
        }
    }

    private static void DeleteFile(FileInfo file, CleanupTarget target, CleanupReport report)
    {
        try
        {
            if (!IsInsideRoots(file.FullName, target))
            {
                report.FilesSkipped++;
                return;
            }

            long size = file.LinkTarget is null ? file.Length : 0;

            file.Delete();

            report.BytesFreed += size;
            report.FilesDeleted++;
        }
        catch
        {
            report.FilesSkipped++;
        }
    }

    private static void TryDeleteLink(DirectoryInfo link, CleanupReport report)
    {
        try
        {
            link.Delete(recursive: false);
            report.FilesDeleted++;
        }
        catch
        {
            report.FilesSkipped++;
        }
    }

    // =====================================================================================
    // Review targets
    // =====================================================================================

    /// <summary>
    /// Removes only the paths the user ticked, and only those that are still inside the
    /// target's roots.
    /// </summary>
    private static void CleanItems(
        CleanupTarget target,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? selectedItems,
        CleanupReport report,
        CancellationToken cancellationToken)
    {
        if (selectedItems is null ||
            !selectedItems.TryGetValue(target.Id, out var selected) ||
            selected.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(target.Items.Select(i => i.Path), StringComparer.Ordinal);

        foreach (var path in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The path has to be one the scan actually offered, as well as being inside
            // the roots. Two independent checks, because this is the review target: the
            // one that touches somebody's own files.
            if (!known.Contains(path) || !IsInsideRoots(path, target))
            {
                report.FilesSkipped++;
                continue;
            }

            try
            {
                long size = SizeOf(path);

                if (target.UseRecycleBin) MoveToTrash(path);
                else DeletePermanently(path);

                report.BytesFreed += size;
                report.FilesDeleted++;
            }
            catch (Exception ex)
            {
                report.FilesSkipped++;
                report.Failures.TryAdd(target.Id, ex.Message);
                Log.Debug($"Could not remove '{path}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// How much a reviewed item is worth, counted the same way the scanner counted it.
    ///
    /// Walks the tree by hand rather than with SearchOption.AllDirectories, because
    /// that option descends *through* directory symlinks. The scanner deliberately does
    /// not - a link inside a leftover folder points at somebody else's data, and the
    /// move to the Trash takes the link, not its target. Counting the target's bytes
    /// here would report gigabytes freed that were never touched, and would walk a
    /// foreign tree while doing it.
    /// </summary>
    private static long SizeOf(string path, int depth = 0)
    {
        if (depth > 24) return 0;

        try
        {
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return file.LinkTarget is null ? file.Length : 0;
            }

            var directory = new DirectoryInfo(path);
            if (!directory.Exists || directory.LinkTarget is not null) return 0;

            long total = 0;

            foreach (var child in directory.EnumerateFiles())
            {
                try
                {
                    if (child.LinkTarget is null) total += child.Length;
                }
                catch
                {
                    // A file that cannot be measured also cannot be deleted.
                }
            }

            foreach (var child in directory.EnumerateDirectories())
            {
                if (child.LinkTarget is null) total += SizeOf(child.FullName, depth + 1);
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static void DeletePermanently(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Moves an item into ~/.Trash, which is what "send to the Trash" is on macOS.
    ///
    /// A move rather than a copy, so it is instant and cannot half-succeed on a large
    /// backup. If the Trash already holds something of that name, a numeric suffix is
    /// added - the same thing Finder does, and the reason it never silently overwrites.
    /// </summary>
    private static void MoveToTrash(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("HOME") ?? throw new IOException("No home folder.");

        var trash = Path.Combine(home, ".Trash");
        Directory.CreateDirectory(trash);

        var name = Path.GetFileName(path.TrimEnd('/'));
        var destination = Path.Combine(trash, name);

        for (int attempt = 2; Exists(destination) && attempt < 1000; attempt++)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            destination = Path.Combine(trash, $"{stem} {attempt}{extension}");
        }

        if (Directory.Exists(path)) Directory.Move(path, destination);
        else File.Move(path, destination);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    // =====================================================================================
    // The guard
    // =====================================================================================

    /// <summary>
    /// True only when <paramref name="path"/> really is inside one of the target's own
    /// roots.
    ///
    /// Compared on the resolved full path, so "..", a trailing slash and a doubled
    /// separator cannot walk out of a root. This is checked again immediately before
    /// every deletion even though the scanner already guaranteed it, because the two
    /// are far enough apart in time for the answer to have changed.
    /// </summary>
    private static bool IsInsideRoots(string path, CleanupTarget target)
    {
        if (target.Roots.Count == 0) return false;

        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        foreach (var root in target.Roots)
        {
            string fullRoot;

            try
            {
                fullRoot = Path.GetFullPath(root).TrimEnd('/');
            }
            catch
            {
                continue;
            }

            if (fullRoot.Length == 0) continue;

            if (full.Equals(fullRoot, StringComparison.Ordinal)) return true;
            if (full.StartsWith(fullRoot + "/", StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
