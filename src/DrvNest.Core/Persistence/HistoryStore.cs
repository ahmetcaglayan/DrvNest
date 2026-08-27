using System.Globalization;
using System.Text;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;

namespace DrvNest.Core.Persistence;

/// <summary>
/// The permanent update history shown in its own menu.
///
/// Backed by an append-only JSON-lines file so that a crash mid-write can cost at
/// most the last record, never the whole history.
/// </summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private List<HistoryRecord>? _cache;

    /// <summary>Raised after a record is appended so the UI can prepend it live.</summary>
    public event Action<HistoryRecord>? RecordAdded;

    /// <summary>All records, newest first.</summary>
    public IReadOnlyList<HistoryRecord> All()
    {
        lock (_gate)
        {
            _cache ??= JsonStore.ReadLines<HistoryRecord>(AppPaths.HistoryFile);
            return _cache.OrderByDescending(r => r.TimestampUtc).ToList();
        }
    }

    /// <summary>Appends one record and notifies listeners.</summary>
    public void Add(HistoryRecord record)
    {
        lock (_gate)
        {
            _cache ??= JsonStore.ReadLines<HistoryRecord>(AppPaths.HistoryFile);
            _cache.Add(record);
            JsonStore.AppendLine(AppPaths.HistoryFile, record);
        }

        Log.Info($"History: {record.DeviceName} {record.VersionTransition} -> {record.Outcome}");
        RecordAdded?.Invoke(record);
    }

    /// <summary>Appends several records in one go.</summary>
    public void AddRange(IEnumerable<HistoryRecord> records)
    {
        foreach (var record in records) Add(record);
    }

    /// <summary>Drops records older than the retention window. 0 keeps everything.</summary>
    public int Prune(int retentionDays)
    {
        if (retentionDays <= 0) return 0;

        lock (_gate)
        {
            _cache ??= JsonStore.ReadLines<HistoryRecord>(AppPaths.HistoryFile);

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var kept = _cache.Where(r => r.TimestampUtc >= cutoff).ToList();
            int removed = _cache.Count - kept.Count;

            if (removed > 0)
            {
                _cache = kept;
                JsonStore.WriteLines(AppPaths.HistoryFile, kept);
                Log.Info($"Pruned {removed} history record(s) older than {retentionDays} days.");
            }

            return removed;
        }
    }

    /// <summary>Deletes the entire history.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache = new List<HistoryRecord>();
            try
            {
                if (File.Exists(AppPaths.HistoryFile)) File.Delete(AppPaths.HistoryFile);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not clear history: {ex.Message}");
            }
        }
    }

    /// <summary>Writes the history to a CSV file for sharing or archiving.</summary>
    public string ExportCsv(string? targetPath = null)
    {
        var path = targetPath ?? Path.Combine(
            AppPaths.ReportsDirectory,
            $"drvnest-history-{DateTime.Now:yyyy-MM-dd_HHmm}.csv");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var builder = new StringBuilder();
        builder.AppendLine("Date,Device,Class,Manufacturer,From,To,Provider,Outcome,Duration(s),Reboot,Error");

        foreach (var record in All())
        {
            builder.AppendLine(string.Join(',',
                Csv(record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(record.DeviceName),
                Csv(record.DeviceClass),
                Csv(record.Manufacturer),
                Csv(record.FromVersion),
                Csv(record.ToVersion),
                Csv(record.Provider.ToString()),
                Csv(record.Outcome.ToString()),
                Csv(record.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)),
                Csv(record.RebootRequired ? "yes" : "no"),
                Csv(record.ErrorMessage)));
        }

        // UTF-8 BOM so Excel opens non-ASCII device names correctly.
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        Log.Info($"History exported to {path}");
        return path;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var escaped = value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
        return $"\"{escaped}\"";
    }
}
