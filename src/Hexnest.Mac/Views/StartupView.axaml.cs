using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        DataContext = new StartupViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
