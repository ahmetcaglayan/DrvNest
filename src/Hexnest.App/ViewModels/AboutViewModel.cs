using System.Diagnostics;
using System.Windows;
using Hexnest.App.Services;
using Hexnest.Core;
using Hexnest.Core.Models;
using Hexnest.Core.Updating;

namespace Hexnest.App.ViewModels;

/// <summary>
/// About and the built-in updater.
///
/// The download is verified against the release's checksums.txt before it is allowed
/// to replace anything, and the swap only ever happens after the user clicks. This
/// executable installs drivers with an elevated token; silently replacing it in the
/// background would be the wrong default no matter how convenient.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    private bool _isChecking;
    private bool _isDownloading;
    private double _downloadPercent;
    private string _downloadText = string.Empty;
    private string? _message;
    private ReleaseInfo? _release;
    private string? _downloadedFile;

    public AboutViewModel()
    {
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking && !IsDownloading);
        DownloadCommand = new AsyncRelayCommand(
            DownloadAndApplyAsync, () => UpdateAvailable && !IsDownloading);

        OpenRepositoryCommand = new RelayCommand(() => OpenUrl(AppInfo.RepositoryUrl));
        OpenIssuesCommand = new RelayCommand(() => OpenUrl(AppInfo.IssuesUrl));
        OpenReleaseCommand = new RelayCommand(
            () => OpenUrl(_release?.HtmlUrl ?? AppInfo.ReleasesUrl), () => _release is not null);

        AppEvents.LanguageChanged += () => OnUi(() => RaiseAll(nameof(Message)));
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public RelayCommand OpenRepositoryCommand { get; }
    public RelayCommand OpenIssuesCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }

    public string ProductName => AppInfo.ProductName;

    /// <summary>Localised; AppInfo.Tagline is the English original used by the CLI.</summary>
    public string Tagline => Loc.T("app.tagline");

    public string Version => AppInfo.Version;
    public string RepositoryUrl => AppInfo.RepositoryUrl;

    /// <summary>
    /// Turns an update check into a localised sentence.
    ///
    /// The service's own Message is English diagnostic text; it is only used as a last
    /// resort, where showing the raw detail beats showing nothing.
    /// </summary>
    private static string Describe(UpdateCheckResult result) => result.Status switch
    {
        UpdateCheckStatus.UpdateAvailable when result.Release is not null
            => Loc.T("about.available", result.Release.Version),
        UpdateCheckStatus.UpToDate => Loc.T("about.upToDate"),
        UpdateCheckStatus.NoReleases => Loc.T("about.noReleases"),
        UpdateCheckStatus.NotConfigured => Loc.T("about.notConfigured"),
        UpdateCheckStatus.NetworkError => Loc.T("about.networkError"),
        _ => result.Message ?? Loc.T("about.checkFailed")
    };

    public string RuntimeSummary =>
        $".NET {Environment.Version}  ·  {Core.Platform.SystemInfo.Current.ProcessArchitecture}";

    public string OsSummary => Core.Platform.SystemInfo.Current.OsDisplay;

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (Set(ref _isChecking, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (Set(ref _isDownloading, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public double DownloadPercent
    {
        get => _downloadPercent;
        private set => Set(ref _downloadPercent, value);
    }

    public string DownloadText
    {
        get => _downloadText;
        private set => Set(ref _downloadText, value);
    }

    public bool UpdateAvailable => _release is not null && _downloadedFile is null;

    public string? ReleaseNotes => _release?.Notes;

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public string? ReleaseTitle =>
        _release is null ? null : $"{_release.Name} ({_release.SizeDisplay})";

    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) Raise(nameof(HasMessage)); }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    // =====================================================================================

    /// <summary>Called at startup when the user has automatic checks enabled.</summary>
    public async Task CheckSilentlyAsync()
    {
        if (!AppInfo.IsRepositoryConfigured) return;

        try
        {
            await CheckAsync().ConfigureAwait(true);
        }
        catch
        {
            // A startup check must never interrupt anything.
        }
    }

    private async Task CheckAsync()
    {
        IsChecking = true;
        Message = Loc.T("about.checking");

        try
        {
            var result = await AppHost.Updater.CheckAsync().ConfigureAwait(true);

            _release = result.UpdateAvailable ? result.Release : null;
            Message = Describe(result);

            RaiseAll(nameof(UpdateAvailable), nameof(ReleaseNotes),
                nameof(HasReleaseNotes), nameof(ReleaseTitle));
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task DownloadAndApplyAsync()
    {
        if (_release is null) return;

        // Replacing the executable while a driver install is in flight would be a
        // spectacular way to break someone's machine.
        if (AppHost.Queue.IsRunning)
        {
            Message = Loc.Language == "tr"
                ? "Kuyruk çalışırken güncelleme yapılamaz."
                : "Cannot update while the queue is running.";
            return;
        }

        IsDownloading = true;
        DownloadPercent = 0;
        Message = Loc.T("about.downloading");

        try
        {
            var progress = new Action<JobProgressLite>(report => OnUi(() =>
            {
                DownloadPercent = report.Percent;
                DownloadText = $"{Formatting.HumanBytes(report.BytesTransferred)} / " +
                               $"{Formatting.HumanBytes(report.BytesTotal)}";
            }));

            Message = Loc.T("about.downloading");

            var file = await AppHost.Updater.DownloadAsync(_release, progress).ConfigureAwait(true);

            if (file is null)
            {
                Message = Loc.Language == "tr"
                    ? "İndirme veya doğrulama başarısız. Sürümü tarayıcıdan indirebilirsiniz."
                    : "Download or verification failed. You can download the release in a browser.";
                return;
            }

            _downloadedFile = file;
            Message = Loc.T("about.restartToApply");

            if (!AppHost.Updater.Apply(file))
            {
                Message = Loc.Language == "tr"
                    ? "Güncelleme uygulanamadı. Yönetici olarak çalıştırdığınızdan emin olun."
                    : "Could not apply the update. Make sure Hexnest is running as administrator.";
                return;
            }

            // Apply() already started the new executable; step aside for it.
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Self-update failed", ex);
            Message = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            RaiseAll(nameof(UpdateAvailable));
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Could not open {url}: {ex.Message}");
        }
    }
}
