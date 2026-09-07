using System.Windows.Controls;
using Hexnest.App.ViewModels;

namespace Hexnest.App.Views;

/// <summary>
/// Code-behind for SystemMonitorView.
///
/// The only thing here is the lifetime of the sampler, which genuinely belongs to the
/// visual tree rather than to the view model: navigation keeps its pages alive and
/// reuses them, so the timer has to follow the page being shown and hidden. A timer
/// nobody stops is exactly how an application that promises "no background service"
/// quietly acquires one.
/// </summary>
public partial class SystemMonitorView : UserControl
{
    private readonly SystemMonitorViewModel _viewModel;

    public SystemMonitorView()
    {
        InitializeComponent();

        _viewModel = new SystemMonitorViewModel();
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Activate();
        Unloaded += (_, _) => _viewModel.Deactivate();
    }
}
