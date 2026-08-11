using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // Selecting a provenance step shows it read-only in the Inspector (Step role); active is unchanged.
    private void ProvenanceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.SelectStep(ProvenanceList.SelectedItem as HistoryRowViewModel);

    // Fit the active single-image viewer to the stage (toolbar Fit action).
    private void Fit_Click(object sender, RoutedEventArgs e) => SingleImage.Fit();

    // Export the active view as a PNG. Persistence/export UI is a later task (P01); this is a lightweight
    // stage capture so the toolbar affordance is real rather than a placeholder.
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasActiveImage)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{_viewModel.ActiveTitle ?? "export"}.png",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var target = _viewModel.IsBeforeAfter ? (FrameworkElement)CompareContent : SingleImage;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Max(1, target.ActualWidth),
            (int)Math.Max(1, target.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }

    // Build transient render inputs and render them; retain nothing borrowed (V02 / ADR-011).
    private void RenderImages()
    {
        var colormap = _viewModel.Colormap;
        if (_viewModel.IsBeforeAfter && _viewModel.BeforeImage is { } before && _viewModel.ActiveImage is { } after)
        {
            // Each pane uses its OWN data range so both stay legible: a transform like Flatten removes the
            // Z offset, so a union range would wash the source to one extreme and the result to the other —
            // hiding the very texture/tilt the comparison exists to show. (A shared-range toggle is a later
            // refinement.) The axes are identical (same X/Y), which the BEFORE/AFTER labels make explicit.
            BeforeImageView.Render(RenderInputFactory.ForImage(before, colormap));
            AfterImageView.Render(RenderInputFactory.ForImage(after, colormap));
            SingleImage.Clear();
        }
        else if (_viewModel.ActiveImage is { } image)
        {
            SingleImage.Render(RenderInputFactory.ForImage(image, colormap));
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
