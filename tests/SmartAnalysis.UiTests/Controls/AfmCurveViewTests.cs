using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>V03 curve-view control smoke: it constructs, resolves the SA Chart theme, renders + clears.</summary>
public sealed class AfmCurveViewTests
{
    [Fact]
    public void Renders_and_clears_without_error()
    {
        var ok = WpfTestHost.Invoke(() =>
        {
            var view = new AfmCurveView();
            var x = new double[] { 0, 1, 2, 3 };
            var input = new CurveRenderInput(
                new[] { new XySeries("Height", x, new double[] { 0, 2, 1, 3 }) },
                new AxisView("Position", "nm", 0, 3, 4),
                new AxisView("Height", "nm", 0, 3, 4));

            view.Render(input); // resolves Chart.* from the merged design system + refreshes
            view.Clear();
            return true;
        });

        Assert.True(ok);
    }
}
