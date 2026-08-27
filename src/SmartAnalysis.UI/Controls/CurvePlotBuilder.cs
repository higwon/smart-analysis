using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.UI.Controls;

/// <summary>Resolved chart colours (from the SA <c>Chart.*</c> tokens) for one render.</summary>
public readonly record struct CurveTheme(
    ScottPlot.Color Figure,
    ScottPlot.Color DataArea,
    ScottPlot.Color Grid,
    ScottPlot.Color Axis,
    ScottPlot.Color[] Series);

/// <summary>
/// Configures a ScottPlot <see cref="ScottPlot.Plot"/> from a V01 <see cref="CurveRenderInput"/> and a
/// theme (V03). Kept separate from the WPF control so the plot composition is testable + head­lessly
/// render­able (ScottPlot core, no WPF). <b>Lifetime (ADR-011):</b> the borrowed <c>XySeries</c> X/Y are
/// copied into ScottPlot-owned arrays during the call — nothing borrowed is retained.
/// </summary>
public static class CurvePlotBuilder
{
    public static void Configure(ScottPlot.Plot plot, CurveRenderInput input, CurveTheme theme)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(input);

        plot.Clear();
        plot.FigureBackground.Color = theme.Figure;
        plot.DataBackground.Color = theme.DataArea;
        plot.Grid.MajorLineColor = theme.Grid;
        plot.Axes.Color(theme.Axis);

        plot.XLabel($"{input.X.Title} ({input.X.Unit})");
        plot.YLabel($"{input.Y.Title} ({input.Y.Unit})");

        var palette = theme.Series is { Length: > 0 } ? theme.Series : [new ScottPlot.Color(37, 99, 235)];
        for (int i = 0; i < input.Series.Count; i++)
        {
            var s = input.Series[i];
            // ToArray copies the borrowed ReadOnlyMemory — ScottPlot owns the copy; we retain nothing (V02/ADR-011).
            var xs = s.X.ToArray();
            var ys = s.Y.ToArray();

            // SignalXY is the fast path, but it REQUIRES ascending X: it indexes by position and takes the data
            // range from the first and last sample. A force curve sweeps Z down and back up, so its abscissa is
            // never ascending — SignalXY throws on it, and where it does not it would report a range running
            // backwards. Anything non-monotonic gets the general scatter line instead.
            if (IsAscending(xs))
            {
                var signal = plot.Add.SignalXY(xs, ys);
                signal.LegendText = s.Name;
                signal.Color = palette[i % palette.Length];
                signal.LineWidth = 1.5f;
            }
            else
            {
                var scatter = plot.Add.ScatterLine(xs, ys);
                scatter.LegendText = s.Name;
                scatter.Color = palette[i % palette.Length];
                scatter.LineWidth = 1.5f;
            }

        }

        if (input.Series.Count > 1)
        {
            plot.ShowLegend();
        }

        plot.Axes.AutoScale();

        // Vertical reference lines (e.g. a crop range's boundaries on the source curve), drawn after auto-scale so they
        // span the data area without widening it.
        foreach (var x in input.VerticalMarkers)
        {
            var marker = plot.Add.VerticalLine(x);
            marker.Color = theme.Axis;
            marker.LineWidth = 1.5f;
            marker.LinePattern = ScottPlot.LinePattern.Dashed;
        }

        // Horizontal reference lines (e.g. the non-contact level, and the force a threshold percentage means).
        foreach (var y in input.HorizontalMarkers)
        {
            var marker = plot.Add.HorizontalLine(y);
            marker.Color = theme.Axis;
            marker.LineWidth = 1.5f;
            marker.LinePattern = ScottPlot.LinePattern.Dashed;
        }
    }
    // Strictly ascending, which is what SignalXY needs. A single non-finite sample makes the answer false,
    // because SignalXY cannot order what it cannot compare.
    private static bool IsAscending(IReadOnlyList<double> xs)
    {
        for (int i = 1; i < xs.Count; i++)
        {
            if (!(xs[i] > xs[i - 1]))
            {
                return false;
            }
        }

        return xs.Count == 0 || double.IsFinite(xs[0]);
    }
}
