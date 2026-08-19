using SmartAnalysis.Domain.Geometry;

namespace SmartAnalysis.Application.Operations;

/// <summary>
/// The current region of interest, shared between the shell (which sets it from the drawn ROI overlay) and the
/// operation launcher (which attaches it to a region-capable op's <see cref="Operations.OperationLauncherUseCase"/>
/// run as <c>OperationInput.Region</c>). Null when no ROI is active — a region-capable op then runs whole-image.
/// A single mutable holder (registered as a singleton), so the ROI persists across runs and across which op is
/// selected, exactly like the active dataset.
/// </summary>
public sealed class RegionContext
{
    /// <summary>The active ROI, or null for the whole dataset.</summary>
    public Roi? Current { get; set; }
}
