using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.Tests.Visualization;

public sealed class ImagePixelMapperTests
{
    private static ImageRenderInput Input(float[] z, int width, int height, Colormap colormap, ValueRange range)
    {
        var axisX = new AxisView("X", "m", 0, width, width);
        var axisY = new AxisView("Y", "m", 0, height, height);
        return new ImageRenderInput(z, width, height, range, colormap, axisX, axisY, "m");
    }

    [Fact]
    public void Maps_values_through_range_and_colormap_row_major()
    {
        // Grayscale over [0,1]: 0 -> black, 1 -> white, 0.5 -> mid-gray.
        var input = Input(new[] { 0f, 0.5f, 1f }, width: 3, height: 1, Colormap.Grayscale, new ValueRange(0, 1));

        var pixels = ImagePixelMapper.Map(input);

        Assert.Equal(3, pixels.Length);
        Assert.Equal(new Rgb(0, 0, 0), pixels[0]);
        Assert.Equal(Colormap.Grayscale.Map(0.5, new ValueRange(0, 1)), pixels[1]);
        Assert.Equal(new Rgb(255, 255, 255), pixels[2]);
    }

    [Fact]
    public void Non_finite_samples_map_to_the_first_colormap_entry()
    {
        var input = Input(new[] { float.NaN, float.PositiveInfinity }, width: 2, height: 1,
            Colormap.AfmGold, new ValueRange(0, 10));

        var pixels = ImagePixelMapper.Map(input);

        var first = Colormap.AfmGold.Entries[0];
        Assert.Equal(first, pixels[0]);
        Assert.Equal(first, pixels[1]);
    }

    [Fact]
    public void Result_length_is_width_times_height()
    {
        var z = new float[3 * 2];
        var pixels = ImagePixelMapper.Map(Input(z, 3, 2, Colormap.Grayscale, new ValueRange(0, 1)));
        Assert.Equal(6, pixels.Length);
    }
}
