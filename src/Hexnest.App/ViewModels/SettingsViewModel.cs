using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Hexnest.App.Services;
using Hexnest.Core.Models;
using Hexnest.Core.Persistence;

namespace Hexnest.App.ViewModels;

/// <summary>
/// Settings.
///
/// Edits a clone and only commits on Save, so a half-typed value can never reach
/// the running job engine.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private AppSettings _draft;
    private string? _notice;

    public SettingsViewModel()
    {
        _draft = AppHost.Settings.Current.Clone();

        foreach (var path in _draft.LocalRepositoryPaths) RepositoryPaths.Add(path);

        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Root));

        AppEvents.LanguageChanged += () => OnUi(() => RaiseAll(nameof(ThemeOptions), nameof(LanguageOptions)));
    }

    public ObservableCollection<string> RepositoryPaths { get; } = new();

    public RelayCommand SaveCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveFolderCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    // ---- General ----------------------------------------------------------------------

    public IReadOnlyList<int> ParallelOptions { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };

    public int MaxParallelJobs
    {
        get => _draft.MaxParallelJobs;
        set { _draft.MaxParallelJobs = value; Raise(); }
    }

    public IReadOnlyList<int> RetryOptions { get; } = new[] { 0, 1, 2, 3, 4, 5 };

    public int MaxRetryAttempts
    {
        get => _draft.MaxRetryAttempts;
        set { _draft.MaxRetryAttempts = value; Raise(); }
    }

    public bool ScanOnStartup
    {
        get => _draft.ScanOnStartup;
        set { _draft.ScanOnStartup = value; Raise(); }
    }

    public int HistoryRetentionDays
    {
        get => _draft.HistoryRetentionDays;
        set { _draft.HistoryRetentionDays = value; Raise(); }
    }

    // ---- Safety -----------------------------------------------------------------------

    public bool CreateRestorePoint
    {
        get => _draft.CreateRestorePoint;
        set { _draft.CreateRestorePoint = value; Raise(); }
    }

    public bool BackupBeforeUpdate
    {
        get => _draft.BackupBeforeUpdate;
        set { _draft.BackupBeforeUpdate = value; Raise(); }
    }

    public bool ResumeAfterReboot
    {
        get => _draft.ResumeAfterReboot;
        set { _draft.ResumeAfterReboot = value; Raise(); }
    }

    public bool AutoReboot
    {
        get => _draft.AutoReboot;
        set { _draft.AutoReboot = value; Raise(); }
    }

    public int AutoRebootDelaySeconds
    {
        get => _draft.AutoRebootDelaySeconds;
        set { _draft.AutoRebootDelaySeconds = value; Raise(); }
    }

    // ---- Sources ----------------------------------------------------------------------

    public bool OfflineMode
    {
        get => _draft.OfflineMode;
        set { _draft.OfflineMode = value; Raise(); }
    }

    public bool IncludeOptionalDrivers
    {
        get => _draft.IncludeOptionalDrivers;
        set { _draft.IncludeOptionalDrivers = value; Raise(); }
    }

    // ---- Updates -----------------------------------------------------------------------

    public bool AutoCheckUpdates
    {
        get => _draft.AutoCheckUpdates;
        set { _draft.AutoCheckUpdates = value; Raise(); }
    }

    public bool AutoInstallUpdates
    {
        get => _draft.AutoInstallUpdates;
        set { _draft.AutoInstallUpdates = value; Raise(); }
    }

    public bool IncludePrereleaseUpdates
    {
        get => _draft.IncludePrereleaseUpdates;
        set { _draft.IncludePrereleaseUpdates = value; Raise(); }
    }

    /// <summary>When the background check last completed, for the settings page.</summary>
    public string LastUpdateCheckDisplay =>
        _draft.LastUpdateCheckUtc is { } at
            ? Loc.T("about.lastChecked", at.ToLocalTime().ToString("g"))
            : Loc.T("about.neverChecked");

    // ---- Appearance --------------------------------------------------------------------

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "dark", "light" };

    public string Theme
    {
        get => _draft.Theme;
        set
        {
            _draft.Theme = value;
            // Applied immediately: a theme picker that needs a Save click feels broken.
            ThemeManager.Apply(value);
            Raise();
        }
    }

    /// <summary>
    /// Every language Hexnest can display, including "System" and any JSON language
    /// pack dropped into the Languages folder.
    /// </summary>
    public IReadOnlyList<LanguageOption> LanguageOptions => Loc.Available;

    public LanguageOption SelectedLanguage
    {
        get => Loc.Available.FirstOrDefault(o =>
                   o.Code.Equals(_draft.Language, StringComparison.OrdinalIgnoreCase))
               ?? Loc.Available[0];
        set
        {
            if (value is null) return;

            _draft.Language = value.Code;
            Loc.SetLanguage(value.Code);
            Raise();
        }
    }

    /// <summary>Shows which language "System" actually resolved to.</summary>
    public string LanguageHint =>
        SelectedLanguage.IsAuto
            ? $"{Loc.T("set.languageHint")}  ({Loc.Language.ToUpperInvariant()})"
            : Loc.T("set.languageHint");

    // ---- Notice ------------------------------------------------------------------------

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public string DataFolderPath => AppPaths.Root;

    // =====================================================================================

    private void Save()
    {
        _draft.LocalRepositoryPaths = RepositoryPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AppHost.Settings.Save(_draft);

        _draft = AppHost.Settings.Current.Clone();
        Notice = Loc.T("set.saved");
    }

    private void Reset()
    {
        var fresh = AppHost.Settings.Reset();
        _draft = fresh.Clone();

        RepositoryPaths.Clear();
        foreach (var path in _draft.LocalRepositoryPaths) RepositoryPaths.Add(path);

        ThemeManager.Apply(_draft.Theme);
        Loc.SetLanguage(_draft.Language);

        RaiseAll(
            nameof(MaxParallelJobs), nameof(MaxRetryAttempts), nameof(ScanOnStartup),
            nameof(HistoryRetentionDays), nameof(CreateRestorePoint), nameof(BackupBeforeUpdate),
            nameof(ResumeAfterReboot), nameof(AutoReboot), nameof(AutoRebootDelaySeconds),
            nameof(OfflineMode), nameof(IncludeOptionalDrivers), nameof(Theme),
            nameof(AutoCheckUpdates), nameof(AutoInstallUpdates),
            nameof(IncludePrereleaseUpdates), nameof(LastUpdateCheckDisplay),
            nameof(SelectedLanguage), nameof(LanguageHint));

        Notice = Loc.T("set.saved");
    }

    private void AddFolder()
    {
        var folder = FolderPicker.Pick(Loc.T("set.addFolder"), AppPaths.PortableRepositoryDirectory);
        if (folder is null) return;

        if (!RepositoryPaths.Contains(folder, StringComparer.OrdinalIgnoreCase))
            RepositoryPaths.Add(folder);
    }

    private void RemoveFolder(object? parameter)
    {
        if (parameter is string path) RepositoryPaths.Remove(path);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open {path}: {ex.Message}");
        }
    }
}
