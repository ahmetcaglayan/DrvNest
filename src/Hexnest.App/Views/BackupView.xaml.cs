using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for BackupView. The view model owns every behaviour.</summary>
public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        DataContext = new BackupViewModel();
    }
}
