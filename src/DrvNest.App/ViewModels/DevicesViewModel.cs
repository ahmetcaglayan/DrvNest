using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using DrvNest.App.Services;
using DrvNest.Core.Models;

namespace DrvNest.App.ViewModels;

/// <summary>
/// The device inventory, joined to the scan results.
///
/// Each row answers both questions at once: is the device working, and is its driver
/// current. Showing only the first was the original design and it left the user unable
/// to tell whether anything could be updated - which is the reason the application
/// exists.
/// </summary>
public sealed class DevicesViewModel : ViewModelBase
{
    public enum DeviceFilter
    {
        All,
        Updatable,
        Problems,
        Missing,
        Generic
    }

    private readonly ObservableCollection<DeviceRow> _all = new();

    private DeviceFilter _filter = DeviceFilter.All;
    private string _search = string.Empty;
    private DeviceRow? _selected;
    private bool _isRescanning;

    public DevicesViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_all);
        View.Filter = Matches;
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceRow.DeviceClass)));

        SetFilterCommand = new RelayCommand(parameter =>
        {
            if (parameter is string name && Enum.TryParse<DeviceFilter>(name, true, out var value))
                Filter = value;
        });

        CopyHardwareIdCommand = new RelayCommand(parameter =>
        {
            if (parameter is DeviceRow row) TryCopy(row.PrimaryHardwareId);
        });

        UpdateAllVisibleCommand = new AsyncRelayCommand(
            UpdateAllVisibleAsync,
            () => UpdatableCount > 0 && !AppHost.Queue.IsRunning);

        // Asks Windows to re-enumerate the device tree. It is the same thing Device
        // Manager's "Scan for hardware changes" does, and it genuinely fixes devices that
        // are waiting on a driver Windows already has in its store.
        RescanHardwareCommand = new AsyncRelayCommand(RescanHardwareAsync, () => !_isRescanning);

        AppEvents.ScanCompleted += result => OnUi(() => Load(result));
        AppEvents.QueueChanged += () => OnUi(() => RelayCommand.RaiseCanExecuteChanged());
        AppEvents.LanguageChanged += () => OnUi(() => Raise(nameof(CountText)));

        if (AppHost.LastScan is not null) Load(AppHost.LastScan);
    }

    public ICollectionView View { get; }

    public RelayCommand SetFilterCommand { get; }
    public RelayCommand CopyHardwareIdCommand { get; }
    public AsyncRelayCommand UpdateAllVisibleCommand { get; }

    /// <summary>Re-enumerates hardware, then rescans. The concrete next step for a
    /// device Windows has not finished setting up.</summary>
    public AsyncRelayCommand RescanHardwareCommand { get; }

    public DeviceFilter Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;
            View.Refresh();
            RaiseAll(nameof(CountText), nameof(IsAll), nameof(IsUpdatable),
                nameof(IsProblems), nameof(IsMissing), nameof(IsGeneric));
        }
    }

    public bool IsAll => Filter == DeviceFilter.All;
    public bool IsUpdatable => Filter == DeviceFilter.Updatable;
    public bool IsProblems => Filter == DeviceFilter.Problems;
    public bool IsMissing => Filter == DeviceFilter.Missing;
    public bool IsGeneric => Filter == DeviceFilter.Generic;

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            View.Refresh();
            Raise(nameof(CountText));
        }
    }

    public DeviceRow? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string CountText => Loc.T("dev.count", View.Cast<object>().Count());

    public bool IsEmpty => _all.Count == 0;

    /// <summary>How many devices in the list have a package waiting for them.</summary>
    public int UpdatableCount => _all.Count(r => r.CanUpdate);

    public bool HasUpdatable => UpdatableCount > 0;

    /// <summary>
    /// Shown when nothing could be checked, so the "up to date" column would be a guess.
    /// </summary>
    public bool SourcesUnchecked => AppHost.LastScan is { HasUsableSource: false };

    public string UpdateAllText => Loc.T("dev.updateAll", UpdatableCount);

    // =====================================================================================

    private void Load(ScanResult result)
    {
        // Index the scan's candidates by device so each row can be joined in one pass
        // rather than scanning the candidate list per device.
        var byDeviceId = new Dictionary<string, UpdateCandidate>(StringComparer.OrdinalIgnoreCase);
        var byHardwareId = new Dictionary<string, UpdateCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in result.Candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.DeviceId))
                byDeviceId.TryAdd(candidate.DeviceId!, candidate);

            foreach (var id in candidate.TargetHardwareIds)
            {
                var key = Formatting.NormalizeHardwareId(id);
                if (key.Length > 0) byHardwareId.TryAdd(key, candidate);
            }
        }

        _all.Clear();

        foreach (var device in result.Devices)
            _all.Add(new DeviceRow(device, FindCandidate(device), result.HasUsableSource));

        View.Refresh();
        RaiseAll(nameof(CountText), nameof(IsEmpty), nameof(UpdatableCount),
            nameof(HasUpdatable), nameof(UpdateAllText), nameof(SourcesUnchecked));

        RelayCommand.RaiseCanExecuteChanged();

        UpdateCandidate? FindCandidate(DeviceItem device)
        {
            if (byDeviceId.TryGetValue(device.DeviceId, out var direct)) return direct;

            foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
            {
                if (byHardwareId.TryGetValue(Formatting.NormalizeHardwareId(id), out var match))
                    return match;
            }

            return null;
        }
    }

    private async Task RescanHardwareAsync()
    {
        _isRescanning = true;
        RelayCommand.RaiseCanExecuteChanged();

        try
        {
            AppEvents.RaiseStatus(Loc.T("dev.rescanning"));
            await Core.Backup.PnpUtil.ScanDevicesAsync().ConfigureAwait(true);

            if (Application.Current?.MainWindow?.DataContext is MainViewModel shell)
                await shell.ScanAsync().ConfigureAwait(true);
        }
        finally
        {
            _isRescanning = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task UpdateAllVisibleAsync()
    {
        var candidates = View.Cast<DeviceRow>()
            .Where(r => r.Candidate is not null)
            .Select(r => r.Candidate!)
            .Distinct()
            .ToList();

        if (candidates.Count == 0) return;

        await AppHost.Queue.StartAsync(candidates).ConfigureAwait(true);
    }

    private bool Matches(object item)
    {
        if (item is not DeviceRow row) return false;

        bool passesFilter = Filter switch
        {
            DeviceFilter.Updatable => row.CanUpdate,
            DeviceFilter.Problems => row.NeedsAttention,
            DeviceFilter.Missing => row.Health == DeviceHealth.DriverMissing,
            DeviceFilter.Generic => row.IsGenericMicrosoftDriver,
            _ => true
        };

        if (!passesFilter) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;

        var needle = Search.Trim();

        return Contains(row.Name, needle)
               || Contains(row.DeviceClass, needle)
               || Contains(row.Device.Manufacturer, needle)
               || Contains(row.DriverProvider, needle)
               || Contains(row.PrimaryHardwareId, needle)
               || Contains(row.DriverVersion, needle);
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null &&
           haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private static void TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            AppEvents.RaiseStatus(text);
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; never crash over it.
            Core.Diagnostics.Log.Warn($"Clipboard copy failed: {ex.Message}");
        }
    }
}
