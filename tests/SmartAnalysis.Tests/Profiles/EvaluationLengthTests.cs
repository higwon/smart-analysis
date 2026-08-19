using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>The centred integer-sampling-length evaluation window (lr = λc, up to the ISO 21920 default of 5).</summary>
public sealed class EvaluationLengthTests
{
    [Fact]
    public void Takes_five_centred_sampling_lengths_when_the_profile_is_long()
    {
        // 2000 samples · dx 0.01 = 20 µm; λc 0.8 µm → 80 samples/length, 25 fit → capped at 5 = 400 samples, centred.
        var window = EvaluationWindow.Central(sampleCount: 2000, dx: 0.01, cutoff: 0.8);

        Assert.Equal(5, window.SamplingLengths);
        Assert.Equal(400, window.Length);
        Assert.Equal(800, window.Start);           // (2000 − 400) / 2
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void Takes_fewer_sampling_lengths_when_the_profile_is_short()
    {
        // 120 samples · 0.01 = 1.2 µm; λc 0.8 → 80 samples/length, only 1 whole length fits.
        var window = EvaluationWindow.Central(sampleCount: 120, dx: 0.01, cutoff: 0.8);

        Assert.Equal(1, window.SamplingLengths);
        Assert.Equal(80, window.Length);
        Assert.Equal(20, window.Start);
    }

    [Fact]
    public void Is_empty_when_not_even_one_sampling_length_fits()
    {
        var window = EvaluationWindow.Central(sampleCount: 50, dx: 0.01, cutoff: 0.8); // 0.5 µm < λc

        Assert.True(window.IsEmpty);
        Assert.Equal(0, window.SamplingLengths);
        Assert.Equal(EvaluationWindow.None, window);
    }

    [Fact]
    public void The_window_stays_within_the_data()
    {
        var window = EvaluationWindow.Central(sampleCount: 137, dx: 0.01, cutoff: 0.5); // 50 samples/length, 2 fit

        Assert.Equal(2, window.SamplingLengths);
        Assert.True(window.Start >= 0);
        Assert.True(window.Start + window.Length <= 137);
    }

    [Fact]
    public void Rejects_a_non_positive_spacing_or_cutoff()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationWindow.Central(100, 0.0, 0.8));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationWindow.Central(100, 0.01, 0.0));
    }
}
