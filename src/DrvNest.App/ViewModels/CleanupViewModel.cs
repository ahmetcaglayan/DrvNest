using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using DrvNest.App.Services;
using DrvNest.Core.Cleanup;
using DrvNest.Core.Models;

namespace DrvNest.App.ViewModels;

/// <summary>One reviewable file or folder inside a target, with its own tick box.</summary>
public sealed class CleanupItemRow : ViewModelBase
{
    private readonly Action _changed;
    private bool _isSelected;

    public CleanupItemRow(CleanupItem item, Action changed)
    {
        Item = item;
        _changed = changed;
    }

    public CleanupItem Item { get; }

    public string Path => Item.Path;
    public string Name => Item.Name;
    public long Bytes => Item.Bytes;
    public bool IsDirectory => Item.IsDirectory;

    public string SizeDisplay => Formatting.HumanBytes(Item.Bytes);

    public string AgeDisplay => Loc.T("clean.ageDays", Item.DaysOld);

    public string Tooltip => Item.Path;

    /// <summary>Off until the user ticks it. Every one of these is the user's own file.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) _changed(); }
    }
}

/// <summary>One category row on the cleanup page.</summary>
public sealed class CleanupTargetRow : ViewModelBase
{
    private readonly Action _changed;
    private bool _isSelected;
    private bool _isExpanded;

    public CleanupTargetRow(CleanupTarget target, Action changed)
    {
        Target = target;
        _changed = changed;

        foreach (var item in target.Items) Items.Add(new CleanupItemRow(item, OnItemChanged));
    }

    public CleanupTarget Target { get; }

    public ObservableCollection<CleanupItemRow> Items { get; } = new();

    public string Id => Target.Id;
    public string Title => Loc.T(Target.TitleKey);
    public string Description => Loc.T(Target.DescriptionKey);
    public string? Note => Target.NoteKey is null ? null : Loc.T(Target.NoteKey);
    public bool HasNote => Target.NoteKey is not null;

    public bool IsReview => Target.Risk == CleanupRisk.Review;
    public bool IsModerate => Target.Risk == CleanupRisk.Moderate;

    public string RiskLabel => Loc.T(Target.Risk switch
    {
        CleanupRisk.Review => "clean.riskReview",
        CleanupRisk.Moderate => "clean.riskModerate",
        _ => "clean.riskSafe"
    });

    public long Bytes => IsReview ? Items.Where(i => i.IsSelected).Sum(i => i.Bytes) : Target.Bytes;

    /// <summary>What the whole category holds, whether or not anything is ticked.</summary>
    public string SizeDisplay =>
        Target.Bytes > 0 ? Formatting.HumanBytes(Target.Bytes) : "-";

    public string CountDisplay => Target.Risk == CleanupRisk.Review
        ? Loc.T("clean.itemCount", Items.Count)
        : Loc.T("clean.fileCount", Target.FileCount);

    public bool HasAnything => Target.HasAnything;

    /// <summary>
    /// Never pre-ticked. Not one category, not once - the user asked for it that way and
    /// it is the right default for anything that deletes.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;

            // Ticking a review category ticks nothing by itself; its items are chosen one
            // by one, so the box only opens the list.
            if (IsReview && value) IsExpanded = true;

            _changed();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>The bytes this row contributes to the total, given what is ticked.</summary>
    public long SelectedBytes
    {
        get
        {
            if (!IsSelected) return 0;
            return IsReview ? Items.Where(i => i.IsSelected).Sum(i => i.Bytes) : Target.Bytes;
        }
    }

    public IReadOnlyList<string> SelectedPaths =>
        Items.Where(i => i.IsSelected).Select(i => i.Path).ToList();

    private void OnItemChanged()
    {
        Raise(nameof(SelectedBytes));
        _changed();
    }

    public void RefreshTexts() => RaiseAll(
        nameof(Title), nameof(Description), nameof(Note), nameof(RiskLabel),
        nameof(SizeDisplay), nameof(CountDisplay));
}

/// <summary>
/// The cleanup page.
///
/// Three rules shape all of it, and they are the difference between this and the genre it
/// belongs to:
///
///   * Nothing is selected by default. The page opens with a total of zero.
///   * The number next to a category is measured, not estimated, so the total at the top
///     is the number of bytes that will actually come back.
///   * Anything that is the user's own file - old downloads, folders left behind by
///     uninstalled software - is listed item by item and goes to the Recycle Bin.
/// </summary>
public sealed class CleanupViewModel : ViewModelBase
{
    private readonly CleanupScanner _scanner = new();
    private readonly CleanupService _service = new();
    private readonly MemoryTrimmer _memory = new();

    private CancellationTokenSource? _scanCancellation;
    private IReadOnlyList<CleanupTarget> _targets = Array.Empty<CleanupTarget>();

    private bool _isScanning;
    private bool _isCleaning;
    private string? _notice;
    private string _scanStatus = string.Empty;
    private long _lastFreed;

    public CleanupViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && !IsCleaning);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => SelectedBytes > 0 && !IsBusy);
        CancelCommand = new RelayCommand(() => _scanCancellation?.Cancel(), () => IsScanning);
        TrimMemoryCommand = new AsyncRelayCommand(TrimMemoryAsync, () => !IsBusy);
        SelectSafeCommand = new RelayCommand(SelectSafe);
        ClearSelectionCommand = new RelayCommand(ClearSelection);

        AppEvents.LanguageChanged += () => OnUi(() =>
        {
            foreach (var row in Targets) row.RefreshTexts();
            RefreshTexts();
        });

        _ = ScanAsync();
    }

    public ObservableCollection<CleanupTargetRow> Targets { get; } = new();

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand TrimMemoryCommand { get; }
    public RelayCommand SelectSafeCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    // =====================================================================================

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (Set(ref _isCleaning, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy => IsScanning || IsCleaning;

    public string ScanStatus
    {
        get => _scanStatus;
        private set => Set(ref _scanStatus, value);
    }

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    /// <summary>Everything the scan found, whether or not it is ticked.</summary>
    public long TotalBytes => _targets.Sum(t => t.Bytes);

    public string TotalDisplay => TotalBytes > 0 ? Formatting.HumanBytes(TotalBytes) : "-";

    /// <summary>What the current selection would free. The headline number on the page.</summary>
    public long SelectedBytes => Targets.Sum(t => t.SelectedBytes);

    public string SelectedDisplay =>
        SelectedBytes > 0 ? Formatting.HumanBytes(SelectedBytes) : "0 B";

    public bool HasSelection => SelectedBytes > 0;

    public string CleanButtonLabel => SelectedBytes > 0
        ? Loc.T("clean.cleanSelected", Formatting.HumanBytes(SelectedBytes))
        : Loc.T("clean.nothingSelected");

    public string LastFreedDisplay =>
        _lastFreed > 0 ? Loc.T("clean.freed", Formatting.HumanBytes(_lastFreed)) : string.Empty;

    public bool HasResult => _lastFreed > 0;

    // =====================================================================================
    // Scanning
    // =====================================================================================

    public async Task ScanAsync()
    {
        if (IsBusy) return;

        IsScanning = true;
        Notice = null;
        _lastFreed = 0;
        ScanStatus = Loc.T("clean.scanning");

        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;

        try
        {
            var targets = await Task.Run(() => _scanner.Scan(
                id => OnUi(() => ScanStatus = Loc.T("clean.scanningTarget", LookupTitle(id))),
                token), token).ConfigureAwait(true);

            _targets = targets;

            Targets.Clear();

            foreach (var target in targets)
            {
                // A category with nothing in it is noise on the page.
                if (!target.HasAnything) continue;

                Targets.Add(new CleanupTargetRow(target, OnSelectionChanged));
            }

            ScanStatus = string.Empty;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = string.Empty;
            Notice = Loc.T("common.cancel");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Cleanup scan failed", ex);
            Notice = ex.Message;
            ScanStatus = string.Empty;
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            IsScanning = false;

            RefreshTexts();
        }
    }

    private string LookupTitle(string id)
    {
        var row = _targets.FirstOrDefault(t => t.Id == id);
        return row is null ? id : Loc.T(row.TitleKey);
    }

    // =====================================================================================
    // Cleaning
    // =====================================================================================

    private async Task CleanAsync()
    {
        if (IsBusy || SelectedBytes <= 0) return;

        var selected = Targets.Where(t => t.IsSelected).ToList();

        // Review categories only count when individual items are ticked inside them.
        var chosen = selected
            .Where(t => !t.IsReview || t.SelectedPaths.Count > 0)
            .ToList();

        if (chosen.Count == 0)
        {
            Notice = Loc.T("clean.pickItems");
            return;
        }

        var confirmation = MessageBox.Show(
            Loc.T("clean.confirm", Formatting.HumanBytes(SelectedBytes), chosen.Count),
            Loc.T("nav.clean"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        IsCleaning = true;
        Notice = null;

        try
        {
            var targets = chosen.Select(t => t.Target).ToList();

            var items = chosen
                .Where(t => t.IsReview)
                .ToDictionary(t => t.Id, t => t.SelectedPaths, StringComparer.Ordinal);

            var report = await Task.Run(() => _service.Clean(
                targets,
                items,
                progress => OnUi(() => ScanStatus =
                    Loc.T("clean.cleaningTarget", LookupTitle(progress.TargetId),
                          progress.Completed, progress.Total)))).ConfigureAwait(true);

            _lastFreed = report.BytesFreed;

            Notice = report.Failures.Count == 0
                ? Loc.T("clean.done", Formatting.HumanBytes(report.BytesFreed),
                        report.FilesDeleted, report.FilesSkipped)
                : Loc.T("clean.donePartial", Formatting.HumanBytes(report.BytesFreed),
                        report.FilesSkipped, report.Failures.Count);

            ScanStatus = string.Empty;

            // Re-measure so the page shows what is left rather than what used to be there.
            await ScanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Cleanup failed", ex);
            Notice = ex.Message;
        }
        finally
        {
            IsCleaning = false;
            ScanStatus = string.Empty;
            RefreshTexts();
        }
    }

    // =====================================================================================
    // Memory
    // =====================================================================================

    private async Task TrimMemoryAsync()
    {
        if (IsBusy) return;

        IsCleaning = true;
        ScanStatus = Loc.T("clean.trimming");

        try
        {
            var result = await Task.Run(() => _memory.Trim()).ConfigureAwait(true);

            Notice = result.BytesReleased > 0
                ? Loc.T("clean.trimmed",
                        Formatting.HumanBytes(result.BytesReleased), result.ProcessesTrimmed)
                : Loc.T("clean.trimmedNothing", result.ProcessesTrimmed);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Memory trim failed: {ex.Message}");
            Notice = ex.Message;
        }
        finally
        {
            IsCleaning = false;
            ScanStatus = string.Empty;
        }
    }

    // =====================================================================================
    // Selection
    // =====================================================================================

    /// <summary>
    /// Ticks the categories that are pure caches. Deliberately does not touch the
    /// Moderate ones and never touches the two that hold the user's own files.
    /// </summary>
    private void SelectSafe()
    {
        foreach (var row in Targets)
            row.IsSelected = row.Target.Risk == CleanupRisk.Safe;
    }

    private void ClearSelection()
    {
        foreach (var row in Targets)
        {
            row.IsSelected = false;
            foreach (var item in row.Items) item.IsSelected = false;
        }
    }

    private void OnSelectionChanged() => RaiseAll(
        nameof(SelectedBytes), nameof(SelectedDisplay), nameof(HasSelection),
        nameof(CleanButtonLabel));

    private void RefreshTexts()
    {
        RaiseAll(nameof(TotalBytes), nameof(TotalDisplay), nameof(SelectedBytes),
                 nameof(SelectedDisplay), nameof(HasSelection), nameof(CleanButtonLabel),
                 nameof(LastFreedDisplay), nameof(HasResult), nameof(IsBusy));

        RelayCommand.RaiseCanExecuteChanged();
    }
}
