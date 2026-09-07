using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

/// <summary>
/// The system monitor page.
///
/// Sampling is tied to the visual tree rather than to the object's lifetime: the page
/// is cached by its nav item and outlives being looked at, so starting in the
/// constructor would leave a monitor running for the rest of the session after one
/// visit. Attached/detached is the event that actually means "on screen".
/// </summary>
public partial class SystemMonitorView : UserControl, IDisposable
{
    private readonly SystemMonitorViewModel _model = new();

    public SystemMonitorView()
    {
        InitializeComponent();
        DataContext = _model;

        AttachedToVisualTree += (_, _) => _model.Start();
        DetachedFromVisualTree += (_, _) => _model.Stop();
    }

    public void Dispose() => _model.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
