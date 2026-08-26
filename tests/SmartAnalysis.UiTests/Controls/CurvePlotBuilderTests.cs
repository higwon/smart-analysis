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
    /// <summary>A force curve: Z sweeps down and back up, so the abscissa returns on itself.</summary>
    private static CurveRenderInput ForceCurveInput()
    {
        var x = new double[] { 0.6, 0.5, 0.4, 0.5, 0.6 };
        var y = new double[] { 0, 1, 40, 1, 0 };
        return new CurveRenderInput(
            [new XySeries("Force", x, y)],
            new AxisView("Z Height", "um", 0.4, 0.6, x.Length),
            new AxisView("Force", "nN", 0, 40, x.Length));
    }

    [Fact]
    public void A_curve_whose_abscissa_returns_on_itself_is_drawn_without_throwing()
    {
        // SignalXY indexes by position and REQUIRES ascending X — it throws outright on a force curve, whose Z
        // sweeps down and back up. Every force-distance plot in the product has this shape.
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, ForceCurveInput(), Theme());

        Assert.Empty(plot.GetPlottables<ScottPlot.Plottables.SignalXY>());
        Assert.Single(plot.GetPlottables<ScottPlot.Plottables.Scatter>());
    }

    [Fact]
    public void A_non_monotonic_curve_scales_to_its_real_extent()
    {
        // SignalXY takes the data range from the FIRST and LAST sample. On a curve that returns to where it
        // started that range is empty or backwards, and the trace ends up squeezed into a corner of the plot.
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, ForceCurveInput(), Theme());
        var limits = plot.Axes.GetLimits();

        Assert.True(limits.Left <= 0.4 && limits.Right >= 0.6, $"X limits {limits.Left}..{limits.Right} miss the data.");
        Assert.True(limits.Bottom <= 0 && limits.Top >= 40, $"Y limits {limits.Bottom}..{limits.Top} miss the data.");
    }

    [Fact]
    public void An_ascending_curve_still_takes_the_fast_path()
    {
        // A spatial profile or a spectrum IS ascending, and SignalXY is much faster for it. The choice is made
        // from the data, not from the caller.
        var plot = new ScottPlot.Plot();

        CurvePlotBuilder.Configure(plot, Input(1), Theme());

        Assert.Single(plot.GetPlottables<ScottPlot.Plottables.SignalXY>());
        Assert.Empty(plot.GetPlottables<ScottPlot.Plottables.Scatter>());
    }
}
