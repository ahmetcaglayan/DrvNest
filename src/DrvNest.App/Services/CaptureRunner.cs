using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DrvNest.App.ViewModels;
using DrvNest.Core.Diagnostics;

namespace DrvNest.App.Services;

/// <summary>
/// Photographs every page and exits. Driven by <c>DrvNest.exe --capture [folder]</c>.
///
/// This exists so the screenshots in the README and on the website can be regenerated
/// by anybody, from a known state, instead of being a set of images one maintainer took
/// by hand once and nobody can reproduce when the interface changes. Run it after a UI
/// change and the documentation catches up in one command.
///
/// It is also the only reliable way to photograph DrvNest at all: the window runs
/// elevated, and User Interface Privilege Isolation stops the (unelevated) Snipping Tool
/// from seeing input aimed at it, so Print Screen does nothing - the same reason
/// <see cref="ScreenshotService"/> exists for bug reports.
/// </summary>
public static class CaptureRunner
{
    /// <summary>Pages to photograph, in menu order.</summary>
    private static readonly string[] Pages =
    {
        "dashboard", "devices", "updates", "queue", "backup",
        "history", "system", "network", "startup", "clean",
        "logs", "settings", "about"
    };

    /// <summary>
    /// Pages whose content only becomes interesting after a few samples, so they are
    /// given time to fill their charts before the shutter.
    /// </summary>
    /// <summary>
    /// Extra time, per page, before the shutter. The monitors need enough samples to draw
    /// a chart worth looking at; the cleaner has to finish measuring every cache on the
    /// disk, which on a well-used machine is tens of gigabytes and takes far longer than
    /// anything else in the application.
    /// </summary>
    private static readonly Dictionary<string, int> LiveSettle = new(StringComparer.Ordinal)
    {
        ["system"] = 32_000,
        ["network"] = 32_000,
        ["clean"] = 90_000,
    };

    /// <summary>
    /// Pages long enough that their table falls below the fold, so a second shot is
    /// taken with the page scrolled to the bottom. Those are the images that actually
    /// show the feature: a process table nobody can see is not a screenshot of a
    /// process table.
    /// </summary>
    private static readonly HashSet<string> ScrolledPages = new(StringComparer.Ordinal)
    {
        "system", "network", "settings"
    };

    private const int SettleMilliseconds = 900;

    /// <summary>
    /// Runs a scan, walks the menu, writes one PNG per page and closes the application.
    /// </summary>
    public static async Task RunAsync(Window window, string folder, bool scanFirst)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Log.Info($"Capture mode: writing screenshots to {folder}");

            if (window.DataContext is not MainViewModel shell)
            {
                Log.Error("Capture mode: the main window has no shell view model.");
                return;
            }

            // Real device data makes for a screenshot worth publishing, and it is also
            // the state the pages are documented in.
            if (scanFirst)
            {
                Log.Info("Capture mode: scanning first...");

                try
                {
                    await shell.ScanAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Capture mode: the scan failed, continuing anyway: {ex.Message}");
                }
            }

            foreach (var page in Pages)
            {
                shell.Navigate(page);

                // Two waits: one for the page to be built and laid out, one for whatever
                // it renders asynchronously - icons, live samples - to arrive.
                await Task.Delay(SettleMilliseconds).ConfigureAwait(true);

                if (LiveSettle.TryGetValue(page, out int extra))
                    await Task.Delay(extra).ConfigureAwait(true);

                await window.Dispatcher
                    .InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

                var path = Path.Combine(folder, page + ".png");
                var result = ScreenshotService.Capture(window, path, copyToClipboard: false);

                Log.Info(result.Success
                    ? $"Capture: {page}.png"
                    : $"Capture failed for {page}: {result.Error}");

                if (!ScrolledPages.Contains(page)) continue;

                if (ScrollToEnd(shell.CurrentView))
                {
                    await Task.Delay(SettleMilliseconds).ConfigureAwait(true);

                    var scrolledPath = Path.Combine(folder, page + "-detail.png");
                    var scrolled = ScreenshotService.Capture(window, scrolledPath, copyToClipboard: false);

                    Log.Info(scrolled.Success
                        ? $"Capture: {page}-detail.png"
                        : $"Capture failed for {page}-detail: {scrolled.Error}");

                    ScrollToHome(shell.CurrentView);
                }
            }

            Log.Info("Capture mode: done.");
        }
        catch (Exception ex)
        {
            Log.Error("Capture mode failed", ex);
        }
        finally
        {
            Application.Current?.Shutdown();
        }
    }

    /// <summary>
    /// Scrolls a page's outermost scroll viewer to the bottom.
    /// Returns false when the page does not scroll, so no second shot is taken.
    /// </summary>
    private static bool ScrollToEnd(DependencyObject? page)
    {
        var viewer = FindScrollViewer(page);

        if (viewer is null || viewer.ScrollableHeight <= 1) return false;

        // A page that marks an anchor is scrolled so the anchor sits just under the top
        // edge. Scrolling to the very bottom instead would leave the table's own column
        // headers off screen, which is most of what makes the picture readable.
        if (page is FrameworkElement { } element &&
            element.FindName("CaptureAnchor") is FrameworkElement anchor)
        {
            try
            {
                double top = anchor
                    .TransformToAncestor(viewer)
                    .Transform(new Point(0, 0)).Y;

                viewer.ScrollToVerticalOffset(
                    Math.Clamp(viewer.VerticalOffset + top - 16, 0, viewer.ScrollableHeight));

                viewer.UpdateLayout();
                return true;
            }
            catch (InvalidOperationException)
            {
                // The anchor is not in this visual tree yet; fall through to the bottom.
            }
        }

        viewer.ScrollToEnd();
        viewer.UpdateLayout();
        return true;
    }

    private static void ScrollToHome(DependencyObject? page)
    {
        var viewer = FindScrollViewer(page);

        if (viewer is null) return;

        viewer.ScrollToHome();
        viewer.UpdateLayout();
    }

    /// <summary>
    /// Breadth-first, so the page's own outer scroll viewer wins over the one inside a
    /// table that scrolls sideways.
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is null) return null;

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current is ScrollViewer { ScrollableHeight: > 0 } viewer) return viewer;

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return null;
    }

    /// <summary>
    /// Reads <c>--lang &lt;code&gt;</c> out of the command line, so the published
    /// screenshots do not depend on the display language of whoever regenerated them.
    /// </summary>
    public static string? ParseLanguage(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            var value = args[i].TrimStart('-', '/').ToLowerInvariant();

            if (value is "lang" or "language") return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Reads <c>--capture [folder]</c> out of the command line.
    /// Returns null when capture mode was not asked for.
    /// </summary>
    public static string? ParseFolder(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            var value = args[i].TrimStart('-', '/').ToLowerInvariant();

            if (value != "capture") continue;

            // The next argument is the folder, unless it is another switch.
            if (i + 1 < args.Count && !args[i + 1].StartsWith('-') && !args[i + 1].StartsWith('/'))
                return args[i + 1];

            return Path.Combine(Core.Persistence.AppPaths.ReportsDirectory, "screenshots");
        }

        return null;
    }
}
