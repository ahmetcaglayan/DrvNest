using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>
/// Code-behind for NetworkMonitorView.
///
/// Same reasoning as <see cref="SystemMonitorView"/>: the sampler follows the page
/// being shown and hidden, so nothing is watching the network once the page is closed.
/// </summary>
public partial class NetworkMonitorView : UserControl
{
    private readonly NetworkMonitorViewModel _viewModel;

    public NetworkMonitorView()
    {
        InitializeComponent();

        _viewModel = new NetworkMonitorViewModel();
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Activate();
        Unloaded += (_, _) => _viewModel.Deactivate();
    }
}
