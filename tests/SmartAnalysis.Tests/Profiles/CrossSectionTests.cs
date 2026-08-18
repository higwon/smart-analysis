using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// Cross-section extraction core: a row or column of a row-major image, copied exactly (no interpolation).
/// Pure and headless.
/// </summary>
public sealed class CrossSectionTests
{
    // A 4×3 image whose Z is the row-major index 0..11 (so a slice's contents are trivially known).
    private static float[] Ramp(int width, int height)
    {
        var z = new float[width * height];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = i;
        }

        return z;
    }

    [Fact]
    public void Extracts_a_row_along_x()
    {
        var z = Ramp(4, 3);

        var row = CrossSection.Extract(z, 4, 3, ProfileOrientation.Row, 1); // second row

        Assert.Equal(new float[] { 4, 5, 6, 7 }, row);
    }

    [Fact]
    public void Extracts_a_column_along_y()
    {
        var z = Ramp(4, 3);

        var column = CrossSection.Extract(z, 4, 3, ProfileOrientation.Column, 2); // third column

        Assert.Equal(new float[] { 2, 6, 10 }, column);
    }

    [Fact]
    public void Rejects_an_out_of_range_index()
    {
        var z = Ramp(4, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => CrossSection.Extract(z, 4, 3, ProfileOrientation.Row, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossSection.Extract(z, 4, 3, ProfileOrientation.Column, 4));
    }

    [Fact]
    public void Returns_empty_for_a_nonpositive_size()
    {
        Assert.Empty(CrossSection.Extract([], 0, 0, ProfileOrientation.Row, 0));
    }
}
