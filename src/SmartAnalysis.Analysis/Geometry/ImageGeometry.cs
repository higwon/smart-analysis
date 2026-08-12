namespace SmartAnalysis.Analysis.Geometry;

/// <summary>The raster orientation transforms offered by the geometry operation (A07).</summary>
public enum GeometryKind
{
    /// <summary>Mirror left↔right (reverse the column order).</summary>
    FlipHorizontal,

    /// <summary>Mirror top↔bottom (reverse the row order).</summary>
    FlipVertical,

    /// <summary>Rotate a half turn (equivalent to a horizontal flip followed by a vertical flip).</summary>
    Rotate180,

    /// <summary>Rotate a quarter turn clockwise (swaps width and height).</summary>
    Rotate90Cw,

    /// <summary>Rotate a quarter turn counter-clockwise (swaps width and height).</summary>
    Rotate90Ccw,
}

/// <summary>
/// Clean-room raster orientation transforms (A07): flips and 90/180° rotations. Pure, deterministic and
/// domain-free — it reorders a row-major <c>float[]</c> exactly like <see cref="Filtering.SpatialFilters"/>,
/// so it is headlessly testable with no WPF or domain types. The quarter-turn kinds swap width and height
/// (<see cref="SwapsAxes"/>), which the operation mirrors by swapping the X/Y scan axes.
/// </summary>
/// <remarks>
/// These are orientation transforms on the raster grid: the pixel extents (axis <c>Origin</c>/<c>Step</c>/
/// <c>Count</c>) travel with the reorientation, but absolute-coordinate bookkeeping under a mirror (reversing
/// an axis <c>Direction</c> so a flipped pixel keeps its original physical coordinate) is deliberately out of
/// scope — a display-orientation follow-up, not part of this MVP.
/// </remarks>
public static class ImageGeometry
{
    /// <summary>Whether the transform swaps width and height (the quarter-turn rotations).</summary>
    public static bool SwapsAxes(GeometryKind kind)
        => kind is GeometryKind.Rotate90Cw or GeometryKind.Rotate90Ccw;

    /// <summary>
    /// Reorients <paramref name="source"/> (row-major, <paramref name="width"/>×<paramref name="height"/>) and
    /// returns a fresh array. <paramref name="outWidth"/>/<paramref name="outHeight"/> report the result shape
    /// (swapped for the quarter-turn kinds).
    /// </summary>
    public static float[] Apply(
        ReadOnlySpan<float> source, int width, int height, GeometryKind kind, out int outWidth, out int outHeight)
    {
        if (width <= 0 || height <= 0)
        {
            outWidth = 0;
            outHeight = 0;
            return [];
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException("source length must equal width*height.", nameof(source));
        }

        outWidth = SwapsAxes(kind) ? height : width;
        outHeight = SwapsAxes(kind) ? width : height;
        var result = new float[width * height];

        switch (kind)
        {
            case GeometryKind.FlipHorizontal:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        result[(y * width) + x] = source[(y * width) + (width - 1 - x)];
                    }
                }

                break;

            case GeometryKind.FlipVertical:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        result[(y * width) + x] = source[((height - 1 - y) * width) + x];
                    }
                }

                break;

            case GeometryKind.Rotate180:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        result[(y * width) + x] = source[((height - 1 - y) * width) + (width - 1 - x)];
                    }
                }

                break;

            case GeometryKind.Rotate90Cw:
                // dest is height-wide, width-tall; dest(ox,oy) ← src(x=oy, y=height-1-ox).
                for (int oy = 0; oy < outHeight; oy++)
                {
                    for (int ox = 0; ox < outWidth; ox++)
                    {
                        result[(oy * outWidth) + ox] = source[((height - 1 - ox) * width) + oy];
                    }
                }

                break;

            case GeometryKind.Rotate90Ccw:
                // dest(ox,oy) ← src(x=width-1-oy, y=ox).
                for (int oy = 0; oy < outHeight; oy++)
                {
                    for (int ox = 0; ox < outWidth; ox++)
                    {
                        result[(oy * outWidth) + ox] = source[(ox * width) + (width - 1 - oy)];
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown geometry kind.");
        }

        return result;
    }
}
