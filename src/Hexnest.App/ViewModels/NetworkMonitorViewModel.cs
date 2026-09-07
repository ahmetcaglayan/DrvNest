using System.Collections.ObjectModel;
using Hexnest.App.Services;
using Hexnest.Core.Models;
using Hexnest.Core.Monitoring;

namespace Hexnest.App.ViewModels;

/// <summary>One program in the per-application traffic table.</summary>
public sealed class NetProcessRow : ViewModelBase
{
    private double _down;
    private double _up;
    private long _sessionDown;
    private long _sessionUp;
    private int _connections;
    private string _endpoint = string.Empty;

    public NetProcessRow(ProcessNetworkMetrics metrics)
    {
        ProcessId = metrics.ProcessId;
        Name = metrics.Name;
        IsSelf = metrics.IsSelf;
        Apply(metrics);
    }

    public int ProcessId { get; }
    public string Name { get; }
    public bool IsSelf { get; }

    public double DownloadRate
    {
        get => _down;
        private set { if (Set(ref _down, value)) Raise(nameof(DownloadDisplay)); }
    }

    public double UploadRate
    {
        get => _up;
        private set { if (Set(ref _up, value)) Raise(nameof(UploadDisplay)); }
    }

    public long SessionDownload
    {
        get => _sessionDown;
        private set { if (Set(ref _sessionDown, value)) Raise(nameof(SessionDownloadDisplay)); }
    }

    public long SessionUpload
    {
        get => _sessionUp;
        private set { if (Set(ref _sessionUp, value)) Raise(nameof(SessionUploadDisplay)); }
    }

    public int ConnectionCount
    {
        get => _connections;
        private set => Set(ref _connections, value);
    }

    /// <summary>The first remote endpoint, so a row says who it is talking to.</summary>
    public string Endpoint
    {
        get => _endpoint;
        private set => Set(ref _endpoint, value);
    }

    public string DownloadDisplay => DownloadRate < 1 ? "-" : Formatting.HumanSpeed(DownloadRate);
    public string UploadDisplay => UploadRate < 1 ? "-" : Formatting.HumanSpeed(UploadRate);

    public string SessionDownloadDisplay =>
        SessionDownload <= 0 ? "-" : Formatting.HumanBytes(SessionDownload);

    public string SessionUploadDisplay =>
        SessionUpload <= 0 ? "-" : Formatting.HumanBytes(SessionUpload);

    public bool IsActive => DownloadRate >= 1 || UploadRate >= 1;

    public void Apply(ProcessNetworkMetrics metrics)
    {
        DownloadRate = metrics.DownloadBytesPerSecond;
        UploadRate = metrics.UploadBytesPerSecond;
        SessionDownload = metrics.SessionDownloadBytes;
        SessionUpload = metrics.SessionUploadBytes;
        ConnectionCount = metrics.ConnectionCount;
        Endpoint = metrics.RemoteEndpoints.Count > 0 ? metrics.RemoteEndpoints[0] : string.Empty;

        Raise(nameof(IsActive));
    }
}

/// <summary>One network adapter row.</summary>
public sealed class AdapterRow : ViewModelBase
{
    private double _down;
    private double _up;
    private bool _isUp;

    public AdapterRow(NetworkAdapterMetrics metrics)
    {
        Id = metrics.Id;
        Name = metrics.Name;
        Description = metrics.Description;
        Kind = metrics.Kind;
        IpAddress = metrics.IpAddress;
        MacAddress = metrics.MacAddress;
        LinkSpeedBps = metrics.LinkSpeedBps;
        Apply(metrics);
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Kind { get; }
    public string? IpAddress { get; }
    public string? MacAddress { get; }
    public long? LinkSpeedBps { get; }

    public bool IsUp
    {
        get => _isUp;
        private set => Set(ref _isUp, value);
    }

    public double DownloadRate
    {
        get => _down;
        private set { if (Set(ref _down, value)) Raise(nameof(RateDisplay)); }
    }

    public double UploadRate
    {
        get => _up;
        private set { if (Set(ref _up, value)) Raise(nameof(RateDisplay)); }
    }

    public string RateDisplay =>
        DownloadRate < 1 && UploadRate < 1
            ? "-"
            : $"↓ {Formatting.HumanSpeed(DownloadRate)}   ↑ {Formatting.HumanSpeed(UploadRate)}";

    public string LinkDisplay => LinkSpeedBps is not { } speed || speed <= 0
        ? "-"
        : speed >= 1_000_000_000
            ? $"{speed / 1_000_000_000.0:0.#} Gbps"
            : $"{speed / 1_000_000} Mbps";

    public string Subtitle
    {
        get
        {
            var parts = new List<string>(3) { Kind };

            if (!string.IsNullOrWhiteSpace(IpAddress)) parts.Add(IpAddress!);
            if (LinkSpeedBps is > 0) parts.Add(LinkDisplay);

            return string.Join("  ·  ", parts);
        }
    }

    public void Apply(NetworkAdapterMetrics metrics)
    {
        IsUp = metrics.IsUp;
        DownloadRate = metrics.DownloadBytesPerSecond;
        UploadRate = metrics.UploadBytesPerSecond;
    }
}

/// <summary>
/// The network panel: what the whole machine is transferring, and what each program
/// is responsible for.
///
/// The two halves of this page are measured differently on purpose, and the page says
/// so: the machine total comes from the adapters and covers every protocol, while the
/// per-program table comes from TCP connection statistics and therefore does not
/// include UDP traffic such as QUIC, video calls or DNS. Presenting them as one number
/// would be tidier and wrong.
/// </summary>
public sealed class NetworkMonitorViewModel : ViewModelBase, IDisposable
{
    private const int DefaultRowLimit = 30;

    private readonly NetworkMonitor _monitor = new();
    private readonly Dictionary<int, NetProcessRow> _rows = new();
    private readonly Dictionary<string, AdapterRow> _adapters = new(StringComparer.Ordinal);

    private NetworkMetrics? _metrics;
    private bool _disposed;

    private string _search = string.Empty;
    private bool _isPaused;
    private bool _showAll;
    private bool _activeOnly = true;
    private bool _showAllAdapters;
    private int _totalRows;
    private IReadOnlyList<NetworkAdapterMetrics> _lastAdapters = Array.Empty<NetworkAdapterMetrics>();

    public NetworkMonitorViewModel()
    {
        _monitor.Sampled += OnSampled;

        PauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        ResetCommand = new RelayCommand(ResetSession);
        ShowAllCommand = new RelayCommand(() => ShowAll = !ShowAll);
        ActiveOnlyCommand = new RelayCommand(() => ActiveOnly = !ActiveOnly);
        ToggleAdaptersCommand = new RelayCommand(() => ShowAllAdapters = !ShowAllAdapters);

        AppEvents.LanguageChanged += () => OnUi(RefreshTexts);
    }

    /// <summary>Starts sampling when the page becomes visible.</summary>
    public void Activate()
    {
        if (!_disposed) _monitor.Start();
    }

    /// <summary>
    /// Stops sampling when the page is navigated away from.
    ///
    /// The session totals deliberately survive: someone who opens the page, starts a
    /// download and comes back expects the counter to have kept running for the adapters,
    /// which it has - the adapter counters are cumulative and are re-read on the next
    /// sample. Only the per-connection sampling pauses.
    /// </summary>
    public void Deactivate() => _monitor.Stop();

    public RelayCommand PauseCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ActiveOnlyCommand { get; }
    public RelayCommand ToggleAdaptersCommand { get; }

    public ObservableCollection<NetProcessRow> Processes { get; } = new();
    public ObservableCollection<AdapterRow> Adapters { get; } = new();

    // =====================================================================================
    // Live numbers
    // =====================================================================================

    public double DownloadRate => _metrics?.DownloadBytesPerSecond ?? 0;
    public double UploadRate => _metrics?.UploadBytesPerSecond ?? 0;

    public string DownloadDisplay => Formatting.HumanSpeed(DownloadRate);
    public string UploadDisplay => Formatting.HumanSpeed(UploadRate);

    /// <summary>
    /// Both charts share one scale so a 2 MB/s download does not look the same height as
    /// a 40 KB/s upload. Rounded up to a sensible ceiling so the axis does not jitter.
    /// </summary>
    public double ChartMaximum
    {
        get
        {
            double peak = Math.Max(DownloadRate, UploadRate);

            if (peak <= 64 * 1024) return 64 * 1024;
            if (peak <= 512 * 1024) return 512 * 1024;
            if (peak <= 2 * 1024 * 1024) return 2 * 1024 * 1024;
            if (peak <= 10 * 1024 * 1024) return 10 * 1024 * 1024;

            return Math.Ceiling(peak / (10 * 1024 * 1024)) * 10 * 1024 * 1024;
        }
    }

    public string SessionDownloadDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.SessionDownloadBytes);

    public string SessionUploadDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.SessionUploadBytes);

    public string SessionTotalDisplay => _metrics is null
        ? "-"
        : Formatting.HumanBytes(_metrics.SessionDownloadBytes + _metrics.SessionUploadBytes);

    public string SessionDurationDisplay => _metrics is null
        ? "-"
        : Formatting.HumanDuration(_metrics.SessionDuration);

    public string BootDownloadDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.TotalDownloadBytes);

    public string BootUploadDisplay =>
        _metrics is null ? "-" : Formatting.HumanBytes(_metrics.TotalUploadBytes);

    public string ConnectionCountDisplay => _metrics?.ConnectionCount.ToString() ?? "-";

/// <summary>False when TCP ESTATS could not be enabled on this machine.</summary>
    public bool HasPerProcessBytes => _monitor.PerProcessCountersAvailable;

    /// <summary>
    /// The footnote under a working table: what the per-application numbers do and do not
    /// cover. Shown only when there are numbers to qualify.
    /// </summary>
    public string PerProcessNotice => Loc.T("net.tcpOnlyNote");

    /// <summary>
    /// Shown instead, directly above the table, when the counters could not be enabled -
    /// which is what a column of dashes actually means, and it is not obvious from the
    /// dashes.
    /// </summary>
    public string UnavailableNotice => Loc.T("net.noPerProcessBytes");

    // =====================================================================================
    // Table controls
    // =====================================================================================

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) RebuildRows(); }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set { if (Set(ref _isPaused, value)) Raise(nameof(PauseLabel)); }
    }

    public string PauseLabel => Loc.T(IsPaused ? "mon.resume" : "mon.pause");

    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (!Set(ref _showAll, value)) return;

            Raise(nameof(ShowAllLabel));
            RebuildRows();
        }
    }

    public string ShowAllLabel => Loc.T(ShowAll ? "mon.showTop" : "mon.showAll", DefaultRowLimit);

    /// <summary>Hides the many processes that merely hold an idle connection open.</summary>
    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (!Set(ref _activeOnly, value)) return;

            Raise(nameof(ActiveOnlyLabel));
            RebuildRows();
        }
    }

    public string ActiveOnlyLabel => Loc.T(ActiveOnly ? "net.showIdle" : "net.onlyActive");

    /// <summary>
    /// A typical laptop reports six adapters and has one plugged in. The five
    /// disconnected ones are hidden by default so the list says something.
    /// </summary>
    public bool ShowAllAdapters
    {
        get => _showAllAdapters;
        set
        {
            if (!Set(ref _showAllAdapters, value)) return;

            Raise(nameof(AdapterToggleLabel));
            SyncAdapters(_lastAdapters);
        }
    }

    public string AdapterToggleLabel =>
        Loc.T(ShowAllAdapters ? "net.onlyConnected" : "net.allAdapters");

    public string RowCountDisplay => Loc.T("mon.showing", Processes.Count, _totalRows);

    // =====================================================================================

    private void OnSampled(NetworkMetrics metrics, IReadOnlyList<ProcessNetworkMetrics> processes)
        => OnUi(() => Apply(metrics, processes));

    private void Apply(NetworkMetrics metrics, IReadOnlyList<ProcessNetworkMetrics> processes)
    {
        if (_disposed) return;

        _metrics = metrics;

        SyncAdapters(metrics.Adapters);

        if (!IsPaused)
        {
            var alive = new HashSet<int>(processes.Count);

            foreach (var row in processes)
            {
                alive.Add(row.ProcessId);

                if (_rows.TryGetValue(row.ProcessId, out var existing)) existing.Apply(row);
                else _rows[row.ProcessId] = new NetProcessRow(row);
            }

            foreach (var pid in _rows.Keys.Where(pid => !alive.Contains(pid)).ToList())
                _rows.Remove(pid);

            _totalRows = _rows.Count;
            RebuildRows();
        }

        RefreshTexts();
    }

    private void RebuildRows()
    {
        if (_disposed) return;

        IEnumerable<NetProcessRow> query = _rows.Values;

        var term = Search.Trim();

        if (term.Length > 0)
        {
            query = query.Where(row =>
                row.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.ProcessId.ToString().Contains(term, StringComparison.Ordinal) ||
                row.Endpoint.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // A process that has transferred something this session stays visible even while
        // it is momentarily idle, so a download that pauses does not vanish from the list.
        //
        // Skipped entirely when the byte counters could not be enabled: without them
        // every row reads as zero bytes, and filtering on "active" would empty the table
        // completely rather than falling back to the connection counts it does have.
        if (ActiveOnly && HasPerProcessBytes)
            query = query.Where(row => row.IsActive || row.SessionDownload + row.SessionUpload > 0);

        query = query
            .OrderByDescending(row => row.DownloadRate + row.UploadRate)
            .ThenByDescending(row => row.SessionDownload + row.SessionUpload)
            .ThenByDescending(row => row.ConnectionCount);

        if (!ShowAll) query = query.Take(DefaultRowLimit);

        ListSync.Apply(Processes, query.ToList());

        Raise(nameof(RowCountDisplay));
    }

    private void SyncAdapters(IReadOnlyList<NetworkAdapterMetrics> adapters)
    {
        _lastAdapters = adapters;

        var ordered = new List<AdapterRow>(adapters.Count);

        foreach (var metrics in adapters)
        {
            // Rows are still created for hidden adapters so their rate history survives
            // the toggle being flipped back on.
            if (!ShowAllAdapters && !metrics.IsUp)
            {
                if (_adapters.TryGetValue(metrics.Id, out var hidden)) hidden.Apply(metrics);
                continue;
            }

            if (_adapters.TryGetValue(metrics.Id, out var row)) row.Apply(metrics);
            else _adapters[metrics.Id] = row = new AdapterRow(metrics);

            ordered.Add(row);
        }

        ListSync.Apply(Adapters, ordered);
    }

    private void ResetSession()
    {
        _monitor.ResetSession();
        _rows.Clear();
        Processes.Clear();
        _totalRows = 0;

        RefreshTexts();
    }

    private void RefreshTexts() => RaiseAll(
        nameof(DownloadRate), nameof(UploadRate),
        nameof(DownloadDisplay), nameof(UploadDisplay), nameof(ChartMaximum),
        nameof(SessionDownloadDisplay), nameof(SessionUploadDisplay),
        nameof(SessionTotalDisplay), nameof(SessionDurationDisplay),
        nameof(BootDownloadDisplay), nameof(BootUploadDisplay),
        nameof(ConnectionCountDisplay),
        nameof(HasPerProcessBytes), nameof(PerProcessNotice), nameof(UnavailableNotice),
        nameof(PauseLabel), nameof(ShowAllLabel), nameof(ActiveOnlyLabel),
        nameof(AdapterToggleLabel), nameof(RowCountDisplay));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.Sampled -= OnSampled;
        _monitor.Dispose();
    }
}
