namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// The segmentation of one force curve: ordered, non-overlapping <see cref="CurveSegment"/>s that together cover every
/// sample. Immutable value object.
/// <para>
/// <b>Computed, not stored</b> (ADR-020): a segmentation is the output of a classifier <i>mode + parameters</i>, so it
/// is an opinion about the raw data, not part of it. Keeping it off <c>ForceCurveDataset</c> means a curve is never
/// frozen to one classifier's answer, and an operation that segments records its mode/parameters in provenance — so
/// the split is reproducible and auditable like any other analysis step.
/// </para>
/// </summary>
public sealed class CurveSegmentation
{
    // Held as a read-only wrapper, not the raw array: a caller must not be able to cast Segments back to
    // CurveSegment[] and mutate it, which would break the ordered/gapless/total-coverage invariants the
    // constructor enforced (or inject a null for OfKind to trip over).
    private readonly IReadOnlyList<CurveSegment> _segments;

    public CurveSegmentation(int sampleCount, IReadOnlyList<CurveSegment> segments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentNullException.ThrowIfNull(segments);

        var copy = new CurveSegment[segments.Count];
        int expectedStart = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i] ?? throw new ArgumentException("Segments must not contain null.", nameof(segments));
            if (s.Start != expectedStart)
            {
                throw new ArgumentException(
                    $"Segments must be ordered and gapless: segment {i} starts at {s.Start}, expected {expectedStart}.",
                    nameof(segments));
            }

            copy[i] = s;
            expectedStart = s.End;
        }

        if (expectedStart != sampleCount)
        {
            throw new ArgumentException(
                $"Segments must cover every sample: covered {expectedStart} of {sampleCount}.", nameof(segments));
        }

        SampleCount = sampleCount;
        _segments = Array.AsReadOnly(copy);
    }

    /// <summary>Number of samples the segmentation covers (the curve's length).</summary>
    public int SampleCount { get; }

    public IReadOnlyList<CurveSegment> Segments => _segments;

    /// <summary>The segments of one kind, in order (empty when the classifier found none).</summary>
    public IEnumerable<CurveSegment> OfKind(SegmentKind kind) => _segments.Where(s => s.Kind == kind);

    /// <summary>How many samples are classified as <paramref name="kind"/>.</summary>
    public int CountOf(SegmentKind kind) => OfKind(kind).Sum(s => s.Length);

    /// <summary>What <paramref name="index"/> was classified as, or <c>null</c> when it is out of range.</summary>
    public SegmentKind? KindAt(int index)
    {
        foreach (var s in _segments)
        {
            if (s.Contains(index))
            {
                return s.Kind;
            }
        }

        return null;
    }

    /// <summary>A segmentation that classifies nothing — every sample <see cref="SegmentKind.Undetermined"/>.</summary>
    public static CurveSegmentation AllUndetermined(int sampleCount)
        => sampleCount == 0
            ? new CurveSegmentation(0, [])
            : new CurveSegmentation(sampleCount, [new CurveSegment(SegmentKind.Undetermined, 0, sampleCount)]);
}
