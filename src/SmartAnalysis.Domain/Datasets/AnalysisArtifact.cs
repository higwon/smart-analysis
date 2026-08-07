using System.Collections.ObjectModel;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A non-dataset analysis result: named scalar measurements produced by an operation from a source
/// dataset (e.g. roughness Sq/Sa, statistics). An <b>entity</b> keyed by <see cref="Id"/> (ADR-012);
/// equality/hash by <see cref="Id"/>. Immutable; holds no buffers (so not <see cref="IDisposable"/>).
/// <para>Richer outputs (grains, histograms, matches) and the mandatory <c>Provenance</c> (F05) are
/// added by the operations/tasks that produce them.</para>
/// </summary>
public sealed class AnalysisArtifact : IEquatable<AnalysisArtifact>
{
    public AnalysisArtifact(
        DatasetId id,
        DatasetId sourceId,
        string operationId,
        IReadOnlyDictionary<string, PhysicalValue> scalars)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Artifact Id must not be empty.", nameof(id));
        }

        if (sourceId.IsEmpty)
        {
            throw new ArgumentException("SourceId must not be empty.", nameof(sourceId));
        }

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

    /// <summary>Named scalar results with their units (read-only, defensively copied).</summary>
    public IReadOnlyDictionary<string, PhysicalValue> Scalars { get; }

    // --- Identity-based equality (ADR-012) ---

    public bool Equals(AnalysisArtifact? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as AnalysisArtifact);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(AnalysisArtifact? left, AnalysisArtifact? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(AnalysisArtifact? left, AnalysisArtifact? right) => !(left == right);
}
