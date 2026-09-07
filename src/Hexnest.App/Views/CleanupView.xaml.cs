using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for CleanupView. The view model does the work.</summary>
public partial class CleanupView : UserControl
{
    public CleanupView()
    {
        InitializeComponent();
        DataContext = new CleanupViewModel();
    }
}
