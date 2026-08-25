namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>
/// What a stretch of a force curve represents. A curve is a round trip: the tip <b>approaches</b> the surface, then
/// <b>retracts</b> from it. Samples a classifier cannot confidently assign are <see cref="Undetermined"/> rather than
/// forced into a segment — a wrong assignment silently corrupts every measurement taken over it.
/// </summary>
public enum SegmentKind
{
    /// <summary>The tip is moving toward the surface (separation decreasing).</summary>
    Approach,

    /// <summary>The tip is moving away from the surface (separation increasing).</summary>
    Retract,

    /// <summary>Not confidently classifiable (too short a run, a flat/noisy stretch, or too short a curve).</summary>
    Undetermined,
}

/// <summary>
/// A contiguous run of samples of one <see cref="SegmentKind"/>, as a half-open index range <c>[Start, End)</c> into
/// the curve's sample arrays. Immutable value object; no buffers are held, so a segmentation can outlive nothing and
/// costs nothing to keep. Ranges from a segmentation are ordered, non-overlapping, and cover the whole curve.
/// </summary>
public sealed record CurveSegment
{
    public CurveSegment(SegmentKind kind, int start, int end)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined segment kind.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, $"End ({end}) must be greater than start ({start}).");
        }

        Kind = kind;
        Start = start;
        End = end;
    }

    public SegmentKind Kind { get; }

    /// <summary>First sample index in the segment (inclusive).</summary>
    public int Start { get; }

    /// <summary>One past the last sample index (exclusive), so <c>End - Start</c> is the sample count.</summary>
    public int End { get; }

    /// <summary>Number of samples the segment covers.</summary>
    public int Length => End - Start;

    /// <summary>Whether <paramref name="index"/> falls in this segment.</summary>
    public bool Contains(int index) => index >= Start && index < End;
}
