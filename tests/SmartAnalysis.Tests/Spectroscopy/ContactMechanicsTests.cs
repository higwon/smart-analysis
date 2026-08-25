using SmartAnalysis.Analysis.Spectroscopy;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A12 core: the contact-mechanics fit. The defining property is a <b>round trip through the physics</b> — build
/// a curve from a known modulus with the model's own formula, fit it back, and recover that modulus. Everything is SI
/// (metres, newtons, pascals).
/// </summary>
public sealed class ContactMechanicsTests
{
    private const double Poisson = 0.3;

    // F = (4/3)·E*·√R·δ^1.5 — the Hertz sphere, sampled as a real curve would be (separation decreasing).
    private static (float[] Separation, float[] Force) HertzCurve(
        double modulusPa, double radiusM, double contactPointM, int n = 60, double maxDepthM = 50e-9)
    {
        double reduced = modulusPa / (1.0 - (Poisson * Poisson));
        double a = 4.0 / 3.0 * reduced * Math.Sqrt(radiusM);
        var separation = new float[n];
        var force = new float[n];
        for (int i = 0; i < n; i++)
        {
            // Start above the surface (no contact) and press in past the contact point.
            double z = contactPointM + (maxDepthM * 0.25) - (maxDepthM * 1.25 * i / (n - 1));
            double depth = contactPointM - z;
            separation[i] = (float)z;
            force[i] = (float)(depth > 0 ? a * Math.Pow(depth, 1.5) : 0.0);
        }

        return (separation, force);
    }

    // F = (2/π)·E*·tanα·δ² — the Sneddon cone.
    private static (float[] Separation, float[] Force) SneddonCurve(
        double modulusPa, double halfAngleDeg, double contactPointM, int n = 60, double maxDepthM = 50e-9)
    {
        double reduced = modulusPa / (1.0 - (Poisson * Poisson));
        double a = 2.0 / Math.PI * reduced * Math.Tan(halfAngleDeg * Math.PI / 180.0);
        var separation = new float[n];
        var force = new float[n];
        for (int i = 0; i < n; i++)
        {
            double z = contactPointM + (maxDepthM * 0.25) - (maxDepthM * 1.25 * i / (n - 1));
            double depth = contactPointM - z;
            separation[i] = (float)z;
            force[i] = (float)(depth > 0 ? a * depth * depth : 0.0);
        }

        return (separation, force);
    }

    [Theory]
    [InlineData(1e6)]      // 1 MPa — a soft sample
    [InlineData(1e9)]      // 1 GPa — a stiff one
    [InlineData(2.5e8)]
    public void A_hertz_curve_built_from_a_known_modulus_fits_back_to_it(double modulusPa)
    {
        double radius = 20e-9;
        var (separation, force) = HertzCurve(modulusPa, radius, contactPointM: 0.0);

        var fit = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, radius);

        Assert.Equal(modulusPa, fit.Modulus, modulusPa * 0.02); // within 2% — the round trip through the physics
        Assert.Equal(separation.Length, fit.SampleCount);
    }

    [Fact]
    public void A_sneddon_curve_built_from_a_known_modulus_fits_back_to_it()
    {
        double halfAngle = 18.0;
        double modulus = 5e8;
        var (separation, force) = SneddonCurve(modulus, halfAngle, contactPointM: 0.0);

        var fit = ContactMechanics.Fit(ContactModel.Sneddon, separation, force, Poisson, halfAngle);

        Assert.Equal(modulus, fit.Modulus, modulus * 0.02);
    }

    [Fact]
    public void The_contact_point_is_recovered_even_when_it_is_not_at_zero()
    {
        double contact = 12e-9;
        var (separation, force) = HertzCurve(4e8, 20e-9, contact);

        var fit = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9);

        Assert.Equal(contact, fit.ContactPoint, 1e-9); // the surface is found, not assumed at z = 0
        Assert.Equal(4e8, fit.Modulus, 4e8 * 0.02);
    }

    [Fact]
    public void A_stiffer_sample_fits_a_larger_modulus()
    {
        var soft = HertzCurve(1e7, 20e-9, 0.0);
        var stiff = HertzCurve(1e9, 20e-9, 0.0);

        var softFit = ContactMechanics.Fit(ContactModel.Hertz, soft.Separation, soft.Force, Poisson, 20e-9);
        var stiffFit = ContactMechanics.Fit(ContactModel.Hertz, stiff.Separation, stiff.Force, Poisson, 20e-9);

        Assert.True(stiffFit.Modulus > softFit.Modulus * 50, "a 100× stiffer sample must fit a much larger modulus");
    }

    [Fact]
    public void A_bigger_tip_radius_yields_a_smaller_modulus_for_the_same_curve()
    {
        // The same measured force is explained by a softer sample when the tip is blunter (E ∝ 1/√R).
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);

        var sharp = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9);
        var blunt = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 80e-9);

        Assert.Equal(sharp.Modulus / 2.0, blunt.Modulus, sharp.Modulus * 0.02); // √4 = 2
    }

    [Fact]
    public void An_exact_model_curve_fits_with_a_negligible_residual()
    {
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);

        var fit = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9);

        double peakForce = force.Max();
        Assert.True(fit.ResidualRms < peakForce * 0.02, $"residual {fit.ResidualRms} vs peak {peakForce}");
    }

    [Fact]
    public void Noise_does_not_move_the_modulus_far()
    {
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);
        var rng = new Random(20260825); // fixed seed: the test must be deterministic
        double peak = force.Max();
        for (int i = 0; i < force.Length; i++)
        {
            force[i] += (float)((rng.NextDouble() - 0.5) * peak * 0.04); // ±2% of full scale
        }

        var fit = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9);

        Assert.Equal(5e8, fit.Modulus, 5e8 * 0.10); // within 10% under noise
    }

    [Theory]
    [InlineData(0.0)]      // a zero-radius tip is not a sphere
    [InlineData(-1e-9)]
    public void A_non_physical_geometry_yields_no_modulus(double radius)
    {
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);

        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, radius).Modulus));
    }

    [Theory]
    [InlineData(90.0)]     // a flat punch, not a cone
    [InlineData(120.0)]
    public void A_cone_half_angle_of_ninety_degrees_or_more_yields_no_modulus(double halfAngle)
    {
        var (separation, force) = SneddonCurve(5e8, 18.0, 0.0);

        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Sneddon, separation, force, Poisson, halfAngle).Modulus));
    }

    [Theory]
    [InlineData(0.5)]      // ν = 0.5 is incompressible: 1 − ν² is fine but the model breaks down
    [InlineData(0.9)]
    [InlineData(-0.1)]
    public void A_non_physical_poisson_ratio_yields_no_modulus(double poisson)
    {
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);

        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Hertz, separation, force, poisson, 20e-9).Modulus));
    }

    [Fact]
    public void Too_few_samples_yield_no_modulus()
    {
        // Two points fit any two-parameter model exactly; that is not evidence of a modulus.
        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Hertz, [1f, 0f], [0f, 1f], Poisson, 20e-9).Modulus));
    }

    [Fact]
    public void A_flat_curve_with_no_travel_yields_no_modulus()
    {
        var separation = new float[20];
        var force = new float[20];
        Array.Fill(separation, 5e-9f); // no indentation range at all

        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9).Modulus));
    }

    [Fact]
    public void A_curve_that_only_pulls_yields_no_modulus()
    {
        // Force is negative throughout (adhesion only, never a push): the coefficient would be negative, which is not
        // a stiffness — report no modulus rather than a nonsense number.
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);
        for (int i = 0; i < force.Length; i++)
        {
            force[i] = -Math.Abs(force[i]) - 1e-12f;
        }

        Assert.True(double.IsNaN(ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9).Modulus));
    }

    [Fact]
    public void Non_finite_samples_are_excluded_from_the_fit()
    {
        var (separation, force) = HertzCurve(5e8, 20e-9, 0.0);
        force[10] = float.NaN;
        separation[20] = float.NaN;

        var fit = ContactMechanics.Fit(ContactModel.Hertz, separation, force, Poisson, 20e-9);

        Assert.Equal(separation.Length - 2, fit.SampleCount);
        Assert.Equal(5e8, fit.Modulus, 5e8 * 0.05);
    }

    [Fact]
    public void Mismatched_lengths_are_a_programmer_error()
        => Assert.Throws<ArgumentException>(() => ContactMechanics.Fit(ContactModel.Hertz, new float[3], new float[4], Poisson, 20e-9));
}
