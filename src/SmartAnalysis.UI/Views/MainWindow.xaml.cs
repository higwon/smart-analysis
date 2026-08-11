using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.ViewModels;

namespace SmartAnalysis.UI.Views;

/// <summary>
/// The application shell window (U01). Pure composition of design-system styles + icons over the
/// <see cref="ShellViewModel"/>. Code-behind is limited to two view-only bridges: the tree-selection
/// forward (WPF has no bindable <c>SelectedItem</c> on <see cref="TreeView"/>) and the OS
/// theme-change hook (a window HWND is where <c>WM_SETTINGCHANGE</c> can be observed — UIX03 contract).
/// </summary>
public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;

    private readonly ShellViewModel _viewModel;
    private readonly ThemeManager _theme;

    public MainWindow(ShellViewModel viewModel, ThemeManager theme)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _viewModel.Select(e.NewValue as DatasetNodeViewModel);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    // When the OS app-theme changes and the preference is "System", re-resolve and swap live (UIX03).
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange)
        {
            _theme.ReapplyIfFollowingSystem();
        }

        return IntPtr.Zero;
    }
}
