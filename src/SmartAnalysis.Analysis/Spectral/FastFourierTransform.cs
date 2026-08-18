using System.Numerics;

namespace SmartAnalysis.Analysis.Spectral;

/// <summary>
/// Shared iterative in-place <b>radix-2 Cooley–Tukey</b> FFT — the single 1D transform primitive reused by the
/// Fourier filter (A05) and the power-spectral-density op (A08). Length must be a power of two; the inverse
/// divides by n. Pure, deterministic, domain-free.
/// </summary>
internal static class FastFourierTransform
{
    /// <summary>Transforms <paramref name="a"/> in place. <paramref name="a"/>.Length must be a power of two.</summary>
    public static void Transform(Complex[] a, bool inverse)
    {
        ArgumentNullException.ThrowIfNull(a);
        int n = a.Length;
        if ((n & (n - 1)) != 0)
        {
            throw new ArgumentException($"FFT length must be a power of two (was {n}).", nameof(a));
        }

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = 2.0 * Math.PI / len * (inverse ? 1 : -1);
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    var even = a[i + k];
                    var odd = a[i + k + (len / 2)] * w;
                    a[i + k] = even + odd;
                    a[i + k + (len / 2)] = even - odd;
                    w *= wLen;
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                a[i] /= n;
            }
        }
    }

    /// <summary>The smallest power of two &gt;= <paramref name="value"/> (at least 1).</summary>
    public static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }
}
