using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Persistence;

namespace DrvNest.App.Services;

/// <summary>
/// Lets DrvNest photograph its own window.
///
/// This exists because of a real Windows behaviour rather than a nice-to-have: DrvNest
/// runs elevated, and on Windows 11 the Print Screen key is handled by a hook owned by
/// the (unelevated) Snipping Tool. User Interface Privilege Isolation stops a lower
/// integrity process from seeing input aimed at a higher integrity window, so pressing
/// Print Screen while any administrator app has focus does nothing at all - Task
/// Manager and Registry Editor behave the same way.
///
/// Capturing from inside the elevated process side-steps the whole problem, and it is
/// exactly what someone filing a bug report needs anyway.
/// </summary>
public static class ScreenshotService
{
    /// <summary>Where a capture goes and whether the clipboard copy worked.</summary>
    public sealed record CaptureResult(string? Path, bool CopiedToClipboard, string? Error)
    {
        public bool Success => Path is not null;
    }

    /// <summary>
    /// Renders a window to a PNG under the reports folder and puts it on the clipboard.
    ///
    /// Uses RenderTargetBitmap rather than the PrintWindow API: the window draws its own
    /// chrome, so the visual tree already contains everything, and rendering it keeps the
    /// capture correct at any DPI without a single P/Invoke.
    /// </summary>
    public static CaptureResult Capture(Window? window = null)
    {
        window ??= Application.Current?.MainWindow;

        if (window is null)
            return new CaptureResult(null, false, "No window to capture.");

        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);

            int width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

            if (width <= 0 || height <= 0)
                return new CaptureResult(null, false, "The window has no size yet.");

            var target = new RenderTargetBitmap(
                width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

            target.Render(window);
            target.Freeze();

            Directory.CreateDirectory(AppPaths.ReportsDirectory);

            var path = Path.Combine(
                AppPaths.ReportsDirectory,
                $"drvnest-{DateTime.Now:yyyy-MM-dd_HHmmss}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));

            using (var stream = File.Create(path)) encoder.Save(stream);

            bool copied = TryCopy(target);

            Log.Info($"Window captured to {path}");
            return new CaptureResult(path, copied, null);
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed", ex);
            return new CaptureResult(null, false, ex.Message);
        }
    }

    /// <summary>
    /// Clipboard copy is best effort: another process can hold the clipboard open, and
    /// losing the copy is not a reason to lose the saved file too.
    /// </summary>
    private static bool TryCopy(BitmapSource image)
    {
        try
        {
            Clipboard.SetImage(image);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy the screenshot to the clipboard: {ex.Message}");
            return false;
        }
    }
}
