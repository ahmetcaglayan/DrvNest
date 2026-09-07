using System.Reflection;

namespace Hexnest.Core;

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
    public const string RepositoryName = "Hexnest";

    public const string ProductName = "Hexnest";

    /// <summary>
    /// One line, and it has to stay true on both platforms. Hexnest started as a
    /// Windows driver tool, which is still the largest thing it does, but it is now a
    /// system utility that happens to install drivers on the platform that has them.
    /// </summary>
    public const string Tagline = "Drivers, monitors and clean-up in one window";

    /// <summary>Asset name published by the release workflow for Windows x64.</summary>
    public const string ReleaseAssetX64 = "Hexnest.exe";

    /// <summary>Asset name published by the release workflow for Windows arm64.</summary>
    public const string ReleaseAssetArm64 = "Hexnest-arm64.exe";

    /// <summary>
    /// Asset name published for macOS. One universal disk image covering Apple silicon
    /// and Intel, because a Mac user who has to work out which of two files they need
    /// is a Mac user who has already been let down.
    /// </summary>
    public const string ReleaseAssetMac = "Hexnest.dmg";

    /// <summary>Checksum file published alongside the binaries.</summary>
    public const string ChecksumAsset = "checksums.txt";

    /// <summary>
    /// The asset this build would update itself from, chosen by the platform and
    /// architecture it is actually running on.
    /// </summary>
    public static string ReleaseAssetForThisPlatform =>
        OperatingSystem.IsMacOS()
            ? ReleaseAssetMac
            : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
              == System.Runtime.InteropServices.Architecture.Arm64
                ? ReleaseAssetArm64
                : ReleaseAssetX64;

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

    /// <summary>"Hexnest 1.0.0", used in the title bar and the user agent.</summary>
    public static string VersionDisplay => $"{ProductName} {Version}";

    public static string UserAgent => $"{ProductName}/{Version} (+{RepositoryUrl})";
}
