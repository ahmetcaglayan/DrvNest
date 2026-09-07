using Hexnest.Core;
using Hexnest.Core.Models;

namespace Hexnest.Cli;

/// <summary>
/// Console rendering.
///
/// Colour is applied only when the output is a real terminal, so piping to a file or
/// running under a CI log produces clean text instead of escape sequences.
/// </summary>
public static class Output
{
    private static readonly object Gate = new();
    private static readonly bool UseColour = !Console.IsOutputRedirected;

    /// <summary>
    /// Redirected output has to survive whatever decodes it.
    ///
    /// The CLI writes UTF-8, but Windows PowerShell 5.1 reads a redirected stream using
    /// the console code page, which turns "·" and "→" into mojibake in log files and CI
    /// output. Plain ASCII separators cost nothing and always arrive intact.
    /// </summary>
    private static readonly bool Ascii = Console.IsOutputRedirected;

    public static string Dot => Ascii ? " - " : "  ·  ";
    public static string Arrow => Ascii ? " -> " : "  →  ";

    private static string? _lastJobLine;

    public static bool Verbose { get; set; }

    public static void Banner()
    {
        Write($"{AppInfo.ProductName} {AppInfo.Version}", ConsoleColor.Green);
        Dim(Core.Platform.SystemInfo.Current.OsDisplay + Dot +
            Core.Platform.SystemInfo.Current.Architecture + Dot +
            (Core.Platform.Elevation.IsAdministrator ? "elevated" : "not elevated"));
        Console.WriteLine();
    }

    public static void Section(string title)
    {
        Console.WriteLine();
        Write(title, ConsoleColor.Cyan);
        Write(new string('-', Math.Min(title.Length, 70)), ConsoleColor.DarkGray);
    }

    public static void KeyValue(string key, string value)
        => Console.WriteLine($"  {key,-20} {value}");

    public static void Line(string message) => Console.WriteLine(message);

    public static void Dim(string message) => Write(message, ConsoleColor.DarkGray);

    public static void Ok(string message) => Write(message, ConsoleColor.Green);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    public static void Error(string message) => Write(message, ConsoleColor.Red, toError: true);

    /// <summary>Progress messages from the scanner and the job engine.</summary>
    public static void Status(string message) => Dim($"  {message}");

    /// <summary>
    /// One line per job state change.
    ///
    /// Percentages are deliberately not rendered as a live-updating bar: this output
    /// is as likely to end up in a log file or a remote session as on a terminal, and
    /// a redrawn bar turns into thousands of unreadable lines there.
    /// </summary>
    public static void Job(DriverJob job)
    {
        var line = $"  [{job.State}] {job.Candidate.DeviceName}";

        // Only print when something meaningful changed.
        if (line == _lastJobLine && !Verbose) return;
        _lastJobLine = line;

        var colour = job.State switch
        {
            JobState.Succeeded or JobState.RebootRequired => ConsoleColor.Green,
            JobState.Failed => ConsoleColor.Red,
            JobState.Cancelled => ConsoleColor.DarkGray,
            JobState.Downloading or JobState.Installing => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray
        };

        Write(line, colour);

        if (Verbose && job.BytesTotal > 0)
            Dim("          " + job.TransferDisplay + Dot + job.SpeedDisplay);

        if (job.ErrorMessage is { Length: > 0 } error) Error($"          {error}");
    }

    private static void Write(string message, ConsoleColor colour, bool toError = false)
    {
        lock (Gate)
        {
            var writer = toError ? Console.Error : Console.Out;

            if (!UseColour)
            {
                writer.WriteLine(message);
                return;
            }

            var previous = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = colour;
                writer.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = previous;
            }
        }
    }
}
