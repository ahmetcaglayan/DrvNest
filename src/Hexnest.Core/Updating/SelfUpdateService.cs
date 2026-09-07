using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Models;
using Hexnest.Core.Persistence;

namespace Hexnest.Core.Updating;

/// <summary>A release published on GitHub.</summary>
public sealed class ReleaseInfo
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public bool IsPrerelease { get; set; }
    public DateTime PublishedUtc { get; set; }

    /// <summary>Download URL of the executable that matches this machine's architecture.</summary>
    public string? AssetUrl { get; set; }

    public string? AssetName { get; set; }
    public long AssetSize { get; set; }

    /// <summary>URL of checksums.txt, when the release publishes one.</summary>
    public string? ChecksumUrl { get; set; }

    /// <summary>Version without the leading "v".</summary>
    public string Version => Tag.TrimStart('v', 'V');

    public string SizeDisplay => Formatting.HumanBytes(AssetSize);
}

/// <summary>
/// Why an update check ended the way it did.
///
/// The UI localises this rather than displaying <see cref="UpdateCheckResult.Message"/>,
/// which is English diagnostic text. Message stays for the log and the CLI.
/// </summary>
public enum UpdateCheckStatus
{
    UpToDate = 0,
    UpdateAvailable = 1,

    /// <summary>The repository exists but has published no releases yet.</summary>
    NoReleases = 2,

    /// <summary>A fork has not filled in its repository in AppInfo.cs.</summary>
    NotConfigured = 3,

    /// <summary>GitHub could not be reached.</summary>
    NetworkError = 4,

    /// <summary>Anything else; Message carries the detail.</summary>
    Error = 5
}

/// <summary>Outcome of a check for updates.</summary>
public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    ReleaseInfo? Release,
    string? Message,
    UpdateCheckStatus Status = UpdateCheckStatus.Error);

/// <summary>
/// Keeps Hexnest itself up to date from GitHub Releases.
///
/// The published executable is a single self-contained file, so updating means
/// replacing exactly one file. Windows lets a running executable be renamed but not
/// overwritten, so the swap is: rename the running exe aside, drop the new one into
/// its place, relaunch, and delete the leftover on the next start.
///
/// The download is always verified against the release's checksums.txt before it is
/// allowed anywhere near the installed executable.
/// </summary>
public sealed class SelfUpdateService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private const string OldSuffix = ".old";

    private readonly HttpClient _http;

    public SelfUpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = Timeout };

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);

        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    // =====================================================================================
    // Check
    // =====================================================================================

    /// <summary>Asks GitHub whether a newer release exists.</summary>
    public async Task<UpdateCheckResult> CheckAsync(
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        var current = AppInfo.Version;

        if (!AppInfo.IsRepositoryConfigured)
        {
            const string message =
                "The GitHub repository is not configured in AppInfo.cs, so update checks are disabled.";
            Log.Debug(message);
            return new UpdateCheckResult(false, current, null, message, UpdateCheckStatus.NotConfigured);
        }

        try
        {
            var url = includePrereleases ? AppInfo.ReleasesApiUrl : AppInfo.LatestReleaseApiUrl;

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                bool notFound = response.StatusCode == System.Net.HttpStatusCode.NotFound;

                var message = notFound
                    ? "No releases have been published yet."
                    : $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.";

                Log.Warn($"Update check failed: {message}");

                return new UpdateCheckResult(false, current, null, message,
                    notFound ? UpdateCheckStatus.NoReleases : UpdateCheckStatus.Error);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = includePrereleases ? ParseFirst(json) : ParseSingle(json);

            if (release is null)
            {
                return new UpdateCheckResult(false, current, null,
                    "No usable release was found.", UpdateCheckStatus.NoReleases);
            }

            bool newer = Formatting.CompareVersions(release.Version, current) > 0;

            Log.Info(newer
                ? $"Update available: {current} -> {release.Version}"
                : $"Hexnest is up to date ({current}).");

            return new UpdateCheckResult(
                newer, current, release,
                newer ? $"Version {release.Version} is available." : "You are on the latest version.",
                newer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"Update check could not reach GitHub: {ex.Message}");
            return new UpdateCheckResult(false, current, null, ex.Message, UpdateCheckStatus.NetworkError);
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(false, current, null, ex.Message, UpdateCheckStatus.Error);
        }
    }

    // =====================================================================================
    // Download
    // =====================================================================================

    /// <summary>
    /// Downloads the release executable into the cache and verifies its SHA-256.
    /// Returns the path of the verified file, or null when anything went wrong.
    /// </summary>
    public async Task<string?> DownloadAsync(
        ReleaseInfo release,
        Action<JobProgressLite>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.AssetUrl))
        {
            Log.Error("The release has no executable for this architecture.");
            return null;
        }

        var folder = Path.Combine(AppPaths.CacheDirectory, "self-update");
        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, release.AssetName ?? "Hexnest.exe");

        try
        {
            Log.Info($"Downloading {release.AssetName} ({release.SizeDisplay})...");

            using var response = await _http
                .GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? release.AssetSize;
            long received = 0;

            await using (var input = await response.Content
                             .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                             target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;

                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    received += read;
                    progress?.Invoke(new JobProgressLite(
                        total > 0 ? received * 100.0 / total : 0, received, total));
                }
            }

            if (!await VerifyChecksumAsync(release, target, cancellationToken).ConfigureAwait(false))
            {
                TryDelete(target);
                return null;
            }

            Log.Info($"Update downloaded and verified: {target}");
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Update download failed", ex);
            TryDelete(target);
            return null;
        }
    }

    /// <summary>
    /// Compares the download against checksums.txt.
    /// A release without a checksum file is refused rather than trusted: this file
    /// replaces the executable that installs drivers with SYSTEM privileges.
    /// </summary>
    private async Task<bool> VerifyChecksumAsync(
        ReleaseInfo release,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(release.ChecksumUrl))
        {
            Log.Error("The release publishes no checksums.txt; refusing to install it.");
            return false;
        }

        try
        {
            var text = await _http.GetStringAsync(release.ChecksumUrl, cancellationToken)
                .ConfigureAwait(false);

            var fileName = Path.GetFileName(filePath);
            string? expected = null;

            // Standard sha256sum layout: "<hash>  <filename>".
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var name = parts[^1].TrimStart('*');
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    expected = parts[0];
                    break;
                }
            }

            if (expected is null)
            {
                Log.Error($"checksums.txt has no entry for {fileName}; refusing to install it.");
                return false;
            }

            await using var stream = File.OpenRead(filePath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash);

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error($"Checksum mismatch for {fileName}. Expected {expected}, got {actual}.");
                return false;
            }

            Log.Info($"Checksum verified for {fileName}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Checksum verification failed", ex);
            return false;
        }
    }

    // =====================================================================================
    // Apply
    // =====================================================================================

    /// <summary>
    /// Swaps in the downloaded executable and relaunches.
    /// The caller must shut the application down immediately afterwards.
    /// </summary>
    public bool Apply(string downloadedExecutable, bool relaunch = true)
    {
        var currentPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
        {
            Log.Error("Cannot apply the update: the current executable path is unknown.");
            return false;
        }

        var backupPath = currentPath + OldSuffix;

        try
        {
            TryDelete(backupPath);

            // Windows allows a running executable to be renamed, but not overwritten.
            File.Move(currentPath, backupPath);

            try
            {
                File.Copy(downloadedExecutable, currentPath, overwrite: true);
            }
            catch
            {
                // Put the original back so the user is never left without an executable.
                File.Move(backupPath, currentPath);
                throw;
            }

            Log.Info($"Update applied to {currentPath}.");

            if (relaunch)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = currentPath,
                    UseShellExecute = true,
                    Arguments = "--updated"
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply the update", ex);
            return false;
        }
    }

    /// <summary>
    /// Deletes the previous executable left behind by an update.
    /// Called once at startup; failure is harmless because the file is inert.
    /// </summary>
    public static void CleanUpAfterUpdate()
    {
        try
        {
            var currentPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentPath)) return;

            var leftover = currentPath + OldSuffix;
            if (!File.Exists(leftover)) return;

            File.Delete(leftover);
            Log.Info("Removed the previous version left over from an update.");
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not remove the previous version yet: {ex.Message}");
        }
    }

    // =====================================================================================
    // Parsing
    // =====================================================================================

    private static ReleaseInfo? ParseSingle(string json)
    {
        using var document = JsonDocument.Parse(json);
        return MapRelease(document.RootElement);
    }

    private static ReleaseInfo? ParseFirst(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var release = MapRelease(element);
            if (release?.AssetUrl is not null) return release;
        }

        return null;
    }

    private static ReleaseInfo? MapRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var release = new ReleaseInfo
        {
            Tag = GetString(element, "tag_name") ?? string.Empty,
            Name = GetString(element, "name") ?? string.Empty,
            Notes = GetString(element, "body") ?? string.Empty,
            HtmlUrl = GetString(element, "html_url") ?? AppInfo.ReleasesUrl,
            IsPrerelease = element.TryGetProperty("prerelease", out var pre) &&
                           pre.ValueKind == JsonValueKind.True
        };

        if (element.TryGetProperty("published_at", out var published) &&
            published.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(published.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            release.PublishedUtc = date;
        }

        if (string.IsNullOrWhiteSpace(release.Tag)) return null;
        if (!element.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array) return release;

        var wanted = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? AppInfo.ReleaseAssetArm64
            : AppInfo.ReleaseAssetX64;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (name is null || url is null) continue;

            if (name.Equals(AppInfo.ChecksumAsset, StringComparison.OrdinalIgnoreCase))
            {
                release.ChecksumUrl = url;
                continue;
            }

            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                release.AssetName = name;
                release.AssetUrl = url;
                release.AssetSize = asset.TryGetProperty("size", out var size) &&
                                    size.TryGetInt64(out var bytes) ? bytes : 0;
            }
        }

        return release;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}

/// <summary>Minimal progress shape for the self-update download.</summary>
public readonly record struct JobProgressLite(double Percent, long BytesTransferred, long BytesTotal);
