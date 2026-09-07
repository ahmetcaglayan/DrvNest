using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class NetworkMonitorView : UserControl, IDisposable
{
    private readonly NetworkMonitorViewModel _model = new();

    public NetworkMonitorView()
    {
        InitializeComponent();
        DataContext = _model;

        AttachedToVisualTree += (_, _) => _model.Start();
        DetachedFromVisualTree += (_, _) => _model.Stop();
    }

    public void Dispose() => _model.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
