using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DrvNest.Core.Abstractions;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;
using static DrvNest.Core.Providers.WuaConstants;

namespace DrvNest.Core.Providers;

/// <summary>
/// Driver packages from Microsoft Update, through the Windows Update Agent.
///
/// WUA ships with every Windows installation, so this needs no redistributable and no
/// extra service. Outbound calls go through IDispatch with `dynamic` - see
/// WuaInterop.cs for why there is no tlbimp-generated interop assembly.
///
/// Threading: everything runs on thread-pool threads, which are MTA. WUA delivers its
/// callbacks on RPC threads there with no message pump needed. Calling any of this
/// from the WPF UI thread would deadlock, which is why every entry point wraps its
/// work in Task.Run.
///
/// Concurrency: downloads for different jobs run at the same time because each job
/// gets its own session. Installs are serialised by the job engine, because Windows
/// Update allows exactly one installation at a time and returns
/// WU_E_OPERATIONINPROGRESS to everyone else.
/// </summary>
public sealed partial class WindowsUpdateProvider : IDriverProvider
{
    private const string ClientId = "DrvNest";

    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(150);

    /// <summary>Live update objects from the last search, keyed by UpdateID.</summary>
    private readonly ConcurrentDictionary<string, object> _updateCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _searchGate = new(1, 1);

    private volatile bool _available = true;
    private volatile string? _unavailableReason;
    private string? _resolvedServiceId;
    private bool _serviceResolutionAttempted;

    public ProviderKind Kind => ProviderKind.WindowsUpdate;

    public string DisplayName => "Windows Update";

    public bool IsAvailable => _available;

    public string? UnavailableReason => _unavailableReason;

    /// <summary>Set false by offline and rescue mode.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Include optional, non auto-selected driver offers.</summary>
    public bool IncludeOptional { get; set; } = true;

    // =====================================================================================
    // Search
    // =====================================================================================

    public async Task<IReadOnlyList<UpdateCandidate>> SearchAsync(
        IReadOnlyList<DeviceItem> devices,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            _unavailableReason = "Offline mode is on; Windows Update was not contacted.";
            return Array.Empty<UpdateCandidate>();
        }

        await _searchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SearchCore(devices, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _searchGate.Release();
        }
    }

    private List<UpdateCandidate> SearchCore(
        IReadOnlyList<DeviceItem> devices,
        CancellationToken cancellationToken)
    {
        var candidates = new List<UpdateCandidate>();

        try
        {
            dynamic session = CreateSession();
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = true;

            ApplyServiceSelection(searcher);

            // BrowseOnly=0 drops the "optional" tier; IsHidden=0 respects what the user
            // hid in Windows Update itself.
            var criteria = IncludeOptional
                ? "IsInstalled=0 and Type='Driver' and IsHidden=0"
                : "IsInstalled=0 and Type='Driver' and IsHidden=0 and BrowseOnly=0";

            Log.Info($"Windows Update search: {criteria}");

            var completion = new SearchCompletedCallback();
            dynamic searchJob = searcher.BeginSearch(criteria, completion, new object());

            using (cancellationToken.Register(() => SafeAbort(searchJob)))
            {
                completion.Completed.GetAwaiter().GetResult();
            }

            GC.KeepAlive(completion);
            cancellationToken.ThrowIfCancellationRequested();

            dynamic result = searcher.EndSearch(searchJob);

            int resultCode = (int)result.ResultCode;
            if (!IsSuccess(resultCode))
            {
                _available = false;
                _unavailableReason = $"Windows Update search {DescribeResultCode(resultCode)}.";
                Log.Warn(_unavailableReason);
                return candidates;
            }

            _available = true;
            _unavailableReason = null;

            var index = BuildDeviceIndex(devices);

            dynamic updates = result.Updates;
            int total = (int)updates.Count;
            Log.Info($"Windows Update returned {total} driver update(s).");

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    object update = updates.Item(i);
                    var candidate = MapUpdate(update, index);
                    if (candidate is null) continue;

                    _updateCache[candidate.ProviderId] = update;
                    candidates.Add(candidate);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Skipping Windows Update entry {i}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (COMException ex)
        {
            _available = false;
            _unavailableReason = DescribeComError(ex.HResult, ex.Message);
            Log.Warn($"Windows Update unavailable: {_unavailableReason}");
        }
        catch (Exception ex)
        {
            _available = false;
            _unavailableReason = ex.Message;
            Log.Error("Windows Update search failed", ex);
        }

        return candidates;
    }

    // =====================================================================================
    // Download
    // =====================================================================================

    public Task<ProviderResult> DownloadAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
        => Task.Run(() => DownloadCore(job, progress, cancellationToken), cancellationToken);

    private ProviderResult DownloadCore(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var update = ResolveUpdate(job.Candidate.ProviderId, cancellationToken);
            if (update is null)
                return ProviderResult.Fail(-1, "This update is no longer offered by Windows Update.");

            dynamic dynamicUpdate = update;
            AcceptEulaIfNeeded(dynamicUpdate);

            if ((bool)dynamicUpdate.IsDownloaded)
            {
                progress(new JobProgress(100, job.Candidate.SizeBytes, job.Candidate.SizeBytes,
                    "Already downloaded"));
                return ProviderResult.Ok();
            }

            dynamic session = CreateSession();
            dynamic collection = CreateCollection();
            collection.Add(update);

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            downloader.Priority = DownloadPriorityHigh;

            var lastReport = DateTime.MinValue;

            var progressCallback = new DownloadProgressCallback(report =>
            {
                int percent = (int)report.PercentComplete;

                var now = DateTime.UtcNow;
                if (now - lastReport < ProgressThrottle && percent < 100) return;
                lastReport = now;

                progress(new JobProgress(
                    percent,
                    ToLong(report.TotalBytesDownloaded),
                    ToLong(report.TotalBytesToDownload),
                    "Downloading"));
            });

            var completion = new DownloadCompletedCallback();
            dynamic downloadJob = downloader.BeginDownload(progressCallback, completion, new object());

            using (cancellationToken.Register(() => SafeAbort(downloadJob)))
            {
                completion.Completed.GetAwaiter().GetResult();
            }

            // Both callbacks must outlive the operation: WUA holds only a COM reference,
            // and a collected CCW would take the download down with it.
            GC.KeepAlive(progressCallback);
            GC.KeepAlive(completion);

            dynamic result = downloader.EndDownload(downloadJob);

            if (cancellationToken.IsCancellationRequested)
                return ProviderResult.Fail(-2, "Cancelled.");

            int resultCode = (int)result.ResultCode;

            if (IsSuccess(resultCode))
            {
                progress(new JobProgress(100, job.Candidate.SizeBytes, job.Candidate.SizeBytes,
                    "Downloaded"));
                return ProviderResult.Ok();
            }

            int hresult = SafeInt(() => (int)result.HResult, -1);
            return ProviderResult.Fail(hresult,
                $"Download {DescribeResultCode(resultCode)} (0x{hresult:X8}).");
        }
        catch (OperationCanceledException)
        {
            return ProviderResult.Fail(-2, "Cancelled.");
        }
        catch (COMException ex)
        {
            return ProviderResult.Fail(ex.HResult, DescribeComError(ex.HResult, ex.Message));
        }
        catch (Exception ex)
        {
            Log.Error($"Download failed for {job.Candidate.Title}", ex);
            return ProviderResult.Fail(-1, ex.Message);
        }
    }

    // =====================================================================================
    // Install
    // =====================================================================================

    public Task<ProviderResult> InstallAsync(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
        => Task.Run(() => InstallWithRetries(job, progress, cancellationToken), cancellationToken);

    private ProviderResult InstallWithRetries(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        const int maxBusyRetries = 6;

        for (int attempt = 0; attempt <= maxBusyRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return InstallOnce(job, progress, cancellationToken);
            }
            catch (COMException ex) when (ex.HResult == WU_E_OPERATIONINPROGRESS && attempt < maxBusyRetries)
            {
                // Usually Windows installing its own updates in the background.
                Log.Warn($"Windows Update installer busy; retry {attempt + 1}/{maxBusyRetries} in 10s.");
                progress(new JobProgress(0, 0, 0, "Waiting for Windows Update to become free"));
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
                return ProviderResult.Fail(-2, "Cancelled.");
            }
            catch (COMException ex)
            {
                return ProviderResult.Fail(ex.HResult, DescribeComError(ex.HResult, ex.Message));
            }
            catch (Exception ex)
            {
                Log.Error($"Install failed for {job.Candidate.Title}", ex);
                return ProviderResult.Fail(-1, ex.Message);
            }
        }

        return ProviderResult.Fail(WU_E_OPERATIONINPROGRESS,
            "Windows Update stayed busy. Try again once the system finishes its own updates.");
    }

    private ProviderResult InstallOnce(
        DriverJob job,
        Action<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        var update = ResolveUpdate(job.Candidate.ProviderId, cancellationToken);
        if (update is null)
            return ProviderResult.Fail(-1, "This update is no longer offered by Windows Update.");

        dynamic dynamicUpdate = update;
        AcceptEulaIfNeeded(dynamicUpdate);

        dynamic session = CreateSession();
        dynamic collection = CreateCollection();
        collection.Add(update);

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = collection;

        TrySet(() => installer.AllowSourcePrompts = false);
        TrySet(() => installer.ForceQuiet = true);

        if (SafeBool(() => (bool)installer.RebootRequiredBeforeInstallation, false))
        {
            return new ProviderResult(false, 0, true,
                "Windows must restart before this driver can be installed.");
        }

        var lastReport = DateTime.MinValue;

        var progressCallback = new InstallProgressCallback(report =>
        {
            int percent = (int)report.PercentComplete;

            var now = DateTime.UtcNow;
            if (now - lastReport < ProgressThrottle && percent < 100) return;
            lastReport = now;

            progress(new JobProgress(percent, 0, 0, "Installing"));
        });

        var completion = new InstallCompletedCallback();
        dynamic installJob = installer.BeginInstall(progressCallback, completion, new object());

        // A driver install is deliberately not cancellable once it starts: aborting half
        // way through leaves the device in a worse state than letting it finish.
        completion.Completed.GetAwaiter().GetResult();

        GC.KeepAlive(progressCallback);
        GC.KeepAlive(completion);

        dynamic result = installer.EndInstall(installJob);

        int resultCode = (int)result.ResultCode;
        bool rebootRequired = SafeBool(() => (bool)result.RebootRequired, false);

        if (IsSuccess(resultCode))
        {
            progress(new JobProgress(100, 0, 0, rebootRequired ? "Restart required" : "Installed"));
            return ProviderResult.Ok(rebootRequired);
        }

        int hresult = SafeInt(() => (int)result.HResult, -1);
        return ProviderResult.Fail(hresult,
            $"Install {DescribeResultCode(resultCode)} (0x{hresult:X8}).");
    }

    // =====================================================================================
    // WUA plumbing
    // =====================================================================================

    private static object CreateSession()
    {
        dynamic session = CreateComObject(SessionProgId);
        session.ClientApplicationID = ClientId;
        return session;
    }

    private static object CreateCollection() => CreateComObject(UpdateCollectionProgId);

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);

        if (type is null)
            throw new InvalidOperationException(
                $"The Windows Update Agent is not registered on this machine ({progId}).");

        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException($"Could not create {progId}.");
    }

    /// <summary>
    /// Points the searcher at Microsoft Update, which is where driver offers live.
    /// Registration is best effort: a WSUS-managed machine, or one where the service is
    /// already registered, simply falls through to the default service.
    /// </summary>
    private void ApplyServiceSelection(dynamic searcher)
    {
        if (!_serviceResolutionAttempted)
        {
            _serviceResolutionAttempted = true;
            _resolvedServiceId = TryRegisterMicrosoftUpdate();
        }

        if (_resolvedServiceId is null) return;

        try
        {
            searcher.ServerSelection = ServerSelectionOthers;
            searcher.ServiceID = _resolvedServiceId;
        }
        catch (Exception ex)
        {
            Log.Warn($"Falling back to the default update service: {ex.Message}");
            TrySet(() => searcher.ServerSelection = ServerSelectionDefault);
        }
    }

    private static string? TryRegisterMicrosoftUpdate()
    {
        try
        {
            dynamic manager = CreateComObject(ServiceManagerProgId);
            manager.ClientApplicationID = ClientId;

            dynamic services = manager.Services;
            int count = (int)services.Count;

            for (int i = 0; i < count; i++)
            {
                string id = (string)services.Item(i).ServiceID;

                if (string.Equals(id, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug("Microsoft Update service is already registered.");
                    return MicrosoftUpdateServiceId;
                }
            }

            manager.AddService2(
                MicrosoftUpdateServiceId,
                AddServiceAllowPendingRegistration | AddServiceAllowOnlineRegistration |
                AddServiceRegisterWithAu,
                string.Empty);

            Log.Info("Registered the Microsoft Update service so driver offers are returned.");
            return MicrosoftUpdateServiceId;
        }
        catch (Exception ex)
        {
            // Common on domain-managed machines. The default service still returns drivers.
            Log.Warn($"Could not register Microsoft Update: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the live COM object behind a candidate. After a restart the cache is empty,
    /// so a fresh search repopulates it - this is what makes a resumed session able to
    /// finish what it started.
    /// </summary>
    private object? ResolveUpdate(string updateId, CancellationToken cancellationToken)
    {
        if (_updateCache.TryGetValue(updateId, out var cached)) return cached;

        // Serialised against the scan path: several parallel jobs can arrive here at
        // once after a restart, and one re-query satisfies all of them.
        _searchGate.Wait(cancellationToken);
        try
        {
            if (_updateCache.TryGetValue(updateId, out var raced)) return raced;

            Log.Info("Update not in cache (resumed session?); re-querying Windows Update.");
            SearchCore(Array.Empty<DeviceItem>(), cancellationToken);
        }
        finally
        {
            _searchGate.Release();
        }

        return _updateCache.TryGetValue(updateId, out var found) ? found : null;
    }

    private static void AcceptEulaIfNeeded(dynamic update)
    {
        try
        {
            if (!(bool)update.EulaAccepted) update.AcceptEula();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not accept the EULA: {ex.Message}");
        }
    }

    // =====================================================================================
    // Mapping
    // =====================================================================================

    private static Dictionary<string, DeviceItem> BuildDeviceIndex(IReadOnlyList<DeviceItem> devices)
    {
        var index = new Dictionary<string, DeviceItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
            {
                var key = Formatting.NormalizeHardwareId(id);
                if (key.Length > 0) index.TryAdd(key, device);
            }
        }

        return index;
    }

    private static UpdateCandidate? MapUpdate(object update, Dictionary<string, DeviceItem> deviceIndex)
    {
        dynamic item = update;
        dynamic identity = item.Identity;

        var candidate = new UpdateCandidate
        {
            Provider = ProviderKind.WindowsUpdate,
            ProviderId = (string)identity.UpdateID,
            ProviderRevision = SafeInt(() => (int)identity.RevisionNumber, 0),
            Title = SafeString(() => (string)item.Title) ?? "Driver update",
            Description = SafeString(() => (string)item.Description) ?? string.Empty,
            SizeBytes = SafeLong(() => ToLong(item.MaxDownloadSize)),
            RebootRequired = SafeBool(() => (bool)item.RebootRequired, false),
            ReleaseDate = SafeDate(() => (DateTime)item.LastDeploymentChangeTime),
            MoreInfoUrl = FirstUrl(item)
        };

        // Driver specific metadata lives on IWindowsDriverUpdate. A software update
        // would not expose these, hence the individually guarded reads.
        candidate.Manufacturer = SafeString(() => (string)item.DriverManufacturer) ?? string.Empty;
        candidate.DeviceClass = SafeString(() => (string)item.DriverClass) ?? string.Empty;
        candidate.DeviceName = SafeString(() => (string)item.DriverModel) ?? string.Empty;

        var hardwareId = SafeString(() => (string)item.DriverHardwareID);

        candidate.NewVersion = ReadDriverVersion(item) ?? ExtractVersionFromTitle(candidate.Title);
        candidate.Severity = ResolveSeverity(item);

        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            candidate.TargetHardwareIds.Add(hardwareId!);

            if (deviceIndex.TryGetValue(Formatting.NormalizeHardwareId(hardwareId), out var device))
            {
                candidate.DeviceId = device.DeviceId;
                candidate.CurrentVersion = device.DriverVersion;

                if (string.IsNullOrWhiteSpace(candidate.DeviceName)) candidate.DeviceName = device.Name;
                if (string.IsNullOrWhiteSpace(candidate.DeviceClass)) candidate.DeviceClass = device.DeviceClass;
                if (string.IsNullOrWhiteSpace(candidate.Manufacturer)) candidate.Manufacturer = device.Manufacturer;

                // Not a raw version comparison. A device that fell back to Microsoft's
                // in-box driver reports the OS build as its version, which would make
                // every real vendor package look like a downgrade and get discarded.
                var reason = DriverComparison.Evaluate(
                    device, candidate.Manufacturer, candidate.NewVersion);

                if (reason == OfferReason.None)
                {
                    Log.Debug($"Not offering {candidate.Title} for {device.Name}: " +
                              $"installed {candidate.CurrentVersion} is not improved on.");
                    return null;
                }

                candidate.Reason = reason;

                if (reason == OfferReason.ReplacesGeneric && candidate.Severity < UpdateSeverity.Recommended)
                    candidate.Severity = UpdateSeverity.Recommended;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate.DeviceName)) candidate.DeviceName = candidate.Title;
        if (string.IsNullOrWhiteSpace(candidate.DeviceClass)) candidate.DeviceClass = "Driver";

        return candidate;
    }

    /// <summary>
    /// Reads the real DriverVer version out of the update's driver entries.
    /// Not every WUA build exposes WindowsDriverUpdateEntries, so a failure here falls
    /// back to parsing the title rather than failing the whole scan.
    /// </summary>
    private static string? ReadDriverVersion(dynamic update)
    {
        try
        {
            dynamic entries = update.WindowsDriverUpdateEntries;
            if (entries is null || (int)entries.Count == 0) return null;

            string? version = (string?)entries.Item(0).DriverVerVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>WU driver titles end with the version, e.g. "Intel - Net - 23.40.1.2".</summary>
    private static string? ExtractVersionFromTitle(string title)
    {
        var match = VersionPattern().Match(title);
        return match.Success ? match.Value : null;
    }

    private static UpdateSeverity ResolveSeverity(dynamic update)
    {
        var msrc = SafeString(() => (string)update.MsrcSeverity);

        if (!string.IsNullOrWhiteSpace(msrc))
        {
            if (msrc!.Equals("Critical", StringComparison.OrdinalIgnoreCase)) return UpdateSeverity.Critical;
            if (msrc.Equals("Important", StringComparison.OrdinalIgnoreCase)) return UpdateSeverity.Important;
        }

        return SafeBool(() => (bool)update.AutoSelectOnWebSites, false)
            ? UpdateSeverity.Recommended
            : UpdateSeverity.Optional;
    }

    private static string? FirstUrl(dynamic update)
    {
        try
        {
            dynamic urls = update.MoreInfoUrls;
            return (int)urls.Count > 0 ? (string)urls.Item(0) : null;
        }
        catch
        {
            return null;
        }
    }

    // =====================================================================================
    // Small helpers
    //
    // Every read from a COM object is guarded: a property that a particular WUA build
    // does not expose throws at the binder, and one missing field should never cost the
    // user the whole scan.
    // =====================================================================================

    private static long ToLong(dynamic value)
    {
        try
        {
            decimal number = (decimal)value;
            return number <= 0 ? 0 : (long)Math.Min(number, long.MaxValue);
        }
        catch
        {
            return 0;
        }
    }

    private static string? SafeString(Func<string> getter)
    {
        try
        {
            var value = getter();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int SafeInt(Func<int> getter, int fallback)
    {
        try { return getter(); } catch { return fallback; }
    }

    private static long SafeLong(Func<long> getter)
    {
        try { return getter(); } catch { return 0; }
    }

    private static bool SafeBool(Func<bool> getter, bool fallback)
    {
        try { return getter(); } catch { return fallback; }
    }

    private static DateTime? SafeDate(Func<DateTime> getter)
    {
        try { return getter(); } catch { return null; }
    }

    private static void TrySet(Action setter)
    {
        try { setter(); } catch { /* optional property on this WUA build */ }
    }

    private static void SafeAbort(dynamic job)
    {
        try { job.RequestAbort(); } catch { /* already finished */ }
    }

    /// <summary>Turns the HRESULTs users actually hit into something actionable.</summary>
    private static string DescribeComError(int hresult, string message) => hresult switch
    {
        WU_E_NO_SERVICE =>
            "The Windows Update service is disabled. Start 'Windows Update' (wuauserv) and try again.",
        WU_E_OPERATIONINPROGRESS =>
            "Windows Update is busy with another operation. Try again in a moment.",
        WU_E_LEGACYSERVER =>
            "This machine is pointed at a WSUS server that does not offer driver updates.",
        unchecked((int)0x80072EE2) or unchecked((int)0x80072EFD) or unchecked((int)0x80072EE7) =>
            "Could not reach Microsoft Update. Check the network connection, or use Rescue Mode " +
            "to install drivers from a local folder.",
        unchecked((int)0x80240438) =>
            "A policy on this machine blocks driver updates from Windows Update.",
        unchecked((int)0x8024402C) =>
            "Windows Update could not resolve its server. Check the DNS or proxy settings.",
        _ => $"{message} (0x{hresult:X8})"
    };

    [GeneratedRegex(@"\b\d+\.\d+\.\d+(\.\d+)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
