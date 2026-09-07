using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using Hexnest.App.Services;
using Hexnest.Core.Models;

namespace Hexnest.App.ViewModels;

/// <summary>
/// One row in the update list.
///
/// Wraps <see cref="UpdateCandidate"/> rather than making the model observable:
/// the model is serialised into the resume session file and should stay a plain
/// data object.
/// </summary>
public sealed class CandidateItem : ViewModelBase
{
    private bool _isSelected;

    public CandidateItem(UpdateCandidate candidate)
    {
        Candidate = candidate;
        _isSelected = candidate.IsSelected;
    }

    public UpdateCandidate Candidate { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            Candidate.IsSelected = value;
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Raised so the parent can recompute the "N selected" summary.</summary>
    public event Action? SelectionChanged;

    public string Title => Candidate.Title;
    public string DeviceName => Candidate.DeviceName;
    public string DeviceClass => Candidate.DeviceClass;
    public string Manufacturer => Candidate.Manufacturer;
    public string SizeDisplay => Candidate.SizeDisplay;
    public bool IsMissing => Candidate.IsMissingDriver;

    /// <summary>True when this swaps Windows' in-box driver for the vendor's own.</summary>
    public bool ReplacesGeneric => Candidate.Reason == OfferReason.ReplacesGeneric;

    public string ProviderName => Candidate.Provider == ProviderKind.WindowsUpdate
        ? "Windows Update"
        : Loc.Language == "tr" ? "Yerel klasör" : "Local folder";

    public string BadgeText => IsMissing
        ? Loc.T("upd.missingBadge")
        : ReplacesGeneric
            ? Loc.T("upd.vendorBadge")
            : Loc.T("upd.updateBadge");

    /// <summary>
    /// Reads as an improvement rather than a downgrade.
    ///
    /// Windows stamps its in-box drivers with the OS build number, so replacing a
    /// generic 10.0.26100.1 with the real Intel 1.41.1420.0 shows a *smaller* number.
    /// Naming what is actually happening beats showing an arrow that looks like a
    /// mistake.
    /// </summary>
    public string VersionTransition => ReplacesGeneric
        ? Loc.T("upd.genericToVendor", Candidate.Manufacturer, Candidate.NewVersion ?? "?")
        : Candidate.VersionTransition;

    public string? ReleaseDateText => Candidate.ReleaseDate?.ToString("yyyy-MM-dd");
}

/// <summary>The update list: what can be installed, and the action that installs it.</summary>
public sealed class UpdatesViewModel : ViewModelBase
{
    private readonly ObservableCollection<CandidateItem> _items = new();
    private string _search = string.Empty;

    public UpdatesViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_items);
        View.Filter = Matches;

        InstallSelectedCommand = new AsyncRelayCommand(InstallSelectedAsync, () => SelectedCount > 0);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));

        HideCommand = new RelayCommand(parameter =>
        {
            if (parameter is not CandidateItem item) return;

            var settings = AppHost.Settings.Current.Clone();
            if (!settings.HiddenUpdateIds.Contains(item.Candidate.ProviderId))
                settings.HiddenUpdateIds.Add(item.Candidate.ProviderId);

            AppHost.Settings.Save(settings);
            _items.Remove(item);
            RefreshSummary();
        });

        IgnoreDeviceCommand = new RelayCommand(parameter =>
        {
            if (parameter is not CandidateItem item) return;

            var settings = AppHost.Settings.Current.Clone();
            foreach (var id in item.Candidate.TargetHardwareIds)
            {
                if (!settings.IgnoredHardwareIds.Contains(id)) settings.IgnoredHardwareIds.Add(id);
            }

            AppHost.Settings.Save(settings);
            _items.Remove(item);
            RefreshSummary();
        });

        AppEvents.ScanCompleted += result => OnUi(() => Load(result));
        AppEvents.LanguageChanged += () => OnUi(RefreshSummary);

        if (AppHost.LastScan is not null) Load(AppHost.LastScan);
    }

    public ICollectionView View { get; }

    public AsyncRelayCommand InstallSelectedCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand HideCommand { get; }
    public RelayCommand IgnoreDeviceCommand { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            View.Refresh();
        }
    }

    public int TotalCount => _items.Count;

    public int SelectedCount => _items.Count(i => i.IsSelected);

    public bool IsEmpty => _items.Count == 0;

    public string SelectionSummary =>
        Loc.T("upd.selected", SelectedCount, Formatting.HumanBytes(SelectedBytes));

    private long SelectedBytes => _items.Where(i => i.IsSelected).Sum(i => i.Candidate.SizeBytes);

    // =====================================================================================

    private void Load(ScanResult result)
    {
        foreach (var item in _items) item.SelectionChanged -= RefreshSummary;
        _items.Clear();

        foreach (var candidate in result.Candidates)
        {
            var item = new CandidateItem(candidate);
            item.SelectionChanged += RefreshSummary;
            _items.Add(item);
        }

        View.Refresh();
        RefreshSummary();
    }

    private async Task InstallSelectedAsync()
    {
        var selected = _items.Where(i => i.IsSelected).Select(i => i.Candidate).ToList();
        if (selected.Count == 0) return;

        await AppHost.Queue.StartAsync(selected).ConfigureAwait(true);
    }

    private void SetAll(bool value)
    {
        foreach (var item in _items) item.IsSelected = value;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        RaiseAll(nameof(SelectedCount), nameof(TotalCount), nameof(IsEmpty), nameof(SelectionSummary));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private bool Matches(object item)
    {
        if (item is not CandidateItem candidate) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;

        var needle = Search.Trim();

        return Contains(candidate.Title, needle)
               || Contains(candidate.DeviceName, needle)
               || Contains(candidate.DeviceClass, needle)
               || Contains(candidate.Manufacturer, needle);
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null &&
           haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
}
