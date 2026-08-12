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
            var line = plot.Add.SignalXY(s.X.ToArray(), s.Y.ToArray());
            line.LegendText = s.Name;
            line.Color = palette[i % palette.Length];
            line.LineWidth = 1.5f;
        }

        if (input.Series.Count > 1)
        {
            plot.ShowLegend();
        }

        plot.Axes.AutoScale();
    }
}
