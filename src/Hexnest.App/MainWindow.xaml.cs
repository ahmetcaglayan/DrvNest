using System.Windows;
using System.Windows.Input;

namespace Hexnest.App;

/// <summary>
/// The shell window. Everything here is chrome mechanics; all behaviour lives in
/// <see cref="ViewModels.MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => ApplyMaximizedPadding();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// A WindowChrome window sits slightly outside the work area when maximised, so
    /// the resize border does not clip the content. Padding the root by that border
    /// keeps the layout aligned in both states, and reading it from
    /// <see cref="SystemParameters"/> keeps it correct at every DPI.
    /// </summary>
    private void ApplyMaximizedPadding()
    {
        if (WindowState == WindowState.Maximized)
        {
            var border = SystemParameters.WindowResizeBorderThickness;

            LayoutRoot.Margin = new Thickness(
                border.Left + 1, border.Top + 1, border.Right + 1, border.Bottom + 1);

            MaximizeButton.Content = TryFindResource("Icon.Restore") ?? MaximizeButton.Content;
        }
        else
        {
            LayoutRoot.Margin = new Thickness(0);
            MaximizeButton.Content = TryFindResource("Icon.Maximize") ?? MaximizeButton.Content;
        }
    }

    /// <summary>Ctrl+R rescans, Escape cancels, F5 refreshes: the usual reflexes.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not ViewModels.MainViewModel shell) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (e.Key == Key.F5 || (ctrl && e.Key == Key.R))
        {
            if (shell.ScanCommand.CanExecute(null)) shell.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && shell.IsScanning)
        {
            shell.CancelScanCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Makes Print Screen work again while Hexnest has focus.
    ///
    /// Hexnest runs elevated, and Windows 11 routes Print Screen through a hook owned by
    /// the unelevated Snipping Tool. UIPI stops that hook from seeing the key, so the
    /// system capture silently does nothing over any administrator window. Windows still
    /// delivers the key to the focused application itself - as a key *up*, which is why
    /// this is handled here rather than in OnPreviewKeyDown - so Hexnest can simply
    /// photograph itself instead.
    /// </summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        if (e.Key != Key.Snapshot && e.SystemKey != Key.Snapshot) return;

        var result = Services.ScreenshotService.Capture(this);

        if (DataContext is ViewModels.MainViewModel shell)
        {
            shell.StatusText = result.Success
                ? Loc.T(result.CopiedToClipboard ? "shot.savedAndCopied" : "shot.saved",
                    result.Path!)
                : Loc.T("shot.failed");
        }

        e.Handled = true;
    }
}
