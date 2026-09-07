using System.Collections.ObjectModel;
using System.Diagnostics;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Startup;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The start-up manager page.
///
/// Reads launchd and lets the user turn their own login agents off. Nothing here
/// writes anything until a switch is actually moved, and moving a switch never deletes
/// a property list - it records an override, which is what makes every change on this
/// page reversible.
/// </summary>
public sealed class StartupViewModel : ViewModelBase
{
    private readonly List<StartupEntry> _all = new();

    private string _filter = "all";
    private bool _isLoading;
    private string _summary = string.Empty;

    public StartupViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        FilterCommand = new RelayCommand(parameter => Filter = parameter as string ?? "all");
        RevealCommand = new RelayCommand(Reveal);

        _ = LoadAsync();
    }

    public ObservableCollection<StartupRow> Entries { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand FilterCommand { get; }

    public RelayCommand RevealCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The counts, short enough to sit on one line beside the filters.</summary>
    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    private string _brokenWarning = string.Empty;

    /// <summary>
    /// The "these point at files that are gone" note, on its own wrapping line.
    ///
    /// It used to be appended to <see cref="Summary"/>, which put a two-line sentence
    /// into a single-line row beside the filter buttons and simply cut it off.
    /// </summary>
    public string BrokenWarning
    {
        get => _brokenWarning;
        private set
        {
            if (Set(ref _brokenWarning, value)) Raise(nameof(HasBrokenWarning));
        }
    }

    public bool HasBrokenWarning => BrokenWarning.Length > 0;

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) ApplyFilter(); }
    }

    public string Title => Loc.T("startup.title");

    public string Subtitle => Loc.T("mac.startupSubtitle");

    public string Explain => Loc.T("mac.startupExplain");

    public string Note => Loc.T("mac.startupNote");

    // =====================================================================================

    private async Task LoadAsync()
    {
        IsLoading = true;
        AppEvents.RaiseStatus(Loc.T("common.loading"));

        try
        {
            var entries = await Task.Run(() => AppHost.Startup.Scan()).ConfigureAwait(true);

            _all.Clear();
            _all.AddRange(entries);

            ApplyFilter();

            RefreshCounts();
            AppEvents.RaiseStatus(Summary);
        }
        catch (Exception ex)
        {
            Log.Error("Startup scan failed", ex);
            AppEvents.RaiseStatus(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Recomputes the counters from <c>_all</c>. Called after a load and after
    /// every accepted toggle, so the summary never disagrees with the switches.</summary>
    private void RefreshCounts()
    {
        int enabled = _all.Count(e => e.IsEnabled);
        int broken = _all.Count(e => e.IsBroken);

        Summary = Loc.T("startup.summary", _all.Count, enabled, _all.Count - enabled);
        BrokenWarning = broken > 0 ? Loc.T("mac.brokenSummary", broken) : string.Empty;

        Raise(nameof(CountText));
    }

    private void ApplyFilter()
    {
        IEnumerable<StartupEntry> visible = Filter switch
        {
            "enabled" => _all.Where(e => e.IsEnabled),
            "disabled" => _all.Where(e => !e.IsEnabled),
            "broken" => _all.Where(e => e.IsBroken),
            _ => _all
        };

        Entries.Clear();

        // The user's own agents first: they are the ones that can actually be changed,
        // and burying them under twenty system daemons would make the page look
        // read-only when it is not.
        foreach (var entry in visible.OrderBy(e => e.RequiresElevation).ThenBy(e => e.DisplayName))
            Entries.Add(new StartupRow(entry, OnToggled));

        RaiseAll(nameof(Filter), nameof(CountText));
    }

    public string CountText => Loc.T("startup.summary",
        Entries.Count, Entries.Count(e => e.IsEnabled), Entries.Count(e => !e.IsEnabled));

    private async void OnToggled(StartupRow row, bool enabled)
    {
        // Security software gets a question first. Hexnest will still do it - it is the
        // user's machine - but turning off the thing that protects it should not be a
        // single unremarkable click.
        if (!enabled && row.Entry.Caution == StartupCaution.Security)
        {
            bool proceed = await Dialogs.ConfirmAsync(
                row.Entry.DisplayName,
                Loc.T("mac.confirmSecurity", row.Entry.DisplayName),
                DialogKind.Danger);

            if (!proceed)
            {
                row.RevertTo(!enabled);
                return;
            }
        }

        var result = await Task.Run(() => AppHost.Startup.SetEnabled(row.Entry, enabled))
            .ConfigureAwait(true);

        if (result.Success)
        {
            // The new state has to reach the cached entry as well as the row. Filtering
            // rebuilds every row from _all, and StartupRow seeds itself from
            // entry.IsEnabled - so without this, switching to the "Disabled" filter
            // would not list what was just turned off, and switching back to "All"
            // would show the switch on again.
            row.Commit(enabled);

            int index = _all.FindIndex(e => e.Id == row.Entry.Id);
            if (index >= 0) _all[index] = row.Entry;

            RefreshCounts();

            AppEvents.RaiseStatus(
                $"{row.Entry.DisplayName}: {Loc.T(enabled ? "startup.enabled" : "startup.disabled")}");
            return;
        }

        row.RevertTo(!enabled);
        AppEvents.RaiseStatus(result.Error ?? string.Empty);

        await Dialogs.InformAsync(row.Entry.DisplayName, result.Error ?? string.Empty, DialogKind.Warning);
    }

    /// <summary>Opens the folder holding an entry's property list in Finder.</summary>
    private static void Reveal(object? parameter)
    {
        if (parameter is not StartupRow row) return;

        var path = row.Entry.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { "-R", path },
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not reveal '{path}': {ex.Message}");
        }
    }
}

/// <summary>One launchd job, as the page shows it.</summary>
public sealed class StartupRow : ViewModelBase
{
    private readonly Action<StartupRow, bool> _onToggled;
    private bool _isEnabled;
    private bool _suppress;

    public StartupRow(StartupEntry entry, Action<StartupRow, bool> onToggled)
    {
        Entry = entry;
        _onToggled = onToggled;
        _isEnabled = entry.IsEnabled;
    }

    public StartupEntry Entry { get; private set; }

    public string Name => Entry.DisplayName;

    public string Label => Entry.Name;

    public string Publisher => string.IsNullOrWhiteSpace(Entry.Publisher)
        ? Loc.T("startup.unknownPublisher")
        : Entry.Publisher!;

    public string LocationText => Entry.Location switch
    {
        StartupLocation.UserLaunchAgent => Loc.T("mac.locUserAgent"),
        StartupLocation.GlobalLaunchAgent => Loc.T("mac.locGlobalAgent"),
        StartupLocation.LaunchDaemon => Loc.T("mac.locDaemon"),
        _ => Entry.Location.ToString()
    };

    public string Command => Entry.Command;

    public string SizeText => Entry.ExecutableSize > 0
        ? Formatting.HumanBytes(Entry.ExecutableSize)
        : string.Empty;

    public bool IsBroken => Entry.IsBroken;

    public bool IsSystem => Entry.RequiresElevation;

    public bool CanToggle => !Entry.RequiresElevation;

    public bool IsSecurity => Entry.Caution == StartupCaution.Security;

    public string CautionText => Entry.Caution switch
    {
        StartupCaution.Security => Loc.T("startup.cautionSecurity"),
        StartupCaution.SystemOrVendor => Loc.T("startup.cautionVendor"),
        _ => Loc.T("startup.cautionNormal")
    };

    public string BrokenText => Loc.T("startup.fileMissing");

    public string SystemText => Loc.T("mac.systemJob");

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!Set(ref _isEnabled, value) || _suppress) return;
            _onToggled(this, value);
        }
    }

    /// <summary>
    /// Puts the switch back without treating it as a new request. Used when the change
    /// was refused or the user cancelled the confirmation.
    /// </summary>
    public void RevertTo(bool value)
    {
        _suppress = true;
        IsEnabled = value;
        _suppress = false;
    }

    /// <summary>
    /// Records an accepted change on the entry itself. <see cref="StartupEntry.IsEnabled"/>
    /// is init-only - deliberately, it is a snapshot of what launchd said - so the entry
    /// is replaced rather than mutated, exactly as the Windows view model does.
    /// </summary>
    public void Commit(bool enabled)
    {
        Entry = new StartupEntry
        {
            Id = Entry.Id,
            Name = Entry.Name,
            Command = Entry.Command,
            ExecutablePath = Entry.ExecutablePath,
            Publisher = Entry.Publisher,
            Description = Entry.Description,
            Location = Entry.Location,
            Caution = Entry.Caution,
            IsEnabled = enabled,
            IsBroken = Entry.IsBroken,
            ExecutableSize = Entry.ExecutableSize
        };
    }
}
