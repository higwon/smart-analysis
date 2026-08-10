namespace SmartAnalysis.Analysis.Statistics;

/// <summary>
/// Whole-dataset summary statistics — the parity target frozen by MV00. Field meanings match the
/// legacy <c>SummaryStatisticsCalculator</c>: <see cref="Rms"/> = population RMS (Sq),
/// <see cref="MeanAbsoluteDeviation"/> = mean |residue| (Sa), <see cref="Skewness"/>/<see cref="Kurtosis"/>
/// are Pearson moments about the mean normalized by the population RMS.
/// </summary>
public readonly record struct SummaryStatisticsResult(
    double Min,
    double Max,
    double PeakToPeak,
    double Mid,
    double Mean,
    double MeanAbsoluteDeviation,
    double Rms,
    double Skewness,
    double Kurtosis,
    double BoundedPointAverageRoughness,
    long Count)
{
    /// <summary>Degenerate result for empty input — all NaN (see <see cref="SummaryStatistics"/> remarks).</summary>
    public static SummaryStatisticsResult Empty { get; } = new(
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0);
}

/// <summary>
/// Pure, headless summary statistics reproducing the legacy numeric core (grade A) so results match
/// the MV00 golden within tolerance. Reused by the image statistics operation and by the parity test.
/// <para>
/// <b>Intentional divergence (doc 07 M5, ADR-016):</b> for <b>empty</b> input the legacy code returns
/// sentinel values (<c>Min = double.MaxValue</c>, <c>Max = double.MinValue</c>, <c>PeakToPeak = -∞</c>) —
/// a silent-bad-value bug. This implementation returns all-NaN (<see cref="SummaryStatisticsResult.Empty"/>)
/// instead. All non-empty results reproduce the legacy formulas.
/// </para>
/// </summary>
public static class SummaryStatistics
{
    public static SummaryStatisticsResult Compute(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n == 0)
        {
            return SummaryStatisticsResult.Empty; // divergence from legacy sentinels (ADR-016)
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double v = data[i];
            sum += v;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        double mean = sum / n;

        double sumAbs = 0, sumSq = 0, sumCube = 0, sumFourth = 0;
        for (int i = 0; i < n; i++)
        {
            double r = data[i] - mean;
            sumAbs += Math.Abs(r);
            sumSq += r * r;
            sumCube += r * r * r;
            sumFourth += r * r * r * r;
        }

        double rms = Math.Sqrt(sumSq / n);
        double meanAbs = sumAbs / n;
        double skewness = sumCube / n / Math.Pow(rms, 3);
        double kurtosis = sumFourth / n / Math.Pow(rms, 4);

        return new SummaryStatisticsResult(
            Min: min,
            Max: max,
            PeakToPeak: max - min,
            Mid: (min + max) / 2,
            Mean: mean,
            MeanAbsoluteDeviation: meanAbs,
            Rms: rms,
            Skewness: skewness,
            Kurtosis: kurtosis,
            BoundedPointAverageRoughness: BoundedPointAverageRoughness(data, mean),
            Count: n);
    }

    /// <summary>
    /// Builds uniform-bin histogram counts over the finite values in <paramref name="data"/>, spanning
    /// [<paramref name="min"/>, <paramref name="max"/>]. Returns all-zero counts for a degenerate range
    /// (no finite values, or all equal) — the caller decides whether to emit a histogram.
    /// </summary>
    public static long[] BuildHistogram(ReadOnlySpan<double> data, int binCount, out double min, out double max)
    {
        if (binCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount), binCount, "Bin count must be positive.");
        }

        min = double.PositiveInfinity;
        max = double.NegativeInfinity;
        for (int i = 0; i < data.Length; i++)
        {
            double v = data[i];
            if (double.IsFinite(v))
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        var counts = new long[binCount];
        if (!(max > min))
        {
            return counts; // degenerate range → all-zero (caller emits no histogram)
        }

        double width = (max - min) / binCount;
        for (int i = 0; i < data.Length; i++)
        {
            double v = data[i];
            if (!double.IsFinite(v))
            {
                continue;
            }

            int index = (int)((v - min) / width);
            if (index >= binCount)
            {
                index = binCount - 1; // include the max edge in the last bin
            }

            if (index >= 0)
            {
                counts[index]++;
            }
        }

        return counts;
    }

    // Legacy "bounded point average roughness": mean of (top-5 peaks − bottom-5 valleys) of the residue
    // signal, where a peak/valley is the extreme of a run of same-sign residues. Ported verbatim so the
    // MV00 golden matches; NaN when fewer than five peaks or valleys exist (e.g. monotonic/constant data).
    private static double BoundedPointAverageRoughness(ReadOnlySpan<double> data, double mean)
    {
        const int Size = 5;
        Span<double> peaks = stackalloc double[Size];
        Span<double> valleys = stackalloc double[Size];
        peaks.Fill(double.NegativeInfinity);
        valleys.Fill(double.PositiveInfinity);

        double prevValue = 0;
        bool prevWasPeak = false;
        bool isFirst = true;

        void FlushRun(bool wasPeak, double value, Span<double> pk, Span<double> vl)
        {
            if (wasPeak)
            {
                if (value > pk[0])
                {
                    pk[0] = value;
                    pk.Sort();
                }
            }
            else if (value < vl[Size - 1])
            {
                vl[Size - 1] = value;
                vl.Sort();
            }
        }

        for (int i = 0; i < data.Length; i++)
        {
            double value = data[i] - mean;
            bool isPeak = value > 0;

            if (isFirst)
            {
                prevWasPeak = isPeak;
                prevValue = value;
                isFirst = false;
                continue;
            }

            if (prevWasPeak == isPeak)
            {
                prevValue = isPeak ? Math.Max(prevValue, value) : Math.Min(prevValue, value);
            }
            else
            {
                FlushRun(prevWasPeak, prevValue, peaks, valleys);
                prevValue = value;
                prevWasPeak = isPeak;
            }
        }

        if (!isFirst)
        {
            FlushRun(prevWasPeak, prevValue, peaks, valleys);
        }

        if (double.IsNegativeInfinity(peaks[0]) || double.IsPositiveInfinity(valleys[Size - 1]))
        {
            return double.NaN;
        }

        double peakSum = 0, valleySum = 0;
        for (int i = 0; i < Size; i++)
        {
            peakSum += peaks[i];
            valleySum += valleys[i];
        }

        return (peakSum - valleySum) / Size;
    }
}
