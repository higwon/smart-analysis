using SmartAnalysis.Analysis.Flattening;
using Xunit;

namespace SmartAnalysis.Tests.Flattening;

/// <summary>TASK-A01: the pure Flatten numeric (each scope removes the matching trend to ~0).</summary>
public sealed class FlattenTests
{
    private const float Eps = 1e-3f; // float-precision subtraction of ~10s of nm

    private static void AssertAllNearZero(float[] values)
    {
        foreach (var v in values)
        {
            Assert.True(Math.Abs(v) < Eps, $"expected ~0 but was {v}");
        }
    }

    [Fact]
    public void Line_removes_an_independent_per_row_tilt()
    {
        const int w = 5, h = 3;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = ((y + 1) * x) + (y * 10); // each row a different line in x
            }
        }

        var result = Flatten.Apply(z, w, h, FlattenScope.Line, order: 1, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);

        AssertAllNearZero(result); // every row's own line is removed
    }

    [Fact]
    public void Whole_removes_a_global_tilt_shared_by_all_rows()
    {
        const int w = 6, h = 4;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = (2f * x) + 5f; // same profile in every row
            }
        }

        var result = Flatten.Apply(z, w, h, FlattenScope.Whole, order: 1, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);

        AssertAllNearZero(result);
    }

    [Fact]
    public void Surface_removes_a_plane()
    {
        const int w = 5, h = 4;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = 1f + (2f * x) + (3f * y);
            }
        }

        var result = Flatten.Apply(z, w, h, FlattenScope.Surface, order: 1, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);

        AssertAllNearZero(result);
    }

    [Fact]
    public void Surface_order2_removes_curvature()
    {
        const int w = 6, h = 5;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = 2f + (0.5f * x * x) + (0.25f * y * y) - x;
            }
        }

        var result = Flatten.Apply(z, w, h, FlattenScope.Surface, order: 2, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);

        AssertAllNearZero(result);
    }

    [Fact]
    public void SlowAxis_line_removes_a_per_column_tilt()
    {
        const int w = 3, h = 5;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                z[(y * w) + x] = ((x + 1) * y) + x; // each column a line in y
            }
        }

        var result = Flatten.Apply(z, w, h, FlattenScope.Line, order: 1, FlattenOrientation.SlowAxis, BasementOption.RegressionToZero);

        AssertAllNearZero(result);
    }

    [Fact]
    public void PreserveOriginalMidpoint_keeps_the_absolute_z_level()
    {
        const int w = 5, h = 1;
        var z = new float[w];
        for (int x = 0; x < w; x++)
        {
            z[x] = (2f * x) + 100f; // tilt around a high level
        }

        float originalMid = (z.Max() + z.Min()) / 2f;

        var zeroed = Flatten.Apply(z, w, h, FlattenScope.Line, 1, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);
        var preserved = Flatten.Apply(z, w, h, FlattenScope.Line, 1, FlattenOrientation.FastAxis, BasementOption.PreserveOriginalMidpoint);

        Assert.True(Math.Abs((zeroed.Max() + zeroed.Min()) / 2f) < Eps);                 // regression-to-zero → ~0 level
        Assert.True(Math.Abs((preserved.Max() + preserved.Min()) / 2f - originalMid) < Eps); // preserved → original level
    }

    [Fact]
    public void Low_order_rank_leaves_lines_unchanged()
    {
        const int w = 2, h = 2;
        var z = new float[] { 1, 2, 3, 4 };

        var result = Flatten.Apply(z, w, h, FlattenScope.Line, order: 3, FlattenOrientation.FastAxis, BasementOption.RegressionToZero);

        Assert.Equal(z, result); // width (2) <= order (3): nothing to fit, original preserved
    }
}
