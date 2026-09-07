namespace DrvNest.Core.Cleanup;

/// <summary>How much thought a category deserves before it is ticked.</summary>
public enum CleanupRisk
{
    /// <summary>
    /// A cache the system or an application rebuilds by itself. Deleting it costs a
    /// little time on next use and nothing else.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Safe, but with a visible consequence: a browser signs you out of nothing but
    /// forgets its cache, the Recycle Bin stops being a safety net, Windows Update
    /// re-downloads what it had.
    /// </summary>
    Moderate = 1,

    /// <summary>
    /// Your own files, or something DrvNest cannot be certain about. Never deleted in
    /// bulk: each item is listed individually with its size and age, and each has to be
    /// ticked on its own.
    /// </summary>
    Review = 2
}

/// <summary>One thing that can be cleaned up.</summary>
public sealed class CleanupTarget
{
    /// <summary>Stable id used by the settings and the selection state.</summary>
    public required string Id { get; init; }

    /// <summary>Localisation key for the title.</summary>
    public required string TitleKey { get; init; }

    /// <summary>Localisation key for the one-line explanation.</summary>
    public required string DescriptionKey { get; init; }

    public CleanupRisk Risk { get; init; }

    /// <summary>Total bytes the scan found.</summary>
    public long Bytes { get; set; }

    /// <summary>How many files were counted, for "1,284 files" in the row.</summary>
    public int FileCount { get; set; }

    /// <summary>The roots this target owns. Never widened at delete time.</summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Individual items, for <see cref="CleanupRisk.Review"/> targets where deleting the
    /// whole folder would be wrong. Empty for the cache targets, which are all-or-nothing.
    /// </summary>
    public IReadOnlyList<CleanupItem> Items { get; set; } = Array.Empty<CleanupItem>();

    /// <summary>True when the target needs an elevated token to clean.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>
    /// Deleted through the shell into the Recycle Bin rather than permanently, so a
    /// mistake with someone's own files is recoverable.
    /// </summary>
    public bool UseRecycleBin { get; init; }

    /// <summary>
    /// A caveat worth showing next to the row, e.g. that a browser has to be closed.
    /// Localisation key; null when there is nothing to add.
    /// </summary>
    public string? NoteKey { get; init; }

    /// <summary>
    /// True when the target deletes the contents of its roots but not the roots
    /// themselves. Windows recreates most of these folders, but not all of them, and
    /// removing a folder Windows expects to exist is a way to break something quietly.
    /// </summary>
    public bool ContentsOnly { get; init; } = true;

    /// <summary>Set when the scan could not read part of the target.</summary>
    public string? ScanWarning { get; set; }

    public bool HasAnything => Bytes > 0 || FileCount > 0 || Items.Count > 0;
}

/// <summary>One file or folder inside a target the user reviews item by item.</summary>
public sealed class CleanupItem
{
    public required string Path { get; init; }

    /// <summary>Display name: the file name, or the folder name for a leftover.</summary>
    public required string Name { get; init; }

    public long Bytes { get; init; }

    /// <summary>When it was last written. Drives the "not touched in 8 months" hint.</summary>
    public DateTime LastWriteTime { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>Why DrvNest thinks this is a candidate. Shown verbatim; already localised.</summary>
    public string? Reason { get; init; }

    public int DaysOld => Math.Max(0, (int)(DateTime.Now - LastWriteTime).TotalDays);
}

/// <summary>What a cleanup run actually did.</summary>
public sealed class CleanupReport
{
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }

    /// <summary>Targets that reported at least one failure, with the first reason.</summary>
    public Dictionary<string, string> Failures { get; } = new(StringComparer.Ordinal);

    /// <summary>True when nothing at all could be removed.</summary>
    public bool IsEmpty => BytesFreed == 0 && FilesDeleted == 0;
}

/// <summary>Progress while a cleanup runs.</summary>
/// <param name="TargetId">Which target is being processed.</param>
/// <param name="Completed">Targets finished so far.</param>
/// <param name="Total">Targets in this run.</param>
/// <param name="BytesFreed">Running total.</param>
public readonly record struct CleanupProgress(string TargetId, int Completed, int Total, long BytesFreed);

/// <summary>What trimming the working sets actually achieved.</summary>
/// <param name="ProcessesTrimmed">How many processes accepted the request.</param>
/// <param name="ProcessesSkipped">How many refused it, which is normal for protected ones.</param>
/// <param name="BytesBefore">Physical memory in use before.</param>
/// <param name="BytesAfter">Physical memory in use after.</param>
public readonly record struct MemoryTrimResult(
    int ProcessesTrimmed,
    int ProcessesSkipped,
    long BytesBefore,
    long BytesAfter)
{
    /// <summary>Can legitimately be negative if the machine got busier while trimming.</summary>
    public long BytesReleased => BytesBefore - BytesAfter;
}
