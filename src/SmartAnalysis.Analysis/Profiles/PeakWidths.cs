namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>peak width</b> at half-prominence — the full width of a detected peak measured where the curve
/// descends to <c>value − prominence/2</c> (the half-maximum relative to the peak's own base, so a peak on a
/// sloping background is still measured against its own height). Each side's crossing is found by walking down from
/// the peak until the curve first reaches the level and <b>linearly interpolating</b> the sub-sample position; the
/// walk is bounded by any taller neighbour, so a shoulder on a bigger peak doesn't run away. Returns the width in
/// <b>sample units</b> (the caller scales by the axis step), or <see cref="double.NaN"/> when a side is cut off by
/// the profile end or a non-finite sample (an undetermined width). Pure, deterministic, domain-free.
/// </summary>
public static class PeakWidths
{
    public static double WidthAtHalfProminence(ReadOnlySpan<float> values, int peakIndex, double value, double prominence)
    {
        if (!(prominence > 0.0) || !double.IsFinite(prominence) || !double.IsFinite(value))
        {
            return double.NaN;
        }

        int n = values.Length;
        if (peakIndex <= 0 || peakIndex >= n - 1)
        {
            return double.NaN; // an endpoint peak has no room to fall on one side
        }

        double height = value - (prominence * 0.5);

        // Left: descend while the sample stays above the half-level and below the peak, then interpolate the crossing.
        int l = peakIndex;
        while (l - 1 >= 0 && values[l - 1] > height && values[l - 1] < value)
        {
            l--;
        }

        if (l - 1 < 0 || !(values[l - 1] <= height))
        {
            return double.NaN; // ran off the end, or blocked by a taller neighbour before crossing
        }

        double left = (l - 1) + ((height - values[l - 1]) / (values[l] - values[l - 1]));

        int r = peakIndex;
        while (r + 1 < n && values[r + 1] > height && values[r + 1] < value)
        {
            r++;
        }

        if (r + 1 >= n || !(values[r + 1] <= height))
        {
            return double.NaN;
        }

        double right = r + ((values[r] - height) / (values[r] - values[r + 1]));

        return right - left;
    }
}
