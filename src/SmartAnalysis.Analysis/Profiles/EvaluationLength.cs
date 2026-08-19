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
    /// in <paramref name="sampleCount"/> samples of spacing <paramref name="dx"/> at cutoff <paramref name="cutoff"/>
    /// (lr = λc), and centres that window. Returns <see cref="None"/> if not even one sampling length fits.
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

        double samplesPerLength = cutoff / dx;
        int fit = (int)Math.Floor(sampleCount / samplesPerLength);
        int lengths = Math.Min(maxSamplingLengths, fit);
        if (lengths < 1)
        {
            return None;
        }

        int windowLength = Math.Min(sampleCount, (int)Math.Round(lengths * samplesPerLength, MidpointRounding.AwayFromZero));
        windowLength = Math.Max(1, windowLength);
        int start = (sampleCount - windowLength) / 2;
        return new EvaluationWindow(start, windowLength, lengths);
    }
}
