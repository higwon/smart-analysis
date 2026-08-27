using SmartAnalysis.Analysis.Spectroscopy;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-A40: a raw deflection signal carries an arbitrary offset, so absolute zero is a property of the
/// detector's electronics and not of the sample. Every force measure is read from the curve's own non-contact
/// level instead.
/// <para>
/// The defect this closes reached the screen: an adhesion map of a real file came out uniformly <c>0</c>, with a
/// colour bar reading 0 to 0, because that file's force never crosses zero. Nothing was thrown, nothing was
/// NaN — it simply looked like a sample with no adhesion anywhere.
/// </para>
/// </summary>
public sealed class BaselineReferencedForceTests
{
    private const int Flat = 10;
    private const int Ramp = 11;

    /// <summary>Out of contact from separation 20 to 10, then a 10 nN/nm spring from 10 to 0, plus an offset.</summary>
    private static (float[] Force, float[] Separation) Approach(float offset = 0f, float pullOff = 0f)
    {
        var separation = new float[Flat + Ramp];
        var force = new float[Flat + Ramp];

        for (int i = 0; i < Flat; i++)
        {
            separation[i] = 20f - i;
            force[i] = offset;
        }

        // A dip just before contact, so there is a pull-off to find below the non-contact level.
        if (pullOff > 0f)
        {
            force[Flat - 1] = offset - pullOff;
        }

        for (int i = 0; i < Ramp; i++)
        {
            separation[Flat + i] = 10f - i;
            force[Flat + i] = offset + (i * (100f / (Ramp - 1)));
        }

        return (force, separation);
    }

    private static ForceDistanceMeasures Measure(float offset = 0f, float pullOff = 0f)
    {
        var (force, separation) = Approach(offset, pullOff);
        return ForceDistanceMeasures.Of(force, separation, 50.0);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(267f)]     // the offset the real sample file carries
    [InlineData(-1000f)]
    public void An_offset_on_every_sample_changes_nothing(float offset)
    {
        // This is the property that makes the change correct: sliding the whole curve up or down is a change to
        // the detector, not to the sample, and no measure of the sample may follow it.
        var at0 = Measure();
        var shifted = Measure(offset);

        Assert.Equal(at0.MaxForce, shifted.MaxForce, 3);
        Assert.Equal(at0.Adhesion, shifted.Adhesion, 3);
        Assert.Equal(at0.Stiffness, shifted.Stiffness, 3);
        Assert.Equal(at0.Deformation, shifted.Deformation, 3);
        Assert.Equal(offset, shifted.Baseline, 3);
    }

    [Fact]
    public void A_pull_off_below_the_non_contact_level_is_found_on_a_curve_that_never_crosses_zero()
    {
        // The screenshot case. Every sample is positive, so measured from absolute zero the adhesion is 0 — and
        // an entire map of that reads as a sample nothing sticks to.
        var (force, separation) = Approach(offset: 267f, pullOff: 27f);
        foreach (var f in force)
        {
            Assert.True(f > 0f, "the fixture must never cross zero, or it does not reproduce the defect");
        }

        var m = ForceDistanceMeasures.Of(force, separation, 50.0);

        Assert.Equal(27.0, m.Adhesion, 3);
    }

    [Fact]
    public void A_curve_that_never_leaves_the_non_contact_level_has_no_adhesion()
    {
        // Zero is still the right answer when there is genuinely no pull-off — the change is about WHERE zero is.
        Assert.Equal(0.0, Measure(offset: 267f).Adhesion, 3);
    }

    [Fact]
    public void A_flat_non_contact_stretch_is_reported_as_flat()
    {
        Assert.True(Measure().BaselineIsFlat);
    }

    [Fact]
    public void A_curve_that_is_ramping_everywhere_has_no_non_contact_level_and_says_so()
    {
        // A bare ramp never leaves contact, so its far end is not a baseline — it is just the low end of the
        // ramp. The mean of it is still returned (there is nothing better) but it is not claimed to be flat.
        var separation = new float[11];
        var force = new float[11];
        for (int i = 0; i < 11; i++)
        {
            separation[i] = 10f - i;
            force[i] = i * 10f;
        }

        var m = ForceDistanceMeasures.Of(force, separation, 50.0);

        Assert.False(m.BaselineIsFlat);
    }

    [Fact]
    public void The_stiffness_of_a_spring_is_its_slope_wherever_the_baseline_sits()
    {
        // The window moves when the baseline does — it is a percentage of the peak ABOVE it — but a chord of a
        // straight line has the line's slope wherever it is taken.
        Assert.Equal(10.0, Measure().Stiffness, 6);
        Assert.Equal(10.0, Measure(offset: 267f).Stiffness, 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(267f)]
    public void The_window_edge_is_interpolated_in_baseline_relative_force(float offset)
    {
        // 55% of a peak of 100 falls BETWEEN the samples at 50 and 60, so the edge is interpolated to
        // separation 4.5 rather than snapped to the sample at 4. Comparing raw forces against a
        // baseline-relative target finds no crossing at all once the signal is offset, and the answer quietly
        // falls back to the nearest qualifying sample — 4.0, which is wrong by a fifth of the window.
        var (force, separation) = Approach(offset);

        var m = ForceDistanceMeasures.Of(force, separation, 55.0);

        Assert.Equal(4.5, m.Deformation, 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(267f)]
    public void A_window_the_curve_never_crosses_falls_back_in_baseline_relative_force(float offset)
    {
        // At a 0% threshold nothing dips below the target, so the edge is the farthest qualifying sample rather
        // than an interpolated crossing — the branch that uses that sample's OWN force. Reading it raw would put
        // the detector's offset straight into the force drop.
        var (force, separation) = Approach(offset);

        var m = ForceDistanceMeasures.Of(force, separation, 0.0);

        Assert.Equal(20.0, m.Deformation, 6);   // peak at 0, farthest qualifying sample at 20
        Assert.Equal(5.0, m.Stiffness, 6);      // 100 nN over that 20 nm
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void A_baseline_fraction_outside_the_curve_is_refused(double fraction)
    {
        // Zero would read a level from no travel at all; more than one would call the whole curve non-contact.
        var (force, separation) = Approach();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ForceDistanceMeasures.Of(force, separation, 50.0, fraction));
    }

    [Fact]
    public void A_wider_baseline_window_still_finds_the_same_flat_level()
    {
        // The fraction says how much of the travel to average. On a curve whose tail really is flat, widening it
        // within that tail must not move the answer — that is what makes the parameter safe to expose.
        var (force, separation) = Approach(offset: 50f, pullOff: 5f);

        var narrow = ForceDistanceMeasures.Of(force, separation, 50.0, 0.1);
        var wide = ForceDistanceMeasures.Of(force, separation, 50.0, 0.4);

        Assert.Equal(narrow.MaxForce, wide.MaxForce, 3);
        Assert.Equal(narrow.Baseline, wide.Baseline, 3);
    }
}
