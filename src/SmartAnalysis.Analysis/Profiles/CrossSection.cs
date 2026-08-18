namespace SmartAnalysis.Analysis.Profiles;

/// <summary>Which line of the image to extract as a 1D profile.</summary>
public enum ProfileOrientation
{
    /// <summary>A horizontal cut at a fixed row (Y = index); the profile runs along X.</summary>
    Row,

    /// <summary>A vertical cut at a fixed column (X = index); the profile runs along Y.</summary>
    Column,
}

/// <summary>
/// Clean-room <b>cross-section</b> extraction: a single row or column of a row-major image, copied into a 1D
/// profile. No interpolation (grid-aligned cut), so the profile keeps the source samples exactly and its axis is
/// the source scan axis (X for a row, Y for a column). Pure, deterministic and domain-free — it works on a
/// row-major span like <see cref="Filtering.SpatialFilters"/>, headlessly testable.
/// </summary>
public static class CrossSection
{
    /// <param name="values">Row-major samples, length <c>width·height</c>.</param>
    /// <param name="width">Samples per row.</param>
    /// <param name="height">Number of rows.</param>
    /// <param name="orientation">Row (along X, at Y = index) or Column (along Y, at X = index).</param>
    /// <param name="index">The row (0..height-1) or column (0..width-1) to extract.</param>
    /// <returns>The extracted line: length <c>width</c> for a row, <c>height</c> for a column.</returns>
    public static float[] Extract(ReadOnlySpan<float> values, int width, int height, ProfileOrientation orientation, int index)
    {
        if (width < 1 || height < 1)
        {
            return [];
        }

        if (orientation == ProfileOrientation.Row)
        {
            if ((uint)index >= (uint)height)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Row must be in [0, {height}).");
            }

            var row = new float[width];
            values.Slice(index * width, width).CopyTo(row);
            return row;
        }

        if ((uint)index >= (uint)width)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Column must be in [0, {width}).");
        }

        var column = new float[height];
        for (int y = 0; y < height; y++)
        {
            column[y] = values[(y * width) + index];
        }

        return column;
    }
}
