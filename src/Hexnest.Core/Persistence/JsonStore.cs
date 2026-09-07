using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Persistence;

/// <summary>
/// Crash-safe JSON persistence.
///
/// Every write goes to a temporary file first and is then atomically swapped in.
/// That matters here: the session file is rewritten on every progress tick and the
/// machine can be restarted at any moment, so a half-written file must never be
/// able to destroy a running recovery session.
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Compact form used for the append-only history log (one object per line).</summary>
    public static readonly JsonSerializerOptions LineOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Reads and deserializes, returning null on any failure.</summary>
    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read {Path.GetFileName(path)}: {ex.Message}");
            TryQuarantine(path);
            return null;
        }
    }

    /// <summary>Serializes atomically: temp file, flush, replace.</summary>
    public static bool Write<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + ".tmp";
            var json = JsonSerializer.Serialize(value, Options);

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // Replace keeps the original in place if the swap itself fails.
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not write {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Appends one JSON line. Used by the history log.</summary>
    public static bool AppendLine<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(value, LineOptions);
            File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not append to {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads a JSON-lines file, skipping any corrupted line.</summary>
    public static List<T> ReadLines<T>(string path)
    {
        var results = new List<T>();
        if (!File.Exists(path)) return results;

        try
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var item = JsonSerializer.Deserialize<T>(line, LineOptions);
                    if (item is not null) results.Add(item);
                }
                catch
                {
                    // A truncated final line after a hard power cut: skip it, keep the rest.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read {Path.GetFileName(path)}: {ex.Message}");
        }

        return results;
    }

    /// <summary>Rewrites a JSON-lines file from scratch (used when pruning history).</summary>
    public static bool WriteLines<T>(string path, IEnumerable<T> values)
    {
        try
        {
            var temporary = path + ".tmp";
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            {
                foreach (var value in values)
                    writer.WriteLine(JsonSerializer.Serialize(value, LineOptions));
            }

            if (File.Exists(path)) File.Replace(temporary, path, null, true);
            else File.Move(temporary, path);

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not rewrite {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Moves an unreadable file aside so the app can start with a clean one.</summary>
    private static void TryQuarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(path, target);
            Log.Warn($"Moved unreadable file to {Path.GetFileName(target)}.");
        }
        catch
        {
            // Nothing else to try.
        }
    }
}
