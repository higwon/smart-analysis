using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Views;

/// <summary>
/// The application shell window (U01/U02). Composition of design-system styles + icons over the
/// <see cref="ShellViewModel"/>. Code-behind holds only view-only bridges: tree-selection forwarding, the
/// OS theme-change hook, and the <b>image render orchestration</b> — on <see cref="ShellViewModel.ImagesChanged"/>
/// it builds a fresh <see cref="ImageRenderInput"/> from the active/before datasets and calls
/// <c>AfmImageView.Render(...)</c>. The render input (which borrows the dataset buffer) is never held by the
/// view-model, honoring the V02 lifetime contract (ADR-011).
/// </summary>
public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;

    // The AFM data colormap (theme-independent, ADR-008). A richer palette picker is a later task.
    private static readonly Colormap DataColormap = Colormap.AfmGold;

    private readonly ShellViewModel _viewModel;
    private readonly ThemeManager _theme;

    public MainWindow(ShellViewModel viewModel, ThemeManager theme)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        InitializeComponent();
        DataContext = viewModel;

        _viewModel.ImagesChanged += (_, _) => RenderImages();
        RenderImages();
    }

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _viewModel.Select(e.NewValue as DatasetNodeViewModel);

    // Build transient render inputs and render them; retain nothing borrowed (V02 / ADR-011).
    private void RenderImages()
    {
        if (_viewModel.IsBeforeAfter && _viewModel.BeforeImage is { } before && _viewModel.ActiveImage is { } after)
        {
            // Each pane uses its OWN data range so both stay legible: a transform like Flatten removes the
            // Z offset, so a union range would wash the source to one extreme and the result to the other —
            // hiding the very texture/tilt the comparison exists to show. (A shared-range toggle is a later
            // refinement.) The axes are identical (same X/Y), which the BEFORE/AFTER labels make explicit.
            BeforeImageView.Render(RenderInputFactory.ForImage(before, DataColormap));
            AfterImageView.Render(RenderInputFactory.ForImage(after, DataColormap));
            SingleImage.Clear();
        }
        else if (_viewModel.ActiveImage is { } image)
        {
            SingleImage.Render(RenderInputFactory.ForImage(image, DataColormap));
            BeforeImageView.Clear();
            AfterImageView.Clear();
        }
        else
        {
            SingleImage.Clear();
            BeforeImageView.Clear();
            AfterImageView.Clear();
        }
    }

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
