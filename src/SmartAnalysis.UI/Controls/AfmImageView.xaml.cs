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
    private (int Left, int Top, int Width, int Height)? _regionPreview; // e.g. the live Crop rectangle
    private readonly (RegionHandle Handle, Rectangle Rect)[] _regionHandles;
    private RegionHandle _regionHandle = RegionHandle.None; // the handle being dragged, if any
    private (int Left, int Top, int Width, int Height) _regionStart;
    private (double X, double Y) _regionStartPixel;
    private bool _regionEditable;

    public AfmImageView()
    {
        InitializeComponent();
        Palette.RangeChanged += (_, r) => RangeChanged?.Invoke(this, r);
        RegionOverlay.Cursor = Cursors.SizeAll;
        RegionOverlay.MouseLeftButtonDown += (_, e) => BeginRegionDrag(RegionHandle.Body, e);
        _regionHandles = BuildRegionHandles();
    }

    /// <summary>Raised while the user drags the region overlay: the new (left, top, width, height) in pixels.</summary>
    public event EventHandler<(int Left, int Top, int Width, int Height)>? RegionChanged;

    /// <summary>Whether the region overlay can be dragged/resized (shows the handles). Single view only.</summary>
    public bool IsRegionEditable
    {
        get => _regionEditable;
        set { _regionEditable = value; UpdateOverlay(); }
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

    /// <summary>Hides the region-preview overlay.</summary>
    public void ClearRegionPreview()
    {
        _regionPreview = null;
        RegionOverlay.Visibility = Visibility.Collapsed;
    }

    // Positions the region overlay in screen space from the current image transform (constant stroke).
    private void UpdateOverlay()
    {
        if (_regionPreview is not { } r || _bmpW <= 0 || _bmpH <= 0)
        {
            HideOverlay();
            return;
        }

        // Clamp to the image (the crop clamps too), so the preview shows the region that will actually be cut.
        int left = Math.Clamp(r.Left, 0, _bmpW);
        int top = Math.Clamp(r.Top, 0, _bmpH);
        int right = Math.Clamp(r.Left + r.Width, 0, _bmpW);
        int bottom = Math.Clamp(r.Top + r.Height, 0, _bmpH);
        if (right <= left || bottom <= top)
        {
            HideOverlay();
            return;
        }

        double s = ImgScale.ScaleX;
        double x = (left * s) + ImgTranslate.X;
        double y = (top * s) + ImgTranslate.Y;
        double w = (right - left) * s;
        double h = (bottom - top) * s;
        Canvas.SetLeft(RegionOverlay, x);
        Canvas.SetTop(RegionOverlay, y);
        RegionOverlay.Width = w;
        RegionOverlay.Height = h;
        RegionOverlay.Visibility = Visibility.Visible;
        PositionRegionHandles(x, y, w, h, _regionEditable);
    }

    private void HideOverlay()
    {
        RegionOverlay.Visibility = Visibility.Collapsed;
        if (_regionHandles is not null)
        {
            foreach (var (_, rect) in _regionHandles)
            {
                rect.Visibility = Visibility.Collapsed;
            }
        }
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

        Palette.Update(input.Colormap, input.DataRange, input.Range, input.ChannelUnit);

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
        if (!_regionEditable || _regionPreview is not { } region)
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
