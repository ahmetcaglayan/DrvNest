using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for BackupView. The view model owns every behaviour.</summary>
public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        DataContext = new BackupViewModel();
    }
}
