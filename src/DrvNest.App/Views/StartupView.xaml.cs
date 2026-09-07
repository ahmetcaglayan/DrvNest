using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for StartupView. The view model does the work.</summary>
public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        DataContext = new StartupViewModel();
    }
}
