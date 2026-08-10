using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Workspaces;

/// <summary>What to do when removing a dataset that has derived children.</summary>
public enum RemovalPolicy
{
    /// <summary>Refuse to remove a dataset that has children (the caller surfaces the block).</summary>
    Block,

    /// <summary>Remove the dataset and its whole derived subtree.</summary>
    Cascade,
}

/// <summary>
/// Outcome of a remove: either it removed one-or-more datasets (<see cref="RemovedIds"/>), or it was
/// blocked by children (<see cref="BlockedByChildren"/>), or the id was not found. A value, not an
/// exception — "removing a dataset with children" is a normal decision the UI surfaces.
/// </summary>
public sealed record RemoveResult(
    bool Removed,
    IReadOnlyList<DatasetId> RemovedIds,
    IReadOnlyList<DatasetId> BlockedByChildren)
{
    public static RemoveResult NotFound { get; } = new(false, [], []);

    public static RemoveResult Blocked(IReadOnlyList<DatasetId> children) => new(false, [], children);

    public static RemoveResult Succeeded(IReadOnlyList<DatasetId> removed) => new(true, removed, []);
}

/// <summary>
/// The in-memory workspace (doc 16): the datasets in play (originals + derived) and a single explicit
/// <see cref="Active"/> context. Lineage is a <b>view over provenance</b> (<c>Provenance.ParentId</c>,
/// ADR-013) — not a separate UI tree (fixes the legacy fused tray/navigator, doc 05).
/// <para>
/// <b>Ownership:</b> <see cref="Add"/> transfers ownership of the dataset to the workspace; the
/// workspace disposes datasets it removes and disposes all remaining datasets on <see cref="Dispose"/>.
/// UI-free (no WPF/<c>INotifyPropertyChanged</c>) — observability is via plain .NET events.
/// </para>
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly Dictionary<DatasetId, AfmDataset> _byId = [];
    private readonly List<DatasetId> _order = []; // stable insertion order for listing
    private ActiveContext _active = ActiveContext.Empty;
    private bool _disposed;

    /// <summary>Raised after the active context changes (active dataset and/or comparison set).</summary>
    public event EventHandler<ActiveContextChangedEventArgs>? ActiveContextChanged;

    /// <summary>Raised after a dataset is added or removed.</summary>
    public event EventHandler? DatasetsChanged;

    /// <summary>Datasets in insertion order.</summary>
    public IReadOnlyList<AfmDataset> Datasets
    {
        get
        {
            ThrowIfDisposed();
            var list = new List<AfmDataset>(_order.Count);
            foreach (var id in _order)
            {
                list.Add(_byId[id]);
            }

            return list;
        }
    }

    public int Count => _byId.Count;

    /// <summary>The current active context (never null; <see cref="ActiveContext.Empty"/> when nothing is active).</summary>
    public ActiveContext Active
    {
        get
        {
            ThrowIfDisposed();
            return _active;
        }
    }

    public bool Contains(DatasetId id) => _byId.ContainsKey(id);

    public bool TryGet(DatasetId id, out AfmDataset dataset) => _byId.TryGetValue(id, out dataset!);

    /// <summary>Adds a dataset, transferring its ownership to the workspace. Rejects a duplicate id.</summary>
    public void Add(AfmDataset dataset)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dataset);
        if (!_byId.TryAdd(dataset.Id, dataset))
        {
            throw new InvalidOperationException($"A dataset with id '{dataset.Id}' is already in the workspace.");
        }

        _order.Add(dataset.Id);
        DatasetsChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Lineage (a view over provenance ParentId) ---

    /// <summary>The parent dataset id (from provenance), or null if this is a root or the parent isn't in the workspace.</summary>
    public DatasetId? ParentOf(DatasetId id)
    {
        if (_byId.TryGetValue(id, out var dataset)
            && dataset.Provenance.ParentId is { } parent
            && _byId.ContainsKey(parent))
        {
            return parent;
        }

        return null;
    }

    /// <summary>Direct children (datasets whose provenance parent is <paramref name="id"/>), in insertion order.</summary>
    public IReadOnlyList<DatasetId> ChildrenOf(DatasetId id)
    {
        var children = new List<DatasetId>();
        foreach (var childId in _order)
        {
            if (_byId[childId].Provenance.ParentId == id)
            {
                children.Add(childId);
            }
        }

        return children;
    }

    /// <summary>Root datasets: those with no provenance parent present in the workspace, in insertion order.</summary>
    public IReadOnlyList<DatasetId> Roots
    {
        get
        {
            var roots = new List<DatasetId>();
            foreach (var id in _order)
            {
                if (ParentOf(id) is null)
                {
                    roots.Add(id);
                }
            }

            return roots;
        }
    }

    /// <summary>All descendants of <paramref name="id"/> (depth-first), excluding <paramref name="id"/> itself.</summary>
    public IReadOnlyList<DatasetId> DescendantsOf(DatasetId id)
    {
        var result = new List<DatasetId>();
        var stack = new Stack<DatasetId>();
        foreach (var child in ChildrenOf(id))
        {
            stack.Push(child);
        }

        // Guard against pathological cycles (provenance should be acyclic, but never loop forever).
        var visited = new HashSet<DatasetId>();
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);
            foreach (var child in ChildrenOf(current))
            {
                stack.Push(child);
            }
        }

        return result;
    }

    // --- Active context (explicit + observable) ---

    /// <summary>Sets the active dataset (keeping the current comparison set). The id must be in the workspace.</summary>
    public void SetActive(DatasetId id)
    {
        ThrowIfDisposed();
        Require(id);
        UpdateActive(new ActiveContext(id, _active.Comparison));
    }

    /// <summary>Clears the active dataset (keeps the comparison set).</summary>
    public void ClearActive()
    {
        ThrowIfDisposed();
        UpdateActive(new ActiveContext(null, _active.Comparison));
    }

    /// <summary>Replaces the comparison set (keeping the active dataset). Every id must be in the workspace.</summary>
    public void SetComparison(IEnumerable<DatasetId> ids)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        foreach (var id in list)
        {
            Require(id);
        }

        UpdateActive(new ActiveContext(_active.ActiveId, list));
    }

    // --- Removal ---

    /// <summary>
    /// Removes <paramref name="id"/> under the given <paramref name="policy"/> and disposes every removed
    /// dataset. <see cref="RemovalPolicy.Block"/> refuses when the dataset has children; <see
    /// cref="RemovalPolicy.Cascade"/> removes the whole subtree. Clears/updates the active context if a
    /// removed dataset was active or in the comparison set.
    /// </summary>
    public RemoveResult Remove(DatasetId id, RemovalPolicy policy = RemovalPolicy.Block)
    {
        ThrowIfDisposed();
        if (!_byId.ContainsKey(id))
        {
            return RemoveResult.NotFound;
        }

        var children = ChildrenOf(id);
        if (children.Count > 0 && policy == RemovalPolicy.Block)
        {
            return RemoveResult.Blocked(children);
        }

        // Remove id + (for cascade) its descendants. Order: descendants first, then the target.
        var toRemove = new List<DatasetId>();
        if (policy == RemovalPolicy.Cascade)
        {
            toRemove.AddRange(DescendantsOf(id));
        }

        toRemove.Add(id);

        foreach (var removeId in toRemove)
        {
            if (_byId.Remove(removeId, out var dataset))
            {
                _order.Remove(removeId);
                dataset.Dispose(); // the workspace owned it
            }
        }

        PruneActiveContext(toRemove);
        DatasetsChanged?.Invoke(this, EventArgs.Empty);
        return RemoveResult.Succeeded(toRemove);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var dataset in _byId.Values)
        {
            dataset.Dispose();
        }

        _byId.Clear();
        _order.Clear();
        _active = ActiveContext.Empty;
        ActiveContextChanged = null;
        DatasetsChanged = null;
    }

    private void PruneActiveContext(IReadOnlyList<DatasetId> removed)
    {
        var removedSet = new HashSet<DatasetId>(removed);
        var newActiveId = _active.ActiveId is { } a && removedSet.Contains(a) ? (DatasetId?)null : _active.ActiveId;
        var newComparison = _active.Comparison.Where(c => !removedSet.Contains(c)).ToList();
        UpdateActive(new ActiveContext(newActiveId, newComparison));
    }

    private void UpdateActive(ActiveContext next)
    {
        if (_active.SameAs(next))
        {
            return;
        }

        var previous = _active;
        _active = next;
        ActiveContextChanged?.Invoke(this, new ActiveContextChangedEventArgs(previous, next));
    }

    private void Require(DatasetId id)
    {
        if (!_byId.ContainsKey(id))
        {
            throw new ArgumentException($"Dataset '{id}' is not in the workspace.", nameof(id));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
