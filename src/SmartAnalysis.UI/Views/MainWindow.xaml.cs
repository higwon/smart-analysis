using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
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
    private readonly ILineProfilePreview _lineProfilePreview;
    private readonly RegionContext _regionContext;

    public MainWindow(ShellViewModel viewModel, ThemeManager theme, ILineProfilePreview lineProfilePreview, RegionContext regionContext)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _lineProfilePreview = lineProfilePreview ?? throw new ArgumentNullException(nameof(lineProfilePreview));
        _regionContext = regionContext ?? throw new ArgumentNullException(nameof(regionContext));
        InitializeComponent();
        DataContext = viewModel;

        _viewModel.ImagesChanged += (_, _) => RenderImages();
        _viewModel.RoiChanged += (_, _) => RefreshRoiOverlay();
        // Dragging the single view's palette-bar handles sets a manual value range on the shell.
        SingleImage.RangeChanged += (_, r) => _viewModel.SetManualRange(r.Min, r.Max);
        // When a region operation form is open (Crop, Region Statistics, …), preview its region live on the
        // image — and let the user drag it; otherwise a drag edits the persistent ROI (when enabled).
        SingleImage.IsRegionEditable = true;
        SingleImage.RegionChanged += (_, r) =>
        {
            if (_regionFields.Count > 0)
            {
                WriteRegionFields(r);
            }
            else if (_viewModel.RoiEnabled)
            {
                UpdateRoi(r);
            }
        };
        // When a line-profile form is open, preview its line live on the image — and let the user drag it, with
        // the profile chart updating live from the dragged endpoints.
        SingleImage.IsLineEditable = true;
        SingleImage.LineChanged += (_, l) =>
        {
            WriteLineFields(l);
            RefreshLineProfileChart();
        };
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RenderImages();
    }

    // ---- Region preview: mirror any form's left/top/width/height as a draggable overlay on the image ----
    private static readonly string[] RegionFieldNames = ["left", "top", "width", "height"];
    private readonly List<ParameterFieldViewModel> _regionFields = new();

    // ---- Line preview: mirror any form's x0/y0/x1/y1 as a draggable profile line on the image ----
    private static readonly string[] LineFieldNames = ["x0", "y0", "x1", "y1"];
    private readonly List<ParameterFieldViewModel> _lineFields = new();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.OperationEditor))
        {
            WireRegionPreview();
            WireLinePreview();
        }
    }

    // Any operation form with left/top/width/height fields (Crop, Region Statistics, …) drives — and is driven
    // by — the draggable region overlay on the image.
    private void WireRegionPreview()
    {
        foreach (var field in _regionFields)
        {
            field.PropertyChanged -= RegionField_PropertyChanged;
        }

        _regionFields.Clear();

        if (_viewModel.OperationEditor is ParameterFormViewModel form && HasRegionFields(form))
        {
            foreach (var field in form.Fields)
            {
                _regionFields.Add(field);
                field.PropertyChanged += RegionField_PropertyChanged;
            }

            UpdateRegionPreview();
        }
        else
        {
            SingleImage.ClearRegionPreview();
        }

        RefreshRoiOverlay(); // restore the persistent ROI once a region form is no longer driving the overlay
    }

    private static bool HasRegionFields(ParameterFormViewModel form)
        => RegionFieldNames.All(name => form.Fields.Any(f => f.Name == name));

    private void RegionField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParameterFieldViewModel.Value))
        {
            UpdateRegionPreview();
        }
    }

    private void UpdateRegionPreview()
    {
        int Field(string name) => AsInt(_regionFields.FirstOrDefault(f => f.Name == name)?.Value);
        // A "shape" Choice field (Region Statistics) selects the ellipse overlay; region ops without it stay rectangular.
        var shape = _regionFields.FirstOrDefault(f => f.Name == "shape")?.Value?.ToString();
        SingleImage.RegionIsEllipse = string.Equals(shape, "Ellipse", StringComparison.OrdinalIgnoreCase);
        SingleImage.SetRegionPreview(Field("left"), Field("top"), Field("width"), Field("height"));
    }

    // A drag/resize of the region overlay writes the new extents back into the form fields.
    private void WriteRegionFields((int Left, int Top, int Width, int Height) r)
    {
        SetRegionField("left", r.Left);
        SetRegionField("top", r.Top);
        SetRegionField("width", r.Width);
        SetRegionField("height", r.Height);
    }

    private void SetRegionField(string name, int value)
    {
        var field = _regionFields.FirstOrDefault(f => f.Name == name);
        if (field is not null)
        {
            field.Value = value;
        }
    }

    // Any operation form with x0/y0/x1/y1 fields (the free line profile) drives — and is driven by — the
    // draggable profile line on the image.
    private void WireLinePreview()
    {
        foreach (var field in _lineFields)
        {
            field.PropertyChanged -= LineField_PropertyChanged;
        }

        _lineFields.Clear();

        if (_viewModel.OperationEditor is ParameterFormViewModel form && HasLineFields(form))
        {
            foreach (var field in form.Fields)
            {
                _lineFields.Add(field);
                field.PropertyChanged += LineField_PropertyChanged;
            }

            SeedDefaultLineIfEmpty();
            LineProfilePanel.Visibility = Visibility.Visible; // dock the live chart
            UpdateLinePreview();
        }
        else
        {
            SingleImage.ClearLinePreview();
            LineProfilePanel.Visibility = Visibility.Collapsed;
            ProfileChart.Clear();
        }
    }

    // A fresh line-profile form has all-zero endpoints (a degenerate point); seed a horizontal mid-line across
    // the active image so there is a usable line to drag from the start.
    private void SeedDefaultLineIfEmpty()
    {
        double Field(string name) => AsDouble(_lineFields.FirstOrDefault(f => f.Name == name)?.Value);
        if (Field("x0") != 0 || Field("y0") != 0 || Field("x1") != 0 || Field("y1") != 0)
        {
            return; // the user (or a prior draw) already set a line
        }

        if (_viewModel.ActiveImage is not { } image)
        {
            return;
        }

        WriteLineFields((0, (image.Y.Count - 1) / 2.0, image.X.Count - 1, (image.Y.Count - 1) / 2.0));
    }

    private static bool HasLineFields(ParameterFormViewModel form)
        => LineFieldNames.All(name => form.Fields.Any(f => f.Name == name));

    private void LineField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_writingLineFields && e.PropertyName == nameof(ParameterFieldViewModel.Value))
        {
            UpdateLinePreview();
        }
    }

    private bool _writingLineFields;

    // Form → overlay, then canonicalize the form to the effective (clamped) line the control shows, so the form
    // value, the drawn line, the drag source, the executed line, and the provenance all agree (the V06 lesson).
    private void UpdateLinePreview()
    {
        if (_writingLineFields)
        {
            return; // our own write-back — the overlay is already in sync
        }

        double Field(string name) => AsDouble(_lineFields.FirstOrDefault(f => f.Name == name)?.Value);
        SingleImage.SetLinePreview(Field("x0"), Field("y0"), Field("x1"), Field("y1"));
        if (SingleImage.EffectiveLine is { } e)
        {
            WriteLineFields(e);
        }

        RefreshLineProfileChart();
    }

    // Renders the live profile of the current effective line into the docked chart (no workspace mutation).
    private void RefreshLineProfileChart()
    {
        if (SingleImage.EffectiveLine is not { } e || _viewModel.ActiveImage is not { } image)
        {
            ProfileChart.Clear();
            return;
        }

        int samples = AsInt(_lineFields.FirstOrDefault(f => f.Name == "samples")?.Value);
        var input = _lineProfilePreview.Preview(image, e.X0, e.Y0, e.X1, e.Y1, samples);
        if (input is not null)
        {
            ProfileChart.Render(input);
        }
        else
        {
            ProfileChart.Clear();
        }
    }

    // Writes the effective endpoints into the form fields (a drag raises the effective line; a form edit is
    // canonicalized to it). Guarded so the write-back doesn't re-enter UpdateLinePreview.
    private void WriteLineFields((double X0, double Y0, double X1, double Y1) l)
    {
        _writingLineFields = true;
        try
        {
            SetLineField("x0", l.X0);
            SetLineField("y0", l.Y0);
            SetLineField("x1", l.X1);
            SetLineField("y1", l.Y1);
        }
        finally
        {
            _writingLineFields = false;
        }
    }

    private void SetLineField(string name, double value)
    {
        var field = _lineFields.FirstOrDefault(f => f.Name == name);
        if (field is not null)
        {
            field.Value = value;
        }
    }

    // ---- Persistent ROI: a drawn region (rect/ellipse) that region-capable ops (Roughness) apply to ----
    private (int Left, int Top, int Width, int Height)? _roiBbox;

    private void RefreshRoiOverlay()
    {
        // A region operation form drives the overlay itself (Crop/Region Statistics); don't fight it.
        if (_regionFields.Count > 0)
        {
            return;
        }

        if (!_viewModel.RoiEnabled || !_viewModel.CanUseRoi || _viewModel.ActiveImage is not { } image)
        {
            SingleImage.RegionIsEllipse = false;
            SingleImage.ClearRegionPreview();
            _regionContext.Current = null; // no ROI → region-capable ops run whole-image
            return;
        }

        // Seed a centred default the first time the ROI is enabled.
        _roiBbox ??= (image.X.Count / 4, image.Y.Count / 4, Math.Max(1, image.X.Count / 2), Math.Max(1, image.Y.Count / 2));
        SingleImage.RegionIsEllipse = _viewModel.RoiIsEllipse;
        SingleImage.SetRegionPreview(_roiBbox.Value.Left, _roiBbox.Value.Top, _roiBbox.Value.Width, _roiBbox.Value.Height);
        CommitRoi();
    }

    private void UpdateRoi((int Left, int Top, int Width, int Height) r)
    {
        _roiBbox = r;
        CommitRoi();
    }

    // Publish the drawn ROI (the effective, clamped region + shape) so the launcher attaches it to region ops.
    private void CommitRoi()
    {
        if (SingleImage.EffectiveRegion is not { } e)
        {
            _regionContext.Current = null;
            return;
        }

        _regionContext.Current = _viewModel.RoiIsEllipse
            ? new EllipseRoi(e.Left, e.Top, e.Width, e.Height)
            : new RectangleRoi(e.Left, e.Top, e.Width, e.Height);
    }

    // The form holds the raw UI primitive (int default, or the TextBox's string once edited).
    private static int AsInt(object? value) => value switch
    {
        int i => i,
        double d => (int)d,
        string s when int.TryParse(s, out var n) => n,
        _ => 0,
    };

    private static double AsDouble(object? value) => value switch
    {
        double d => d,
        int i => i,
        string s when double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
        _ => 0.0,
    };

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

        // ShowSingle3D (not raw Is3D) so an overlay editor that forces the 2D stage exports the visible 2D view.
        var target = ChooseExportTarget(_viewModel.IsBeforeAfter, _viewModel.ShowSingle3D, CompareContent, SingleSurface, SingleImage);
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

    /// <summary>
    /// Which view to export: the Before/After grid in compare mode, else the surface only when it is actually
    /// shown (<c>ShowSingle3D</c> — not the raw 3D preference, so an overlay editor forcing the 2D stage exports
    /// the 2D image the user sees), else the 2D image.
    /// </summary>
    public static FrameworkElement ChooseExportTarget(
        bool isBeforeAfter, bool showSingle3D, FrameworkElement compare, FrameworkElement surface, FrameworkElement image)
        => isBeforeAfter ? compare : showSingle3D ? surface : image;

    // Build transient render inputs and render them; retain nothing borrowed (V02 / ADR-011).
    private void RenderImages()
    {
        var colormap = _viewModel.Colormap;
        if (_viewModel.ActiveCurve is { } curve)
        {
            // The first curve-producing op (A08 PSD): route the active line profile to the curve view.
            SingleCurve.Render(RenderInputFactory.ForLineProfile(curve));
            SingleImage.Clear();
            SingleSurface.Clear();
            BeforeImageView.Clear();
            AfterImageView.Clear();
            return;
        }

        SingleCurve.Clear();
        if (_viewModel.ShowSingle3D && _viewModel.ActiveImage is { } surfaceImage)
        {
            // 3D surface view of the single active image (V04) — same render input as the 2D view. ShowSingle3D
            // (not raw Is3D) so an open overlay editor keeps the 2D stage even when 3D is the preference.
            SingleSurface.Render(RenderInputFactory.ForImage(surfaceImage, colormap, _viewModel.EffectiveRange));
            SingleImage.Clear();
            BeforeImageView.Clear();
            AfterImageView.Clear();
            return;
        }

        SingleSurface.Clear();
        if (_viewModel.IsFlattenPreview && _viewModel.ActiveImage is { } previewSource)
        {
            // Flatten settings preview: SOURCE (the active image) vs the uncommitted PREVIEW result, both with the
            // same colormap/range so the comparison is fair. The preview input is owned (safe to render/hold).
            BeforeImageView.Render(RenderInputFactory.ForImage(previewSource, colormap, _viewModel.EffectiveRange));
            if (_viewModel.FlattenPreviewInput is { } preview)
            {
                // Re-style the (cached) preview with the CURRENT colormap/range so a live palette change keeps SOURCE
                // and PREVIEW consistent — without re-running Flatten just because a colour changed.
                AfterImageView.Render(preview.WithStyle(colormap, _viewModel.EffectiveRange));
            }
            else
            {
                AfterImageView.Clear();
            }

            SingleImage.Clear();
            return;
        }

        if (_viewModel.IsBeforeAfter && _viewModel.BeforeImage is { } before && _viewModel.ActiveImage is { } after)
        {
            // Both panes honour the SAME palette setting the main screen uses (EffectiveRange): a manual range set
            // on the toolbar carries into compare so the two palettes share one scale and are directly comparable,
            // and the AFTER pane (the active image) matches its single-view palette exactly. In auto mode
            // (EffectiveRange is null) each pane still falls back to its own data min/max so both stay legible —
            // a Flatten result that removed the Z offset isn't washed to one extreme. Axes are identical (same X/Y).
            var range = _viewModel.EffectiveRange;
            BeforeImageView.Render(RenderInputFactory.ForImage(before, colormap, range));
            AfterImageView.Render(RenderInputFactory.ForImage(after, colormap, range));
            SingleImage.Clear();
        }
        else if (_viewModel.ActiveImage is { } image)
        {
            // The palette range (auto = data min/max, or a manual min/max set on the toolbar).
            SingleImage.Render(RenderInputFactory.ForImage(image, colormap, _viewModel.EffectiveRange));
            BeforeImageView.Clear();
            AfterImageView.Clear();
            RefreshRoiOverlay(); // draw + publish the persistent ROI onto the just-rendered 2D image
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
