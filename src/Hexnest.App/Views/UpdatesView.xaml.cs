using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for UpdatesView. The view model owns every behaviour.</summary>
public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
        DataContext = new UpdatesViewModel();
    }
}
