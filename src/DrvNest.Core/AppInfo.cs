using System.Reflection;

namespace DrvNest.Core;

/// <summary>
/// Build-time identity of the application.
///
/// The two repository constants are the only thing you must change when you fork
/// this project: they are what the built-in updater uses to find new releases on
/// GitHub. Everything else is derived from the assembly.
/// </summary>
public static class AppInfo
{
    // ---------------------------------------------------------------------------------
    // The repository the built-in updater checks for new releases.
    // If you fork this project, change these two lines to your own repository.
    // ---------------------------------------------------------------------------------
    public const string RepositoryOwner = "ahmetcaglayan";
    public const string RepositoryName = "DrvNest";

    public const string ProductName = "DrvNest";
    public const string Tagline = "Windows driver scanner, installer and updater";

    /// <summary>Asset name published by the release workflow for x64.</summary>
    public const string ReleaseAssetX64 = "DrvNest.exe";

    /// <summary>Asset name published by the release workflow for arm64.</summary>
    public const string ReleaseAssetArm64 = "DrvNest-arm64.exe";

    /// <summary>Checksum file published alongside the binaries.</summary>
    public const string ChecksumAsset = "checksums.txt";

    public static string RepositoryUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}";

    public static string ReleasesUrl => $"{RepositoryUrl}/releases";

    public static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";

    public static string ReleasesApiUrl =>
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=10";

    public static string IssuesUrl => $"{RepositoryUrl}/issues";

    /// <summary>False when a fork has not filled in its own repository yet.</summary>
    public static bool IsRepositoryConfigured =>
        RepositoryOwner.Length > 0 &&
        !RepositoryOwner.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase);

    /// <summary>Semantic version of this build, e.g. "1.0.0".</summary>
    public static string Version
    {
        get
        {
            var informational = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip the "+abc1234" source revision suffix the SDK appends.
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    /// <summary>"DrvNest 1.0.0", used in the title bar and the user agent.</summary>
    public static string VersionDisplay => $"{ProductName} {Version}";

    public static string UserAgent => $"{ProductName}/{Version} (+{RepositoryUrl})";
}
