using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using DrvNest.App.Services;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Persistence;

namespace DrvNest.App.ViewModels;

/// <summary>
/// Live diagnostics.
///
/// Exists because driver failures are almost always reported as an opaque HRESULT,
/// and a user filing an issue needs one button that produces something useful to
/// paste into it.
/// </summary>
public sealed class LogsViewModel : ViewModelBase
{
    private const int MaxDisplayed = 2000;

    private bool _autoScroll = true;
    private string? _notice;

    public LogsViewModel()
    {
        foreach (var entry in Log.Snapshot().TakeLast(MaxDisplayed)) Entries.Add(entry);

        CopyCommand = new RelayCommand(CopyAll);
        ClearCommand = new RelayCommand(() => { Log.Clear(); Entries.Clear(); });
        OpenFileCommand = new RelayCommand(OpenLogFile, () => File.Exists(Log.FilePath));
        OpenFolderCommand = new RelayCommand(() => OpenPath(AppPaths.LogsDirectory));
        ScreenshotCommand = new RelayCommand(TakeScreenshot);

        Log.EntryWritten += OnEntryWritten;
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public RelayCommand CopyCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    /// <summary>
    /// Captures the window to a PNG. Needed because Print Screen does nothing while an
    /// elevated window has focus - see <see cref="ScreenshotService"/>.
    /// </summary>
    public RelayCommand ScreenshotCommand { get; }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => Set(ref _autoScroll, value);
    }

    public string? LogFilePath => Log.FilePath;

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    /// <summary>Raised after an entry is appended so the view can scroll to the end.</summary>
    public event Action? EntryAppended;

    // =====================================================================================

    private void OnEntryWritten(LogEntry entry) => OnUi(() =>
    {
        Entries.Add(entry);

        // The buffer is bounded; the view must be too, or a long session makes the
        // list virtualiser do a lot of pointless work.
        while (Entries.Count > MaxDisplayed) Entries.RemoveAt(0);

        if (AutoScroll) EntryAppended?.Invoke();
    });

    private void CopyAll()
    {
        try
        {
            var header =
                $"{Core.AppInfo.VersionDisplay}{Environment.NewLine}" +
                $"{Core.Platform.SystemInfo.Current.OsDisplay} " +
                $"({Core.Platform.SystemInfo.Current.Architecture}){Environment.NewLine}" +
                $"{Core.Platform.SystemInfo.Current.MachineDisplay}{Environment.NewLine}" +
                new string('-', 60) + Environment.NewLine;

            Clipboard.SetText(header + Log.Export());
            Notice = Loc.Language == "tr" ? "Panoya kopyalandı." : "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Notice = ex.Message;
        }
    }

    private void TakeScreenshot()
    {
        var result = ScreenshotService.Capture();

        Notice = result.Success
            ? Loc.T(result.CopiedToClipboard ? "shot.savedAndCopied" : "shot.saved", result.Path!)
            : result.Error ?? Loc.T("shot.failed");
    }

    private void OpenLogFile()
    {
        var path = Log.FilePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) OpenPath(path);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path}: {ex.Message}");
        }
    }
}
