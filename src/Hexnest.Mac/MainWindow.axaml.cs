using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hexnest.Mac.ViewModels;

namespace Hexnest.Mac;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
