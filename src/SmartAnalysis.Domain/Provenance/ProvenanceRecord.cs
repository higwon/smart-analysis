using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// The lineage of a dataset/artifact (doc 16's "Provenance record"): an optional <see cref="ParentId"/>
/// (the dataset it was derived from) and the ordered <see cref="Steps"/> that produced it. Mandatory
/// on every dataset/artifact (ADR-004) — a result without provenance is unrepresentable.
/// <para>
/// Lineage is carried here (ADR-013) rather than duplicating the owning dataset's <c>Id</c>/<c>Source</c>,
/// which live on the dataset itself. A freshly-imported/original dataset uses <see cref="Root"/>
/// (no parent, no steps). Immutable; <see cref="Append"/> returns a new instance.
/// </para>
/// <para>Named <c>ProvenanceRecord</c> (not <c>Provenance</c>) to avoid clashing with the
/// <c>SmartAnalysis.Domain.Provenance</c> namespace; dataset members are still named <c>Provenance</c>.</para>
/// </summary>
public sealed class ProvenanceRecord
{
    /// <summary>Provenance for an original/imported/synthetic dataset: no parent, no steps.</summary>
    public static ProvenanceRecord Root { get; } = new(null, []);

    public ProvenanceRecord(DatasetId? parentId, IReadOnlyList<ProvenanceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ParentId = parentId;
        Steps = steps.ToArray().AsReadOnly();
    }

    /// <summary>The dataset this one was derived from, or null for an original/root dataset.</summary>
    public DatasetId? ParentId { get; }

    /// <summary>The ordered analysis steps that produced this dataset (empty for a root).</summary>
    public IReadOnlyList<ProvenanceStep> Steps { get; }

    /// <summary>True when there is no parent and no steps (an original/imported dataset).</summary>
    public bool IsRoot => ParentId is null && Steps.Count == 0;

    /// <summary>Returns a new record with <paramref name="step"/> appended (this instance is unchanged).</summary>
    public ProvenanceRecord Append(ProvenanceStep step)
    {
        DomainGuard.NotNull(step, nameof(step));
        var next = new ProvenanceStep[Steps.Count + 1];
        for (var i = 0; i < Steps.Count; i++)
        {
            next[i] = Steps[i];
        }

        next[^1] = step;
        return new ProvenanceRecord(ParentId, next);
    }

    /// <summary>Provenance for a dataset derived from <paramref name="parentId"/> via <paramref name="steps"/>.</summary>
    public static ProvenanceRecord DerivedFrom(DatasetId parentId, IReadOnlyList<ProvenanceStep> steps)
        => new(parentId, steps);
}
