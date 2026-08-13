namespace SmartAnalysis.Analysis.Geometry;

/// <summary>
/// Clean-room row-major crop (A07a): copies a rectangular sub-region out of an image. Pure, deterministic and
/// domain-free (a <c>float[]</c> in, a <c>float[]</c> out), like <see cref="ImageGeometry"/>. The rectangle
/// must already lie within the source (the operation clamps it before calling).
/// </summary>
public static class ImageCrop
{
    /// <summary>Extracts the <paramref name="width"/>×<paramref name="height"/> block at (<paramref name="left"/>, <paramref name="top"/>).</summary>
    public static float[] Extract(
        ReadOnlySpan<float> source, int sourceWidth, int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        if (left < 0 || top < 0 || sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "The crop rectangle must be within the source.");
        }

        var result = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            int srcRow = (top + y) * sourceWidth;
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                result[dstRow + x] = source[srcRow + left + x];
            }
        }

        return result;
    }
}
