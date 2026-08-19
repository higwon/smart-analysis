using SmartAnalysis.Analysis.Profiles;
using Xunit;

namespace SmartAnalysis.Tests.Profiles;

/// <summary>
/// The centred integer-sampling-length evaluation window (lr = λc, up to the ISO 21920 default of 5). All length
/// reasoning is on the interval span (N samples enclose N−1 intervals), so the window never overstates its coverage.
/// </summary>
public sealed class EvaluationLengthTests
{
    [Fact]
    public void Takes_five_centred_sampling_lengths_when_the_profile_is_long()
    {
        // 2000 samples · dx 0.01 → span 19.99 µm; λc 0.8 → 5 lengths cap. Target 4.0 µm = 400 intervals = 401 samples.
        var window = EvaluationWindow.Central(sampleCount: 2000, dx: 0.01, cutoff: 0.8);

        Assert.Equal(5, window.SamplingLengths);
        Assert.Equal(401, window.Length);
        Assert.Equal(799, window.Start);                          // (2000 − 401) / 2
        Assert.Equal(4.0, (window.Length - 1) * 0.01, 12);        // actual sampled span == 5·λc here (λc is a multiple of dx)
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void Takes_fewer_sampling_lengths_when_the_profile_is_short()
    {
        // 120 samples · 0.01 → span 1.19 µm; λc 0.8 → only 1 whole length fits (80 intervals = 81 samples).
        var window = EvaluationWindow.Central(sampleCount: 120, dx: 0.01, cutoff: 0.8);

        Assert.Equal(1, window.SamplingLengths);
        Assert.Equal(81, window.Length);
        Assert.Equal(19, window.Start);
    }

    [Theory]
    [InlineData(80, false)]  // span (80−1)·0.01 = 0.79 µm < λc → NOT one sampling length
    [InlineData(81, true)]   // span (81−1)·0.01 = 0.80 µm = λc → exactly one sampling length
    public void One_sampling_length_needs_a_full_interval_span_not_a_sample_count(int sampleCount, bool fits)
    {
        var window = EvaluationWindow.Central(sampleCount, dx: 0.01, cutoff: 0.8);

        Assert.Equal(fits, !window.IsEmpty);
        Assert.Equal(fits ? 1 : 0, window.SamplingLengths);
        if (fits)
        {
            Assert.Equal(0.8, (window.Length - 1) * 0.01, 12); // exactly one sampling length of data
        }
    }

    [Fact]
    public void Reports_a_window_no_longer_than_the_target_when_cutoff_is_not_a_multiple_of_dx()
    {
        // dx 0.03, λc 0.8 → 26.67 samples/length; 5·λc = 4.0 µm target → 133 whole intervals = 134 samples = 3.99 µm.
        var window = EvaluationWindow.Central(sampleCount: 200, dx: 0.03, cutoff: 0.8);

        Assert.Equal(5, window.SamplingLengths);
        Assert.Equal(134, window.Length);
        double actualSpan = (window.Length - 1) * 0.03;
        Assert.True(actualSpan <= 5 * 0.8, "the sampled span must not exceed the theoretical target");
        Assert.True(actualSpan > 5 * 0.8 - 0.03, "and it must be within one interval of it");
    }

    [Fact]
    public void Is_empty_when_not_even_one_sampling_length_fits()
    {
        var window = EvaluationWindow.Central(sampleCount: 50, dx: 0.01, cutoff: 0.8); // span 0.49 µm < λc

        Assert.True(window.IsEmpty);
        Assert.Equal(0, window.SamplingLengths);
        Assert.Equal(EvaluationWindow.None, window);
    }

    [Fact]
    public void The_window_stays_within_the_data()
    {
        var window = EvaluationWindow.Central(sampleCount: 137, dx: 0.01, cutoff: 0.5); // span 1.36 µm → 2 lengths

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
