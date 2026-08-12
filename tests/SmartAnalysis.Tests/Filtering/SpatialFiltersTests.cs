using SmartAnalysis.Analysis.Filtering;
using Xunit;

namespace SmartAnalysis.Tests.Filtering;

/// <summary>The pure spatial-filter numeric core (A04): kernel/rank behaviour, unit gain, and borders.</summary>
public sealed class SpatialFiltersTests
{
    [Theory]
    [InlineData(FilterKind.Mean)]
    [InlineData(FilterKind.Gaussian)]
    [InlineData(FilterKind.Median)]
    public void Smoothing_a_constant_surface_returns_the_constant(FilterKind kind)
    {
        var src = new float[25];
        System.Array.Fill(src, 7.5f);

        var result = SpatialFilters.Apply(src, 5, 5, kind, 3);

        Assert.All(result, v => Assert.Equal(7.5f, v, 4));
    }

    [Fact]
    public void Output_keeps_the_input_dimensions()
    {
        var src = new float[6 * 4];

        var result = SpatialFilters.Apply(src, 6, 4, FilterKind.Mean, 3);

        Assert.Equal(24, result.Length);
    }

    [Fact]
    public void Mean_3x3_center_is_the_neighbourhood_average()
    {
        // 3×3 ramp 1..9; the centre's 3×3 window average is the mean of all nine = 5.
        var src = new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var result = SpatialFilters.Apply(src, 3, 3, FilterKind.Mean, 3);

        Assert.Equal(5f, result[4], 4); // index (1,1)
    }

    [Fact]
    public void Median_removes_a_single_spike()
    {
        var src = new float[] { 1, 1, 1, 1, 9, 1, 1, 1, 1 }; // lone spike at the centre

        var result = SpatialFilters.Apply(src, 3, 3, FilterKind.Median, 3);

        Assert.Equal(1f, result[4], 4);
    }

    [Fact]
    public void Sobel_of_a_constant_surface_is_zero()
    {
        var src = new float[16];
        System.Array.Fill(src, 3f);

        var result = SpatialFilters.Apply(src, 4, 4, FilterKind.Sobel, 3);

        Assert.All(result, v => Assert.Equal(0f, v, 4));
    }

    [Fact]
    public void Laplacian_of_a_constant_surface_is_zero()
    {
        var src = new float[16];
        System.Array.Fill(src, 2f);

        var result = SpatialFilters.Apply(src, 4, 4, FilterKind.Laplacian, 3);

        Assert.All(result, v => Assert.Equal(0f, v, 4));
    }

    [Fact]
    public void Is_deterministic()
    {
        var src = new float[] { 1, 4, 2, 8, 5, 7, 3, 6, 9 };

        var a = SpatialFilters.Apply(src, 3, 3, FilterKind.Gaussian, 3);
        var b = SpatialFilters.Apply(src, 3, 3, FilterKind.Gaussian, 3);

        Assert.Equal(a, b);
    }
}
