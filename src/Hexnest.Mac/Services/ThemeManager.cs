using Avalonia;
using Avalonia.Styling;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Mac.Services;

/// <summary>
/// Applies the dark, light or system theme.
///
/// On Windows this class swaps two resource dictionaries by hand. Avalonia resolves
/// theme dictionaries itself from the requested variant, so all this has to do is set
/// the variant - and "system" then costs nothing at all, because macOS switching to
/// dark at sunset re-resolves every DynamicResource without the application being
/// involved. That is the behaviour a Mac user expects and the reason "system" is the
/// sensible default here even though Windows defaults to dark.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    /// <summary>The preference currently applied: "dark", "light" or "system".</summary>
    public static string Current { get; private set; } = System;

    public static void Apply(string? theme)
    {
        var wanted = string.IsNullOrWhiteSpace(theme) ? System : theme!.Trim().ToLowerInvariant();

        // The Windows build has no "system" option and stores "dark" or "light". A
        // settings file carried between the two must not be rejected, so anything
        // unrecognised falls back to following the system.
        Current = wanted switch
        {
            Dark => Dark,
            Light => Light,
            _ => System
        };

        var application = Application.Current;
        if (application is null) return;

        application.RequestedThemeVariant = Current switch
        {
            Dark => ThemeVariant.Dark,
            Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        Log.Debug($"Theme: {Current}.");
    }
}
