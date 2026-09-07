using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;
using Hexnest.Core.Models;
using Hexnest.Core.Persistence;
using Hexnest.Mac.Services;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// The log page.
///
/// The log is the thing somebody attaches to a bug report, so the page exists mostly to
/// make it easy to get at: copy it, or open the folder it lives in.
/// </summary>
public sealed class LogsViewModel : ViewModelBase, IDisposable
{
    private const int MaxRows = 2000;

    private bool _disposed;

    public LogsViewModel()
    {
        foreach (var entry in Log.Snapshot().TakeLast(MaxRows)) Entries.Add(new LogRow(entry));

        Log.EntryWritten += OnEntry;

        CopyCommand = new RelayCommand(Copy);
        ClearCommand = new RelayCommand(() => { Log.Clear(); Entries.Clear(); });
        OpenFolderCommand = new RelayCommand(() => Reveal(AppPaths.LogsDirectory));
    }

    public ObservableCollection<LogRow> Entries { get; } = new();

    public RelayCommand CopyCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public string Title => Loc.T("log.title");

    public string Subtitle => Loc.T("log.subtitle");

    public string FilePath => Log.FilePath ?? AppPaths.LogFile;

    private void OnEntry(LogEntry entry) => OnUi(() =>
    {
        Entries.Add(new LogRow(entry));

        // The log is a ring buffer in memory and this list has to be one too, or a
        // session left open overnight would grow a control with a hundred thousand
        // rows in it.
        while (Entries.Count > MaxRows) Entries.RemoveAt(0);
    });

    private async void Copy()
    {
        try
        {
            var clipboard = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;

            if (clipboard is null) return;

            var header = $"{Core.AppInfo.VersionDisplay}  ·  " +
                         $"{Core.Platform.SystemInfo.Current.MachineDisplay}  ·  " +
                         $"{Core.Platform.SystemInfo.Current.OsDisplay}{Environment.NewLine}{Environment.NewLine}";

            await clipboard.SetTextAsync(header + Log.Export());
            AppEvents.RaiseStatus(Loc.T("log.copied"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard copy failed: {ex.Message}");
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { path },
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open '{path}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.EntryWritten -= OnEntry;
    }
}

/// <summary>One log line.</summary>
public sealed class LogRow
{
    public LogRow(LogEntry entry)
    {
        Time = entry.TimeDisplay;
        Level = entry.Level.ToString().ToUpperInvariant();
        Message = entry.Message;
        IsError = entry.Level == LogLevel.Error;
        IsWarning = entry.Level == LogLevel.Warn;
    }

    public string Time { get; }
    public string Level { get; }
    public string Message { get; }
    public bool IsError { get; }
    public bool IsWarning { get; }
}
