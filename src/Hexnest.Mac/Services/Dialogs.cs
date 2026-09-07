using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hexnest.Core.Diagnostics;
using Hexnest.Core.Localization;

namespace Hexnest.Mac.Services;

/// <summary>How a message should read.</summary>
public enum DialogKind
{
    Information,
    Warning,
    Danger
}

/// <summary>
/// The confirmation and message sheets.
///
/// Avalonia has no MessageBox, which turns out to be the right shape for this
/// application anyway: the WPF build's confirmations are the one place where its
/// otherwise consistent visual language breaks and a Win32 dialog appears. These are
/// built from the same palette as everything else and open as a modal sheet over the
/// main window, which is what a Mac user expects.
///
/// Everything here is async, because a modal dialog on Avalonia is awaited rather than
/// blocking the calling thread as <c>MessageBox.Show</c> does. That difference reaches
/// into the view models: where the Windows code says "if the answer was not Yes,
/// return", the Mac code awaits the same question.
/// </summary>
public static class Dialogs
{
    /// <summary>The window a sheet should attach to, or null before the shell exists.</summary>
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;

    /// <summary>Asks a yes/no question. Returns false when there is no window to ask over.</summary>
    public static Task<bool> ConfirmAsync(
        string title, string message, DialogKind kind = DialogKind.Warning)
        => ShowAsync(title, message, kind, confirm: true);

    /// <summary>States something and waits for it to be dismissed.</summary>
    public static Task InformAsync(
        string title, string message, DialogKind kind = DialogKind.Information)
        => ShowAsync(title, message, kind, confirm: false);

    private static async Task<bool> ShowAsync(
        string title, string message, DialogKind kind, bool confirm)
    {
        var owner = Owner;

        if (owner is null)
        {
            // Nothing to be modal over. Refusing is the safe answer for a confirmation;
            // a message with nowhere to go belongs in the log.
            Log.Warn($"Dialog '{title}' had no owner window: {message}");
            return false;
        }

        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(title, message, kind, confirm));

        bool answer = false;

        var accent = kind switch
        {
            DialogKind.Danger => "Brush.Danger",
            DialogKind.Warning => "Brush.Warning",
            _ => "Brush.Info"
        };

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var body = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            MaxWidth = 460
        };

        body.Bind(TextBlock.ForegroundProperty, ResourceRef("Brush.TextMuted"));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 9,
            Margin = new Thickness(0, 20, 0, 0)
        };

        Window? dialog = null;

        if (confirm)
        {
            var cancel = new Button { Content = Loc.T("common.cancel"), MinWidth = 92 };
            cancel.Classes.Add("ghost");
            cancel.Click += (_, _) => { answer = false; dialog?.Close(); };
            buttons.Children.Add(cancel);
        }

        var ok = new Button
        {
            Content = confirm ? Loc.T("common.yes") : Loc.T("common.close"),
            MinWidth = 92,
            IsDefault = true
        };

        ok.Classes.Add(kind == DialogKind.Danger ? "danger" : "primary");
        ok.Click += (_, _) => { answer = true; dialog?.Close(); };
        buttons.Children.Add(ok);

        var stripe = new Border { Width = 3, CornerRadius = new CornerRadius(2) };
        stripe.Bind(Border.BackgroundProperty, ResourceRef(accent));

        var content = new StackPanel();
        content.Children.Add(heading);
        content.Children.Add(body);
        content.Children.Add(buttons);

        var layout = new DockPanel { Margin = new Thickness(22) };
        DockPanel.SetDock(stripe, Dock.Left);
        stripe.Margin = new Thickness(0, 0, 16, 0);
        layout.Children.Add(stripe);
        layout.Children.Add(content);

        var shell = new Border { Child = layout };
        shell.Bind(Border.BackgroundProperty, ResourceRef("Brush.Surface"));

        dialog = new Window
        {
            Title = title,
            Content = shell,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly
        };

        dialog.Bind(TopLevel.RequestedThemeVariantProperty,
            new Avalonia.Data.Binding(nameof(TopLevel.ActualThemeVariant)) { Source = owner });

        await dialog.ShowDialog(owner);
        return answer;
    }

    private static Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension ResourceRef(string key)
        => new(key);
}
