namespace SmartAnalysis.Analysis.Profiles;

/// <summary>The band a <see cref="GaussianProfileFilter"/> keeps.</summary>
public enum ProfileBand
{
    /// <summary>Short-wavelength roughness — the profile minus its Gaussian mean line (high-pass).</summary>
    Roughness,

    /// <summary>Long-wavelength waviness — the Gaussian mean line itself (low-pass).</summary>
    Waviness,
}

/// <summary>
/// Clean-room <b>Gaussian profile filter</b> (the ISO 16610-21 weighting): a phase-correct Gaussian low-pass whose
/// weighting function is <c>S(x) = (1/(α·λc))·exp(−π·(x/(α·λc))²)</c> with <c>α = √(ln2/π)</c>, so the mean line
/// (waviness) transmits exactly <b>50% at the cutoff wavelength λc</b> — hence the roughness band (profile − mean
/// line) also transmits 50% at λc. Convolution is windowed to a few σ and normalized; ends use reflected padding
/// (a basic end-effect handling — the standard's tapered end treatment is a follow-up). Pure, deterministic,
/// domain-free — it works on a plain span, headlessly testable.
/// </summary>
public static class GaussianProfileFilter
{
    /// <summary>The ISO 16610-21 constant: α = √(ln2/π), giving 50% transmission at λc.</summary>
    public static readonly double Alpha = Math.Sqrt(Math.Log(2.0) / Math.PI);

    /// <param name="values">The profile samples.</param>
    /// <param name="sampleSpacing">Physical spacing between samples (dx, same length unit as <paramref name="cutoff"/>).</param>
    /// <param name="cutoff">The cutoff wavelength λc (&gt; 0, same unit as <paramref name="sampleSpacing"/>).</param>
    /// <param name="band">Roughness (high-pass) or Waviness (the Gaussian mean line).</param>
    public static float[] Apply(ReadOnlySpan<float> values, double sampleSpacing, double cutoff, ProfileBand band)
    {
        int n = values.Length;
        if (n == 0)
        {
            return [];
        }

        if (!(sampleSpacing > 0.0) || !double.IsFinite(sampleSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSpacing), sampleSpacing, "Sample spacing must be a finite positive length.");
        }

        if (!(cutoff > 0.0) || !double.IsFinite(cutoff))
        {
            throw new ArgumentOutOfRangeException(nameof(cutoff), cutoff, "The cutoff wavelength must be a finite positive length.");
        }

        // Gaussian weights over ±half samples (out to ~4σ, where σ = α·λc expressed in samples).
        double sigmaSamples = Alpha * cutoff / sampleSpacing;
        int half = Math.Max(1, (int)Math.Ceiling(4.0 * sigmaSamples));
        var weights = new double[half + 1];
        double norm = 0.0;
        double c = Math.PI / (sigmaSamples * sigmaSamples);
        for (int k = 0; k <= half; k++)
        {
            weights[k] = Math.Exp(-c * k * k);
            norm += k == 0 ? weights[k] : 2.0 * weights[k]; // symmetric
        }

        var meanLine = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = weights[0] * values[i];
            for (int k = 1; k <= half; k++)
            {
                acc += weights[k] * (values[Reflect(i - k, n)] + values[Reflect(i + k, n)]);
            }

            meanLine[i] = acc / norm;
        }

        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = band == ProfileBand.Waviness ? (float)meanLine[i] : (float)(values[i] - meanLine[i]);
        }

        return result;
    }

    // Reflect an out-of-range index back into [0, n) (mirror at the ends).
    private static int Reflect(int index, int n)
    {
        if (n == 1)
        {
            return 0;
        }

        int period = 2 * (n - 1);
        int m = ((index % period) + period) % period;
        return m < n ? m : period - m;
    }
}
