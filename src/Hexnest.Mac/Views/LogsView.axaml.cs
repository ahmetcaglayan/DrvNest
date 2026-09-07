using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class LogsView : UserControl, IDisposable
{
    private readonly LogsViewModel _model = new();

    public LogsView()
    {
        InitializeComponent();
        DataContext = _model;
    }

    public void Dispose() => _model.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
