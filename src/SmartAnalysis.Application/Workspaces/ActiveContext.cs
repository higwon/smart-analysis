using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Workspaces;

/// <summary>
/// The single, explicit "what am I acting on" model (doc 17): one <see cref="ActiveId"/> (the current
/// dataset, or null when nothing is active) plus an ordered <see cref="Comparison"/> set (for
/// before/after and multi-dataset views). Immutable value; the <see cref="Workspace"/> owns the
/// current instance and raises an event when it changes. Replaces the legacy three-way ambiguous
/// current item (tray vs view vs dock, doc 05).
/// </summary>
public sealed record ActiveContext
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

    /// <summary>Value equality over the active id and the ordered comparison set (records compare the list by reference).</summary>
    public bool SameAs(ActiveContext other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || ActiveId != other.ActiveId || Comparison.Count != other.Comparison.Count)
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
}

/// <summary>Carries the previous and current <see cref="ActiveContext"/> when it changes.</summary>
public sealed class ActiveContextChangedEventArgs(ActiveContext previous, ActiveContext current) : EventArgs
{
    public ActiveContext Previous { get; } = previous;

    public ActiveContext Current { get; } = current;
}
