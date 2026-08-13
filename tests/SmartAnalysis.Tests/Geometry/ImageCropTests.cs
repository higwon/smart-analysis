using SmartAnalysis.Analysis.Geometry;
using Xunit;

namespace SmartAnalysis.Tests.Geometry;

/// <summary>A07a crop numeric core: copies a rectangular block out of a row-major image.</summary>
public sealed class ImageCropTests
{
    [Fact]
    public void Extract_copies_the_requested_block()
    {
        // 4×3 source, values = row-major index:
        //   0  1  2  3
        //   4  5  6  7
        //   8  9 10 11
        var source = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        var block = ImageCrop.Extract(source, 4, left: 1, top: 1, width: 2, height: 2);

        Assert.Equal(new float[] { 5, 6, 9, 10 }, block);
    }

    [Fact]
    public void Extract_returns_empty_for_a_nonpositive_size()
    {
        Assert.Empty(ImageCrop.Extract([1, 2, 3, 4], 2, 0, 0, 0, 0));
    }
}
