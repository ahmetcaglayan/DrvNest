using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class PowerView : UserControl
{
    public PowerView()
    {
        InitializeComponent();
        DataContext = new PowerViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
