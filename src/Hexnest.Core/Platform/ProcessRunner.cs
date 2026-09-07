using System.Diagnostics;
using System.Text;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Platform;

/// <summary>Result of running a console tool.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>pnputil returns 3010 when the operation succeeded but a restart is required.</summary>
    public bool RebootRequired => ExitCode == 3010;

    /// <summary>Treats 0 and 3010 as success, which is what every setup API does.</summary>
    public bool SucceededOrNeedsReboot => ExitCode is 0 or 3010;

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// Thin async wrapper around <see cref="Process"/> for the in-box Windows tools we
/// shell out to (pnputil.exe, schtasks.exe, shutdown.exe). All of them ship with
/// Windows, so this adds no deployment requirement.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Runs a tool and captures both streams.</summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var argumentList = arguments.ToList();

        var info = new ProcessStartInfo
        {
            FileName = ResolveSystemTool(fileName),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,

            // System32 on Windows so a relative path in a tool's own arguments cannot
            // resolve against whatever folder Hexnest happens to have been started
            // from. macOS has no equivalent folder and returns an empty string here,
            // which Process reads as "inherit", so it is left alone there.
            WorkingDirectory = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                : "/"
        };

        foreach (var argument in argumentList) info.ArgumentList.Add(argument);

        Log.Debug($"Running: {info.FileName} {string.Join(' ', argumentList)}");

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        if (!process.Start())
            return new ProcessResult(-1, string.Empty, $"Could not start {fileName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Give the async readers a moment to flush the tail of the streams.
        process.WaitForExit();

        var result = new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
        Log.Debug($"Exit code {result.ExitCode} from {Path.GetFileName(info.FileName)}");
        return result;
    }

    /// <summary>
    /// Resolves bare tool names against the real System32 folder.
    /// A 32-bit process on 64-bit Windows would otherwise be redirected to SysWOW64,
    /// where pnputil behaves differently.
    /// </summary>
    private static string ResolveSystemTool(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return fileName;

        // The redirection this guards against is a Windows-only mechanism. On macOS
        // every tool Hexnest runs is passed by absolute path anyway, so the name is
        // handed to execvp unchanged.
        if (!OperatingSystem.IsWindows()) return fileName;

        var native = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.Is64BitProcess ? "System32" : "Sysnative",
            fileName);

        return File.Exists(native) ? native : fileName;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Nothing useful to do if the process is already gone.
        }
    }
}
