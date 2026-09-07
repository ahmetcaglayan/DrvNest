using System.Collections.ObjectModel;
using Hexnest.Core.Localization;
using Hexnest.Core.Monitoring;
using Hexnest.Core.Monitoring.Mac;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The battery as a thing that wears out, and the list of processes stopping this Mac
/// from sleeping.
///
/// Deliberately separate from the System Monitor. That page answers "what is happening
/// right now" and samples once a second; nothing here changes on that timescale - a
/// cycle count moves a few times a week - so this page reads on open and on refresh and
/// then sits still. A second live-sampling page would cost battery to tell you about
/// your battery.
/// </summary>
public sealed class PowerViewModel : ViewModelBase
{
    private BatteryHealth? _health;
    private bool _busy;
    private bool _loaded;

    public PowerViewModel()
    {
        RefreshCommand = new RelayCommand(async () => await LoadAsync(), () => !IsBusy);
        _ = LoadAsync();
    }

    public RelayCommand RefreshCommand { get; }

    /// <summary>The assertions worth acting on: an application holding the Mac awake.</summary>
    public ObservableCollection<AssertionRow> Applications { get; } = new();

    /// <summary>
    /// The ones macOS itself holds. Shown, because hiding them would make the page look
    /// like it had missed something, but kept apart from the ones a user can do
    /// something about.
    /// </summary>
    public ObservableCollection<AssertionRow> System { get; } = new();

    // =====================================================================================
    // Loading
    // =====================================================================================

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadAsync()
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            var health = await PowerMonitor.ReadHealthAsync().ConfigureAwait(false);
            var assertions = await PowerMonitor.ReadAssertionsAsync().ConfigureAwait(false);

            OnUi(() =>
            {
                _health = health;
                _loaded = true;

                Applications.Clear();
                System.Clear();

                foreach (var assertion in assertions
                             .OrderByDescending(a => a.Held ?? TimeSpan.Zero))
                {
                    var row = new AssertionRow(assertion);
                    (assertion.IsSystem ? System : Applications).Add(row);
                }

                RaiseAll(nameof(HasBattery), nameof(NoBatteryText), nameof(CycleCount),
                    nameof(MaximumCapacity), nameof(MaximumCapacityNote), nameof(Capacities),
                    nameof(Condition), nameof(ConditionIsGood), nameof(Adapter), nameof(Temperature),
                    nameof(HasTemperature), nameof(SleepSummary), nameof(HasApplications),
                    nameof(HasSystem), nameof(NothingHoldingAwake));
            });
        }
        finally
        {
            OnUi(() => IsBusy = false);
        }
    }

    // =====================================================================================
    // Chrome
    // =====================================================================================

    public string Title => Loc.T("nav.power");

    public string Subtitle => Loc.T("power.subtitle");

    public string Explain => Loc.T("power.explain");

    // =====================================================================================
    // Battery
    // =====================================================================================

    public bool HasBattery => _health is { HasAnything: true };

    /// <summary>Shown instead of the cards on a Mac mini or a Mac Studio.</summary>
    public string NoBatteryText => _loaded ? Loc.T("power.noBattery") : Loc.T("common.loading");

    public string CycleCount => _health?.CycleCount is { } cycles
        ? cycles.ToString("N0")
        : Loc.T("power.unknown");

    public string MaximumCapacity => _health?.MaximumCapacityPercent is { } percent
        ? $"{percent}%"
        : Loc.T("power.unknown");

    /// <summary>
    /// Says where the percentage came from.
    ///
    /// Apple's own figure is not a plain ratio of the two capacities, so when Hexnest
    /// has had to compute one it can differ from System Settings by a point or two. The
    /// page says which it is showing rather than letting the user find the discrepancy.
    /// </summary>
    public string MaximumCapacityNote => _health?.MaximumCapacityPercent is null
        ? string.Empty
        : Loc.T(_health.MaximumCapacityIsDerived ? "power.capacityDerived" : "power.capacityFromMacOs");

    public string Capacities => _health is { DesignCapacityMah: { } design, FullChargeCapacityMah: { } full }
        ? Loc.T("power.capacities", full.ToString("N0"), design.ToString("N0"))
        : string.Empty;

    public string Condition => _health?.Condition is { Length: > 0 } condition
        ? condition
        : Loc.T("power.unknown");

    /// <summary>macOS says "Normal" when it is fine and something else when it is not.</summary>
    public bool ConditionIsGood =>
        _health?.Condition is { Length: > 0 } condition &&
        condition.Equals("Normal", StringComparison.OrdinalIgnoreCase);

    public string Adapter => _health?.AdapterWatts is { } watts
        ? Loc.T("power.adapterWatts", watts.ToString())
        : Loc.T("power.adapterNone");

    public bool HasTemperature => _health?.TemperatureCelsius is not null;

    public string Temperature => _health?.TemperatureCelsius is { } celsius
        ? Loc.T("power.celsius", celsius.ToString("F1"))
        : string.Empty;

    // =====================================================================================
    // Sleep
    // =====================================================================================

    public bool HasApplications => Applications.Count > 0;

    public bool HasSystem => System.Count > 0;

    public bool NothingHoldingAwake => _loaded && Applications.Count == 0;

    public string SleepSummary => Applications.Count switch
    {
        0 => Loc.T("power.sleepClear"),
        1 => Loc.T("power.sleepOne", Applications[0].Process),
        _ => Loc.T("power.sleepMany", Applications.Count.ToString()),
    };
}

/// <summary>One row of the assertion tables.</summary>
public sealed class AssertionRow
{
    public AssertionRow(PowerAssertion assertion)
    {
        Process = assertion.ProcessName;
        ProcessId = assertion.ProcessId.ToString();
        Kind = assertion.Kind;
        Reason = assertion.Name ?? string.Empty;
        Held = assertion.Held is { } held ? Describe(held) : string.Empty;

        Effect = Loc.T(assertion.BlocksSystemSleep ? "power.blocksSystem" : "power.blocksDisplay");
    }

    public string Process { get; }
    public string ProcessId { get; }
    public string Kind { get; }
    public string Reason { get; }
    public string Held { get; }

    /// <summary>Plain words for what the assertion actually prevents.</summary>
    public string Effect { get; }

    /// <summary>
    /// How long an assertion has been held, in the same shape the rest of the
    /// application writes durations.
    ///
    /// These run long: powerd holds one for as long as the machine has been awake, and
    /// six days is an ordinary reading rather than an outlier.
    /// </summary>
    private static string Describe(TimeSpan held) => held.TotalDays >= 1
        ? Loc.T("power.heldDays", (int)held.TotalDays, held.Hours, held.Minutes)
        : held.TotalHours >= 1
            ? Loc.T("power.heldHours", (int)held.TotalHours, held.Minutes)
            : held.TotalMinutes >= 1
                ? Loc.T("power.heldMinutes", (int)held.TotalMinutes)

                // An assertion taken seconds ago would otherwise read "0m", which looks
                // like a bug rather than like something that has only just started - and
                // these are common, because an application takes one the moment it plays
                // a sound or starts a transfer.
                : Loc.T("power.heldMoments");
}
