using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

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

    /// <summary>
    /// Computes an <b>uncommitted preview</b> of Flatten for the settings panel: runs the operation on a transient
    /// copy (never added to the workspace, no provenance, active state untouched) and returns an <b>owned</b> image
    /// render input for the result — rendered with the caller's <paramref name="colormap"/>/<paramref name="range"/>
    /// so the settings panel can show source-vs-preview live. Returns <c>null</c> for a non-image source, invalid
    /// options, or a run failure. Applying (<see cref="ApplyFlattenAsync"/>) is what materializes the dataset.
    /// </summary>
    Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes whole-image summary statistics for the image identified by <paramref name="sourceId"/>.
    /// The resulting measurement (a real <c>AnalysisArtifact</c> entity) is <b>attached to that image</b> in
    /// the measurement store and does <b>not</b> change the active dataset; returns the readouts + histogram
    /// for the Inspector result card (a typed error on failure).
    /// </summary>
    Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the same whole-image statistics as <see cref="ComputeStatisticsAsync"/> but as an <b>ephemeral
    /// preview</b>: it is <b>not</b> attached to the measurement store (no explorer node) and never changes active
    /// state — for the inline readout shown on the Dataset inspector, which must not accumulate saved measurements.
    /// </summary>
    Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads an already-attached measurement by its artifact id into the UI DTO (e.g. when its explorer
    /// node is selected). Returns <c>null</c> when no such measurement is attached. Never changes active state.
    /// </summary>
    StatisticsResult? GetMeasurement(DatasetId artifactId);

    /// <summary>
    /// Reconstructs, from an attached measurement's provenance, the region it was taken over — so the shell can
    /// overlay "this came from here" on the source image when the measurement is selected. Returns <c>null</c> when
    /// the measurement is not attached or carries no drawable region (e.g. a whole-image statistic, or a shape the
    /// overlay can't draw). Never changes active state.
    /// </summary>
    MeasurementRegion? GetMeasurementRegion(DatasetId artifactId) => null;
}
