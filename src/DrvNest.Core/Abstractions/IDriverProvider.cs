using DrvNest.Core.Models;

namespace DrvNest.Core.Abstractions;

/// <summary>Progress report emitted while a job downloads or installs.</summary>
public readonly record struct JobProgress(
    double Percent,
    long BytesTransferred,
    long BytesTotal,
    string? StatusText);

/// <summary>Outcome of a provider operation.</summary>
public sealed record ProviderResult(
    bool Success,
    int ResultCode,
    bool RebootRequired,
    string? Message = null)
{
    public static ProviderResult Ok(bool rebootRequired = false, string? message = null)
        => new(true, 0, rebootRequired, message);

    public static ProviderResult Fail(int code, string message)
        => new(false, code, false, message);
}

/// <summary>
/// A source of driver packages.
///
/// Everything the job engine and the UI do goes through this interface, which is why
/// a new source (vendor catalog, network share, WSUS...) can be added later without
/// touching either. Implementations must be safe to call concurrently.
/// </summary>
public interface IDriverProvider
{
    ProviderKind Kind { get; }

    /// <summary>Name shown next to a candidate in the UI.</summary>
    string DisplayName { get; }

    /// <summary>False when the provider cannot run right now, e.g. no internet.</summary>
    bool IsAvailable { get; }

    /// <summary>Explains why <see cref="IsAvailable"/> is false, for the warnings list.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Looks for packages that apply to <paramref name="devices"/>.
    /// Implementations must never throw for an expected failure: report it through
    /// <see cref="UnavailableReason"/> and return an empty list instead.
    /// </summary>
    Task<IReadOnlyList<UpdateCandidate>> SearchAsync(
        IReadOnlyList<DeviceItem> devices,
        CancellationToken cancellationToken);

    /// <summary>Fetches the package. May be called concurrently for different jobs.</summary>
    Task<ProviderResult> DownloadAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Installs the package. The job engine serialises calls to this method across all
    /// providers, because Windows itself allows only one driver installation at a time.
    /// </summary>
    Task<ProviderResult> InstallAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken);
}
