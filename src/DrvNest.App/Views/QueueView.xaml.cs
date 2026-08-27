using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>Code-behind for QueueView. The view model owns every behaviour.</summary>
public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
        DataContext = new QueueViewModel();
    }
}
