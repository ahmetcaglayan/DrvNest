using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Hexnest.App.Services;
using Hexnest.Core.Backup;
using Hexnest.Core.Persistence;

namespace Hexnest.App.ViewModels;

/// <summary>
/// Backup and restore.
///
/// This page is the answer to the hardest part of the post-format story: a machine
/// whose network adapter has no driver cannot reach Windows Update at all. Export
/// before the format, keep the folder next to Hexnest.exe on a USB stick, restore
/// afterwards with no internet involved.
/// </summary>
public sealed class BackupViewModel : ViewModelBase
{
    private bool _isBusy;
    private bool _compress;
    private string _status = string.Empty;
    private BackupEntry? _selected;

    public BackupViewModel()
    {
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(RestoreSelectedAsync, () => !IsBusy && Selected is not null);
        RestoreFolderCommand = new AsyncRelayCommand(RestoreFromFolderAsync, () => !IsBusy);
        RefreshCommand = new RelayCommand(Load, () => !IsBusy);

        DeleteCommand = new RelayCommand(parameter =>
        {
            if (parameter is not BackupEntry entry) return;
            AppHost.Backup.Delete(entry);
            Load();
        }, _ => !IsBusy);

        OpenCommand = new RelayCommand(parameter =>
        {
            if (parameter is BackupEntry entry) OpenInExplorer(entry.Path);
        });

        AppEvents.LanguageChanged += () => OnUi(() => Raise(nameof(Tip)));

        Load();
    }

    public ObservableCollection<BackupEntry> Backups { get; } = new();

    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand RestoreFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool Compress
    {
        get => _compress;
        set => Set(ref _compress, value);
    }

    public string Status
    {
        get => _status;
        private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    public BackupEntry? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsEmpty => Backups.Count == 0;

    public string Tip => Loc.T("bk.tip");

    public string BackupRootPath => AppHost.Backup.BackupRoot;

    // =====================================================================================

    private void Load()
    {
        Backups.Clear();

        foreach (var entry in AppHost.Backup.ListBackups(AppHost.Settings.Current.LocalRepositoryPaths))
            Backups.Add(entry);

        Raise(nameof(IsEmpty));
    }

    private async Task CreateAsync()
    {
        IsBusy = true;
        Status = string.Empty;

        try
        {
            var progress = new Progress<string>(message => OnUi(() => Status = message));

            var entry = await AppHost.Backup
                .ExportAllAsync(note: null, compress: Compress, status: progress)
                .ConfigureAwait(true);

            Status = $"{entry.Name}  ·  {entry.SizeDisplay}  ·  {Loc.T("bk.packages", entry.PackageCount)}";
            Load();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Backup failed", ex);
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreSelectedAsync()
    {
        if (Selected is null) return;
        await RestoreAsync(Selected.Path).ConfigureAwait(true);
    }

    /// <summary>
    /// Restores from any folder the user points at, not only a Hexnest backup.
    /// A vendor's extracted driver folder works just as well.
    /// </summary>
    private async Task RestoreFromFolderAsync()
    {
        var folder = FolderPicker.Pick(Loc.T("bk.restoreFolder"), AppPaths.BackupsDirectory);
        if (folder is null) return;

        await RestoreAsync(folder).ConfigureAwait(true);
    }

    private async Task RestoreAsync(string path)
    {
        IsBusy = true;
        Status = string.Empty;

        try
        {
            var progress = new Progress<string>(message => OnUi(() => Status = message));

            var (success, rebootRequired, message) = await AppHost.Backup
                .RestoreAsync(path, progress)
                .ConfigureAwait(true);

            Status = message;

            if (success && rebootRequired) AppEvents.RequestNavigation("queue");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Restore failed", ex);
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            bool isFile = File.Exists(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = isFile ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open Explorer: {ex.Message}");
        }
    }
}
