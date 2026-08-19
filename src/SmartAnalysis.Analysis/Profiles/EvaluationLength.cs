namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// The central slice of a filtered profile over which roughness parameters are evaluated: an integer number of
/// <b>sampling lengths</b> (each <c>lr = λc</c>), up to the ISO 21920 default evaluation length of 5 sampling
/// lengths. Ends carry the Gaussian filter's transient, so evaluating a whole-sampling-length window centred in
/// the data — rather than the raw ends — is what makes the parameters comparable across profiles.
/// </summary>
public readonly record struct EvaluationWindow(int Start, int Length, int SamplingLengths)
{
    /// <summary>An empty window (no whole sampling length fits).</summary>
    public static readonly EvaluationWindow None = new(0, 0, 0);

    public bool IsEmpty => SamplingLengths == 0;

    /// <summary>
    /// Picks the largest integer number of whole sampling lengths (≤ <paramref name="maxSamplingLengths"/>) that fit
    /// in the profile's physical span and centres that window. All length reasoning is on the <b>interval</b> span
    /// <c>(sampleCount − 1)·dx</c> — N samples enclose N−1 intervals — so the window never overstates the data it
    /// covers. Returns <see cref="None"/> if not even one sampling length fits.
    /// </summary>
    public static EvaluationWindow Central(int sampleCount, double dx, double cutoff, int maxSamplingLengths = 5)
    {
        if (sampleCount <= 0)
        {
            return None;
        }

        if (!(dx > 0.0) || !double.IsFinite(dx))
        {
            throw new ArgumentOutOfRangeException(nameof(dx), dx, "Sample spacing must be a finite positive length.");
        }

        if (!(cutoff > 0.0) || !double.IsFinite(cutoff))
        {
            throw new ArgumentOutOfRangeException(nameof(cutoff), cutoff, "The cutoff wavelength must be a finite positive length.");
        }

        if (maxSamplingLengths < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSamplingLengths), maxSamplingLengths, "There must be at least one sampling length.");
        }

        double availableSpan = (sampleCount - 1) * dx;
        int lengths = Math.Min(maxSamplingLengths, (int)Math.Floor(availableSpan / cutoff));
        if (lengths < 1)
        {
            return None;
        }

        // Largest whole number of intervals that fits the target span (lengths·λc); window = intervals + 1 samples.
        double targetSpan = lengths * cutoff;
        int intervals = Math.Min(sampleCount - 1, (int)Math.Floor(targetSpan / dx));
        return new EvaluationWindow((sampleCount - (intervals + 1)) / 2, intervals + 1, lengths);
    }
}
