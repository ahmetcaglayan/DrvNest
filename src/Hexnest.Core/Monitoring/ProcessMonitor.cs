using System.Diagnostics;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Monitoring;

/// <summary>
/// Works out what every running process is costing, the same way Task Manager does.
///
/// CPU for a process is not a number Windows stores; it is a rate, and a rate needs two
/// samples. Each pass records the process' cumulative kernel + user time and compares it
/// with the previous pass:
///
///     cpu% = (cpuTimeNow - cpuTimeBefore) / (wallClockElapsed * logicalProcessors) * 100
///
/// which is why the first sample after opening the page always reports 0% for everything
/// and the second one is the first real reading.
///
/// Some processes cannot be opened at all, even from an elevated token: the ones running
/// as Protected Process Light, such as the antimalware service and parts of the kernel.
/// Those rows are marked restricted rather than dropped, because a monitor that silently
/// hides the process using the CPU is worse than one that admits it cannot see inside it.
/// </summary>
public sealed class ProcessMonitor
{
    /// <summary>Cumulative processor time and when it was read, per process.</summary>
    private readonly Dictionary<int, Baseline> _previous = new();

    private readonly int _selfPid = Environment.ProcessId;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _lastSweepSeconds;
    private long _totalPhysicalBytes;

    private readonly record struct Baseline(double Seconds, TimeSpan Cpu, long IoBytes);

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

        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not enumerate processes: {ex.Message}");
            return Array.Empty<ProcessMetrics>();
        }

        var rows = new List<ProcessMetrics>(processes.Length);
        var alive = new HashSet<int>(processes.Length);

        int cores = Math.Max(1, Environment.ProcessorCount);

        foreach (var process in processes)
        {
            try
            {
                int pid = process.Id;
                alive.Add(pid);

                // The Idle process is pid 0 and its "usage" is the machine being idle.
                if (pid == 0) continue;

                var row = Measure(process, pid, now, interval, cores);
                if (row is not null) rows.Add(row);
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipping a process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
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
        _lastSweepSeconds = 0;
    }

    // =====================================================================================

    private ProcessMetrics? Measure(Process process, int pid, double now, double interval, int cores)
    {
        string name;
        try { name = process.ProcessName; }
        catch { return null; }

        bool restricted = false;

        TimeSpan cpuTime = TimeSpan.Zero;
        long workingSet = 0;
        long privateBytes = 0;
        int threads = 0;
        long ioBytes = 0;
        DateTime? started = null;
        string? path = null;

        try
        {
            cpuTime = process.TotalProcessorTime;
        }
        catch
        {
            // Protected processes refuse PROCESS_QUERY_INFORMATION.
            restricted = true;
        }

        try
        {
            workingSet = process.WorkingSet64;
            privateBytes = process.PrivateMemorySize64;
            threads = process.Threads.Count;
        }
        catch
        {
            restricted = true;
        }

        try { started = process.StartTime; }
        catch { /* Not readable for a protected process; the row simply has no start time. */ }

        try { path = process.MainModule?.FileName; }
        catch { /* Reading the module list of another process is often denied. */ }

        try
        {
            if (MonitorInterop.GetProcessIoCounters(process.Handle, out var io))
                ioBytes = (long)(io.ReadTransferCount + io.WriteTransferCount);
        }
        catch
        {
            // No I/O figure for this process; the column shows a dash.
        }

        double cpuPercent = 0;
        double diskRate = 0;

        if (_previous.TryGetValue(pid, out var before) && interval > 0)
        {
            // A process that restarted into the same pid would show a negative delta.
            double cpuDelta = (cpuTime - before.Cpu).TotalSeconds;

            if (cpuDelta >= 0)
                cpuPercent = Math.Clamp(cpuDelta / (interval * cores) * 100.0, 0, 100);

            if (ioBytes >= before.IoBytes)
                diskRate = (ioBytes - before.IoBytes) / interval;
        }

        _previous[pid] = new Baseline(now, cpuTime, ioBytes);

        return new ProcessMetrics
        {
            ProcessId = pid,
            Name = name,
            ExecutablePath = path,
            CpuPercent = cpuPercent,
            WorkingSetBytes = workingSet,
            PrivateBytes = privateBytes,
            MemoryPercent = _totalPhysicalBytes > 0 ? workingSet * 100.0 / _totalPhysicalBytes : 0,
            ThreadCount = threads,
            DiskBytesPerSecond = diskRate,
            IsRestricted = restricted,
            IsSelf = pid == _selfPid,
            StartedAt = started
        };
    }

    private void Prune(HashSet<int> alive)
    {
        var dead = _previous.Keys.Where(pid => !alive.Contains(pid)).ToList();
        foreach (var pid in dead) _previous.Remove(pid);
    }
}
