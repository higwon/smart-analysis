using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The concrete WPF backend for the V01 <see cref="IImageView"/> port (V02): renders an
/// <see cref="ImageRenderInput"/> to a <c>Bgra32 WriteableBitmap</c> through the AFM data colormap
/// (<see cref="ImagePixelMapper"/>) and a colormap legend. Nearest-neighbor scaling keeps AFM pixels crisp.
/// Interaction (legacy-style, math in the testable <see cref="ImageViewportMath"/>): the image <b>fits the
/// viewport by default</b> and fit is the <b>zoomed-out limit</b>; the wheel <b>zooms toward the cursor</b>;
/// the image is <b>only pannable once zoomed in past fit</b> (at fit it stays centred, pan clamped so no edge
/// enters the viewport); double-click re-fits. The data colormap is theme-independent (ADR-008); only the
/// chrome uses <c>SA.*</c> tokens. No ROI (V06).
/// <para>
/// <b>Lifetime (ADR-011 / V01 contract):</b> <see cref="Render"/> consumes/copies the borrowed
/// <see cref="ImageRenderInput.Z"/> during the call and <b>retains nothing borrowed</b> — the control
/// keeps only its own owned <c>WriteableBitmap</c> and legend brush. It deliberately exposes <b>no
/// bindable <c>ImageRenderInput</c> property</b>: a DP would hold the input (and thus the borrowed <c>Z</c>
/// view) for the control's lifetime, violating the contract. Callers (U02 orchestration) build a fresh
/// input at render time and call <see cref="Render"/>; nothing keeps it afterwards.
/// </para>
/// </summary>
public partial class AfmImageView : UserControl, IImageView
{
    private int _bmpW;
    private int _bmpH;
    private bool _needsFit;
    private bool _dragging;
    private Point _lastPos;
    private const double HandleSize = 9.0;

    private double _fitScale = ImageViewportMath.MinScale; // the zoomed-out limit; refreshed by Fit()/SizeChanged
    private (int Left, int Top, int Width, int Height)? _regionPreview;   // the requested region (raw form values)
    private (int Left, int Top, int Width, int Height)? _effectiveRegion; // clamped to the image — what is shown AND dragged
    private readonly (RegionHandle Handle, Rectangle Rect)[] _regionHandles;
    private RegionHandle _regionHandle = RegionHandle.None; // the handle being dragged, if any
    private (int Left, int Top, int Width, int Height) _regionStart;
    private (double X, double Y) _regionStartPixel;
    private bool _regionEditable;
    private bool _regionIsEllipse;

    private (double X0, double Y0, double X1, double Y1)? _linePreview;   // the requested line (raw form values)
    private (double X0, double Y0, double X1, double Y1)? _effectiveLine; // clamped to the image — shown AND dragged
    private readonly (LineHandle Handle, Ellipse Dot)[] _lineHandles;     // [Start, End]
    private LineHandle _lineHandle = LineHandle.None;
    private (double X0, double Y0, double X1, double Y1) _lineStart;
    private (double X, double Y) _lineStartPixel;
    private bool _lineEditable;

    public AfmImageView()
    {
        InitializeComponent();
        Palette.RangeChanged += (_, r) => RangeChanged?.Invoke(this, r);
        RegionOverlay.Cursor = Cursors.SizeAll;
        RegionOverlay.MouseLeftButtonDown += (_, e) => BeginRegionDrag(RegionHandle.Body, e);
        _regionHandles = BuildRegionHandles();
        LineHitArea.Cursor = Cursors.SizeAll; // the wide transparent line is the grab target for the thin visible one
        LineHitArea.MouseLeftButtonDown += (_, e) => BeginLineDrag(LineHandle.Body, e);
        _lineHandles = BuildLineHandles();
    }

    /// <summary>Raised while the user drags the region overlay: the new (left, top, width, height) in pixels.</summary>
    public event EventHandler<(int Left, int Top, int Width, int Height)>? RegionChanged;

    /// <summary>The region shown/dragged: the requested rectangle clamped to the image (null when hidden).</summary>
    public (int Left, int Top, int Width, int Height)? EffectiveRegion => _effectiveRegion;

    /// <summary>Whether the region overlay can be dragged/resized (shows the handles). Single view only.</summary>
    public bool IsRegionEditable
    {
        get => _regionEditable;
        set { _regionEditable = value; UpdateOverlay(); }
    }

    /// <summary>Draw the region as an inscribed ellipse (with a dashed bounding box) instead of a filled rectangle.</summary>
    public bool RegionIsEllipse
    {
        get => _regionIsEllipse;
        set { _regionIsEllipse = value; UpdateOverlay(); }
    }

    /// <summary>
    /// Shows a live rectangle overlay in image-pixel space (e.g. the Crop region while its form is open),
    /// clamped to the image so it matches the effective crop. Tracks pan/zoom. Pass nothing to hide it.
    /// </summary>
    public void SetRegionPreview(int left, int top, int width, int height)
    {
        _regionPreview = (left, top, width, height);
        UpdateOverlay();
    }

    /// <summary>Hides the region-preview overlay — the rectangle, the eight handles, and the effective region.</summary>
    public void ClearRegionPreview()
    {
        _regionPreview = null;
        HideOverlay(); // clears _effectiveRegion and hides BOTH the rectangle and the handles
    }

    // Positions the region overlay in screen space from the current image transform (constant stroke).
    private void UpdateOverlay()
    {
        UpdateLineOverlay(); // the profile line tracks pan/zoom from the same call sites
        PositionPointMarkers(); // and so do the measurement-point markers
        if (_regionPreview is not { } r || _bmpW <= 0 || _bmpH <= 0)
        {
            HideOverlay();
            return;
        }

        // Clamp to the image (the crop clamps too): the same box is shown, dragged, and cut.
        var (left, top, width, height) = RegionEditMath.ClampToImage(r.Left, r.Top, r.Width, r.Height, _bmpW, _bmpH);
        if (width <= 0 || height <= 0)
        {
            HideOverlay();
            return;
        }

        _effectiveRegion = (left, top, width, height);
        double s = ImgScale.ScaleX;
        double x = (left * s) + ImgTranslate.X;
        double y = (top * s) + ImgTranslate.Y;
        double w = width * s;
        double h = height * s;
        Canvas.SetLeft(RegionOverlay, x);
        Canvas.SetTop(RegionOverlay, y);
        RegionOverlay.Width = w;
        RegionOverlay.Height = h;
        RegionOverlay.Visibility = Visibility.Visible;

        // For an ellipse ROI: the ellipse carries the fill, and the bounding rectangle becomes a dashed guide.
        if (_regionIsEllipse)
        {
            Canvas.SetLeft(RegionEllipseOverlay, x);
            Canvas.SetTop(RegionEllipseOverlay, y);
            RegionEllipseOverlay.Width = w;
            RegionEllipseOverlay.Height = h;
            RegionEllipseOverlay.Visibility = Visibility.Visible;
            RegionOverlay.Fill = Brushes.Transparent;
            RegionOverlay.StrokeDashArray = new DoubleCollection { 3, 3 };
        }
        else
        {
            RegionEllipseOverlay.Visibility = Visibility.Collapsed;
            RegionOverlay.SetResourceReference(Shape.FillProperty, "SA.Brush.Chart.Selection");
            RegionOverlay.StrokeDashArray = null;
        }

        PositionRegionHandles(x, y, w, h, _regionEditable);
    }

    private void HideOverlay()
    {
        _effectiveRegion = null;
        RegionOverlay.Visibility = Visibility.Collapsed;
        RegionEllipseOverlay.Visibility = Visibility.Collapsed;
        if (_regionHandles is not null)
        {
            foreach (var (_, rect) in _regionHandles)
            {
                rect.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ---- Profile line overlay: draw/drag a 2-point line whose endpoints drive the line-profile op ----

    /// <summary>Raised while the user drags the profile line: the new (x0, y0, x1, y1) endpoints in image pixels.</summary>
    public event EventHandler<(double X0, double Y0, double X1, double Y1)>? LineChanged;

    /// <summary>The line shown/dragged: the requested endpoints clamped to the image (null when hidden).</summary>
    public (double X0, double Y0, double X1, double Y1)? EffectiveLine => _effectiveLine;

    /// <summary>Whether the profile line can be dragged (shows the endpoint handles). Single view only.</summary>
    public bool IsLineEditable
    {
        get => _lineEditable;
        set { _lineEditable = value; UpdateLineOverlay(); }
    }

    /// <summary>Shows a live profile line in image-pixel space (e.g. while the line-profile form is open).</summary>
    public void SetLinePreview(double x0, double y0, double x1, double y1)
    {
        _linePreview = (x0, y0, x1, y1);
        UpdateLineOverlay();
    }

    /// <summary>Hides the profile-line overlay — the line, both endpoint handles, and the effective line.</summary>
    public void ClearLinePreview()
    {
        _linePreview = null;
        HideLineOverlay();
    }


    // ---- Measurement-point markers (UX03) ----------------------------------------------------------------

    private IReadOnlyList<(double X, double Y)> _pointMarkers = [];
    private int _selectedMarker = -1;
    private readonly List<System.Windows.Shapes.Ellipse> _markerShapes = [];

    /// <summary>Raised when a marker is clicked, with its index.</summary>
    public event EventHandler<int>? PointMarkerClicked;

    /// <summary>
    /// Shows the places a spectroscopy acquisition measured, in image-pixel space, with one marked as selected.
    /// The markers keep a constant screen size so a dense map stays clickable at any zoom.
    /// </summary>
    public void SetPointMarkers(IReadOnlyList<(double X, double Y)> points, int selectedIndex)
    {
        _pointMarkers = points ?? [];
        _selectedMarker = selectedIndex;
        RebuildPointMarkers();
    }

    /// <summary>Removes the markers — a dataset with no recorded positions must not keep the last one's.</summary>
    public void ClearPointMarkers()
    {
        _pointMarkers = [];
        _selectedMarker = -1;
        RebuildPointMarkers();
    }

    private void RebuildPointMarkers()
    {
        foreach (var shape in _markerShapes)
        {
            OverlayLayer.Children.Remove(shape);
        }

        _markerShapes.Clear();

        if (_pointMarkers.Count == 0 || _bmpW <= 0 || _bmpH <= 0)
        {
            return;
        }

        for (int i = 0; i < _pointMarkers.Count; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = MarkerSize,
                Height = MarkerSize,
                StrokeThickness = 1.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = i,
            };

            // Unselected markers are hollow so they never hide the surface underneath; the selected one is
            // filled, because "which curve am I looking at" has to be answerable at a glance.
            dot.SetResourceReference(
                System.Windows.Shapes.Shape.StrokeProperty,
                i == _selectedMarker ? "SA.Brush.Accent.Primary" : "SA.Brush.Text.Secondary");
            if (i == _selectedMarker)
            {
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SA.Brush.Accent.Primary");
            }
            else
            {
                dot.Fill = System.Windows.Media.Brushes.Transparent;
            }

            dot.MouseLeftButtonDown += PointMarker_Click;
            OverlayLayer.Children.Add(dot);
            _markerShapes.Add(dot);
        }

        PositionPointMarkers();
    }

    private void PointMarker_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            PointMarkerClicked?.Invoke(this, index);
            e.Handled = true; // a marker click selects a point; it must not also start a pan
        }
    }

    private void PositionPointMarkers()
    {
        double s = ImgScale.ScaleX;
        for (int i = 0; i < _markerShapes.Count; i++)
        {
            var (x, y) = _pointMarkers[i];
            Canvas.SetLeft(_markerShapes[i], (x * s) + ImgTranslate.X - (MarkerSize / 2));
            Canvas.SetTop(_markerShapes[i], (y * s) + ImgTranslate.Y - (MarkerSize / 2));
        }
    }

    private const double MarkerSize = 9.0;

    private void UpdateLineOverlay()
    {
        if (_linePreview is not { } l || _bmpW <= 0 || _bmpH <= 0)
        {
            HideLineOverlay();
            return;
        }

        var (x0, y0, x1, y1) = LineEditMath.ClampToImage(l.X0, l.Y0, l.X1, l.Y1, _bmpW, _bmpH);
        _effectiveLine = (x0, y0, x1, y1);

        double s = ImgScale.ScaleX;
        double sx0 = (x0 * s) + ImgTranslate.X, sy0 = (y0 * s) + ImgTranslate.Y;
        double sx1 = (x1 * s) + ImgTranslate.X, sy1 = (y1 * s) + ImgTranslate.Y;
        LineOverlay.X1 = sx0; LineOverlay.Y1 = sy0;
        LineOverlay.X2 = sx1; LineOverlay.Y2 = sy1;
        LineOverlay.Visibility = Visibility.Visible;
        LineHitArea.X1 = sx0; LineHitArea.Y1 = sy0;
        LineHitArea.X2 = sx1; LineHitArea.Y2 = sy1;
        LineHitArea.Visibility = _lineEditable ? Visibility.Visible : Visibility.Collapsed; // only grab-able when editable
        PositionLineHandles(sx0, sy0, sx1, sy1, _lineEditable);
    }

    private void HideLineOverlay()
    {
        _effectiveLine = null;
        LineOverlay.Visibility = Visibility.Collapsed;
        LineHitArea.Visibility = Visibility.Collapsed;
        if (_lineHandles is not null)
        {
            foreach (var (_, dot) in _lineHandles)
            {
                dot.Visibility = Visibility.Collapsed;
            }
        }
    }

    // Builds the two endpoint handles once; positioned/shown by UpdateLineOverlay.
    private (LineHandle Handle, Ellipse Dot)[] BuildLineHandles()
    {
        var handles = new (LineHandle, Ellipse)[2];
        var which = new[] { LineHandle.Start, LineHandle.End };
        for (int i = 0; i < which.Length; i++)
        {
            var handle = which[i];
            var dot = new Ellipse
            {
                Width = HandleSize,
                Height = HandleSize,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Cursor = Cursors.Cross,
                Visibility = Visibility.Collapsed,
            };
            dot.SetResourceReference(Shape.FillProperty, "SA.Brush.Accent.Primary"); // theme-aware
            dot.MouseLeftButtonDown += (_, e) => BeginLineDrag(handle, e);
            OverlayLayer.Children.Add(dot);
            handles[i] = (handle, dot);
        }

        return handles;
    }

    private void PositionLineHandles(double sx0, double sy0, double sx1, double sy1, bool visible)
    {
        foreach (var (handle, dot) in _lineHandles)
        {
            dot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                continue;
            }

            double cx = handle == LineHandle.Start ? sx0 : sx1;
            double cy = handle == LineHandle.Start ? sy0 : sy1;
            Canvas.SetLeft(dot, cx - (HandleSize / 2.0));
            Canvas.SetTop(dot, cy - (HandleSize / 2.0));
        }
    }

    private void BeginLineDrag(LineHandle handle, MouseButtonEventArgs e)
    {
        if (!_lineEditable || _effectiveLine is not { } line)
        {
            return;
        }

        _lineHandle = handle;
        _lineStart = line;
        var p = e.GetPosition(Viewport);
        _lineStartPixel = RegionEditMath.ScreenToPixel(p.X, p.Y, ImgScale.ScaleX, ImgTranslate.X, ImgTranslate.Y);
        Viewport.CaptureMouse();
        e.Handled = true; // don't let the Viewport start a pan
    }

    private void DragLine(MouseEventArgs e)
    {
        var p = e.GetPosition(Viewport);
        var (px, py) = RegionEditMath.ScreenToPixel(p.X, p.Y, ImgScale.ScaleX, ImgTranslate.X, ImgTranslate.Y);
        var next = LineEditMath.Drag(
            _lineHandle, _lineStart.X0, _lineStart.Y0, _lineStart.X1, _lineStart.Y1,
            px - _lineStartPixel.X, py - _lineStartPixel.Y, _bmpW, _bmpH);
        _linePreview = next;
        UpdateLineOverlay();
        LineChanged?.Invoke(this, next);
    }

    /// <summary>Raised while the user drags a palette-bar handle: the new (min, max) value window.</summary>
    public event EventHandler<(double Min, double Max)>? RangeChanged;

    /// <summary>Whether the palette bar's min/max handles are draggable (single view) or read-only (compare panes).</summary>
    public bool IsRangeEditable
    {
        get => Palette.Editable;
        set => Palette.Editable = value;
    }

    /// <summary>
    /// V01 port entry point: render <paramref name="input"/> now. The borrowed pixels are consumed into an
    /// owned bitmap during this call; the input is <b>not</b> retained (ADR-011). Call again to re-render.
    /// </summary>
    public void Render(ImageRenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RenderCore(input);
    }

    /// <summary>Clears the view (e.g. when there is no active image). Releases the owned bitmap.</summary>
    public void Clear()
    {
        ClearPointMarkers();
        Img.Source = null;
        Palette.Clear();
        _bmpW = _bmpH = 0;
        UpdateOverlay(); // hides the region overlay when there is no image
    }

    private void RenderCore(ImageRenderInput input)
    {

        // Map the borrowed Z during this call (ADR-011) and pack into a Bgra32 bitmap.
        var rgb = ImagePixelMapper.Map(input);
        int w = input.Width, h = input.Height;
        var buffer = new byte[checked(w * h * 4)];
        for (int i = 0; i < rgb.Length; i++)
        {
            int o = i * 4;
            buffer[o] = rgb[i].B;
            buffer[o + 1] = rgb[i].G;
            buffer[o + 2] = rgb[i].R;
            buffer[o + 3] = 255;
        }

        var bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, w, h), buffer, w * 4, 0);
        Img.Source = bitmap;
        _bmpW = w;
        _bmpH = h;

        Palette.Update(input.Colormap, input.DataRange, input.Range, input.ChannelUnit, input.HasUnmeasured);

        if (Viewport.ActualWidth > 0 && Viewport.ActualHeight > 0)
        {
            Fit();
        }
        else
        {
            _needsFit = true;
        }
    }

    /// <summary>Fits the image to the viewport and centers it (also the double-click / toolbar Fit action).</summary>
    public void Fit()
    {
        if (_bmpW <= 0 || _bmpH <= 0 || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        _fitScale = ImageViewportMath.FitScale(Viewport.ActualWidth, Viewport.ActualHeight, _bmpW, _bmpH);
        var (x, y) = ImageViewportMath.Center(_fitScale, Viewport.ActualWidth, Viewport.ActualHeight, _bmpW, _bmpH);
        ImgScale.ScaleX = ImgScale.ScaleY = _fitScale;
        ImgTranslate.X = x;
        ImgTranslate.Y = y;
        _needsFit = false;
        UpdateOverlay();
    }

    private void ApplyTranslateClamp()
    {
        var (x, y) = ImageViewportMath.ClampTranslate(
            ImgTranslate.X, ImgTranslate.Y, ImgScale.ScaleX, Viewport.ActualWidth, Viewport.ActualHeight, _bmpW, _bmpH);
        ImgTranslate.X = x;
        ImgTranslate.Y = y;
        UpdateOverlay();
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_bmpW <= 0 || _bmpH <= 0)
        {
            return;
        }

        // Decide against the OLD fit scale (were we at fit?) BEFORE refreshing it — comparing the current
        // scale to the NEW fit floor would drop out of fit when the viewport shrinks.
        double newFit = ImageViewportMath.FitScale(Viewport.ActualWidth, Viewport.ActualHeight, _bmpW, _bmpH);
        bool refit = _needsFit || ImageViewportMath.ShouldRefitOnResize(ImgScale.ScaleX, _fitScale, newFit);
        _fitScale = newFit;

        if (refit)
        {
            Fit(); // stays at fit (re-centred for the new size); also recomputes _fitScale
        }
        else
        {
            ApplyTranslateClamp(); // keep the zoom, just re-clamp the pan so no edge enters the viewport
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Img.Source is null)
        {
            return;
        }

        e.Handled = true; // the wheel is our zoom — don't also scroll a parent ScrollViewer

        var p = e.GetPosition(Viewport);
        var (scale, x, y) = ImageViewportMath.ZoomAtCursor(
            ImgScale.ScaleX, ImgTranslate.X, ImgTranslate.Y, p.X, p.Y, e.Delta > 0, _fitScale);

        // Zooming back out to (or below) fit snaps to a centred fit rather than an off-centre fit-scale image.
        if (scale <= _fitScale)
        {
            Fit();
            return;
        }

        ImgScale.ScaleX = ImgScale.ScaleY = scale;
        ImgTranslate.X = x;
        ImgTranslate.Y = y;
        ApplyTranslateClamp();
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Img.Source is null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            Fit();
            return;
        }

        // Pan only when zoomed in past fit — at fit the image is fully shown and stays centred.
        if (!ImageViewportMath.CanPan(ImgScale.ScaleX, _fitScale))
        {
            return;
        }

        _dragging = true;
        _lastPos = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_regionHandle != RegionHandle.None)
        {
            DragRegion(e);
            return;
        }

        if (_lineHandle != LineHandle.None)
        {
            DragLine(e);
            return;
        }

        if (!_dragging)
        {
            // Hint pannability while zoomed in.
            Cursor = ImageViewportMath.CanPan(ImgScale.ScaleX, _fitScale) ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        var p = e.GetPosition(Viewport);
        ImgTranslate.X += p.X - _lastPos.X;
        ImgTranslate.Y += p.Y - _lastPos.Y;
        _lastPos = p;
        ApplyTranslateClamp();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_regionHandle != RegionHandle.None)
        {
            _regionHandle = RegionHandle.None;
            Viewport.ReleaseMouseCapture();
            return;
        }

        if (_lineHandle != LineHandle.None)
        {
            _lineHandle = LineHandle.None;
            Viewport.ReleaseMouseCapture();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            Viewport.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    // ---- Region overlay interaction (V06): drag the body to move, an edge/corner handle to resize ----

    private void BeginRegionDrag(RegionHandle handle, MouseButtonEventArgs e)
    {
        // Drag from the EFFECTIVE (clamped, on-screen) region, not the raw request — so the box the user sees
        // is exactly the box that moves/resizes (an over-large form width must not drag from its raw value).
        if (!_regionEditable || _effectiveRegion is not { } region)
        {
            return;
        }

        _regionHandle = handle;
        _regionStart = region;
        var p = e.GetPosition(Viewport);
        _regionStartPixel = RegionEditMath.ScreenToPixel(p.X, p.Y, ImgScale.ScaleX, ImgTranslate.X, ImgTranslate.Y);
        Viewport.CaptureMouse();
        e.Handled = true; // don't let the Viewport start a pan
    }

    private void DragRegion(MouseEventArgs e)
    {
        var p = e.GetPosition(Viewport);
        var (px, py) = RegionEditMath.ScreenToPixel(p.X, p.Y, ImgScale.ScaleX, ImgTranslate.X, ImgTranslate.Y);
        var next = RegionEditMath.Drag(
            _regionHandle, _regionStart.Left, _regionStart.Top, _regionStart.Width, _regionStart.Height,
            px - _regionStartPixel.X, py - _regionStartPixel.Y, _bmpW, _bmpH);
        _regionPreview = next;
        UpdateOverlay();
        RegionChanged?.Invoke(this, next);
    }

    // Builds the eight resize handles once; they are positioned/shown by UpdateOverlay.
    private (RegionHandle Handle, Rectangle Rect)[] BuildRegionHandles()
    {
        (RegionHandle Handle, Cursor Cursor)[] specs =
        [
            (RegionHandle.TopLeft, Cursors.SizeNWSE), (RegionHandle.Top, Cursors.SizeNS), (RegionHandle.TopRight, Cursors.SizeNESW),
            (RegionHandle.Left, Cursors.SizeWE), (RegionHandle.Right, Cursors.SizeWE),
            (RegionHandle.BottomLeft, Cursors.SizeNESW), (RegionHandle.Bottom, Cursors.SizeNS), (RegionHandle.BottomRight, Cursors.SizeNWSE),
        ];

        var handles = new (RegionHandle, Rectangle)[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            var handle = specs[i].Handle;
            var rect = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Cursor = specs[i].Cursor,
                Visibility = Visibility.Collapsed,
            };
            rect.SetResourceReference(Shape.FillProperty, "SA.Brush.Accent.Primary"); // theme-aware
            rect.MouseLeftButtonDown += (_, e) => BeginRegionDrag(handle, e);
            OverlayLayer.Children.Add(rect);
            handles[i] = (handle, rect);
        }

        return handles;
    }

    // Positions each handle at its corner/edge of the screen-space region rect; hidden when not editable.
    private void PositionRegionHandles(double x, double y, double w, double h, bool visible)
    {
        foreach (var (handle, rect) in _regionHandles)
        {
            rect.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                continue;
            }

            double cx = handle switch
            {
                RegionHandle.TopLeft or RegionHandle.Left or RegionHandle.BottomLeft => x,
                RegionHandle.TopRight or RegionHandle.Right or RegionHandle.BottomRight => x + w,
                _ => x + (w / 2.0),
            };
            double cy = handle switch
            {
                RegionHandle.TopLeft or RegionHandle.Top or RegionHandle.TopRight => y,
                RegionHandle.BottomLeft or RegionHandle.Bottom or RegionHandle.BottomRight => y + h,
                _ => y + (h / 2.0),
            };
            Canvas.SetLeft(rect, cx - (HandleSize / 2.0));
            Canvas.SetTop(rect, cy - (HandleSize / 2.0));
        }
    }
}
