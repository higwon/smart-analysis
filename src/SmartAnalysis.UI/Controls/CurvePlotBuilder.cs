using System.Linq;
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

        // Clear() drops plottables but NOT axes added via AddRightAxis(), so on a reused plot they accumulate every
        // render (one extra right axis per refresh). Remove any non-default Y axis before rebuilding.
        foreach (var extra in plot.Axes.GetYAxes().Where(a => a != plot.Axes.Left).ToList())
        {
            plot.Axes.Remove(extra);
        }

        plot.FigureBackground.Color = theme.Figure;
        plot.DataBackground.Color = theme.DataArea;
        plot.Grid.MajorLineColor = theme.Grid;
        plot.Axes.Color(theme.Axis);

        plot.XLabel($"{input.X.Title} ({input.X.Unit})");
        plot.YLabel($"{input.Y.Title} ({input.Y.Unit})");

        var palette = theme.Series is { Length: > 0 } ? theme.Series : [new ScottPlot.Color(37, 99, 235)];
        ScottPlot.IYAxis? rightAxis = null;
        for (int i = 0; i < input.Series.Count; i++)
        {
            var s = input.Series[i];
            var color = palette[i % palette.Length];
            // ToArray copies the borrowed ReadOnlyMemory — ScottPlot owns the copy; we retain nothing (V02/ADR-011).
            var line = plot.Add.SignalXY(s.X.ToArray(), s.Y.ToArray());
            line.LegendText = s.Name;
            line.Color = color;
            line.LineWidth = 1.5f;

            // A secondary-axis series (e.g. a mean-removed PREVIEW next to its SOURCE) gets its own right Y axis,
            // auto-scaled to its own values, so both curves' shapes read clearly instead of one crushing the other.
            if (s.OnSecondaryAxis)
            {
                rightAxis ??= plot.Axes.AddRightAxis();
                line.Axes.YAxis = rightAxis;
                rightAxis.Label.Text = s.Name;                 // name the right scale (e.g. "PREVIEW")
                rightAxis.Label.ForeColor = color;             // tie the axis label to its series colour
                rightAxis.FrameLineStyle.Color = color;
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
    }
}
