using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Data;
using Hexnest.App.Services;
using Hexnest.Core.Models;

namespace Hexnest.App.ViewModels;

/// <summary>The update history, in its own menu as a permanent record.</summary>
public sealed class HistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<HistoryRecord> _records = new();

    private string _search = string.Empty;
    private HistoryOutcome? _outcomeFilter;
    private string? _notice;

    public HistoryViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_records);
        View.Filter = Matches;

        ExportCommand = new RelayCommand(Export, () => _records.Count > 0);
        ClearCommand = new RelayCommand(Clear, () => _records.Count > 0);
        RefreshCommand = new RelayCommand(Load);

        SetOutcomeCommand = new RelayCommand(parameter =>
        {
            OutcomeFilter = parameter is string name && Enum.TryParse<HistoryOutcome>(name, true, out var value)
                ? value
                : null;
        });

        OpenBackupCommand = new RelayCommand(parameter =>
        {
            if (parameter is not HistoryRecord record) return;

            var path = record.BackupPath;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) OpenInExplorer(path);
        });

        AppHost.History.RecordAdded += _ => OnUi(Load);
        AppEvents.LanguageChanged += () => OnUi(() => RaiseAll(nameof(CountText)));

        Load();
    }

    public ICollectionView View { get; }

    public RelayCommand ExportCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SetOutcomeCommand { get; }
    public RelayCommand OpenBackupCommand { get; }

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) { View.Refresh(); Raise(nameof(CountText)); } }
    }

    public HistoryOutcome? OutcomeFilter
    {
        get => _outcomeFilter;
        set
        {
            if (!Set(ref _outcomeFilter, value)) return;
            View.Refresh();
            RaiseAll(nameof(CountText), nameof(IsAll), nameof(IsSuccess), nameof(IsFailed));
        }
    }

    public bool IsAll => OutcomeFilter is null;
    public bool IsSuccess => OutcomeFilter == HistoryOutcome.Success;
    public bool IsFailed => OutcomeFilter == HistoryOutcome.Failed;

    public bool IsEmpty => _records.Count == 0;

    public string CountText => $"{View.Cast<object>().Count()} / {_records.Count}";

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    // =====================================================================================

    private void Load()
    {
        _records.Clear();
        foreach (var record in AppHost.History.All()) _records.Add(record);

        View.Refresh();
        RaiseAll(nameof(IsEmpty), nameof(CountText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void Export()
    {
        try
        {
            var path = AppHost.History.ExportCsv();
            Notice = path;
            OpenInExplorer(path, select: true);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("History export failed", ex);
            Notice = ex.Message;
        }
    }

    private void Clear()
    {
        AppHost.History.Clear();
        Load();
        Notice = null;
    }

    private bool Matches(object item)
    {
        if (item is not HistoryRecord record) return false;
        if (OutcomeFilter is { } outcome && record.Outcome != outcome) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;

        var needle = Search.Trim();

        return Contains(record.DeviceName, needle)
               || Contains(record.Title, needle)
               || Contains(record.Manufacturer, needle)
               || Contains(record.DeviceClass, needle)
               || Contains(record.ToVersion, needle);
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null &&
           haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private static void OpenInExplorer(string path, bool select = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = select ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open Explorer: {ex.Message}");
        }
    }
}
