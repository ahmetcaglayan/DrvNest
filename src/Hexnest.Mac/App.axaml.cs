using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.Services;

namespace Hexnest.Mac;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppHost.Initialize();

            // The theme is applied a second time here: AppHost.Initialize runs before
            // Avalonia has an Application to set a variant on.
            ThemeManager.Apply(AppHost.Settings.Current.Theme);

            var args = desktop.Args ?? Array.Empty<string>();
            var captureFolder = CaptureRunner.OutputFolder(args);

            // The capture language has to be set before the shell exists. The pages
            // resolve their strings at parse time, but the shell's own bindings - the
            // page title and the status line - are read once when the window is built,
            // so switching afterwards leaves those two in the previous language.
            if (captureFolder is not null && CaptureRunner.Language(args) is { } language)
                Core.Localization.Loc.SetLanguage(language);

            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => AppHost.Shutdown();

            // --capture photographs every page and quits. The window is deliberately
            // never shown: the renderer works off the visual tree, so a capture can run
            // in CI or over SSH without a screen to put a window on.
            if (captureFolder is not null)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = CaptureRunner.RunAsync(window, captureFolder);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
