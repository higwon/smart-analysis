using System.Numerics;

namespace SmartAnalysis.Analysis.Spectral;

/// <summary>The one-sided 1D line-average PSD of a scan (A08 core).</summary>
/// <param name="Frequencies">Spatial frequencies (1/length), uniform: <c>f_k = k·Δf</c> for k=1..M/2 (DC dropped).</param>
/// <param name="Psd">Power spectral density at each frequency, in <c>[value]²·[length]</c>.</param>
/// <param name="FrequencyStep">The bin spacing Δf = 1/(M·dx) (also the first frequency, since DC is dropped).</param>
/// <param name="RowsUsed">Fast-scan lines that contributed (rows with any non-finite sample are skipped).</param>
public readonly record struct PsdResult(double[] Frequencies, double[] Psd, double FrequencyStep, int RowsUsed);

/// <summary>
/// Clean-room <b>1D line-average power spectral density</b> (A08): the periodogram of each fast-scan line
/// (along X), averaged over all lines. Each line is mean-subtracted (removing the DC / tilt offset), zero-padded
/// to the next power of two, and transformed with the shared <see cref="FastFourierTransform"/>; the one-sided
/// PSD is <c>C_k = (dx / N)·|Z_k|²</c> with the interior bins doubled (Parseval: <c>Σ C_k·Δf = variance</c> of the
/// line, so magnitudes are physically normalized and independent of the zero-padding). Pure, deterministic and
/// domain-free — it works on a row-major span like <see cref="Filtering.FourierFilters"/>, headlessly testable.
/// <para>The rectangular window keeps the normalization exact and the peak location sharp on a bin-aligned tone; a
/// smooth window (Hann/Welch) to cut spectral leakage on off-bin tones is a tracked follow-up.</para>
/// </summary>
public static class PowerSpectralDensity
{
    /// <param name="values">Row-major samples, length <c>width·height</c>.</param>
    /// <param name="width">Samples per fast-scan line (the transform length before padding).</param>
    /// <param name="height">Number of fast-scan lines.</param>
    /// <param name="dx">Physical spacing between adjacent samples along X (a positive length; the sign of the axis
    /// step is irrelevant to the spectrum).</param>
    public static PsdResult LineAverageAlongX(ReadOnlySpan<float> values, int width, int height, double dx)
    {
        if (width < 2 || height < 1)
        {
            return new PsdResult([], [], double.NaN, 0);
        }

        if (!(dx > 0.0) || !double.IsFinite(dx))
        {
            throw new ArgumentOutOfRangeException(nameof(dx), dx, "Sample spacing must be a finite positive length.");
        }

        int m = FastFourierTransform.NextPowerOfTwo(width);
        int half = m / 2;                      // one-sided bins k = 1..half (DC dropped)
        double frequencyStep = 1.0 / (m * dx); // Δf = 1/(M·dx)

        var accum = new double[half];          // Σ_rows |Z_k|² for k = 1..half
        var line = new Complex[m];
        int rowsUsed = 0;

        for (int y = 0; y < height; y++)
        {
            int baseIndex = y * width;

            double sum = 0.0;
            bool finite = true;
            for (int x = 0; x < width; x++)
            {
                double v = values[baseIndex + x];
                if (!double.IsFinite(v))
                {
                    finite = false;
                    break;
                }

                sum += v;
            }

            if (!finite)
            {
                continue; // a line with a dropout can't be transformed; skip it
            }

            double mean = sum / width;
            for (int x = 0; x < width; x++)
            {
                line[x] = new Complex(values[baseIndex + x] - mean, 0.0);
            }

            for (int x = width; x < m; x++)
            {
                line[x] = Complex.Zero; // zero-pad (adds no energy → Parseval preserved)
            }

            FastFourierTransform.Transform(line, inverse: false);

            for (int k = 1; k <= half; k++)
            {
                double power = line[k].Real * line[k].Real + (line[k].Imaginary * line[k].Imaginary);
                accum[k - 1] += power;
            }

            rowsUsed++;
        }

        var frequencies = new double[half];
        var psd = new double[half];
        for (int k = 1; k <= half; k++)
        {
            frequencies[k - 1] = k * frequencyStep;
        }

        if (rowsUsed == 0)
        {
            return new PsdResult(frequencies, psd, frequencyStep, 0); // all-zero PSD (no usable lines)
        }

        // One-sided PSD normalized so Σ C_k·Δf = line variance: C_k = (dx/N)·mean|Z_k|², interior bins doubled
        // (the Nyquist bin k = M/2 is its own mirror, so it is not doubled).
        double norm = dx / width;
        for (int k = 1; k <= half; k++)
        {
            double meanPower = accum[k - 1] / rowsUsed;
            double oneSided = k < half ? 2.0 * meanPower : meanPower;
            psd[k - 1] = norm * oneSided;
        }

        return new PsdResult(frequencies, psd, frequencyStep, rowsUsed);
    }
}
