using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for AboutView. The view model owns every behaviour.</summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }
}
