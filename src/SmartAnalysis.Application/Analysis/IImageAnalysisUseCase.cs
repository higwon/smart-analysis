using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// The Application use case the UI calls to run image operations (doc 11: the UI depends on Use Cases, not
/// on the Analysis operation contract). It owns the workspace-mutation policy of a transform: run the
/// operation, add the derived dataset, make it active, and put the source into the comparison set
/// (Before/After, doc 22 §5).
/// </summary>
public interface IImageAnalysisUseCase
{
    /// <summary>
    /// Applies Flatten to the image identified by <paramref name="sourceId"/>. On success the derived
    /// dataset becomes active and the source is the comparison set; returns the derived id + any warnings.
    /// Invalid parameters / a non-image source / a run failure come back as a typed <see cref="FlattenOutcome.Error"/>.
    /// </summary>
    Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken cancellationToken = default);
}
