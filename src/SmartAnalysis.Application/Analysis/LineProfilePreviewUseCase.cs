using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Live line-profile preview (V07 split view) over the shared <see cref="LineProfileBuilder"/> — the same
/// sampling, effective-line clamping, and arc-length axis the operation uses, but built as a transient dataset
/// (never added to the workspace) and projected to a <see cref="CurveRenderInput"/>.
/// <para><b>Lifetime (ADR-011/012):</b> the returned render input is <b>fully owned and outlives the transient
/// dataset</b> — <see cref="RenderInputFactory.ForLineProfile"/> copies the profile's values into new
/// <c>double[]</c> series arrays and reads the axis into a scalar <c>AxisView</c>, retaining no reference to the
/// dataset buffer. The buffer is therefore disposed here (the <c>using</c>) before this method returns, and the
/// caller may render the input afterwards with no use-after-dispose. A regression test locks this: the input's
/// values are correct after the dataset has been disposed.</para>
/// </summary>
public sealed class LineProfilePreviewUseCase : ILineProfilePreview
{
    public CurveRenderInput? Preview(ScanImageDataset image, double x0, double y0, double x1, double y1, int samples)
    {
        ArgumentNullException.ThrowIfNull(image);

        // The preview curve needs a metric arc-length axis, so the same spatial-axis rule as the operation applies.
        if (image.X.Unit.Dimension != StandardUnits.Length || image.Y.Unit.Dimension != StandardUnits.Length)
        {
            return null;
        }

        if (samples < 2 || LineProfileBuilder.IsDegenerate(image, x0, y0, x1, y1))
        {
            return null; // nothing to draw for a point (or too few samples)
        }

        // ForLineProfile materializes owned series arrays from the profile before this returns, so disposing the
        // transient dataset here leaves the returned render input fully self-contained (see the lifetime note above).
        using var profile = LineProfileBuilder.Build(image, x0, y0, x1, y1, samples, DatasetId.New(), ProvenanceRecord.Root);
        return RenderInputFactory.ForLineProfile(profile, "Profile");
    }
}
