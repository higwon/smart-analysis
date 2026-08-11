using System.Windows;
using System.Windows.Controls;
using SmartAnalysis.UI.ViewModels;

namespace SmartAnalysis.UI.Views;

/// <summary>
/// The application shell window (U01). Pure composition of design-system styles + icons over the
/// <see cref="ShellViewModel"/>; the only code-behind is the tree-selection bridge (WPF has no bindable
/// <c>SelectedItem</c> on <see cref="TreeView"/>), which forwards selection to the view-model so it sets
/// the workspace active context.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _viewModel.Select(e.NewValue as DatasetNodeViewModel);
}
