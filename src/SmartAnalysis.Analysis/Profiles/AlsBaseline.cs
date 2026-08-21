namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Clean-room <b>Asymmetric Least Squares (ALS) baseline</b> (Eilers &amp; Boelens): estimates a smooth baseline
/// <c>z</c> minimising <c>Σ wᵢ(yᵢ−zᵢ)² + λ Σ(Δ²zᵢ)²</c>, where the weights are updated asymmetrically each
/// iteration — a point above the current baseline (a peak) gets weight <c>p</c>, a point below gets <c>1−p</c> — so
/// the baseline is pulled up to the signal in valleys but not into peaks. <c>λ</c> controls stiffness (larger =
/// smoother). Each iteration solves <c>(W + λ DᵀD) z = W y</c>; the system is symmetric positive-definite and
/// <b>penta-diagonal</b> (the 2nd-difference penalty), so it is factored with an O(n) banded LDLᵀ solve, not a dense
/// one. Non-finite samples are given zero weight (excluded from the fit; the penalty interpolates the baseline
/// across them). Pure, deterministic, domain-free.
/// </summary>
public static class AlsBaseline
{
    /// <param name="y">The signal samples.</param>
    /// <param name="lambda">Smoothness penalty λ (&gt; 0; larger = stiffer baseline).</param>
    /// <param name="p">Asymmetry weight for points above the baseline (0 &lt; p &lt; 1; small = baseline stays under peaks).</param>
    /// <param name="iterations">Number of reweighting iterations (≥ 1).</param>
    /// <returns>The estimated baseline (length n).</returns>
    public static double[] Compute(ReadOnlySpan<float> y, double lambda, double p, int iterations)
    {
        if (!(lambda > 0.0) || !double.IsFinite(lambda))
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "λ must be a finite positive number.");
        }

        if (!(p > 0.0 && p < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, "The asymmetry p must be in (0, 1).");
        }

        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "There must be at least one iteration.");
        }

        int n = y.Length;
        if (n < 3)
        {
            throw new ArgumentException("ALS needs at least three samples (a second-difference penalty).", nameof(y));
        }

        var finite = new bool[n];
        var yd = new double[n];
        int finiteCount = 0;
        for (int i = 0; i < n; i++)
        {
            finite[i] = double.IsFinite(y[i]);
            if (finite[i])
            {
                yd[i] = y[i];
                finiteCount++;
            }
        }

        if (finiteCount < 3)
        {
            throw new ArgumentException("ALS needs at least three finite samples.", nameof(y));
        }

        // Lower bands of DᵀD (2nd-difference penalty): accumulate each difference row [1,-2,1] into the bands.
        var pd0 = new double[n];
        var pd1 = new double[n];
        var pd2 = new double[n];
        Span<int> cols = stackalloc int[3];
        Span<double> cf = [1.0, -2.0, 1.0];
        for (int k = 0; k <= n - 3; k++)
        {
            cols[0] = k;
            cols[1] = k + 1;
            cols[2] = k + 2;
            for (int a = 0; a < 3; a++)
            {
                for (int b = 0; b < 3; b++)
                {
                    int ia = cols[a];
                    int ib = cols[b];
                    if (ia < ib)
                    {
                        continue; // lower triangle (incl. diagonal) only
                    }

                    double v = cf[a] * cf[b];
                    switch (ia - ib)
                    {
                        case 0: pd0[ia] += v; break;
                        case 1: pd1[ia] += v; break;
                        case 2: pd2[ia] += v; break;
                    }
                }
            }
        }

        var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            w[i] = finite[i] ? 1.0 : 0.0; // start from equal weights on the finite samples
        }

        var a0 = new double[n];
        var a1 = new double[n];
        var a2 = new double[n];
        var rhs = new double[n];
        double[] z = new double[n];

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < n; i++)
            {
                a0[i] = w[i] + (lambda * pd0[i]);
                a1[i] = lambda * pd1[i];
                a2[i] = lambda * pd2[i];
                rhs[i] = w[i] * yd[i];
            }

            z = SolvePentadiagonal(a0, a1, a2, rhs);

            for (int i = 0; i < n; i++)
            {
                w[i] = finite[i] ? (yd[i] > z[i] ? p : 1.0 - p) : 0.0;
            }
        }

        return z;
    }

    // Symmetric positive-definite penta-diagonal solve via banded LDLᵀ (half-bandwidth 2). a1[i]=A[i,i-1],
    // a2[i]=A[i,i-2]. Throws if a non-positive pivot appears (the system is not positive-definite).
    private static double[] SolvePentadiagonal(double[] a0, double[] a1, double[] a2, double[] b)
    {
        int n = a0.Length;
        var d = new double[n];
        var l1 = new double[n];
        var l2 = new double[n];

        for (int i = 0; i < n; i++)
        {
            double e2 = i >= 2 ? a2[i] / d[i - 2] : 0.0;
            double e1 = i >= 1 ? (a1[i] - (i >= 2 ? e2 * d[i - 2] * l1[i - 1] : 0.0)) / d[i - 1] : 0.0;
            double pivot = a0[i]
                - (i >= 1 ? e1 * e1 * d[i - 1] : 0.0)
                - (i >= 2 ? e2 * e2 * d[i - 2] : 0.0);
            if (!(pivot > 0.0) || !double.IsFinite(pivot))
            {
                throw new InvalidOperationException("The ALS system is not positive-definite (singular baseline fit).");
            }

            d[i] = pivot;
            l1[i] = e1;
            l2[i] = e2;
        }

        // Forward: L c = b.
        var c = new double[n];
        for (int i = 0; i < n; i++)
        {
            c[i] = b[i] - (i >= 1 ? l1[i] * c[i - 1] : 0.0) - (i >= 2 ? l2[i] * c[i - 2] : 0.0);
        }

        for (int i = 0; i < n; i++)
        {
            c[i] /= d[i]; // c ← D⁻¹ c
        }

        // Backward: Lᵀ z = c.
        var z = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            z[i] = c[i] - (i + 1 < n ? l1[i + 1] * z[i + 1] : 0.0) - (i + 2 < n ? l2[i + 2] * z[i + 2] : 0.0);
        }

        return z;
    }
}
