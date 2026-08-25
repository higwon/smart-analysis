using SmartAnalysis.Domain.Spectroscopy;
using Xunit;

namespace SmartAnalysis.Tests.Spectroscopy;

/// <summary>
/// TASK-D03 domain model: a segmentation is ordered, gapless, and covers every sample — an inconsistent split is
/// unrepresentable, so no consumer has to defend against gaps or overlaps.
/// </summary>
public sealed class CurveSegmentationTests
{
    [Fact]
    public void A_segment_is_a_half_open_range_and_reports_its_length()
    {
        var s = new CurveSegment(SegmentKind.Approach, 2, 7);

        Assert.Equal(5, s.Length);
        Assert.True(s.Contains(2));
        Assert.True(s.Contains(6));
        Assert.False(s.Contains(7)); // exclusive end
        Assert.False(s.Contains(1));
    }

    [Theory]
    [InlineData(0, 0)]   // empty range
    [InlineData(5, 3)]   // inverted
    public void A_segment_rejects_a_non_positive_range(int start, int end)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CurveSegment(SegmentKind.Approach, start, end));

    [Fact]
    public void A_segment_rejects_an_undefined_kind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CurveSegment((SegmentKind)99, 0, 2));

    [Fact]
    public void A_segmentation_exposes_kinds_counts_and_the_kind_at_an_index()
    {
        var seg = new CurveSegmentation(10,
        [
            new CurveSegment(SegmentKind.Approach, 0, 4),
            new CurveSegment(SegmentKind.Undetermined, 4, 6),
            new CurveSegment(SegmentKind.Retract, 6, 10),
        ]);

        Assert.Equal(10, seg.SampleCount);
        Assert.Equal(4, seg.CountOf(SegmentKind.Approach));
        Assert.Equal(2, seg.CountOf(SegmentKind.Undetermined));
        Assert.Equal(4, seg.CountOf(SegmentKind.Retract));
        Assert.Equal(SegmentKind.Approach, seg.KindAt(3));
        Assert.Equal(SegmentKind.Retract, seg.KindAt(6));
        Assert.Null(seg.KindAt(10)); // out of range
        Assert.Single(seg.OfKind(SegmentKind.Retract));
    }

    [Fact]
    public void A_segmentation_rejects_a_gap()
        => Assert.Throws<ArgumentException>(() => new CurveSegmentation(10,
        [
            new CurveSegment(SegmentKind.Approach, 0, 4),
            new CurveSegment(SegmentKind.Retract, 5, 10), // gap at 4
        ]));

    [Fact]
    public void A_segmentation_rejects_an_overlap()
        => Assert.Throws<ArgumentException>(() => new CurveSegmentation(10,
        [
            new CurveSegment(SegmentKind.Approach, 0, 6),
            new CurveSegment(SegmentKind.Retract, 4, 10), // overlaps 4..6
        ]));

    [Fact]
    public void A_segmentation_rejects_partial_coverage()
        => Assert.Throws<ArgumentException>(() => new CurveSegmentation(10,
            [new CurveSegment(SegmentKind.Approach, 0, 8)])); // 8 of 10 samples

    [Fact]
    public void All_undetermined_covers_the_whole_curve_and_an_empty_curve_has_no_segments()
    {
        var all = CurveSegmentation.AllUndetermined(5);
        Assert.Equal(5, all.CountOf(SegmentKind.Undetermined));
        Assert.Single(all.Segments);

        var empty = CurveSegmentation.AllUndetermined(0);
        Assert.Empty(empty.Segments);
        Assert.Equal(0, empty.SampleCount);
    }
}
