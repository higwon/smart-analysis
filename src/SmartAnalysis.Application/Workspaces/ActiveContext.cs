using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Workspaces;

/// <summary>
/// The single, explicit "what am I acting on" model (doc 17): one <see cref="ActiveId"/> (the current
/// dataset, or null when nothing is active) plus an ordered <see cref="Comparison"/> set (for
/// before/after and multi-dataset views). Immutable <b>value object</b> with <b>structural equality</b>
/// — two instances are equal when their active id and their ordered comparison ids match. Replaces the
/// legacy three-way ambiguous current item (tray vs view vs dock, doc 05).
/// </summary>
public sealed class ActiveContext : IEquatable<ActiveContext>
{
    /// <summary>No active dataset and an empty comparison set.</summary>
    public static ActiveContext Empty { get; } = new(null, []);

    public ActiveContext(DatasetId? activeId, IReadOnlyList<DatasetId> comparison)
    {
        if (activeId is { IsEmpty: true })
        {
            throw new ArgumentException("ActiveId, when present, must not be empty.", nameof(activeId));
        }

        ArgumentNullException.ThrowIfNull(comparison);
        var seen = new HashSet<DatasetId>();
        var copy = new List<DatasetId>(comparison.Count);
        foreach (var id in comparison)
        {
            if (id.IsEmpty)
            {
                throw new ArgumentException("Comparison ids must not be empty.", nameof(comparison));
            }

            if (seen.Add(id))
            {
                copy.Add(id); // preserve order, drop duplicates
            }
        }

        ActiveId = activeId;
        Comparison = copy.AsReadOnly();
    }

    public DatasetId? ActiveId { get; }

    public IReadOnlyList<DatasetId> Comparison { get; }

    // --- Structural equality (value object; the ordered comparison set participates) ---

    public bool Equals(ActiveContext? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (ActiveId != other.ActiveId || Comparison.Count != other.Comparison.Count)
        {
            return false;
        }

        for (int i = 0; i < Comparison.Count; i++)
        {
            if (Comparison[i] != other.Comparison[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ActiveContext);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActiveId);
        foreach (var id in Comparison)
        {
            hash.Add(id); // order-dependent, matching the ordered-sequence equality above
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ActiveContext? left, ActiveContext? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ActiveContext? left, ActiveContext? right) => !(left == right);
}

/// <summary>Carries the previous and current <see cref="ActiveContext"/> when it changes.</summary>
public sealed class ActiveContextChangedEventArgs(ActiveContext previous, ActiveContext current) : EventArgs
{
    public ActiveContext Previous { get; } = previous;

    public ActiveContext Current { get; } = current;
}
