using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for StartupView. The view model does the work.</summary>
public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        DataContext = new StartupViewModel();
    }
}
