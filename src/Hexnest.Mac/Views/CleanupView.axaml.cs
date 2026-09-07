using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac.Views;

public partial class CleanupView : UserControl
{
    public CleanupView()
    {
        InitializeComponent();
        DataContext = new CleanupViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
