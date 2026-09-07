using System.Collections.ObjectModel;
using System.Diagnostics;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Persistence;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The settings page.
///
/// Shows only the settings that mean something on a Mac. The Windows page also carries
/// the driver source list, restore points, reboot behaviour and the parallel job count,
/// none of which has anything to act on here; they stay in the settings file untouched,
/// so a settings file is portable between the two builds.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private bool _loading = true;

    public SettingsViewModel()
    {
        var settings = AppHost.Settings.Current;

        foreach (var option in Loc.Available) Languages.Add(option);

        _language = Languages.FirstOrDefault(l =>
            string.Equals(l.Code, settings.Language, StringComparison.OrdinalIgnoreCase))
            ?? Languages[0];

        _theme = ThemeManager.Current;
        _autoCheckUpdates = settings.AutoCheckUpdates;
        _includePrerelease = settings.IncludePrereleaseUpdates;
        _historyRetentionDays = settings.HistoryRetentionDays;

        OpenDataFolderCommand = new RelayCommand(() => Reveal(AppPaths.Root));
        OpenLogFolderCommand = new RelayCommand(() => Reveal(AppPaths.LogsDirectory));

        _loading = false;
    }

    // =====================================================================================
    // Appearance
    // =====================================================================================

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    private LanguageOption _language;

    public LanguageOption Language
    {
        get => _language;
        set
        {
            if (!Set(ref _language, value) || _loading || value is null) return;

            Save(s => s.Language = value.Code);
            Loc.SetLanguage(value.Code);
        }
    }

    /// <summary>
    /// The three theme options. "System" is first and is the default on macOS, where
    /// following the appearance setting is what every other application does.
    /// </summary>
    public IReadOnlyList<ThemeOption> Themes { get; } = new[]
    {
        new ThemeOption(ThemeManager.System, Loc.T("set.themeSystem")),
        new ThemeOption(ThemeManager.Light, Loc.T("set.themeLight")),
        new ThemeOption(ThemeManager.Dark, Loc.T("set.themeDark"))
    };

    private string _theme;

    public string Theme
    {
        get => _theme;
        set
        {
            if (!Set(ref _theme, value) || _loading || string.IsNullOrWhiteSpace(value)) return;

            ThemeManager.Apply(value);
            Save(s => s.Theme = value);
        }
    }

    public string ThemeHint => Loc.T("set.themeSystemHint");

    // =====================================================================================
    // Updates
    // =====================================================================================

    private bool _autoCheckUpdates;

    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            if (Set(ref _autoCheckUpdates, value) && !_loading) Save(s => s.AutoCheckUpdates = value);
        }
    }

    private bool _includePrerelease;

    public bool IncludePrerelease
    {
        get => _includePrerelease;
        set
        {
            if (Set(ref _includePrerelease, value) && !_loading)
                Save(s => s.IncludePrereleaseUpdates = value);
        }
    }

    // =====================================================================================
    // Data
    // =====================================================================================

    private int _historyRetentionDays;

    public int HistoryRetentionDays
    {
        get => _historyRetentionDays;
        set
        {
            if (Set(ref _historyRetentionDays, value) && !_loading)
                Save(s => s.HistoryRetentionDays = value);
        }
    }

    public string DataFolder => AppPaths.Root;

    public string LogFolder => AppPaths.LogsDirectory;

    public RelayCommand OpenDataFolderCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

    // =====================================================================================

    public string Title => Loc.T("set.title");

    public string Subtitle => Loc.T("mac.settingsSubtitle");

    /// <summary>
    /// The store hands out the live instance, so a change is applied to a copy and
    /// saved - a failed write would otherwise still have mutated memory.
    /// </summary>
    private static void Save(Action<Core.Models.AppSettings> change)
    {
        try
        {
            var copy = AppHost.Settings.Current.Clone();
            change(copy);
            AppHost.Settings.Save(copy);

            AppEvents.RaiseStatus(Loc.T("set.saved"));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
            AppEvents.RaiseStatus(ex.Message);
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { path },
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open '{path}': {ex.Message}");
        }
    }
}

/// <summary>One entry in the theme picker.</summary>
/// <param name="Code">"system", "light" or "dark".</param>
/// <param name="Label">What the picker shows.</param>
public sealed record ThemeOption(string Code, string Label)
{
    public override string ToString() => Label;
}
