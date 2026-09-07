using System.Diagnostics;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Platform;

namespace Hexnest.Core.Monitoring;

/// <summary>
/// Works out what every running process is costing, the same way Activity Monitor does.
///
/// CPU for a process is not a number the kernel stores; it is a rate, and a rate needs
/// two samples. Each pass records the process' cumulative user + system time and
/// compares it with the previous pass:
///
///     cpu% = (cpuTimeNow - cpuTimeBefore) / (wallClockElapsed * logicalProcessors) * 100
///
/// which is why the first pass after opening the page reports 0% for everything and the
/// second one is the first real reading.
///
/// Processes belonging to another user - every system daemon, on a Mac that is not being
/// run as root - cannot be opened by libproc. Those rows are marked restricted and
/// listed with their name only, rather than dropped: a monitor that silently hides the
/// process using the processor is worse than one that admits it cannot see inside it.
/// </summary>
public sealed class ProcessMonitor
{
    /// <summary>Cumulative processor time and when it was read, per process.</summary>
    private readonly Dictionary<int, Baseline> _previous = new();

    /// <summary>Executable paths, which never change for a given pid and are not cheap.</summary>
    private readonly Dictionary<int, string?> _paths = new();

    private readonly int _selfPid = Environment.ProcessId;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _lastSweepSeconds;
    private long _totalPhysicalBytes;

    private readonly record struct Baseline(double Seconds, ulong CpuNanoseconds);

    /// <summary>
    /// Takes one pass over the process table.
    /// Returns rows sorted by processor usage, highest first.
    /// </summary>
    public IReadOnlyList<ProcessMetrics> Sample(long totalPhysicalBytes = 0)
    {
        if (totalPhysicalBytes > 0) _totalPhysicalBytes = totalPhysicalBytes;

        double now = _clock.Elapsed.TotalSeconds;
        double interval = _lastSweepSeconds > 0 ? now - _lastSweepSeconds : 0;
        _lastSweepSeconds = now;

        int[] pids;

        try
        {
            pids = MacProcNative.ListPids();
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not enumerate processes: {ex.Message}");
            return Array.Empty<ProcessMetrics>();
        }

        var rows = new List<ProcessMetrics>(pids.Length);
        var alive = new HashSet<int>(pids.Length);

        int cores = Math.Max(1, Environment.ProcessorCount);

        foreach (int pid in pids)
        {
            try
            {
                // pid 0 is the kernel task; its "usage" is the machine itself.
                if (pid == 0) continue;

                alive.Add(pid);

                var row = Measure(pid, now, interval, cores);
                if (row is not null) rows.Add(row);
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipping a process: {ex.Message}");
            }
        }

        // Processes that exited must not keep their baseline forever; the pid will be
        // reused and the next process to get it would show a nonsensical first reading.
        if (_previous.Count > alive.Count * 2) Prune(alive);

        rows.Sort((a, b) =>
        {
            int byCpu = b.CpuPercent.CompareTo(a.CpuPercent);
            return byCpu != 0 ? byCpu : b.WorkingSetBytes.CompareTo(a.WorkingSetBytes);
        });

        return rows;
    }

    /// <summary>Forgets every baseline, so the next pass starts clean.</summary>
    public void Reset()
    {
        _previous.Clear();
        _paths.Clear();
        _lastSweepSeconds = 0;
    }

    // =====================================================================================

    private ProcessMetrics? Measure(int pid, double now, double interval, int cores)
    {
        var snapshot = MacProcNative.Read(pid);
        if (snapshot is null) return null;

        if (snapshot.IsRestricted)
        {
            return new ProcessMetrics
            {
                ProcessId = pid,
                Name = snapshot.Name,
                IsRestricted = true,
                IsSelf = pid == _selfPid
            };
        }

        double cpuPercent = 0;

        if (_previous.TryGetValue(pid, out var baseline) && interval > 0)
        {
            // An unsigned subtraction that would go negative means the pid was reused
            // between passes, so there is no honest rate to report for it this time.
            if (snapshot.CpuNanoseconds >= baseline.CpuNanoseconds)
            {
                double elapsed = now - baseline.Seconds;
                double busySeconds = (snapshot.CpuNanoseconds - baseline.CpuNanoseconds) / 1e9;

                if (elapsed > 0)
                    cpuPercent = Math.Clamp(busySeconds / (elapsed * cores) * 100.0, 0, 100);
            }
        }

        _previous[pid] = new Baseline(now, snapshot.CpuNanoseconds);

        if (!_paths.TryGetValue(pid, out var path))
        {
            path = MacProcNative.ReadPath(pid);
            _paths[pid] = path;
        }

        // The kernel truncates its own process name to fifteen characters, so
        // "Microsoft Outlo" is what a raw read gives. The .app bundle around the
        // executable carries the real one.
        var bundle = MacProcNative.BundleName(path);

        double memoryPercent = _totalPhysicalBytes > 0
            ? snapshot.ResidentBytes * 100.0 / _totalPhysicalBytes
            : 0;

        return new ProcessMetrics
        {
            ProcessId = pid,
            Name = bundle ?? snapshot.Name,
            Description = bundle is null ? null : snapshot.Name,
            ExecutablePath = path,
            CpuPercent = cpuPercent,
            WorkingSetBytes = snapshot.ResidentBytes,

            // macOS has no per process commit charge. Resident memory is what
            // Activity Monitor's own "Memory" column reports, and inventing a second
            // number from the virtual size would be worse than leaving it at zero.
            PrivateBytes = 0,

            MemoryPercent = memoryPercent,
            ThreadCount = snapshot.ThreadCount,

            // Per process disk throughput needs a privileged IOKit subscription; it is
            // reported as zero and the page shows no column for it.
            DiskBytesPerSecond = 0,

            IsRestricted = false,
            IsSelf = pid == _selfPid,
            StartedAt = snapshot.StartedAt
        };
    }

    /// <summary>Drops baselines for processes that are no longer running.</summary>
    private void Prune(HashSet<int> alive)
    {
        foreach (int pid in _previous.Keys.Where(p => !alive.Contains(p)).ToList())
        {
            _previous.Remove(pid);
            _paths.Remove(pid);
        }
    }
}
