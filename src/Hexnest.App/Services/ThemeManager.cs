using System.Windows;

namespace Hexnest.App.Services;

/// <summary>
/// Swaps the palette dictionary at runtime.
///
/// Works because every control style references colours through DynamicResource and
/// both palettes declare an identical set of keys, so replacing one dictionary
/// re-themes the live visual tree without recreating any control.
/// </summary>
public static class ThemeManager
{
    private const string DarkUri = "pack://application:,,,/Themes/Palette.Dark.xaml";
    private const string LightUri = "pack://application:,,,/Themes/Palette.Light.xaml";

    public static string Current { get; private set; } = "dark";

    public static void Apply(string theme)
    {
        var application = Application.Current;
        if (application is null) return;

        theme = theme == "light" ? "light" : "dark";

        var wanted = new Uri(theme == "light" ? LightUri : DarkUri, UriKind.Absolute);
        var dictionaries = application.Resources.MergedDictionaries;

        var replacement = new ResourceDictionary { Source = wanted };

        // The palette is always the first merged dictionary; control styles come after
        // it and must stay in place so their DynamicResource lookups keep resolving.
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString.Contains("Palette.Dark", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.Contains("Palette.Light", StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            int index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        Current = theme;
        Core.Diagnostics.Log.Debug($"Theme switched to {theme}.");
    }
}
