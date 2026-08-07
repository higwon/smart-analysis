using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Domain.Provenance;

/// <summary>
/// The lineage of a dataset/artifact (doc 16's "Provenance record"): an optional <see cref="ParentId"/>
/// (the dataset it was derived from) and the ordered <see cref="Steps"/> that produced it. Mandatory
/// on every dataset/artifact (ADR-004) — a result without provenance is unrepresentable.
/// <para>
/// Lineage is carried here (ADR-013) rather than duplicating the owning dataset's <c>Id</c>/<c>Source</c>.
/// <b>State rule (ADR-013): parent and steps are both-or-neither</b> — exactly two valid shapes:
/// <c>Root</c> (no parent, no steps) for originals/imports, and <c>Derived</c> (a non-empty parent id
/// with one or more steps). A record with steps therefore always has a parent. <see cref="Steps"/> are
/// <b>contiguously ordered from 0</b> (step <c>i</c> has <c>Order == i</c>) with unique, non-null step
/// ids. Immutable; <see cref="Append"/> returns a new instance and is invalid on <see cref="Root"/>.
/// </para>
/// </summary>
public sealed class ProvenanceRecord
{
    /// <summary>Provenance for an original/imported/synthetic dataset: no parent, no steps.</summary>
    public static ProvenanceRecord Root { get; } = new(null, []);

    public ProvenanceRecord(DatasetId? parentId, IReadOnlyList<ProvenanceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (parentId is { IsEmpty: true })
        {
            throw new ArgumentException("ParentId, when present, must not be empty.", nameof(parentId));
        }

        // both-or-neither: steps ⇔ parent
        if (steps.Count > 0 && parentId is null)
        {
            throw new ArgumentException("A provenance with steps must have a parent (use DerivedFrom); Root has no steps.", nameof(steps));
        }

        if (steps.Count == 0 && parentId is not null)
        {
            throw new ArgumentException("A derived provenance (with a parent) must have at least one step.", nameof(parentId));
        }

        var copy = new ProvenanceStep[steps.Count];
        var ids = new HashSet<string>(steps.Count, StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i] ?? throw new ArgumentException("Steps must not contain null elements.", nameof(steps));
            if (step.Order != i)
            {
                throw new ArgumentException(
                    $"Steps must be contiguously ordered from 0: step at index {i} has Order {step.Order}.", nameof(steps));
            }

            if (!ids.Add(step.StepId))
            {
                throw new ArgumentException($"Duplicate StepId '{step.StepId}' within a provenance record.", nameof(steps));
            }

            copy[i] = step;
        }

        ParentId = parentId;
        Steps = Array.AsReadOnly(copy);
    }

    /// <summary>The dataset this one was derived from, or null for an original/root dataset.</summary>
    public DatasetId? ParentId { get; }

    /// <summary>The ordered analysis steps that produced this dataset (empty only for <see cref="Root"/>).</summary>
    public IReadOnlyList<ProvenanceStep> Steps { get; }

    /// <summary>True when there is no parent and no steps (an original/imported dataset).</summary>
    public bool IsRoot => ParentId is null && Steps.Count == 0;

    /// <summary>
    /// Returns a new record with <paramref name="step"/> appended (this instance is unchanged). The new
    /// step's <c>Order</c> must equal the current step count. Invalid on <see cref="Root"/> (a step
    /// requires a parent — build derived provenance with <see cref="DerivedFrom"/>).
    /// </summary>
    public ProvenanceRecord Append(ProvenanceStep step)
    {
        DomainGuard.NotNull(step, nameof(step));
        var next = new ProvenanceStep[Steps.Count + 1];
        for (var i = 0; i < Steps.Count; i++)
        {
            next[i] = Steps[i];
        }

        next[^1] = step;
        return new ProvenanceRecord(ParentId, next); // ctor enforces order/dup/null/both-or-neither
    }

    /// <summary>
    /// Provenance for a dataset derived from <paramref name="parentId"/> via <paramref name="steps"/>
    /// (at least one step, contiguously ordered from 0). <paramref name="parentId"/> must be non-empty.
    /// </summary>
    public static ProvenanceRecord DerivedFrom(DatasetId parentId, IReadOnlyList<ProvenanceStep> steps)
    {
        if (parentId.IsEmpty)
        {
            throw new ArgumentException("Derived provenance requires a non-empty parent id.", nameof(parentId));
        }

        return new ProvenanceRecord(parentId, steps);
    }
}
