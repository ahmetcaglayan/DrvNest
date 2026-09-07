using Avalonia;
using Hexnest.Core.Diagnostics;

namespace Hexnest.Mac;

/// <summary>
/// Entry point.
///
/// Kept to the shape Avalonia's own tooling expects, because the designer and the
/// XAML compiler both look for <see cref="BuildAvaloniaApp"/> by name.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A crash before the window exists has nowhere to display itself, so it
            // goes to the log and to stderr, where `open -a` will not swallow it.
            Log.Error("Hexnest could not start", ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
