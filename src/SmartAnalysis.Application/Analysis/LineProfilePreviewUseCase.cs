using SmartAnalysis.Analysis.Profiles;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Live line-profile preview (V07 split view) over the shared <see cref="LineProfileBuilder"/> — the same
/// sampling, effective-line clamping, and arc-length axis the operation uses, but built as a transient dataset
/// (never added to the workspace) and projected to a <see cref="CurveRenderInput"/>. The transient dataset owns a
/// buffer, so it is disposed here once its values are copied into the render input (ADR-011/012).
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

        using var profile = LineProfileBuilder.Build(image, x0, y0, x1, y1, samples, DatasetId.New(), ProvenanceRecord.Root);
        return RenderInputFactory.ForLineProfile(profile, "Profile");
    }
}
