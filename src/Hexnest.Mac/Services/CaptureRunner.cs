using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hexnest.Core.Diagnostics;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Services;

/// <summary>
/// Photographs every page and exits. Driven by <c>Hexnest --capture [folder]</c>.
///
/// The counterpart of the Windows build's runner, and it exists for the same reason:
/// the screenshots in the README and on the website should be reproducible by anybody
/// from a known state, rather than being images one person took by hand and nobody can
/// regenerate when the interface changes.
///
/// It renders the window's visual tree to a bitmap rather than asking macOS for a
/// screen capture. That needs no Screen Recording permission, it produces the same
/// image on any display, and it cannot accidentally photograph whatever else happens to
/// be on the desktop.
/// </summary>
public static class CaptureRunner
{
    /// <summary>Pages to photograph, in menu order.</summary>
    private static readonly string[] Pages =
    {
        "dashboard", "system", "network", "startup", "clean", "logs", "settings", "about"
    };

    /// <summary>
    /// Extra time, per page, before the shutter.
    ///
    /// The monitors need enough samples to draw a chart worth looking at, and the
    /// network page additionally has to wait for nettop's first delta. The cleaner has
    /// to finish measuring every cache on the disk, which on a well-used Mac is tens of
    /// gigabytes and takes far longer than anything else in the application.
    /// </summary>
    private static readonly Dictionary<string, int> Settle = new(StringComparer.Ordinal)
    {
        ["dashboard"] = 4_000,
        ["system"] = 24_000,
        ["network"] = 26_000,
        ["clean"] = 120_000,
    };

    /// <summary>The capture canvas. Wide enough for the tables, tall enough for the pages.</summary>
    private const int CaptureWidth = 1280;
    private const int CaptureHeight = 1060;

    /// <summary>Reads the command line. Null when this is an ordinary launch.</summary>
    public static string? OutputFolder(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--capture", StringComparison.OrdinalIgnoreCase)) continue;

            var next = i + 1 < args.Length ? args[i + 1] : null;

            return string.IsNullOrWhiteSpace(next) || next!.StartsWith('-')
                ? Path.Combine(Environment.CurrentDirectory, "screenshots")
                : next;
        }

        return null;
    }

    /// <summary>The language to capture in, from <c>--lang xx</c>.</summary>
    public static string? Language(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Walks every page, waits for it to have something to show, and writes a PNG.
    /// </summary>
    public static async Task RunAsync(Window window, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            // Taller than the window a person would open, so that a whole page fits in
            // one image. A screenshot that stops half way down the table is a
            // screenshot of the top half of a table.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                window.Width = CaptureWidth;
                window.Height = CaptureHeight;
            });

            if (window.DataContext is not MainViewModel shell)
            {
                Log.Error("Capture: the window has no shell view model.");
                return;
            }

            // The window is never shown on screen during a capture, but it does have to
            // be laid out, so it is measured and arranged explicitly below.
            await Task.Delay(1500);

            foreach (var page in Pages)
            {
                Log.Info($"Capture: {page}");

                await Dispatcher.UIThread.InvokeAsync(() => shell.Navigate(page));

                int settle = Settle.TryGetValue(page, out int wait) ? wait : 1_500;
                await Task.Delay(settle);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(folder, page + ".png");
                    Render(window, path);
                });
            }

            Log.Info($"Capture: finished, images are in {folder}");
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                (Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown());
        }
    }

    /// <summary>
    /// Renders the window at twice its logical size, which is what a Retina screenshot
    /// is and what the website's image set expects.
    /// </summary>
    private static void Render(Window window, string path)
    {
        try
        {
            var size = new Size(window.Width, window.Height);

            window.Measure(size);
            window.Arrange(new Rect(size));

            var pixels = new PixelSize((int)(size.Width * 2), (int)(size.Height * 2));

            using var bitmap = new RenderTargetBitmap(pixels, new Vector(192, 192));
            bitmap.Render(window);
            bitmap.Save(path);

            Log.Debug($"Capture: wrote {path}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not capture '{path}': {ex.Message}");
        }
    }
}
