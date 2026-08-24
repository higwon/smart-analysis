using System.Linq;
using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>V03 curve composition: the ScottPlot plot built from a V01 <see cref="CurveRenderInput"/>.</summary>
public sealed class CurvePlotBuilderTests
{
    private static CurveTheme Theme() => new(
        new ScottPlot.Color(0xF7, 0xF8, 0xFA),
        new ScottPlot.Color(0xF7, 0xF8, 0xFA),
        new ScottPlot.Color(0xE2, 0xE5, 0xEA),
        new ScottPlot.Color(0x5B, 0x64, 0x72),
        [new ScottPlot.Color(0x25, 0x63, 0xEB), new ScottPlot.Color(0x7C, 0x84, 0x94)]);

    private static CurveRenderInput Input(int seriesCount, double[]? markers = null)
    {
        var x = new double[] { 0, 1, 2, 3 };
        var series = Enumerable.Range(0, seriesCount)
            .Select(i => new XySeries($"s{i}", x, new double[] { 0, i + 1, i, i + 2 }))
            .ToArray();
        return new CurveRenderInput(series, new AxisView("Position", "nm", 0, 3, 4), new AxisView("Height", "nm", 0, 5, 4), markers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Configure_adds_one_plottable_per_series(int seriesCount)
    {
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(seriesCount), Theme());

        Assert.Equal(seriesCount, plot.GetPlottables<ScottPlot.Plottables.SignalXY>().Count());
    }

    [Fact]
    public void Configure_sets_the_axis_titles_from_the_input()
    {
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(1), Theme());

        Assert.Equal("Position (nm)", plot.Axes.Bottom.Label.Text);
        Assert.Equal("Height (nm)", plot.Axes.Left.Label.Text);
    }

    [Fact]
    public void Configure_draws_a_vertical_line_per_marker()
    {
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(1, markers: [0.5, 2.5]), Theme()); // e.g. a crop range's boundaries

        Assert.Equal(2, plot.GetPlottables<ScottPlot.Plottables.VerticalLine>().Count());
    }

    [Fact]
    public void Configure_puts_both_series_on_the_same_shared_y_axis()
    {
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(2), Theme()); // SOURCE + PREVIEW overlay

        // Both series must share ONE Y axis (the left) — a truthful before/after amplitude comparison. (Independent
        // per-curve axes would make a small-amplitude result look the same height as a large-amplitude source.)
        var signals = plot.GetPlottables<ScottPlot.Plottables.SignalXY>().ToList();
        Assert.Equal(2, signals.Count);
        Assert.All(signals, s => Assert.Same(plot.Axes.Left, s.Axes.YAxis));
    }

    [Fact]
    public void Configure_clears_previous_series_on_reconfigure()
    {
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(2), Theme());
        CurvePlotBuilder.Configure(plot, Input(1), Theme()); // re-render with fewer series

        Assert.Single(plot.GetPlottables<ScottPlot.Plottables.SignalXY>()); // not accumulated
    }
}
