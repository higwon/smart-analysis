using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Visualization.Colormaps;

namespace SmartAnalysis.Visualization.Rendering;

/// <summary>
/// The conversion boundary (doc 15): turns Domain datasets into library-agnostic render inputs. No
/// chart-library types cross into Domain/Analysis; downsampling/decimation of very large curves is a
/// documented follow-up. The AFM data colormap is supplied by the caller (theme-independent, ADR-008).
/// </summary>
public static class RenderInputFactory
{
    /// <summary>Builds an image render input; the value range defaults to the finite data min/max.</summary>
    public static ImageRenderInput ForImage(ScanImageDataset image, Colormap colormap, ValueRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(colormap);

        var z = image.Data.Memory;
        var dataRange = ValueRange.FromData(z.Span);       // the full extent → the palette bar's fixed axis
        var effectiveRange = range ?? dataRange;           // the display window (manual sub-range, or the full extent)
        return new ImageRenderInput(
            z,
            image.X.Count,
            image.Y.Count,
            effectiveRange,
            colormap,
            AxisView.FromAxis(image.X),
            AxisView.FromAxis(image.Y),
            image.Channel.Unit.Symbol,
            dataRange);
    }

    /// <summary>
    /// Like <see cref="ForImage"/> but <b>copies</b> the Z buffer so the input owns its data — for a transient/preview
    /// dataset that is disposed right after (e.g. the Flatten settings preview), where borrowing the pooled buffer
    /// would dangle once it is recycled.
    /// </summary>
    public static ImageRenderInput ForImageOwned(ScanImageDataset image, Colormap colormap, ValueRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(colormap);

        var z = image.Data.Memory.ToArray();               // owned copy — safe after the source dataset is disposed
        var dataRange = ValueRange.FromData(z);
        var effectiveRange = range ?? dataRange;
        return new ImageRenderInput(
            z,
            image.X.Count,
            image.Y.Count,
            effectiveRange,
            colormap,
            AxisView.FromAxis(image.X),
            AxisView.FromAxis(image.Y),
            image.Channel.Unit.Symbol,
            dataRange);
    }

    /// <summary>
    /// Builds a curve render input from a force curve: <b>force against separation</b>, the way a force–distance plot
    /// is read. Unlike a line profile there is no regular axis — separation is a measured channel, so the X values are
    /// its samples. Both channels are copied into owned arrays, so the input outlives the dataset (ADR-011).
    /// The axis extents are taken over <b>drawable pairs only</b>: a sample whose separation OR force is non-finite
    /// cannot be plotted, so neither of its coordinates contributes to a range (the samples themselves are kept as-is,
    /// so the dropout still reads as a gap).
    /// </summary>
    public static CurveRenderInput ForForceCurve(ForceCurveDataset curve, string? seriesName = null)
    {
        ArgumentNullException.ThrowIfNull(curve);

        int n = curve.Length;
        var separation = curve.Separation.Memory.Span;
        var force = curve.Force.Memory.Span;
        var xs = new double[n];
        var ys = new double[n];
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double x = separation[i], y = force[i];
            xs[i] = x;   // the raw samples are kept as-is, so a dropout stays a gap in the plotted line
            ys[i] = y;

            // In an XY plot a sample is a PAIR: if either coordinate is non-finite the point cannot be drawn, so
            // neither of its coordinates may set an extent. Checking the axes independently would let an undrawable
            // sample's finite half stretch the other axis and squash the real curve into a corner.
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }

        if (xMax < xMin)
        {
            xMin = xMax = 0.0; // no drawable pair at all
        }

        if (yMax < yMin)
        {
            yMin = yMax = 0.0;
        }

        var series = new XySeries(seriesName ?? curve.ForceChannel.DisplayName, xs, ys);
        var xAxis = new AxisView(curve.SeparationChannel.DisplayName, curve.SeparationChannel.Unit.Symbol, xMin, xMax, n);
        var yAxis = new AxisView(curve.ForceChannel.DisplayName, curve.ForceChannel.Unit.Symbol, yMin, yMax, n);
        return new CurveRenderInput([series], xAxis, yAxis);
    }

    /// <summary>Builds a single-series curve render input from a line profile (x = axis positions, y = values).</summary>
    public static CurveRenderInput ForLineProfile(LineProfileDataset profile, string? seriesName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        int n = profile.X.Count;
        var values = profile.Values.Memory.Span;
        var xs = new double[n];
        var ys = new double[n];
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            xs[i] = profile.X.RawToReal(i);
            double v = values[i];
            ys[i] = v;
            if (double.IsFinite(v))
            {
                if (v < yMin) yMin = v;
                if (v > yMax) yMax = v;
            }
        }

        if (yMax < yMin)
        {
            yMin = yMax = 0.0; // no finite values
        }

        var series = new XySeries(seriesName ?? profile.Channel.DisplayName, xs, ys);
        var xAxis = AxisView.FromAxis(profile.X);
        var yAxis = new AxisView(profile.Channel.DisplayName, profile.Channel.Unit.Symbol, yMin, yMax, n);
        return new CurveRenderInput([series], xAxis, yAxis);
    }
}
