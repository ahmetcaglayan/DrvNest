namespace DrvNest.Core.Models;

/// <summary>Why a package is (or is not) worth offering for a device.</summary>
public enum OfferReason
{
    /// <summary>Nothing to gain; do not show it.</summary>
    None = 0,

    /// <summary>The device has no driver at all.</summary>
    MissingDriver = 1,

    /// <summary>Same publisher, higher version.</summary>
    NewerVersion = 2,

    /// <summary>The device is running an in-box Microsoft driver; a vendor one is better.</summary>
    ReplacesGeneric = 3
}

/// <summary>
/// Decides whether a driver package is an improvement on what a device already has.
///
/// The naive rule - "offer it when its version number is higher" - is wrong, and
/// wrong in exactly the case this application exists for. When Windows falls back to
/// its own generic driver it stamps it with the OS build number, so a device running
/// Microsoft's generic component driver reports version 10.0.26100.1 while the real
/// Intel package for the same device is 1.41.1420.0. Compared as numbers, the
/// generic driver looks nine major versions newer, and the correct vendor driver
/// gets discarded as a downgrade.
///
/// Version numbers only mean something *within one publisher*. Across publishers the
/// question is not "which number is bigger" but "which driver is the right one".
/// </summary>
public static class DriverComparison
{
    private static readonly string[] GenericPublishers =
    {
        "Microsoft", "Microsoft Corporation", "Microsoft Windows"
    };

    /// <summary>True when a publisher name is Microsoft's in-box driver stamp.</summary>
    public static bool IsGenericPublisher(string? publisher)
        => !string.IsNullOrWhiteSpace(publisher) &&
           GenericPublishers.Contains(publisher!.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decides whether <paramref name="packageVersion"/> from
    /// <paramref name="packagePublisher"/> is worth installing over what the device has.
    /// </summary>
    public static OfferReason Evaluate(
        DeviceItem device,
        string? packagePublisher,
        string? packageVersion)
    {
        // Nothing installed: anything that matches is an improvement.
        if (device.Health == DeviceHealth.DriverMissing ||
            string.IsNullOrWhiteSpace(device.DriverVersion))
        {
            return OfferReason.MissingDriver;
        }

        bool deviceIsGeneric = device.IsGenericMicrosoftDriver ||
                               IsGenericPublisher(device.DriverProvider);

        bool packageIsGeneric = IsGenericPublisher(packagePublisher);

        // A real vendor package always beats the in-box fallback, whatever the numbers
        // say. This is the case that matters most in practice.
        if (deviceIsGeneric && !packageIsGeneric) return OfferReason.ReplacesGeneric;

        // Same publisher: now the version numbers are comparable and meaningful.
        bool samePublisher =
            !string.IsNullOrWhiteSpace(packagePublisher) &&
            !string.IsNullOrWhiteSpace(device.DriverProvider) &&
            SamePublisher(packagePublisher!, device.DriverProvider!);

        if (samePublisher)
        {
            return Formatting.CompareVersions(packageVersion, device.DriverVersion) > 0
                ? OfferReason.NewerVersion
                : OfferReason.None;
        }

        // Different vendors and neither is the in-box driver. Comparing their numbering
        // schemes would be guesswork, so only a clearly higher version is offered - and
        // never a swap between two third parties on equal footing.
        return Formatting.CompareVersions(packageVersion, device.DriverVersion) > 0
            ? OfferReason.NewerVersion
            : OfferReason.None;
    }

    /// <summary>
    /// Compares publisher names loosely: "Intel", "INTEL" and "Intel(R) Corporation"
    /// are the same company, and INF files are wildly inconsistent about it.
    /// </summary>
    public static bool SamePublisher(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0) return false;

        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
               a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Strips the corporate noise that makes two names of one company differ.</summary>
    private static string Normalize(string publisher)
    {
        var text = publisher.Trim();

        foreach (var noise in new[]
                 {
                     "(R)", "(TM)", "(C)", ",", ".", " Corporation", " Corp", " Incorporated",
                     " Inc", " Ltd", " LLC", " GmbH", " Co", " Technologies", " Technology",
                     " Semiconductor", " Systems", " COMPUTER"
                 })
        {
            text = text.Replace(noise, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return text.Replace(" ", string.Empty).Trim();
    }
}
