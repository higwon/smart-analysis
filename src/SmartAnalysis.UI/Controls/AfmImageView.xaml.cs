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
/// (<see cref="ImagePixelMapper"/>), with mouse-wheel zoom (around the cursor), drag pan, double-click Fit,
/// and a colormap legend. Nearest-neighbor scaling keeps AFM pixels crisp. The data colormap is
/// theme-independent (ADR-008); only the chrome uses <c>SA.*</c> tokens. No ROI (V06).
/// </summary>
public partial class AfmImageView : UserControl, IImageView
{
    private const double MinScale = 0.02;
    private const double MaxScale = 128.0;

    private int _bmpW;
    private int _bmpH;
    private bool _needsFit;
    private bool _dragging;
    private Point _lastPos;

    public AfmImageView() => InitializeComponent();

    /// <summary>The image to display. Setting it (re)renders and fits to the viewport.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageRenderInput), typeof(AfmImageView),
        new PropertyMetadata(null, (d, e) => ((AfmImageView)d).ApplyInput(e.NewValue as ImageRenderInput)));

    public ImageRenderInput? Source
    {
        get => (ImageRenderInput?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>V01 port entry point — equivalent to setting <see cref="Source"/>.</summary>
    public void Render(ImageRenderInput input) => Source = input;

    private void ApplyInput(ImageRenderInput? input)
    {
        if (input is null)
        {
            Img.Source = null;
            LegendBar.Background = null;
            MaxLabel.Text = MinLabel.Text = UnitLabel.Text = string.Empty;
            return;
        }

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

        BuildLegend(input.Colormap, input.Range, input.ChannelUnit);

        if (Viewport.ActualWidth > 0 && Viewport.ActualHeight > 0)
        {
            Fit();
        }
        else
        {
            _needsFit = true;
        }
    }

    private void BuildLegend(Colormap colormap, ValueRange range, string unit)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        const int stops = 32;
        for (int i = 0; i <= stops; i++)
        {
            double offset = (double)i / stops;
            var c = colormap.SampleNormalized(1.0 - offset); // top = max, bottom = min
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), offset));
        }

        LegendBar.Background = brush;
        MaxLabel.Text = Format(range.Max);
        MinLabel.Text = Format(range.Min);
        UnitLabel.Text = unit;
    }

    private static string Format(double value)
        => double.IsFinite(value) ? value.ToString("G4", CultureInfo.InvariantCulture) : "—";

    private void Fit()
    {
        if (_bmpW <= 0 || _bmpH <= 0 || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        double scale = Math.Min(Viewport.ActualWidth / _bmpW, Viewport.ActualHeight / _bmpH) * 0.96;
        scale = Clamp(scale);
        ImgScale.ScaleX = ImgScale.ScaleY = scale;
        ImgTranslate.X = (Viewport.ActualWidth - (_bmpW * scale)) / 2.0;
        ImgTranslate.Y = (Viewport.ActualHeight - (_bmpH * scale)) / 2.0;
        _needsFit = false;
    }

    private static double Clamp(double scale) => scale < MinScale ? MinScale : scale > MaxScale ? MaxScale : scale;

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_needsFit)
        {
            Fit();
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Img.Source is null)
        {
            return;
        }

        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newScale = Clamp(ImgScale.ScaleX * factor);
        double actual = newScale / ImgScale.ScaleX;
        var p = e.GetPosition(Viewport); // zoom around the cursor
        ImgTranslate.X = p.X - ((p.X - ImgTranslate.X) * actual);
        ImgTranslate.Y = p.Y - ((p.Y - ImgTranslate.Y) * actual);
        ImgScale.ScaleX = ImgScale.ScaleY = newScale;
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

        _dragging = true;
        _lastPos = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var p = e.GetPosition(Viewport);
        ImgTranslate.X += p.X - _lastPos.X;
        ImgTranslate.Y += p.Y - _lastPos.Y;
        _lastPos = p;
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
