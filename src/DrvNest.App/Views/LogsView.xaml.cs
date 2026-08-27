using System.Windows.Controls;
using DrvNest.App.ViewModels;

namespace DrvNest.App.Views;

/// <summary>
/// Code-behind for LogsView.
///
/// The one thing that genuinely belongs here is the follow-the-tail scroll: it is a
/// property of the visual tree, not of the view model, and there is no clean way to
/// express "scroll this list to the bottom" through a binding.
/// </summary>
public partial class LogsView : UserControl
{
    private readonly LogsViewModel _viewModel;

    public LogsView()
    {
        InitializeComponent();

        _viewModel = new LogsViewModel();
        DataContext = _viewModel;

        _viewModel.EntryAppended += ScrollToEnd;
        Unloaded += (_, _) => _viewModel.EntryAppended -= ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count == 0) return;

        var last = LogList.Items[^1];
        if (last is not null) LogList.ScrollIntoView(last);
    }
}
