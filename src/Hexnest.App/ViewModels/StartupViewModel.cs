using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Hexnest.App.Services;
using Hexnest.Core.Models;
using Hexnest.Core.Startup;

namespace Hexnest.App.ViewModels;

/// <summary>One row in the startup table.</summary>
public sealed class StartupRow : ViewModelBase
{
    private readonly StartupViewModel _owner;
    private StartupEntry _entry;
    private bool _isBusy;

    public StartupRow(StartupViewModel owner, StartupEntry entry)
    {
        _owner = owner;
        _entry = entry;
    }

    public StartupEntry Entry => _entry;

    public string Id => _entry.Id;
    public string Title => _entry.DisplayName;
    public string Command => _entry.Command;
    public string? ExecutablePath => _entry.ExecutablePath;
    public bool IsRunOnce => _entry.IsRunOnce;
    public bool IsBroken => _entry.IsBroken;

    public string Publisher => string.IsNullOrWhiteSpace(_entry.Publisher)
        ? Loc.T("startup.unknownPublisher")
        : _entry.Publisher!;

    public string LocationDisplay => Loc.T(_entry.Location switch
    {
        StartupLocation.CurrentUserRun => "startup.locUser",
        StartupLocation.LocalMachineRun => "startup.locMachine",
        StartupLocation.LocalMachineRun32 => "startup.locMachine32",
        StartupLocation.CurrentUserStartupFolder => "startup.locUserFolder",
        StartupLocation.CommonStartupFolder => "startup.locCommonFolder",
        _ => "startup.locRunOnce"
    });

    public string SizeDisplay =>
        _entry.ExecutableSize > 0 ? Formatting.HumanBytes(_entry.ExecutableSize) : "-";

    /// <summary>The two-way binding behind the switch in each row.</summary>
    public bool IsEnabled
    {
        get => _entry.IsEnabled;
        set
        {
            if (_entry.IsEnabled == value || _isBusy) return;

            // Guarded because a refusal calls back into this row to put the switch back,
            // and because the security prompt is modal - the user can reach the same
            // switch again while it is open.
            _isBusy = true;

            try
            {
                // Applied through the owner so one place reports the failure, and so a
                // refusal can restore the switch.
                _owner.Toggle(this, value);
            }
            finally
            {
                _isBusy = false;
            }
        }
    }

    /// <summary>A RunOnce entry deletes itself after one run; there is nothing to toggle.</summary>
    public bool CanToggle => !IsRunOnce;

    public bool IsSecurity => _entry.Caution == StartupCaution.Security;
    public bool IsVendor => _entry.Caution == StartupCaution.SystemOrVendor;

    public string CautionLabel => Loc.T(_entry.Caution switch
    {
        StartupCaution.Security => "startup.cautionSecurity",
        StartupCaution.SystemOrVendor => "startup.cautionVendor",
        _ => "startup.cautionNormal"
    });

    public string Tooltip => string.IsNullOrWhiteSpace(_entry.Command) ? Title : _entry.Command;

    /// <summary>Rebinds after a scan without replacing the row object.</summary>
    public void Apply(StartupEntry entry)
    {
        _entry = entry;

        RaiseAll(nameof(Title), nameof(Command), nameof(Publisher), nameof(LocationDisplay),
                 nameof(SizeDisplay), nameof(IsEnabled), nameof(IsBroken), nameof(IsRunOnce),
                 nameof(CanToggle), nameof(IsSecurity), nameof(IsVendor), nameof(CautionLabel),
                 nameof(Tooltip));
    }

    /// <summary>Puts the switch back after a refused change, without re-entering Toggle.</summary>
    internal void RevertTo(bool enabled)
    {
        _entry = CloneWith(enabled);
        Raise(nameof(IsEnabled));
    }

    internal void Commit(bool enabled)
    {
        _entry = CloneWith(enabled);
        Raise(nameof(IsEnabled));
    }

    private StartupEntry CloneWith(bool enabled) => new()
    {
        Id = _entry.Id,
        Name = _entry.Name,
        Command = _entry.Command,
        ExecutablePath = _entry.ExecutablePath,
        Publisher = _entry.Publisher,
        Description = _entry.Description,
        Location = _entry.Location,
        Caution = _entry.Caution,
        IsEnabled = enabled,
        IsBroken = _entry.IsBroken,
        ExecutableSize = _entry.ExecutableSize
    };
}

/// <summary>Which rows the table shows.</summary>
public enum StartupFilter
{
    All = 0,
    Enabled = 1,
    Disabled = 2,
    Broken = 3
}

/// <summary>
/// The startup page: what runs when Windows starts, and a switch for each of them.
///
/// Turning something off writes the same approval value Task Manager writes, so the two
/// agree with each other and nothing is ever deleted. See <see cref="StartupService"/> for
/// why that matters more than it sounds.
/// </summary>
public sealed class StartupViewModel : ViewModelBase
{
    private readonly StartupService _service = new();
    private readonly Dictionary<string, StartupRow> _rows = new(StringComparer.Ordinal);

    private IReadOnlyList<StartupEntry> _entries = Array.Empty<StartupEntry>();

    private string _search = string.Empty;
    private StartupFilter _filter = StartupFilter.All;
    private string? _notice;
    private bool _isBusy;

    public StartupViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);

        FilterCommand = new RelayCommand(parameter =>
        {
            if (parameter is string name && Enum.TryParse<StartupFilter>(name, true, out var parsed))
                Filter = parsed;
        });

        OpenLocationCommand = new RelayCommand(parameter =>
        {
            if (parameter is StartupRow row) OpenLocation(row);
        });

        CopyCommand = new RelayCommand(parameter =>
        {
            if (parameter is StartupRow row) Copy(row.Command);
        });

        OpenTaskManagerCommand = new RelayCommand(OpenTaskManager);

        AppEvents.LanguageChanged += () => OnUi(() =>
        {
            foreach (var row in _rows.Values) row.Apply(row.Entry);
            RefreshTexts();
        });

        _ = RefreshAsync();
    }

    public ObservableCollection<StartupRow> Rows { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand OpenLocationCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand OpenTaskManagerCommand { get; }

    // =====================================================================================

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) Rebuild(); }
    }

    public StartupFilter Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;

            RaiseAll(nameof(IsFilterAll), nameof(IsFilterEnabled),
                     nameof(IsFilterDisabled), nameof(IsFilterBroken));
            Rebuild();
        }
    }

    public bool IsFilterAll => Filter == StartupFilter.All;
    public bool IsFilterEnabled => Filter == StartupFilter.Enabled;
    public bool IsFilterDisabled => Filter == StartupFilter.Disabled;
    public bool IsFilterBroken => Filter == StartupFilter.Broken;

    public int EnabledCount => _entries.Count(e => e.IsEnabled && !e.IsRunOnce);
    public int DisabledCount => _entries.Count(e => !e.IsEnabled);
    public int BrokenCount => _entries.Count(e => e.IsBroken);

    public string SummaryDisplay =>
        Loc.T("startup.summary", _entries.Count, EnabledCount, DisabledCount);

    public bool HasBroken => BrokenCount > 0;

    public string BrokenDisplay => Loc.T("startup.brokenSummary", BrokenCount);

    public string RowCountDisplay => Loc.T("mon.showing", Rows.Count, _entries.Count);

    public bool IsEmpty => Rows.Count == 0;

    // =====================================================================================

    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Notice = null;

        try
        {
            // Reading the registry and pulling version information out of a few dozen
            // executables is not instant; it does not belong on the UI thread.
            var entries = await Task.Run(() => _service.Scan()).ConfigureAwait(true);

            _entries = entries;

            var alive = new HashSet<string>(entries.Count, StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                alive.Add(entry.Id);

                if (_rows.TryGetValue(entry.Id, out var row)) row.Apply(entry);
                else _rows[entry.Id] = new StartupRow(this, entry);
            }

            foreach (var id in _rows.Keys.Where(id => !alive.Contains(id)).ToList())
                _rows.Remove(id);

            Rebuild();
            RefreshTexts();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Could not read the startup entries", ex);
            Notice = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Applies a switch, and puts it back when Windows refuses.</summary>
    internal void Toggle(StartupRow row, bool enabled)
    {
        // Security software is the one case worth a question rather than a silent switch.
        if (!enabled && row.IsSecurity)
        {
            var answer = MessageBox.Show(
                Loc.T("startup.confirmSecurity", row.Title),
                Loc.T("nav.startup"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                row.RevertTo(!enabled);
                return;
            }
        }

        var result = _service.SetEnabled(row.Entry, enabled);

        if (result.Success)
        {
            row.Commit(enabled);
            Notice = Loc.T(enabled ? "startup.enabled" : "startup.disabled", row.Title);

            _entries = _entries
                .Select(e => e.Id == row.Id ? row.Entry : e)
                .ToList();

            RefreshTexts();
            Rebuild();
            return;
        }

        row.RevertTo(!enabled);
        Notice = result.Error;
    }

    private void Rebuild()
    {
        IEnumerable<StartupRow> query = _rows.Values;

        var term = Search.Trim();

        if (term.Length > 0)
        {
            query = query.Where(row =>
                row.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Command.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = Filter switch
        {
            StartupFilter.Enabled => query.Where(r => r.IsEnabled && !r.IsRunOnce),
            StartupFilter.Disabled => query.Where(r => !r.IsEnabled),
            StartupFilter.Broken => query.Where(r => r.IsBroken),
            _ => query
        };

        // Broken first - the easiest decision on the page. RunOnce last: those entries
        // delete themselves after one logon, so they are information rather than a
        // choice, and three "cmd /c del" cleanups at the top of the list are noise.
        var ordered = query
            .OrderBy(r => r.IsRunOnce)
            .ThenByDescending(r => r.IsBroken)
            .ThenByDescending(r => r.IsEnabled)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ListSync.Apply(Rows, ordered);

        RaiseAll(nameof(RowCountDisplay), nameof(IsEmpty));
    }

    private void RefreshTexts() => RaiseAll(
        nameof(EnabledCount), nameof(DisabledCount), nameof(BrokenCount),
        nameof(SummaryDisplay), nameof(HasBroken), nameof(BrokenDisplay),
        nameof(RowCountDisplay), nameof(IsEmpty));

    // =====================================================================================

    private void OpenLocation(StartupRow row)
    {
        var path = row.ExecutablePath;

        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });

                return;
            }

            Notice = Loc.T("startup.fileMissing");
        }
        catch (Exception ex)
        {
            Notice = ex.Message;
        }
    }

    private void Copy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Clipboard.SetText(text);
            Notice = Loc.T("startup.copied");
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; that is not worth an error box.
            Core.Diagnostics.Log.Warn($"Could not copy to the clipboard: {ex.Message}");
        }
    }

    private static void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open Task Manager: {ex.Message}");
        }
    }
}
