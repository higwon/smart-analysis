namespace SmartAnalysis.Analysis.Spectroscopy;

/// <summary>The contact model relating indentation depth to force — the tip geometry being pressed into the sample.</summary>
public enum ContactModel
{
    /// <summary>A sphere of known radius: <c>F = (4/3)·E*·√R·δ^1.5</c> (Hertz).</summary>
    Hertz,

    /// <summary>A cone of known half-angle: <c>F = (2/π)·E*·tanα·δ²</c> (Sneddon).</summary>
    Sneddon,
}

/// <summary>The outcome of a contact-mechanics fit, in SI units.</summary>
/// <param name="Modulus">Young's modulus of the sample, in pascals; NaN when the fit could not be made.</param>
/// <param name="Coefficient">The fitted power-law coefficient <c>A</c> (SI), before the model's geometry factor.</param>
/// <param name="ContactPoint">The fitted contact point, as a separation in metres — where indentation begins.</param>
/// <param name="ResidualRms">RMS residual of the fit, in newtons — how well the model describes the data.</param>
/// <param name="SampleCount">How many samples the fit used.</param>
public readonly record struct ContactFit(double Modulus, double Coefficient, double ContactPoint, double ResidualRms, int SampleCount);

/// <summary>
/// Clean-room contact mechanics (A12): fits an indentation model to a force curve and converts the fitted stiffness
/// into Young's modulus. Pure, deterministic, SI throughout (metres, newtons, pascals) — the operation converts.
/// <para>
/// <b>How the fit works.</b> Both models are a power law in indentation depth, <c>F = A·(z₀ − z)^p</c>, with <c>p</c>
/// fixed by the geometry (1.5 for a sphere, 2 for a cone) and two unknowns: the coefficient <c>A</c> and the contact
/// point <c>z₀</c>. For <b>any fixed</b> <c>z₀</c> the best <c>A</c> is a closed-form least-squares solution, so the fit
/// reduces to a one-dimensional search over the contact point — a coarse scan refined by golden-section. That avoids
/// the derivative-based iteration a general non-linear fitter needs (and its initial-guess sensitivity) while giving
/// the same optimum: it cannot diverge, and it is deterministic.
/// </para>
/// <para>
/// <b>Modulus.</b> The reduced modulus <c>E* = E/(1−ν²)</c> falls out of the fitted <c>A</c> by the model's geometry
/// factor: <c>E = (3/4)(1−ν²)·A/√R</c> for Hertz, <c>E = (π/2)(1−ν²)·A/tanα</c> for Sneddon.
/// </para>
/// </summary>
public static class ContactMechanics
{
    /// <summary>The exponent of the model's power law in indentation depth.</summary>
    public static double Exponent(ContactModel model) => model == ContactModel.Sneddon ? 2.0 : 1.5;

    /// <summary>
    /// Fits <paramref name="model"/> to a contact region and returns the modulus.
    /// <paramref name="separation"/> is in metres (decreasing as the tip presses in), <paramref name="force"/> in
    /// newtons. <paramref name="geometry"/> is the tip radius in metres (Hertz) or the half-angle in degrees
    /// (Sneddon). Returns a fit whose <see cref="ContactFit.Modulus"/> is NaN when the data cannot support one
    /// (too few samples, no indentation range, a non-physical geometry, or a degenerate fit).
    /// </summary>
    public static ContactFit Fit(
        ContactModel model,
        ReadOnlySpan<float> separation,
        ReadOnlySpan<float> force,
        double poissonRatio,
        double geometry)
    {
        if (separation.Length != force.Length)
        {
            throw new ArgumentException("Separation and force must have equal length.", nameof(force));
        }

        double geometryFactor = GeometryFactor(model, geometry, poissonRatio);
        if (!double.IsFinite(geometryFactor))
        {
            return Undefined();
        }

        // Keep only finite pairs; the fit is over indentation depth, so a dropout would poison the residual.
        int n = 0;
        var z = new double[separation.Length];
        var f = new double[force.Length];
        for (int i = 0; i < separation.Length; i++)
        {
            if (double.IsFinite(separation[i]) && double.IsFinite(force[i]))
            {
                z[n] = separation[i];
                f[n] = force[i];
                n++;
            }
        }

        if (n < 3)
        {
            return Undefined(); // two points fit any two-parameter model exactly — that is not evidence of a modulus
        }

        double p = Exponent(model);
        double deepest = double.PositiveInfinity, shallowest = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            deepest = Math.Min(deepest, z[i]);
            shallowest = Math.Max(shallowest, z[i]);
        }

        if (deepest >= shallowest)
        {
            return Undefined(); // no separation travel: there is no indentation to fit
        }

        // The contact point lies inside the measured span: at or above the shallowest sample there is no contact, and
        // below the deepest there is no data. Search that interval for the z0 minimising the residual.
        var (contactPoint, coefficient, sse) = SearchContactPoint(z, f, n, p, deepest, shallowest);
        if (!double.IsFinite(coefficient) || coefficient <= 0.0)
        {
            return Undefined(); // a non-positive coefficient is not a stiffness — the model does not describe this data
        }

        double residualRms = Math.Sqrt(sse / n);
        return new ContactFit(geometryFactor * coefficient, coefficient, contactPoint, residualRms, n);
    }

    // E = factor · A. Hertz: (3/4)(1−ν²)/√R. Sneddon: (π/2)(1−ν²)/tan(α). NaN for a non-physical geometry, so the
    // caller reports "no modulus" instead of a number derived from an impossible tip.
    private static double GeometryFactor(ContactModel model, double geometry, double poissonRatio)
    {
        if (!double.IsFinite(geometry) || geometry <= 0.0 || !double.IsFinite(poissonRatio) || poissonRatio is < 0.0 or >= 0.5)
        {
            return double.NaN;
        }

        double compliance = 1.0 - (poissonRatio * poissonRatio);
        if (model == ContactModel.Sneddon)
        {
            if (geometry >= 90.0)
            {
                return double.NaN; // a half-angle of 90° or more is a flat punch, not a cone
            }

            double tangent = Math.Tan(geometry * Math.PI / 180.0);
            return tangent > 0.0 ? Math.PI / 2.0 * compliance / tangent : double.NaN;
        }

        return 3.0 / 4.0 * compliance / Math.Sqrt(geometry);
    }

    // A coarse scan over the contact point followed by golden-section refinement. The objective is smooth and
    // single-minimum in practice; the scan makes the refinement start in the right basin regardless of the data.
    private static (double ContactPoint, double Coefficient, double Sse) SearchContactPoint(
        double[] z, double[] f, int n, double p, double deepest, double shallowest)
    {
        const int ScanSteps = 64;
        double span = shallowest - deepest;
        double best = shallowest, bestSse = double.PositiveInfinity, bestA = double.NaN;

        for (int i = 0; i <= ScanSteps; i++)
        {
            double candidate = deepest + (span * i / ScanSteps);
            var (a, sse) = SolveCoefficient(z, f, n, p, candidate);
            if (sse < bestSse)
            {
                bestSse = sse;
                best = candidate;
                bestA = a;
            }
        }

        // Refine inside the bracket around the best scan point (golden-section on a bounded, deterministic interval).
        double step = span / ScanSteps;
        double lo = Math.Max(deepest, best - step), hi = Math.Min(shallowest, best + step);
        const double InverseGolden = 0.6180339887498949;
        double x1 = hi - ((hi - lo) * InverseGolden), x2 = lo + ((hi - lo) * InverseGolden);
        var (a1, s1) = SolveCoefficient(z, f, n, p, x1);
        var (a2, s2) = SolveCoefficient(z, f, n, p, x2);
        for (int i = 0; i < 60 && hi - lo > span * 1e-9; i++)
        {
            if (s1 <= s2)
            {
                hi = x2;
                x2 = x1;
                (a2, s2) = (a1, s1);
                x1 = hi - ((hi - lo) * InverseGolden);
                (a1, s1) = SolveCoefficient(z, f, n, p, x1);
            }
            else
            {
                lo = x1;
                x1 = x2;
                (a1, s1) = (a2, s2);
                x2 = lo + ((hi - lo) * InverseGolden);
                (a2, s2) = SolveCoefficient(z, f, n, p, x2);
            }
        }

        return s1 <= s2 && s1 <= bestSse ? (x1, a1, s1)
            : s2 <= bestSse ? (x2, a2, s2)
            : (best, bestA, bestSse);
    }

    // For a FIXED contact point the model is linear in A, so the least-squares coefficient is closed form:
    // A = Σ(f·u) / Σ(u²) with u = depth^p. Samples shallower than the contact point carry no indentation and are
    // modelled as zero force, so they still constrain the fit (a bad contact point shows up as residual there).
    private static (double Coefficient, double Sse) SolveCoefficient(double[] z, double[] f, int n, double p, double contactPoint)
    {
        double numerator = 0.0, denominator = 0.0;
        for (int i = 0; i < n; i++)
        {
            double depth = contactPoint - z[i];
            if (depth <= 0.0)
            {
                continue;
            }

            double u = Math.Pow(depth, p);
            numerator += f[i] * u;
            denominator += u * u;
        }

        if (denominator <= 0.0)
        {
            return (double.NaN, double.PositiveInfinity);
        }

        double a = numerator / denominator;
        double sse = 0.0;
        for (int i = 0; i < n; i++)
        {
            double depth = contactPoint - z[i];
            double predicted = depth > 0.0 ? a * Math.Pow(depth, p) : 0.0;
            double residual = f[i] - predicted;
            sse += residual * residual;
        }

        return (a, sse);
    }

    private static ContactFit Undefined() => new(double.NaN, double.NaN, double.NaN, double.NaN, 0);
}
