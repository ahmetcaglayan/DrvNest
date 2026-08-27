using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for UpdatesView. The view model owns every behaviour.</summary>
public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
        DataContext = new UpdatesViewModel();
    }
}
