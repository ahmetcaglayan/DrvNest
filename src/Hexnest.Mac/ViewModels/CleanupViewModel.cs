using System.Collections.ObjectModel;
using Hexnest.Core.Cleanup;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The clean-up page.
///
/// Two rules carried over verbatim from the Windows build, because they are what make
/// this feature something other than a liability:
///
///   Nothing is ticked when the page opens. Not one row.
///   Every size shown was measured, not estimated, so the number next to a box is the
///   space that will actually come back.
///
/// The measuring is what makes the scan slow - on a well-used Mac it walks several
/// hundred thousand files - and that is the trade being made deliberately.
/// </summary>
public sealed class CleanupViewModel : ViewModelBase
{
    private readonly List<CleanupTarget> _targets = new();

    private CancellationTokenSource? _cancellation;
    private bool _isScanning;
    private bool _isCleaning;
    private bool _hasScanned;
    private string _progressText = string.Empty;

    public CleanupViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !IsBusy && SelectedBytes > 0);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
        SelectSafeCommand = new RelayCommand(SelectSafe, () => !IsBusy && HasScanned);
        SelectNoneCommand = new RelayCommand(SelectNone, () => !IsBusy && HasScanned);
    }

    public ObservableCollection<CleanupRow> Rows { get; } = new();

    // =====================================================================================
    // State
    // =====================================================================================

    public bool IsBusy => _isScanning || _isCleaning;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            RaiseAll(nameof(IsBusy), nameof(ShowEmptyState));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (!Set(ref _isCleaning, value)) return;
            Raise(nameof(IsBusy));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            if (Set(ref _hasScanned, value)) Raise(nameof(ShowEmptyState));
        }
    }

    public bool ShowEmptyState => !HasScanned && !IsScanning;

    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    public long FoundBytes => _targets.Sum(t => t.Bytes);

    public string FoundText => Formatting.HumanBytes(FoundBytes);

    public long SelectedBytes => Rows.Where(r => r.IsSelected).Sum(r => r.SelectedBytes);

    public string SelectedText => Formatting.HumanBytes(SelectedBytes);

    public string CleanButtonText => SelectedBytes > 0
        ? Loc.T("clean.cleanSelected", SelectedText)
        : Loc.T("clean.nothingSelected");

    public string Title => Loc.T("clean.title");

    public string Subtitle => Loc.T("clean.subtitle");

    public string Footnote => Loc.T("clean.footnote") + "  " + Loc.T("mac.trashNote");

    /// <summary>
    /// macOS compresses memory continuously and offers no way to trim another process'
    /// working set, so the Windows page's memory card has nothing behind it here. The
    /// explanation is shown once instead of a button that would do nothing.
    /// </summary>
    public string MemoryNote => Loc.T("mac.noMemoryTrim");

    // =====================================================================================
    // Commands
    // =====================================================================================

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectSafeCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    private async Task ScanAsync()
    {
        IsScanning = true;
        ProgressText = Loc.T("clean.scanning");
        _cancellation = new CancellationTokenSource();

        try
        {
            var token = _cancellation.Token;

            var targets = await Task.Run(() => AppHost.CleanupScanner.Scan(
                id => OnUi(() => ProgressText = Loc.T("clean.scanningTarget", TargetTitle(id))),
                token), token).ConfigureAwait(true);

            _targets.Clear();
            _targets.AddRange(targets);

            Rows.Clear();

            // Rows with nothing in them are not shown: a page listing eight categories
            // that all say "0 bytes" reads as a broken scan rather than a clean machine.
            foreach (var target in targets.Where(t => t.HasAnything))
                Rows.Add(new CleanupRow(target, OnSelectionChanged));

            HasScanned = true;
            ProgressText = string.Empty;

            AppEvents.RaiseStatus($"{Loc.T("clean.found")}: {FoundText}");
            RaiseAll(nameof(FoundBytes), nameof(FoundText), nameof(SelectedText), nameof(CleanButtonText));
        }
        catch (OperationCanceledException)
        {
            ProgressText = string.Empty;
            AppEvents.RaiseStatus(Loc.T("common.cancel"));
        }
        catch (Exception ex)
        {
            Log.Error("Cleanup scan failed", ex);
            ProgressText = string.Empty;
            AppEvents.RaiseStatus(ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsScanning = false;
        }
    }

    private async Task CleanAsync()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool proceed = await Dialogs.ConfirmAsync(
            Loc.T("clean.title"),
            Loc.T("mac.cleanConfirm", SelectedText, selected.Count),
            DialogKind.Warning);

        if (!proceed) return;

        IsCleaning = true;
        _cancellation = new CancellationTokenSource();

        try
        {
            var token = _cancellation.Token;
            var targets = selected.Select(r => r.Target).ToList();

            var items = selected
                .Where(r => r.Target.Risk == CleanupRisk.Review)
                .ToDictionary(
                    r => r.Target.Id,
                    r => (IReadOnlyList<string>)r.Items.Where(i => i.IsSelected)
                        .Select(i => i.Item.Path).ToList());

            var report = await Task.Run(() => AppHost.Cleanup.Clean(
                targets,
                items,
                progress => OnUi(() => ProgressText = Loc.T(
                    "clean.cleaningTarget", TargetTitle(progress.TargetId),
                    progress.Completed, progress.Total)),
                token), token).ConfigureAwait(true);

            ProgressText = string.Empty;

            var message = report.Failures.Count == 0
                ? Loc.T("clean.done",
                    Formatting.HumanBytes(report.BytesFreed), report.FilesDeleted, report.FilesSkipped)
                : Loc.T("clean.donePartial",
                    Formatting.HumanBytes(report.BytesFreed), report.FilesSkipped, report.Failures.Count);

            AppEvents.RaiseStatus(message);
            await Dialogs.InformAsync(Loc.T("clean.title"), message);

            // What was just deleted is no longer there, so the page re-measures rather
            // than showing sizes that are now fiction.
            await ScanAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AppEvents.RaiseStatus(Loc.T("common.cancel"));
        }
        catch (Exception ex)
        {
            Log.Error("Cleanup failed", ex);
            AppEvents.RaiseStatus(ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsCleaning = false;
            ProgressText = string.Empty;
        }
    }

    /// <summary>Ticks the pure caches, and only those. Never anything the user owns.</summary>
    private void SelectSafe()
    {
        foreach (var row in Rows) row.IsSelected = row.Target.Risk == CleanupRisk.Safe;
        OnSelectionChanged();
    }

    private void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
            foreach (var item in row.Items) item.IsSelected = false;
        }

        OnSelectionChanged();
    }

    /// <summary>
    /// The scanner reports progress by target id, which is an internal identifier.
    /// The page shows the same title the row will have, so the two agree.
    /// </summary>
    private static string TargetTitle(string id) => id switch
    {
        "usercache" => Loc.T("clean.mac.userCache"),
        "devcache" => Loc.T("clean.mac.devCache"),
        "applogs" => Loc.T("clean.mac.appLogs"),
        "savedstate" => Loc.T("clean.mac.savedState"),
        "trash" => Loc.T("clean.mac.trash"),
        "downloads" => Loc.T("clean.downloads"),
        "iosbackups" => Loc.T("clean.mac.iosBackups"),
        "leftovers" => Loc.T("clean.leftovers"),
        "hexnest" => Loc.T("clean.hexnest"),
        _ => id
    };

    private void OnSelectionChanged()
    {
        RaiseAll(nameof(SelectedBytes), nameof(SelectedText), nameof(CleanButtonText));
        RelayCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>One clean-up category.</summary>
public sealed class CleanupRow : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isSelected;
    private bool _isExpanded;

    public CleanupRow(CleanupTarget target, Action onChanged)
    {
        Target = target;
        _onChanged = onChanged;

        foreach (var item in target.Items) Items.Add(new CleanupItemRow(item, onChanged));
    }

    public CleanupTarget Target { get; }

    public ObservableCollection<CleanupItemRow> Items { get; } = new();

    public string Title => Loc.T(Target.TitleKey);

    public string Description => Loc.T(Target.DescriptionKey);

    public string? Note => string.IsNullOrWhiteSpace(Target.NoteKey) ? null : Loc.T(Target.NoteKey!);

    public bool HasNote => Note is not null;

    public string SizeText => Formatting.HumanBytes(Target.Bytes);

    public string CountText => IsReview
        ? Loc.T("clean.itemCount", Items.Count)
        : Loc.T("clean.fileCount", Target.FileCount);

    public bool IsReview => Target.Risk == CleanupRisk.Review;

    public string RiskText => Target.Risk switch
    {
        CleanupRisk.Safe => Loc.T("clean.riskSafe"),
        CleanupRisk.Moderate => Loc.T("clean.riskModerate"),
        _ => Loc.T("clean.riskReview")
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;

            // Ticking a review category means "show me the list", not "delete all of
            // it". The individual items stay unticked and it is expanded instead.
            if (IsReview && value) IsExpanded = true;

            _onChanged();
            Raise(nameof(SelectedBytes));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>
    /// What ticking this row would actually free. For a review category that is the sum
    /// of the individually ticked items, which is zero until the user picks some.
    /// </summary>
    public long SelectedBytes => IsReview
        ? Items.Where(i => i.IsSelected).Sum(i => i.Item.Bytes)
        : Target.Bytes;
}

/// <summary>One individually reviewable file or folder.</summary>
public sealed class CleanupItemRow : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public CleanupItemRow(CleanupItem item, Action onChanged)
    {
        Item = item;
        _onChanged = onChanged;
    }

    public CleanupItem Item { get; }

    public string Name => Item.Name;

    public string SizeText => Formatting.HumanBytes(Item.Bytes);

    public string AgeText => Loc.T("clean.ageDays", Item.DaysOld);

    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) _onChanged(); }
    }
}
