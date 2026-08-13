using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>
/// The interactive palette bar / legend: a vertical colormap gradient whose axis is the fixed data extent,
/// with draggable <b>min</b> and <b>max</b> handles that set the value window mapped across the colormap
/// (the legacy palette-bar interaction). Below the min handle the colormap clamps to its first entry, above
/// the max handle to its last. Drag geometry lives in the pure, tested <see cref="PaletteBarMath"/>. When
/// <see cref="Editable"/> is false it is a plain read-only legend (e.g. the Before/After panes).
/// </summary>
public partial class PaletteBar : UserControl
{
    private const double HandleHalf = 7.0; // half the 14px triangle height (to centre it on its value)

    private Colormap? _colormap;
    private ValueRange _data;
    private double _min;
    private double _max;
    private int _dragging; // 0 none, 1 min, 2 max
    private bool _editable;

    public PaletteBar() => InitializeComponent();

    /// <summary>
    /// Raised when a handle drag is committed (mouse released), with the new (min, max) window. The bar itself
    /// updates live during the drag; the (potentially expensive) image re-render is deferred to commit.
    /// </summary>
    public event EventHandler<(double Min, double Max)>? RangeChanged;

    /// <summary>Whether the min/max handles are shown and draggable.</summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            var v = value ? Visibility.Visible : Visibility.Collapsed;
            MinHandle.Visibility = v;
            MaxHandle.Visibility = v;
        }
    }

    /// <summary>Sets the colormap, the fixed data axis and the current value window, then repaints.</summary>
    public void Update(Colormap colormap, ValueRange dataRange, ValueRange window, string unit)
    {
        _colormap = colormap;
        _data = dataRange;
        _min = window.Min;
        _max = window.Max;
        MaxLabel.Text = Format(dataRange.Max);
        MinLabel.Text = Format(dataRange.Min);
        UnitLabel.Text = unit;
        Repaint();
    }

    /// <summary>Clears the bar.</summary>
    public void Clear()
    {
        _colormap = null;
        Bar.Background = null;
        MaxLabel.Text = MinLabel.Text = UnitLabel.Text = string.Empty;
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => Repaint();

    private void Repaint()
    {
        if (_colormap is null || Track.ActualHeight <= 0)
        {
            return;
        }

        Bar.Height = Track.ActualHeight;
        Bar.Background = BuildGradient(_colormap, _data, _min, _max);
        PositionHandle(MinHandle, _min);
        PositionHandle(MaxHandle, _max);
    }

    private void PositionHandle(UIElement handle, double value)
    {
        double y = PaletteBarMath.YFor(value, Track.ActualHeight, _data);
        Canvas.SetTop(handle, y - HandleHalf);
    }

    // The colormap ramp mapped across [min,max] within the [dataMin,dataMax] axis: clamped solid below/above.
    private static Brush BuildGradient(Colormap colormap, ValueRange data, double min, double max)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0) }; // bottom→top
        double span = data.Max - data.Min;
        var low = ToColor(colormap.SampleNormalized(0.0));
        var high = ToColor(colormap.SampleNormalized(1.0));
        if (span <= 0)
        {
            brush.GradientStops.Add(new GradientStop(low, 0));
            brush.GradientStops.Add(new GradientStop(high, 1));
            return brush;
        }

        double nMin = Clamp01((min - data.Min) / span);
        double nMax = Clamp01((max - data.Min) / span);
        brush.GradientStops.Add(new GradientStop(low, 0));
        brush.GradientStops.Add(new GradientStop(low, nMin));
        if (nMax > nMin)
        {
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double offset = nMin + (t * (nMax - nMin));
                brush.GradientStops.Add(new GradientStop(ToColor(colormap.SampleNormalized(t)), offset));
            }
        }

        brush.GradientStops.Add(new GradientStop(high, nMax));
        brush.GradientStops.Add(new GradientStop(high, 1));
        return brush;
    }

    private void MinHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginDrag(1);

    private void MaxHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginDrag(2);

    private void BeginDrag(int which)
    {
        if (!_editable)
        {
            return;
        }

        _dragging = which;
        Track.CaptureMouse();
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == 0)
        {
            return;
        }

        double y = e.GetPosition(Track).Y;
        var (min, max) = _dragging == 1
            ? PaletteBarMath.DragMin(y, Track.ActualHeight, _data, _max)
            : PaletteBarMath.DragMax(y, Track.ActualHeight, _data, _min);
        _min = min;
        _max = max;
        Bar.Background = BuildGradient(_colormap!, _data, _min, _max); // live feedback on the bar
        PositionHandle(MinHandle, _min);
        PositionHandle(MaxHandle, _max);
    }

    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging != 0)
        {
            _dragging = 0;
            Track.ReleaseMouseCapture();
            RangeChanged?.Invoke(this, (_min, _max)); // commit → re-render the image once
        }
    }

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    private static string Format(double value)
        => double.IsFinite(value) ? value.ToString("G4", CultureInfo.InvariantCulture) : "—";
}
