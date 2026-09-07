using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class DashboardView : UserControl, IDisposable
{
    private readonly DashboardViewModel _model = new();

    public DashboardView()
    {
        InitializeComponent();
        DataContext = _model;

        AttachedToVisualTree += (_, _) => _model.Start();
        DetachedFromVisualTree += (_, _) => _model.Stop();
    }

    public void Dispose() => _model.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
