using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearRegression;

namespace SmartAnalysis.Analysis.Flattening;

/// <summary>
/// Polynomial least-squares fits reproducing the legacy numeric primitives (grade A) with the same
/// MathNet routines the MV00 golden was generated from — so Flatten's baselines match legacy within
/// tolerance. <see cref="Fit1D"/> mirrors <c>PolynomialLeastSquaresRegression</c>;
/// <see cref="SurfacePolynomial"/> mirrors <c>MultiplePolynomialRegression</c>.
/// </summary>
public static class Polynomials
{
    /// <summary>1D least-squares fit; returns coefficients <c>[b0, b1, ... b_order]</c> (ascending powers).</summary>
    public static double[] Fit1D(double[] x, double[] y, int order) => Fit.Polynomial(x, y, order);

    /// <summary>Evaluates <c>Σ coeff[i]·x^i</c> at each x (matches the legacy <c>Infer</c>).</summary>
    public static double[] Infer1D(double[] coefficients, double[] x)
    {
        var result = new double[x.Length];
        for (int j = 0; j < x.Length; j++)
        {
            double xv = x[j];
            double acc = 0.0;
            for (int i = 0; i < coefficients.Length; i++)
            {
                acc += coefficients[i] * Math.Pow(xv, i);
            }

            result[j] = acc;
        }

        return result;
    }
}

/// <summary>
/// A bivariate polynomial least-squares surface of total degree ≤ <c>order</c>, reproducing the legacy
/// <c>MultiplePolynomialRegression</c> (Vandermonde basis + normal equations). Predictions at the same
/// nodes are invariant under affine reparameterization of x/y, so pixel-index coordinates match the
/// legacy visual-position fit.
/// </summary>
public sealed class SurfacePolynomial
{
    private readonly int _order;
    private Vector<double>? _coefficients;

    public SurfacePolynomial(int order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        _order = order;
    }

    public void Fit(double[] x1, double[] x2, double[] y)
    {
        var a = FormSystem(x1, x2, _order);
        _coefficients = MultipleRegression.NormalEquations(a, Vector<double>.Build.DenseOfArray(y));
    }

    public double[] Infer(double[] x1, double[] x2)
    {
        if (_coefficients is null)
        {
            throw new InvalidOperationException("Fit must be called before Infer.");
        }

        return FormSystem(x1, x2, _order).Multiply(_coefficients).ToArray();
    }

    // Vandermonde: 1, x, y, then every monomial x^(d-k)·y^k for total degree d in 2..order (legacy order).
    private static Matrix<double> FormSystem(double[] xArray, double[] yArray, int maximumDegree)
    {
        var columns = new List<Vector<double>>
        {
            Vector<double>.Build.Dense(xArray.Length, 1.0), // degree 0
        };

        if (maximumDegree == 0)
        {
            return Matrix<double>.Build.DenseOfColumns(columns);
        }

        var x = Vector<double>.Build.DenseOfArray(xArray);
        var yv = Vector<double>.Build.DenseOfArray(yArray);
        columns.Add(x); // degree 1
        columns.Add(yv);

        for (int degree = 2; degree <= maximumDegree; degree++)
        {
            for (int yDegree = 0; yDegree <= degree; yDegree++)
            {
                int xDegree = degree - yDegree;
                columns.Add(x.PointwisePower(xDegree).PointwiseMultiply(yv.PointwisePower(yDegree)));
            }
        }

        return Matrix<double>.Build.DenseOfColumns(columns);
    }
}
