using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for DevicesView. The view model owns every behaviour.</summary>
public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        DataContext = new DevicesViewModel();
    }
}
