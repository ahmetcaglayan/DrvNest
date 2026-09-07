using System.Collections.ObjectModel;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Monitoring;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The network monitor page.
///
/// Same lifetime rule as the system monitor: nothing is measured until the page is on
/// screen. That matters more here, because the per-application figures come from a
/// child nettop process which is started and stopped with the sampling.
/// </summary>
public sealed class NetworkMonitorViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 120;

    private readonly NetworkMonitor _monitor = new();

    private readonly List<double> _downHistory = new(HistoryLength);
    private readonly List<double> _upHistory = new(HistoryLength);

    private bool _showIdle;
    private bool _disposed;

    public NetworkMonitorViewModel()
    {
        ResetCommand = new RelayCommand(() =>
        {
            _monitor.ResetSession();
            _downHistory.Clear();
            _upHistory.Clear();
        });

        ToggleIdleCommand = new RelayCommand(() => ShowIdle = !ShowIdle);

        _monitor.Sampled += OnSampled;
    }

    public void Start()
    {
        if (!_disposed) _monitor.Start();
    }

    public void Stop() => _monitor.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.Sampled -= OnSampled;
        _monitor.Dispose();
    }

    // =====================================================================================
    // Sampling
    // =====================================================================================

    private void OnSampled(NetworkMetrics metrics, IReadOnlyList<ProcessNetworkMetrics> processes)
        => OnUi(() => Apply(metrics, processes));

    private void Apply(NetworkMetrics metrics, IReadOnlyList<ProcessNetworkMetrics> processes)
    {
        Latest = metrics;

        Push(_downHistory, metrics.DownloadBytesPerSecond);
        Push(_upHistory, metrics.UploadBytesPerSecond);

        // The two charts share one scale, so the relative height of upload against
        // download is readable at a glance rather than being an artefact of two
        // independent autoscales.
        ChartMaximum = Math.Max(64 * 1024, Math.Max(Max(_downHistory), Max(_upHistory)));

        DownHistory = _downHistory.ToArray();
        UpHistory = _upHistory.ToArray();

        RaiseAll(
            nameof(Latest), nameof(DownText), nameof(UpText),
            nameof(SessionDownText), nameof(SessionUpText), nameof(SessionDurationText),
            nameof(TotalDownText), nameof(TotalUpText), nameof(ConnectionText),
            nameof(DownHistory), nameof(UpHistory), nameof(ChartMaximum),
            nameof(PerProcessReady), nameof(PerProcessWaiting));

        SyncAdapters(metrics.Adapters);
        SyncProcesses(processes);
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength) history.RemoveAt(0);
    }

    private static double Max(List<double> values) => values.Count == 0 ? 0 : values.Max();

    // =====================================================================================
    // Totals
    // =====================================================================================

    public NetworkMetrics? Latest { get; private set; }

    public double ChartMaximum { get; private set; } = 64 * 1024;

    public IReadOnlyList<double> DownHistory { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<double> UpHistory { get; private set; } = Array.Empty<double>();

    public string DownText => Rate(Latest?.DownloadBytesPerSecond);

    public string UpText => Rate(Latest?.UploadBytesPerSecond);

    public string SessionDownText =>
        Latest is null ? "-" : Formatting.HumanBytes(Latest.SessionDownloadBytes);

    public string SessionUpText =>
        Latest is null ? "-" : Formatting.HumanBytes(Latest.SessionUploadBytes);

    public string SessionDurationText
    {
        get
        {
            if (Latest is null) return "-";

            var span = Latest.SessionDuration;

            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    public string TotalDownText =>
        Latest is null ? "-" : Formatting.HumanBytes(Latest.TotalDownloadBytes);

    public string TotalUpText =>
        Latest is null ? "-" : Formatting.HumanBytes(Latest.TotalUploadBytes);

    public string ConnectionText => Latest is null ? "-" : Latest.ConnectionCount.ToString("N0");

    /// <summary>
    /// A rate, or a plain dash when nothing is moving. "-/s" is not a unit anybody
    /// reads, and "0 B/s" is a number nobody needs.
    /// </summary>
    private static string Rate(double? value)
    {
        if (value is not > 1) return "-";
        return Formatting.HumanBytes((long)value.Value) + "/s";
    }

    // =====================================================================================
    // Adapters
    // =====================================================================================

    public ObservableCollection<AdapterRow> Adapters { get; } = new();

    private void SyncAdapters(IReadOnlyList<NetworkAdapterMetrics> adapters)
    {
        if (Adapters.Count != adapters.Count)
        {
            Adapters.Clear();
            foreach (var adapter in adapters) Adapters.Add(new AdapterRow(adapter));
            return;
        }

        for (int i = 0; i < adapters.Count; i++) Adapters[i].Update(adapters[i]);
    }

    // =====================================================================================
    // Per application
    // =====================================================================================

    public ObservableCollection<AppNetworkRow> Applications { get; } = new();

    /// <summary>True once nettop has produced a real sample.</summary>
    public bool PerProcessReady => _monitor.PerProcessCountersAvailable;

    /// <summary>
    /// True while nettop is still warming up. It takes a few seconds to produce its
    /// first delta, and a page that showed an empty table in the meantime would look
    /// like it had nothing to report rather than like it was still counting.
    /// </summary>
    public bool PerProcessWaiting => !PerProcessReady;

    public bool ShowIdle
    {
        get => _showIdle;
        set { if (Set(ref _showIdle, value)) Raise(nameof(ShowIdleLabel)); }
    }

    public string ShowIdleLabel => ShowIdle ? Loc.T("net.onlyActive") : Loc.T("net.showIdle");

    private void SyncProcesses(IReadOnlyList<ProcessNetworkMetrics> processes)
    {
        var visible = ShowIdle
            ? processes
            : processes
                .Where(p => p.DownloadBytesPerSecond + p.UploadBytesPerSecond > 0)
                .ToList();

        if (visible.Count == 0 && !ShowIdle) visible = processes.Take(10).ToList();

        if (Applications.Count != visible.Count)
        {
            Applications.Clear();
            foreach (var process in visible) Applications.Add(new AppNetworkRow(process));
        }
        else
        {
            for (int i = 0; i < visible.Count; i++) Applications[i].Update(visible[i]);
        }
    }

    // =====================================================================================
    // Chrome
    // =====================================================================================

    public RelayCommand ResetCommand { get; }

    public RelayCommand ToggleIdleCommand { get; }

    public string Title => Loc.T("net.title");

    public string Subtitle => Loc.T("net.subtitle");

    public string Note => Loc.T("mac.netNote");
}

/// <summary>One network interface.</summary>
public sealed class AdapterRow : ViewModelBase
{
    public AdapterRow(NetworkAdapterMetrics adapter) => Update(adapter);

    public string Name { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public string RateText { get; private set; } = string.Empty;
    public string TotalText { get; private set; } = string.Empty;
    public bool IsUp { get; private set; }

    private static string Rate(double value)
        => value > 1 ? Formatting.HumanBytes((long)value) + "/s" : "-";

    public void Update(NetworkAdapterMetrics adapter)
    {
        Name = $"{adapter.Description}  ·  {adapter.Name}";
        IsUp = adapter.IsUp;

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(adapter.IpAddress)) parts.Add(adapter.IpAddress!);
        if (!string.IsNullOrWhiteSpace(adapter.MacAddress)) parts.Add(adapter.MacAddress!);

        // A link speed is quoted in bits, not bytes: a "300 Mbps" Wi-Fi link shown as
        // 37 MB/s would be right arithmetic and the wrong number entirely.
        if (adapter.LinkSpeedBps is > 0)
            parts.Add($"{adapter.LinkSpeedBps.Value / 1_000_000.0:0.#} Mbps");

        Detail = parts.Count > 0 ? string.Join("  ·  ", parts) : "-";

        RateText = $"↓ {Rate(adapter.DownloadBytesPerSecond)}   ↑ {Rate(adapter.UploadBytesPerSecond)}";

        TotalText = $"↓ {Formatting.HumanBytes(adapter.TotalDownloadBytes)}" +
                    $"   ↑ {Formatting.HumanBytes(adapter.TotalUploadBytes)}";

        RaiseAll(nameof(Name), nameof(Detail), nameof(RateText), nameof(TotalText), nameof(IsUp));
    }
}

/// <summary>One application's share of the network.</summary>
public sealed class AppNetworkRow : ViewModelBase
{
    public AppNetworkRow(ProcessNetworkMetrics process) => Update(process);

    public string Name { get; private set; } = string.Empty;
    public string DownText { get; private set; } = string.Empty;
    public string UpText { get; private set; } = string.Empty;
    public string SessionText { get; private set; } = string.Empty;
    public bool IsSelf { get; private set; }

    public void Update(ProcessNetworkMetrics process)
    {
        Name = process.Name;
        IsSelf = process.IsSelf;
        DownText = Formatting.HumanBytes((long)process.DownloadBytesPerSecond) + "/s";
        UpText = Formatting.HumanBytes((long)process.UploadBytesPerSecond) + "/s";

        SessionText = $"↓ {Formatting.HumanBytes(process.SessionDownloadBytes)}" +
                      $"   ↑ {Formatting.HumanBytes(process.SessionUploadBytes)}";

        RaiseAll(nameof(Name), nameof(DownText), nameof(UpText), nameof(SessionText), nameof(IsSelf));
    }
}
