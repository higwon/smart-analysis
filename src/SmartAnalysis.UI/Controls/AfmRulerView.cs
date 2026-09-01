using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>Which edge of the image a ruler runs along.</summary>
public enum RulerEdge
{
    /// <summary>Under the image, counting left to right.</summary>
    Bottom,

    /// <summary>Beside the image, counting top to bottom.</summary>
    Left,
}

/// <summary>
/// One edge's ruler: an axis line, a tick per mark, the mark's number, and the unit named once.
/// <para>
/// Drawn <b>outside</b> the image, as legacy does, which is why nothing here needs a halo or an outline: a bar
/// sitting over pixel data would, because the colormap can be any colour under it. Out here the control paints on
/// the window's own background in the theme's own axis colour, which is legible there by construction.
/// </para>
/// <para>
/// It knows nothing about scans, zoom or units — it is handed <see cref="RulerTicks"/> and draws them. Where the
/// marks go is <see cref="AxisRuler"/>'s decision and what part of the image is visible is the viewport's; this
/// is the part that is only about pixels and glyphs.
/// </para>
/// </summary>
public sealed class AfmRulerView : FrameworkElement
{
    /// <summary>How far the tick lines reach from the axis line, in device-independent pixels.</summary>
    private const double TickLength = 5.0;

    private const double LabelGap = 2.0;
    private const double FontSize = 11.0;

    private RulerTicks _ticks = RulerTicks.None;
    private double _offset;
    private double _length;

    /// <summary>
    /// The colour of the line, the ticks and the numbers.
    /// <para>
    /// A dependency property, and set by <b>reference</b> to the theme's axis brush rather than copied from it.
    /// Switching Light to Dark swaps the whole palette dictionary — a new <see cref="Brush"/> instance, not an
    /// edited one — so anything that read the old instance once keeps painting in the theme the user just left.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(AfmRulerView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public AfmRulerView()
    {
        Edge = RulerEdge.Bottom;

        // Gray stays the fallback for a host with no design system merged in, which is what the test host is.
        SetResourceReference(ForegroundProperty, "SA.Brush.Chart.Axis");
    }

    public RulerEdge Edge { get; set; }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Replaces the marks and redraws. Passing <see cref="RulerTicks.None"/> leaves a blank gutter.
    /// <para>
    /// <paramref name="offset"/> and <paramref name="length"/> are where the IMAGE sits along this edge, not
    /// where the gutter does. A fitted image is letterboxed inside its viewport, and a ruler stretched across the
    /// whole gutter would put its 0 mark in the empty margin — every number correct and none of them over the
    /// sample it names.
    /// </para>
    /// </summary>
    public void SetTicks(RulerTicks ticks, double offset, double length)
    {
        _ticks = ticks ?? RulerTicks.None;
        _offset = offset;
        _length = length;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);

        double w = ActualWidth;
        double h = ActualHeight;
        if (!(w > 0) || !(h > 0) || !(_length > 0) || _ticks.Ticks.Count == 0)
        {
            return;
        }

        var pen = new Pen(Foreground, 1.0);
        pen.Freeze();

        // The axis line runs along the image, not along the gutter: it is the image's edge that is being measured.
        if (Edge == RulerEdge.Bottom)
        {
            drawingContext.DrawLine(pen, new Point(_offset, 0), new Point(_offset + _length, 0));
        }
        else
        {
            drawingContext.DrawLine(pen, new Point(w, _offset), new Point(w, _offset + _length));
        }

        foreach (var tick in _ticks.Ticks)
        {
            if (!double.IsFinite(tick.Fraction))
            {
                continue;
            }

            if (Edge == RulerEdge.Bottom)
            {
                double x = _offset + (tick.Fraction * _length);
                drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, TickLength));

                var text = Label(tick.Label);
                // Centred on its tick, then held inside the gutter so the first and last numbers are not clipped.
                double left = Math.Clamp(x - (text.Width / 2), 0, Math.Max(0, w - text.Width));
                drawingContext.DrawText(text, new Point(left, TickLength + LabelGap));
            }
            else
            {
                double y = _offset + (tick.Fraction * _length);
                drawingContext.DrawLine(pen, new Point(w - TickLength, y), new Point(w, y));

                var text = Label(tick.Label);
                double top = Math.Clamp(y - (text.Height / 2), 0, Math.Max(0, h - text.Height));
                drawingContext.DrawText(text, new Point(Math.Max(0, w - TickLength - LabelGap - text.Width), top));
            }
        }

        if (_ticks.Ticks.Count > 0 && !string.IsNullOrEmpty(_ticks.Unit))
        {
            DrawUnit(drawingContext, w, h);
        }
    }

    // Named once rather than suffixed onto every number: a column of "0.5 um / 1.0 um / 1.5 um" is three words
    // saying the same thing and two fewer numbers that fit.
    private void DrawUnit(DrawingContext drawingContext, double w, double h)
    {
        var unit = Label(_ticks.Unit);
        if (Edge == RulerEdge.Bottom)
        {
            drawingContext.DrawText(unit, new Point(Math.Max(0, w - unit.Width), TickLength + LabelGap));
            return;
        }

        // Rotated a quarter turn, as legacy does, because a vertical gutter is 30 px wide and a word is not.
        drawingContext.PushTransform(new RotateTransform(-90, 0, 0));
        drawingContext.DrawText(unit, new Point(-unit.Width, 0));
        drawingContext.Pop();
    }

    private FormattedText Label(string text)
        => new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            FontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
