namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>arbitrary-angle line profile</b> sampling: <paramref name="samples"/> evenly spaced points along
/// the segment from (x0,y0) to (x1,y1) in <b>pixel</b> coordinates, each read by <b>bilinear interpolation</b> of
/// the four surrounding samples. This is the drawn-line profile (legacy draws a line at any angle over the image);
/// it complements the grid-aligned <see cref="CrossSection"/> (exact row/column, no interpolation). Points are
/// clamped to the grid, so an endpoint on the border still samples. A point whose bilinear neighbourhood contains a
/// non-finite value yields <c>NaN</c> (the dropout is not silently interpolated away). Pure, deterministic,
/// domain-free — headlessly testable.
/// </summary>
public static class LineSampler
{
    /// <param name="values">Row-major samples, length <c>width·height</c>.</param>
    /// <param name="samples">Number of points along the line (&gt;= 2).</param>
    public static float[] Sample(
        ReadOnlySpan<float> values, int width, int height, double x0, double y0, double x1, double y1, int samples)
    {
        if (samples < 2 || width < 1 || height < 1)
        {
            return [];
        }

        var result = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / (samples - 1);
            result[i] = BilinearSample(values, width, height, x0 + (t * (x1 - x0)), y0 + (t * (y1 - y0)));
        }

        return result;
    }

    private static float BilinearSample(ReadOnlySpan<float> values, int width, int height, double px, double py)
    {
        // Clamp the sample point into the grid so a border endpoint still reads a value.
        px = Math.Clamp(px, 0.0, width - 1.0);
        py = Math.Clamp(py, 0.0, height - 1.0);

        int ix = (int)Math.Floor(px);
        int iy = (int)Math.Floor(py);
        if (ix >= width - 1)
        {
            ix = Math.Max(0, width - 2);
        }

        if (iy >= height - 1)
        {
            iy = Math.Max(0, height - 2);
        }

        double fx = px - ix;
        double fy = py - iy;
        int ix1 = Math.Min(ix + 1, width - 1);
        int iy1 = Math.Min(iy + 1, height - 1);

        float z00 = values[(iy * width) + ix];
        float z10 = values[(iy * width) + ix1];
        float z01 = values[(iy1 * width) + ix];
        float z11 = values[(iy1 * width) + ix1];
        if (!(float.IsFinite(z00) && float.IsFinite(z10) && float.IsFinite(z01) && float.IsFinite(z11)))
        {
            return float.NaN;
        }

        double top = z00 + (fx * (z10 - z00));
        double bottom = z01 + (fx * (z11 - z01));
        return (float)(top + (fy * (bottom - top)));
    }
}
