using System.Collections.Concurrent;
using System.Text;
using Hexnest.Core.Models;

namespace Hexnest.Core.Diagnostics;

/// <summary>One line in the in-memory and on-disk log.</summary>
public sealed record LogEntry(DateTime TimestampUtc, LogLevel Level, string Message)
{
    public string TimeDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
    public override string ToString() => $"{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{Level,-5}] {Message}";
}

/// <summary>
/// Dependency-free logger: a bounded ring buffer the UI binds to, plus a rolling
/// text file for support. Writing is fire-and-forget and never throws, because a
/// logging failure must not break a driver install.
/// </summary>
public static class Log
{
    private const int MaxInMemoryEntries = 5000;
    private const long MaxFileBytes = 8L * 1024 * 1024;

    private static readonly ConcurrentQueue<LogEntry> Buffer = new();
    private static readonly object FileLock = new();

    private static string? _logFilePath;
    private static LogLevel _minimumLevel = LogLevel.Info;

    /// <summary>Raised for every entry, so the UI can append without polling.</summary>
    public static event Action<LogEntry>? EntryWritten;

    public static LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    public static string? FilePath => _logFilePath;

    /// <summary>Points the logger at a file. Safe to call more than once.</summary>
    public static void Initialize(string logFilePath, LogLevel minimumLevel = LogLevel.Info)
    {
        _minimumLevel = minimumLevel;
        try
        {
            var directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            RollIfTooLarge(logFilePath);
            _logFilePath = logFilePath;

            Info($"---- Hexnest log started ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ----");
        }
        catch
        {
            // Running without a log file is acceptable; the ring buffer still works.
            _logFilePath = null;
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception exception)
        => Write(LogLevel.Error, $"{message} :: {exception.GetType().Name}: {exception.Message}");

    /// <summary>Snapshot of the ring buffer, oldest first.</summary>
    public static IReadOnlyList<LogEntry> Snapshot() => Buffer.ToArray();

    public static void Clear()
    {
        while (Buffer.TryDequeue(out _)) { }
    }

    /// <summary>Renders the buffer as text, for the "copy diagnostics" button.</summary>
    public static string Export()
    {
        var builder = new StringBuilder();
        foreach (var entry in Buffer) builder.AppendLine(entry.ToString());
        return builder.ToString();
    }

    private static void Write(LogLevel level, string message)
    {
        if (level < _minimumLevel) return;

        var entry = new LogEntry(DateTime.UtcNow, level, message);

        Buffer.Enqueue(entry);
        while (Buffer.Count > MaxInMemoryEntries) Buffer.TryDequeue(out _);

        try { EntryWritten?.Invoke(entry); } catch { /* a bad subscriber must not break logging */ }

        var path = _logFilePath;
        if (path is null) return;

        try
        {
            lock (FileLock)
            {
                File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Disk full, file locked, permissions: never propagate.
        }
    }

    private static void RollIfTooLarge(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        var previous = path + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(path, previous);
    }
}
