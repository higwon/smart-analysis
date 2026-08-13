using SmartAnalysis.UI.Controls;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.Controls;

/// <summary>V02 image-view control smoke: it constructs, renders an <see cref="ImageRenderInput"/>, fits, clears.</summary>
public sealed class AfmImageViewTests
{
    [Fact]
    public void Renders_fits_and_clears_without_error()
    {
        var ok = WpfTestHost.Invoke(() =>
        {
            var view = new AfmImageView();
            var z = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
            var input = new ImageRenderInput(
                z, 4, 4,
                ValueRange.FromData(z),
                Colormap.AfmGold,
                new AxisView("X", "um", 0, 4, 4),
                new AxisView("Y", "um", 0, 4, 4),
                "um");

            view.Render(input); // packs the borrowed pixels into an owned bitmap + builds the legend
            view.Fit();         // fit math runs even without a laid-out viewport (no-op when unmeasured)
            view.Clear();
            return true;
        });

        Assert.True(ok);
    }
}
