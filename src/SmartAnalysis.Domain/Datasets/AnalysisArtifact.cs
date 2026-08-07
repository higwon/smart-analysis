using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A non-dataset analysis result: named scalar measurements produced by an operation from a source
/// dataset (e.g. roughness Sq/Sa, statistics). Immutable.
/// <para>
/// F03 provides the minimal shape. Richer outputs (grains, histograms, matches) and the mandatory
/// <c>Provenance</c> (F05) are added by the operations/tasks that produce them.
/// </para>
/// </summary>
public sealed record AnalysisArtifact
{
    public AnalysisArtifact(
        DatasetId id,
        DatasetId sourceId,
        string operationId,
        IReadOnlyDictionary<string, PhysicalValue> scalars)
    {
        Id = id;
        SourceId = sourceId;
        OperationId = DomainGuard.Text(operationId, nameof(operationId));
        ArgumentNullException.ThrowIfNull(scalars);
        Scalars = new ReadOnlyDictionary<string, PhysicalValue>(
            new Dictionary<string, PhysicalValue>(scalars, StringComparer.Ordinal));
    }

    /// <summary>Stable identity of this artifact.</summary>
    public DatasetId Id { get; }

    /// <summary>The dataset this result was computed from (lineage; provenance in F05).</summary>
    public DatasetId SourceId { get; }

    /// <summary>The operation that produced it (e.g. "image.roughness").</summary>
    public string OperationId { get; }

    /// <summary>Named scalar results with their units.</summary>
    public IReadOnlyDictionary<string, PhysicalValue> Scalars { get; }
}
