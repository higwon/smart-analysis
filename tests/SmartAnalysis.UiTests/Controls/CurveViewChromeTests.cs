using System.Linq;
using SmartAnalysis.UI.Controls;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>
/// TASK-UX07: the chart backend ships developer tooling that a viewer can reach by accident. A frame-rate badge
/// drawn over someone's data, with no label and no way to dismiss it, is not a feature of this product.
/// </summary>
public sealed class CurveViewChromeTests
{
    private static bool HasBenchmarkResponse(ScottPlot.WPF.WpfPlot plot)
        => plot.UserInputProcessor.UserActionResponses.Any(r => r.GetType().Name.Contains("Benchmark"));

    [Fact]
    public void The_chart_backend_really_does_ship_a_benchmark_response()
    {
        // The fix filters by type NAME, so it goes quiet the day ScottPlot renames the type. This is the half of
        // the pair that notices: if a stock plot no longer has one, the filter below is no longer doing anything
        // and something else is keeping the badge away — or nothing is.
        Assert.True(WpfTestHost.Invoke(() => HasBenchmarkResponse(new ScottPlot.WPF.WpfPlot())));
    }

    [Fact]
    public void The_curve_view_does_not_carry_it()
    {
        Assert.False(WpfTestHost.Invoke(() =>
        {
            var view = new AfmCurveView();
            return HasBenchmarkResponse((ScottPlot.WPF.WpfPlot)view.FindName("Plot")!);
        }));
    }

    [Fact]
    public void The_benchmark_overlay_starts_hidden()
        => Assert.False(WpfTestHost.Invoke(() =>
        {
            var view = new AfmCurveView();
            return ((ScottPlot.WPF.WpfPlot)view.FindName("Plot")!).Plot.Benchmark.IsVisible;
        }));
}
