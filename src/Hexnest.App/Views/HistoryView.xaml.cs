using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>Code-behind for HistoryView. The view model owns every behaviour.</summary>
public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        DataContext = new HistoryViewModel();
    }
}
