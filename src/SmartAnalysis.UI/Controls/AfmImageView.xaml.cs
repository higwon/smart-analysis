using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private double _fitScale = ImageViewportMath.MinScale; // the zoomed-out limit; refreshed by Fit()/SizeChanged

    public AfmImageView()
    {
        InitializeComponent();
        Palette.RangeChanged += (_, r) => RangeChanged?.Invoke(this, r);
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
    }

    private void ApplyTranslateClamp()
    {
        var (x, y) = ImageViewportMath.ClampTranslate(
            ImgTranslate.X, ImgTranslate.Y, ImgScale.ScaleX, Viewport.ActualWidth, Viewport.ActualHeight, _bmpW, _bmpH);
        ImgTranslate.X = x;
        ImgTranslate.Y = y;
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
        if (_dragging)
        {
            _dragging = false;
            Viewport.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }
}
