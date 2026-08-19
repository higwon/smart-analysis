using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// The ISO 16610-21 Gaussian profile filter: 50% transmission at the cutoff λc (the defining property), exact
/// roughness+waviness recombination, DC fully in the waviness, and long/short wavelength separation. Pure/headless.
/// </summary>
public sealed class GaussianProfileFilterTests
{
    private static float[] Sine(int n, double wavelengthSamples, double amplitude)
    {
        var z = new float[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * i / wavelengthSamples));
        }

        return z;
    }

    // Peak amplitude away from the reflected ends (middle half of the profile).
    private static double MidAmplitude(float[] v)
    {
        double max = 0;
        for (int i = v.Length / 4; i < v.Length * 3 / 4; i++)
        {
            max = Math.Max(max, Math.Abs(v[i]));
        }

        return max;
    }

    [Fact]
    public void Transmits_fifty_percent_at_the_cutoff_wavelength()
    {
        // A pure sinusoid whose wavelength equals λc: the ISO Gaussian transmits exactly 50% to each band.
        const double lambdaCInSamples = 20.0, amplitude = 1.0;
        var wave = Sine(600, lambdaCInSamples, amplitude);
        double cutoff = lambdaCInSamples; // dx = 1 → λc in the same unit

        var roughness = GaussianProfileFilter.Apply(wave, 1.0, cutoff, ProfileBand.Roughness);
        var waviness = GaussianProfileFilter.Apply(wave, 1.0, cutoff, ProfileBand.Waviness);

        Assert.Equal(0.5 * amplitude, MidAmplitude(roughness), 2); // ≈ 0.5
        Assert.Equal(0.5 * amplitude, MidAmplitude(waviness), 2);
    }

    [Fact]
    public void Roughness_plus_waviness_reconstructs_the_profile()
    {
        var wave = Sine(200, 15.0, 3.0);

        var roughness = GaussianProfileFilter.Apply(wave, 1.0, 30.0, ProfileBand.Roughness);
        var waviness = GaussianProfileFilter.Apply(wave, 1.0, 30.0, ProfileBand.Waviness);

        for (int i = 0; i < wave.Length; i++)
        {
            Assert.Equal(wave[i], roughness[i] + waviness[i], 4); // the two bands partition the profile
        }
    }

    [Fact]
    public void A_constant_profile_is_all_waviness_and_no_roughness()
    {
        var flat = new float[128];
        Array.Fill(flat, 5.0f);

        var roughness = GaussianProfileFilter.Apply(flat, 1.0, 20.0, ProfileBand.Roughness);
        var waviness = GaussianProfileFilter.Apply(flat, 1.0, 20.0, ProfileBand.Waviness);

        Assert.All(roughness, r => Assert.Equal(0.0, r, 5)); // DC carries no roughness
        Assert.All(waviness, w => Assert.Equal(5.0, w, 5));
    }

    [Fact]
    public void A_short_wavelength_stays_in_the_roughness_a_long_one_in_the_waviness()
    {
        var shortWave = Sine(400, 5.0, 1.0);   // λ = λc/8 → mostly passes to roughness
        var longWave = Sine(400, 320.0, 1.0);  // λ = 8·λc → mostly passes to waviness
        double cutoff = 40.0;

        Assert.True(MidAmplitude(GaussianProfileFilter.Apply(shortWave, 1.0, cutoff, ProfileBand.Roughness)) > 0.9);
        Assert.True(MidAmplitude(GaussianProfileFilter.Apply(longWave, 1.0, cutoff, ProfileBand.Waviness)) > 0.9);
    }

    [Fact]
    public void Rejects_a_nonpositive_spacing_or_cutoff_and_returns_empty_for_no_samples()
    {
        Assert.Empty(GaussianProfileFilter.Apply([], 1.0, 10.0, ProfileBand.Roughness));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianProfileFilter.Apply(new float[4], 0.0, 10.0, ProfileBand.Roughness));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianProfileFilter.Apply(new float[4], 1.0, 0.0, ProfileBand.Roughness));
    }
}
