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
    public void Configure_puts_a_secondary_axis_series_on_its_own_right_y_axis()
    {
        var plot = new ScottPlot.Plot();
        var x = new double[] { 0, 1, 2, 3 };
        var input = new CurveRenderInput(
            [
                new XySeries("SOURCE", x, new double[] { 0.24, 0.24, 0.24, 0.24 }),
                new XySeries("PREVIEW", x, new double[] { 0, 0, 0, 0 }, onSecondaryAxis: true),
            ],
            new AxisView("Position", "nm", 0, 3, 4), new AxisView("Height", "nm", 0, 1, 4));

        CurvePlotBuilder.Configure(plot, input, Theme());

        Assert.True(plot.Axes.GetYAxes().Count() >= 2); // a left (source) + a right (preview) Y axis, each auto-scaled
    }

    [Fact]
    public void Configure_does_not_accumulate_right_axes_across_reconfigures()
    {
        var plot = new ScottPlot.Plot();
        var x = new double[] { 0, 1, 2, 3 };
        CurveRenderInput WithSecondary() => new(
            [
                new XySeries("SOURCE", x, new double[] { 0.24, 0.24, 0.24, 0.24 }),
                new XySeries("PREVIEW", x, new double[] { 0, 0, 0, 0 }, onSecondaryAxis: true),
            ],
            new AxisView("Position", "nm", 0, 3, 4), new AxisView("Height", "nm", 0, 1, 4));

        CurvePlotBuilder.Configure(plot, WithSecondary(), Theme());
        CurvePlotBuilder.Configure(plot, WithSecondary(), Theme()); // a live param change re-renders on the same plot
        CurvePlotBuilder.Configure(plot, WithSecondary(), Theme());

        Assert.Equal(2, plot.Axes.GetYAxes().Count()); // one left + exactly one right — not a new right axis each render
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
