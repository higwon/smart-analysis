namespace SmartAnalysis.Visualization.Rendering;

/// <summary>What a reference line on a curve is, which is what decides how it is drawn.</summary>
public enum CurveMarkKind
{
    /// <summary>The level a measurement is taken <b>from</b> — the non-contact baseline.</summary>
    Reference,

    /// <summary>Where a setting the viewer chose lands on this curve.</summary>
    Setting,

    /// <summary>Something the curve itself has, which no setting put there — its peak.</summary>
    Feature,
}

/// <summary>
/// One reference line on a curve: where it sits, what to call it, and what kind of thing it is.
/// <para>
/// The label is not decoration. The threshold window draws four lines at once, and drawn in one style with no
/// names they are four identical dashes: a viewer can see that something was marked but not which mark is the
/// baseline, which is the threshold, and which is the peak. That is the state this type replaced.
/// </para>
/// </summary>
public readonly record struct CurveMark(double Position, string Label, CurveMarkKind Kind);
