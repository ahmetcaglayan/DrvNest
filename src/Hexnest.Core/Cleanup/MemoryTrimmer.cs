using System.Diagnostics;
using System.Runtime.InteropServices;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Monitoring;

namespace Hexnest.Core.Cleanup;

/// <summary>
/// Trims process working sets and the system file cache.
///
/// This is the feature every "PC optimiser" calls Free RAM, and it is worth being precise
/// about what it does, because what those tools imply is not true.
///
/// It asks Windows to page each process' working set out to the page file. Physical memory
/// in use genuinely drops, and the number on the dashboard genuinely falls. But the pages
/// are not gone - they are on disk, and the moment the program touches that memory again
/// Windows reads them back, which is slower than leaving them where they were. Unused
/// memory is not wasted memory: Windows was already keeping it available.
///
/// So this is not a performance feature and Hexnest does not present it as one. It is
/// occasionally genuinely useful - immediately before starting something that needs a
/// large contiguous allocation, or to see how much a leaking program is really holding -
/// and the UI says exactly that instead of promising a faster computer.
/// </summary>
public sealed class MemoryTrimmer
{
    /// <summary>
    /// Trims every process the current token can open, plus the system file cache.
    /// Returns what actually changed, measured before and after.
    /// </summary>
    public MemoryTrimResult Trim(bool includeFileCache = true)
    {
        long before = UsedPhysicalBytes();

        int trimmed = 0;
        int skipped = 0;

        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not enumerate processes to trim: {ex.Message}");
            return new MemoryTrimResult(0, 0, before, before);
        }

        int self = Environment.ProcessId;

        foreach (var process in processes)
        {
            try
            {
                // Trimming Hexnest itself would be theatre: it would page out the very
                // code that is about to measure the result.
                if (process.Id is 0 or 4 || process.Id == self)
                {
                    skipped++;
                    continue;
                }

                if (CleanupInterop.EmptyWorkingSet(process.Handle)) trimmed++;
                else skipped++;
            }
            catch
            {
                // Protected processes refuse the handle. That is expected, not an error.
                skipped++;
            }
            finally
            {
                process.Dispose();
            }
        }

        if (includeFileCache) TrimFileCache();

        // Windows does the paging lazily; a moment's pause makes the "after" figure mean
        // something rather than measuring the state before the work started.
        Thread.Sleep(600);

        long after = UsedPhysicalBytes();

        Log.Info($"Memory trim: {trimmed} process(es) trimmed, {skipped} skipped, " +
                 $"{Models.Formatting.HumanBytes(Math.Max(0, before - after))} released.");

        return new MemoryTrimResult(trimmed, skipped, before, after);
    }

    /// <summary>
    /// Asks Windows to shrink the system file cache. Needs SeIncreaseQuotaPrivilege,
    /// which an elevated token has; without it the call simply fails and is logged.
    /// </summary>
    private static void TrimFileCache()
    {
        try
        {
            if (!CleanupInterop.SetSystemFileCacheSize(
                    CleanupInterop.TrimFileCache, CleanupInterop.TrimFileCache, 0))
            {
                Log.Debug($"SetSystemFileCacheSize failed: {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not trim the file cache: {ex.Message}");
        }
    }

    /// <summary>Physical memory currently in use, from the same source as the monitor.</summary>
    private static long UsedPhysicalBytes()
    {
        var monitor = new SystemMonitor();

        try
        {
            // One synchronous sample; the monitor's own timer is not involved.
            monitor.Start();
            Thread.Sleep(120);

            var latest = monitor.Latest;
            return latest is null ? 0 : latest.MemoryUsedBytes;
        }
        finally
        {
            monitor.Dispose();
        }
    }
}
