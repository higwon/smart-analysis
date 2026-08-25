using SmartAnalysis.Analysis.Spectroscopy;
using SmartAnalysis.Domain.Spectroscopy;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-D03: clean-room approach/retract segmentation. The defining property is that a round-trip curve splits into a
/// leading Approach and a trailing Retract at the turning point, and that anything the mode cannot judge is left
/// Undetermined rather than guessed (ADR-020).
/// </summary>
public sealed class ApproachRetractSegmentationTests
{
    // A real ramp: separation falls to the surface over `down` samples, then rises again.
    private static float[] Ramp(int down, int up, float start = 100f, float step = 1f)
    {
        var v = new float[down + up];
        for (int i = 0; i < down; i++)
        {
            v[i] = start - (i * step);
        }

        float bottom = start - ((down - 1) * step);
        for (int i = 0; i < up; i++)
        {
            v[down + i] = bottom + ((i + 1) * step);
        }

        return v;
    }

    [Fact]
    public void A_round_trip_ramp_splits_into_a_leading_approach_and_a_trailing_retract()
    {
        var separation = Ramp(down: 60, up: 60);

        var seg = ApproachRetractSegmentation.BySeparationTrend(separation);

        // Both phases are found, in order, and dominate the curve (the boundary itself is approximate).
        Assert.Equal(SegmentKind.Approach, seg.KindAt(5));
        Assert.Equal(SegmentKind.Retract, seg.KindAt(115));
        Assert.True(seg.CountOf(SegmentKind.Approach) > 40, $"approach={seg.CountOf(SegmentKind.Approach)}");
        Assert.True(seg.CountOf(SegmentKind.Retract) > 40, $"retract={seg.CountOf(SegmentKind.Retract)}");
        Assert.Equal(separation.Length, seg.SampleCount);

        // The split is a single turn: approach entirely precedes retract.
        var approachEnd = seg.OfKind(SegmentKind.Approach).Max(s => s.End);
        var retractStart = seg.OfKind(SegmentKind.Retract).Min(s => s.Start);
        Assert.True(approachEnd <= retractStart, "approach must precede retract");
    }

    [Fact]
    public void A_one_directional_ramp_is_all_one_phase()
    {
        var seg = ApproachRetractSegmentation.BySeparationTrend(Ramp(down: 100, up: 0));

        Assert.Equal(100, seg.CountOf(SegmentKind.Approach));
        Assert.Equal(0, seg.CountOf(SegmentKind.Retract));
    }

    [Fact]
    public void A_curve_too_short_to_show_a_trend_is_undetermined_not_guessed()
    {
        var seg = ApproachRetractSegmentation.BySeparationTrend([5f, 4f, 3f, 2f, 1f]);

        Assert.Equal(5, seg.CountOf(SegmentKind.Undetermined));
    }

    [Fact]
    public void A_run_shorter_than_the_minimum_is_undetermined_rather_than_a_phase()
    {
        // A long approach with a brief wobble upward near the end: the wobble is not a retract phase.
        var separation = Ramp(down: 100, up: 4);

        var seg = ApproachRetractSegmentation.BySeparationTrend(separation, minSegmentRatio: 0.2);

        Assert.Equal(0, seg.CountOf(SegmentKind.Retract)); // the 4-sample wobble is not promoted to a phase
        Assert.True(seg.CountOf(SegmentKind.Undetermined) > 0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void An_out_of_range_window_ratio_is_rejected(double windowRatio)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ApproachRetractSegmentation.BySeparationTrend(Ramp(30, 30), windowRatio));

    [Fact]
    public void Non_finite_separation_samples_do_not_throw_and_do_not_invent_a_phase()
    {
        // A dropout in the middle of a round-trip ramp. ForceCurveDataset does not constrain sample finiteness, so
        // this is a legal curve — and "unclassifiable" must be an outcome, not an exception (the NaN difference
        // would otherwise reach Math.Sign and throw).
        var separation = Ramp(down: 60, up: 60);
        separation[30] = float.NaN;
        separation[31] = float.PositiveInfinity;
        separation[90] = float.NegativeInfinity;

        var seg = ApproachRetractSegmentation.BySeparationTrend(separation);

        Assert.Equal(separation.Length, seg.SampleCount);          // still a total, gapless segmentation
        Assert.Equal(SegmentKind.Approach, seg.KindAt(5));         // the surrounding ramp still reads as a phase
        Assert.Equal(SegmentKind.Retract, seg.KindAt(115));
        var approachEnd = seg.OfKind(SegmentKind.Approach).Max(s => s.End);
        var retractStart = seg.OfKind(SegmentKind.Retract).Min(s => s.Start);
        Assert.True(approachEnd <= retractStart, "the dropout must not invent an extra phase");
    }

    [Fact]
    public void An_all_non_finite_separation_is_undetermined()
    {
        var separation = new float[40];
        Array.Fill(separation, float.NaN);

        var seg = ApproachRetractSegmentation.BySeparationTrend(separation);

        Assert.Equal(40, seg.CountOf(SegmentKind.Undetermined)); // no finite trend anywhere → nothing is claimed
    }

    // ---- MaxForce mode ----

    [Fact]
    public void Max_force_splits_at_the_force_peak()
    {
        // Force rises to a peak at index 40, then falls back.
        var force = new float[100];
        for (int i = 0; i < 100; i++)
        {
            force[i] = (float)(-Math.Abs(i - 40) + 40);
        }

        var seg = ApproachRetractSegmentation.ByMaxForce(force);

        Assert.Equal(SegmentKind.Approach, seg.KindAt(40)); // the peak belongs to the push
        Assert.Equal(SegmentKind.Retract, seg.KindAt(41));
        Assert.Equal(41, seg.CountOf(SegmentKind.Approach));
        Assert.Equal(59, seg.CountOf(SegmentKind.Retract));
    }

    [Fact]
    public void Max_force_declines_to_split_a_curve_whose_peak_sits_at_an_end()
    {
        // Monotonically rising force: the "peak" is the last sample — there is no round trip to split.
        var force = Enumerable.Range(0, 50).Select(i => (float)i).ToArray();

        var seg = ApproachRetractSegmentation.ByMaxForce(force);

        Assert.Equal(50, seg.CountOf(SegmentKind.Undetermined));
    }

    [Fact]
    public void Max_force_ignores_non_finite_samples_when_finding_the_peak()
    {
        var force = new float[40];
        Array.Fill(force, 1f);
        force[20] = 9f;                    // the real peak
        force[30] = float.NaN;             // must not win
        force[31] = float.PositiveInfinity; // must not win

        var seg = ApproachRetractSegmentation.ByMaxForce(force);

        Assert.Equal(21, seg.CountOf(SegmentKind.Approach)); // split just after index 20
    }

    [Fact]
    public void An_all_non_finite_curve_is_undetermined()
    {
        var force = new float[20];
        Array.Fill(force, float.NaN);

        Assert.Equal(20, ApproachRetractSegmentation.ByMaxForce(force).CountOf(SegmentKind.Undetermined));
    }
}
