namespace SmartAnalysis.Analysis.Flattening;

// NOTE: the explicit numeric values are part of the provenance contract (recorded as dimensionless
// integers in the ProvenanceStep with operation version 1). Do not renumber without bumping the
// operation version.

/// <summary>Which regression the flatten subtracts.</summary>
public enum FlattenScope
{
    /// <summary>Fit and subtract an independent polynomial per line.</summary>
    Line = 0,

    /// <summary>Fit the perpendicular-averaged profile once and subtract it from every line.</summary>
    Whole = 1,

    /// <summary>Fit and subtract a single bivariate polynomial surface.</summary>
    Surface = 2,
}

/// <summary>The line direction for Line/Whole flatten.</summary>
public enum FlattenOrientation
{
    /// <summary>Lines run along the fast (X / width) axis.</summary>
    FastAxis = 0,

    /// <summary>Lines run along the slow (Y / height) axis.</summary>
    SlowAxis = 1,
}

/// <summary>What to do with the absolute Z level after subtracting the regression (legacy's two options).</summary>
public enum BasementOption
{
    /// <summary>Leave the subtracted regression at zero (legacy <c>Set_Regression_Line_To_Zero</c>, default).</summary>
    RegressionToZero = 0,

    /// <summary>Shift so the flattened midpoint equals the original midpoint (legacy <c>Preserve_Original_Midpoint</c>).</summary>
    PreserveOriginalMidpoint = 1,
}

/// <summary>
/// Full-image Flatten reproducing the legacy numeric behavior headlessly (no WPF/ROI). Operates on a
/// row-major <c>width × height</c> Z buffer, fits with the golden-matched <see cref="Polynomials"/>,
/// subtracts in <b>float</b> precision (legacy parity), then applies the basement option. ROI-restricted
/// flatten is D02 (out of MVP). See ADR — end-to-end legacy orchestration golden is deferred (MV00
/// captured the fit primitives only).
/// </summary>
public static class Flatten
{
    public static float[] Apply(
        ReadOnlySpan<float> z,
        int width,
        int height,
        FlattenScope scope,
        int order,
        FlattenOrientation orientation,
        BasementOption basement)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (z.Length != checked(width * height))
        {
            throw new ArgumentException($"z length ({z.Length}) must equal width*height ({width}*{height}).", nameof(z));
        }

        var result = new float[z.Length];
        z.CopyTo(result);

        switch (scope)
        {
            case FlattenScope.Line:
                ApplyLine(z, result, width, height, order, orientation);
                break;
            case FlattenScope.Whole:
                ApplyWhole(z, result, width, height, order, orientation);
                break;
            case FlattenScope.Surface:
                ApplySurface(z, result, width, height, order);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Undefined flatten scope.");
        }

        ApplyBasement(z, result, basement);
        return result;
    }

    private static void ApplyLine(ReadOnlySpan<float> z, float[] result, int width, int height, int order, FlattenOrientation orientation)
    {
        bool fast = orientation == FlattenOrientation.FastAxis;
        int lineCount = fast ? height : width;
        int lineLength = fast ? width : height;
        if (lineLength <= order)
        {
            return; // low rank: leave lines unflattened (legacy catches and skips)
        }

        var positions = Indices(lineLength);
        for (int line = 0; line < lineCount; line++)
        {
            var zs = new double[lineLength];
            for (int i = 0; i < lineLength; i++)
            {
                zs[i] = z[LinearIndex(fast, line, i, width)];
            }

            double[] baseline;
            try
            {
                baseline = Polynomials.Infer1D(Polynomials.Fit1D(positions, zs, order), positions);
            }
            catch
            {
                continue; // low-rank / singular line: leave as original
            }

            for (int i = 0; i < lineLength; i++)
            {
                int idx = LinearIndex(fast, line, i, width);
                result[idx] = (float)z[idx] - (float)baseline[i];
            }
        }
    }

    private static void ApplyWhole(ReadOnlySpan<float> z, float[] result, int width, int height, int order, FlattenOrientation orientation)
    {
        bool fast = orientation == FlattenOrientation.FastAxis;
        int lineCount = fast ? height : width;
        int lineLength = fast ? width : height;
        if (lineLength <= order)
        {
            return;
        }

        // Perpendicular-averaged profile along the orientation axis.
        var averaged = new double[lineLength];
        for (int i = 0; i < lineLength; i++)
        {
            double sum = 0;
            for (int line = 0; line < lineCount; line++)
            {
                sum += z[LinearIndex(fast, line, i, width)];
            }

            averaged[i] = sum / lineCount;
        }

        var positions = Indices(lineLength);
        double[] baseline;
        try
        {
            baseline = Polynomials.Infer1D(Polynomials.Fit1D(positions, averaged, order), positions);
        }
        catch
        {
            return; // leave original
        }

        for (int line = 0; line < lineCount; line++)
        {
            for (int i = 0; i < lineLength; i++)
            {
                int idx = LinearIndex(fast, line, i, width);
                result[idx] = (float)z[idx] - (float)baseline[i];
            }
        }
    }

    private static void ApplySurface(ReadOnlySpan<float> z, float[] result, int width, int height, int order)
    {
        int n = width * height;
        var xs = new double[n];
        var ys = new double[n];
        var zs = new double[n];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width) + x;
                xs[idx] = x;
                ys[idx] = y;
                zs[idx] = z[idx];
            }
        }

        double[] surface;
        try
        {
            var fit = new SurfacePolynomial(order);
            fit.Fit(xs, ys, zs);
            surface = fit.Infer(xs, ys);
        }
        catch
        {
            return; // rank-deficient (too few points for the order): leave original
        }

        for (int i = 0; i < n; i++)
        {
            result[i] = (float)z[i] - (float)surface[i];
        }
    }

    private static void ApplyBasement(ReadOnlySpan<float> original, float[] flattened, BasementOption basement)
    {
        switch (basement)
        {
            case BasementOption.RegressionToZero:
                return; // subtracting the regression already places the baseline at zero
            case BasementOption.PreserveOriginalMidpoint:
                double originalMid = Midpoint(original);
                double flattenedMid = Midpoint(flattened);
                float shift = (float)(originalMid - flattenedMid);
                for (int i = 0; i < flattened.Length; i++)
                {
                    flattened[i] += shift;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(basement), basement, "Undefined basement option.");
        }
    }

    private static double Midpoint(ReadOnlySpan<float> values)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return (max + min) / 2;
    }

    private static double[] Indices(int count)
    {
        var xs = new double[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = i;
        }

        return xs;
    }

    // Fast axis: line = row y, i = column x → y*width + x. Slow axis: line = column x, i = row y → y*width + x.
    private static int LinearIndex(bool fast, int line, int i, int width)
        => fast ? (line * width) + i : (i * width) + line;
}
