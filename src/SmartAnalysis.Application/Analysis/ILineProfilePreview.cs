using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Visualization.Rendering;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// The Application use case the UI calls to compute a <b>live, uncommitted</b> line profile while the user drags
/// the profile line over an image — the preview behind the split view (doc 11: the UI depends on Use Cases, not
/// on the Analysis sampler). It samples the same effective line the <c>image.line-profile</c> operation would,
/// but produces only a render input (no workspace mutation, no provenance), so dragging never pollutes history;
/// running the operation is what materializes the profile dataset.
/// </summary>
public interface ILineProfilePreview
{
    /// <summary>
    /// Samples <paramref name="image"/> along the line between the two endpoints (pixel coords, clamped to the
    /// image) into a curve render input of Z vs arc length. Returns <c>null</c> when there is no image, the axes
    /// are not spatial, or the effective line is degenerate (a point).
    /// </summary>
    CurveRenderInput? Preview(ScanImageDataset image, double x0, double y0, double x1, double y1, int samples);
}
