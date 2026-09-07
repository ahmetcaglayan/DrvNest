using System.Diagnostics;
using Hexnest.Core;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Platform;
using Hexnest.Core.Updating;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The about page: what this build is, and whether a newer one exists.
///
/// The Windows build downloads an update, verifies its checksum and swaps its own
/// executable on the way out. The Mac build deliberately stops at "there is a new
/// version, here it is": replacing a running .app bundle in place breaks its code
/// signature and its quarantine state, and an application that silently rewrites
/// itself inside /Applications is not something a Mac user should have to trust.
/// Hexnest checks, tells you, and opens the download.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    private readonly SelfUpdateService _updater = new(new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    });

    private string _status = string.Empty;
    private bool _isChecking;
    private ReleaseInfo? _available;

    public AboutViewModel()
    {
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking);
        DownloadCommand = new RelayCommand(OpenDownload, () => _available is not null);
        OpenRepositoryCommand = new RelayCommand(() => Open(AppInfo.RepositoryUrl));
        OpenIssuesCommand = new RelayCommand(() => Open(AppInfo.IssuesUrl));

        Status = Loc.T("about.neverChecked");
    }

    public string Title => Loc.T("about.title");
    public string Subtitle => Loc.T("about.subtitle");
    public string ProductName => AppInfo.ProductName;
    public string Tagline => AppInfo.Tagline;
    public string VersionText => AppInfo.Version;
    public string LicenseText => Loc.T("mac.licenseText");

    public string MachineText =>
        $"{SystemInfo.Current.MachineDisplay}  ·  {SystemInfo.Current.OsDisplay}  ·  " +
        $"{SystemInfo.Current.Architecture}";

    public string ProcessorText =>
        $"{SystemInfo.Current.ProcessorName}  ·  {SystemInfo.Current.CoreDisplay}";

    public AsyncRelayCommand CheckCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand OpenRepositoryCommand { get; }
    public RelayCommand OpenIssuesCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (Set(ref _isChecking, value)) RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool HasUpdate => _available is not null;

    private async Task CheckAsync()
    {
        IsChecking = true;
        Status = Loc.T("about.checking");

        try
        {
            var result = await _updater
                .CheckAsync(AppHost.Settings.Current.IncludePrereleaseUpdates)
                .ConfigureAwait(true);

            if (result.UpdateAvailable && result.Release is not null)
            {
                _available = result.Release;
                Status = Loc.T("about.available", result.Release.Version);
            }
            else
            {
                _available = null;

                Status = result.Status switch
                {
                    UpdateCheckStatus.NoReleases => Loc.T("about.noReleases"),
                    UpdateCheckStatus.NotConfigured => Loc.T("about.notConfigured"),
                    UpdateCheckStatus.Error => result.Message ?? Loc.T("about.checkFailed"),
                    _ => Loc.T("about.upToDate")
                };
            }

            RaiseAll(nameof(HasUpdate));
            RelayCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            Status = Loc.T("about.networkError");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void OpenDownload()
        => Open(_available?.HtmlUrl ?? AppInfo.ReleasesUrl);

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { url },
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open '{url}': {ex.Message}");
        }
    }
}
