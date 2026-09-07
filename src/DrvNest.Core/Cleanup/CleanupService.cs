using System.Runtime.InteropServices;
using DrvNest.Core.Diagnostics;

namespace DrvNest.Core.Cleanup;

/// <summary>
/// Deletes what the user ticked, and nothing else.
///
/// Every path this class touches comes from a <see cref="CleanupTarget"/> the scanner
/// built from the well-known folder APIs. Nothing is constructed from a string here, and
/// <see cref="IsInsideRoots"/> re-checks every single deletion against the target's own
/// roots before it happens - a redundant check on purpose, because the cost of a bug in
/// this file is somebody's data.
///
/// Files that are open stay where they are. A cleaner that fights the operating system
/// for a locked file is a cleaner that eventually wins against something it should have
/// lost to; the count of skipped files is reported instead.
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
                if (target.Id == "recyclebin") EmptyRecycleBin(report);
                else if (target.Risk == CleanupRisk.Review) CleanItems(target, selectedItems, report, cancellationToken);
                else CleanTree(target, report, cancellationToken);
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

    private void CleanTree(CleanupTarget target, CleanupReport report, CancellationToken cancellationToken)
    {
        foreach (var root in target.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target.Id == "thumbnails")
            {
                DeleteMatching(root, "thumbcache_*.db", target, report);
                DeleteMatching(root, "iconcache_*.db", target, report);
                continue;
            }

            DeleteContents(root, target, report, cancellationToken, depth: 0);

            // Most of these folders are recreated by whatever owns them, but not all of
            // them are, and removing one Windows expects to exist breaks things quietly.
            // Only the contents ever go.
        }
    }

    private void DeleteMatching(string root, string pattern, CleanupTarget target, CleanupReport report)
    {
        foreach (var file in CleanupScanner.SafeFiles(root, pattern))
            DeleteFile(file, target, report);
    }

    private void DeleteContents(
        string directory, CleanupTarget target, CleanupReport report,
        CancellationToken cancellationToken, int depth)
    {
        const int MaxDepth = 24;

        if (depth > MaxDepth) return;

        DirectoryInfo info;

        try
        {
            info = new DirectoryInfo(directory);
            if (!info.Exists || info.Attributes.HasFlag(CleanupInterop.ReparsePoint)) return;
        }
        catch
        {
            return;
        }

        try
        {
            foreach (var file in info.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteFile(file.FullName, target, report);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Access denied on a folder is normal; the counters record the skips. */ }

        try
        {
            foreach (var child in info.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.Attributes.HasFlag(CleanupInterop.ReparsePoint)) continue;

                DeleteContents(child.FullName, target, report, cancellationToken, depth + 1);

                // Remove the now-empty subfolder, but never the root itself.
                try
                {
                    if (!child.EnumerateFileSystemInfos().Any()) child.Delete();
                }
                catch
                {
                    // Still has something in it, or is locked. Leaving it is correct.
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* As above. */ }
    }

    private void DeleteFile(string path, CleanupTarget target, CleanupReport report)
    {
        if (!IsInsideRoots(path, target))
        {
            // Should be impossible. If it ever happens, it is a bug worth a loud line.
            Log.Error($"Refusing to delete '{path}': outside the '{target.Id}' roots.");
            report.FilesSkipped++;
            return;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;

            long size = info.Length;

            // A read-only cache file is still a cache file.
            if (info.IsReadOnly) info.IsReadOnly = false;

            info.Delete();

            report.BytesFreed += size;
            report.FilesDeleted++;
        }
        catch (Exception)
        {
            // In use, or protected. Both are reasons to leave it alone.
            report.FilesSkipped++;
        }
    }

    // =====================================================================================
    // Review targets
    // =====================================================================================

    private void CleanItems(
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

        // Only paths the scan actually offered. A path that arrives here without a
        // matching item did not come from the scan.
        var known = target.Items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);

        var batch = new List<string>(selected.Count);
        long bytes = 0;

        foreach (var path in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!known.TryGetValue(path, out var item))
            {
                Log.Error($"Refusing to delete '{path}': it was not offered by the scan.");
                report.FilesSkipped++;
                continue;
            }

            if (!IsInsideRoots(path, target))
            {
                Log.Error($"Refusing to delete '{path}': outside the '{target.Id}' roots.");
                report.FilesSkipped++;
                continue;
            }

            batch.Add(path);
            bytes += item.Bytes;
        }

        if (batch.Count == 0) return;

        // The user's own files go to the Recycle Bin, so a mistake here is a mistake the
        // user can undo. That is the whole reason these targets are separate.
        if (target.UseRecycleBin)
        {
            if (SendToRecycleBin(batch))
            {
                report.BytesFreed += bytes;
                report.FilesDeleted += batch.Count;
            }
            else
            {
                report.FilesSkipped += batch.Count;
                report.Failures[target.Id] = "The shell refused the delete operation.";
            }

            return;
        }

        foreach (var path in batch)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    report.BytesFreed += known[path].Bytes;
                    report.FilesDeleted++;
                }
                catch
                {
                    report.FilesSkipped++;
                }
            }
            else
            {
                DeleteFile(path, target, report);
            }
        }
    }

    // =====================================================================================
    // Shell operations
    // =====================================================================================

    /// <summary>
    /// Moves a batch of paths to the Recycle Bin in one shell call.
    /// The source list is double-null terminated, which is what SHFileOperation expects
    /// and the single easiest thing to get wrong about this API.
    /// </summary>
    private static bool SendToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return true;

        var from = string.Join('\0', paths) + "\0\0";

        var operation = new CleanupInterop.ShFileOpStruct
        {
            Window = IntPtr.Zero,
            Function = CleanupInterop.FoDelete,
            From = from,
            To = null,
            Flags = CleanupInterop.FofAllowUndo |
                    CleanupInterop.FofNoConfirmation |
                    CleanupInterop.FofNoErrorUi |
                    CleanupInterop.FofSilent |
                    CleanupInterop.FofNoConfirmMkDir,
            ProgressTitle = null
        };

        int result = CleanupInterop.SHFileOperation(ref operation);

        if (result != 0)
        {
            Log.Warn($"SHFileOperation returned {result} while recycling {paths.Count} item(s).");
            return false;
        }

        if (operation.AnyOperationsAborted)
        {
            Log.Warn("The recycle operation was aborted.");
            return false;
        }

        return true;
    }

    private static void EmptyRecycleBin(CleanupReport report)
    {
        var info = new CleanupInterop.ShQueryRbInfo
        {
            StructSize = Marshal.SizeOf<CleanupInterop.ShQueryRbInfo>()
        };

        long before = 0;
        long items = 0;

        if (CleanupInterop.SHQueryRecycleBin(null, ref info) == 0)
        {
            before = info.BinSize;
            items = info.ItemCount;
        }

        int result = CleanupInterop.SHEmptyRecycleBin(
            IntPtr.Zero, null,
            CleanupInterop.RecycleNoConfirmation |
            CleanupInterop.RecycleNoProgressUi |
            CleanupInterop.RecycleNoSound);

        // 0 is success; -2147418113 (E_UNEXPECTED) is what an already-empty bin returns.
        if (result != 0 && result != unchecked((int)0x8000FFFF))
        {
            report.Failures["recyclebin"] = $"SHEmptyRecycleBin returned 0x{result:X8}.";
            return;
        }

        report.BytesFreed += before;
        report.FilesDeleted += (int)Math.Min(int.MaxValue, items);
    }

    // =====================================================================================
    // The guard
    // =====================================================================================

    /// <summary>
    /// True when <paramref name="path"/> is inside one of the target's own roots.
    ///
    /// Compared on the fully resolved path, so "..\..\Windows\System32" cannot walk out of
    /// a root, and with a trailing separator so "C:\Temp2" does not count as being inside
    /// "C:\Temp".
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
            string resolved;

            try
            {
                resolved = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            if (!resolved.EndsWith(Path.DirectorySeparatorChar))
                resolved += Path.DirectorySeparatorChar;

            if (full.StartsWith(resolved, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
