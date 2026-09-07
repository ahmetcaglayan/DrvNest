using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for DevicesView. The view model owns every behaviour.</summary>
public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        DataContext = new DevicesViewModel();
    }
}
