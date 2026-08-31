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

    // Leases and deferred disposals are the ONE part of this type touched from more than one thread: a lease is
    // taken on the caller's thread and released on whichever thread finished the work.
    private readonly object _gate = new();
    private readonly Dictionary<DatasetId, int> _leases = [];
    private readonly List<AfmDataset> _deferred = [];
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

    /// <summary>
    /// Adds a dataset, <b>transferring its ownership</b> to the workspace (the workspace disposes it on
    /// remove/dispose). Rejects a duplicate id and rejects an add that would create a provenance
    /// <b>cycle</b> (self-parent, or completing a loop with datasets already present) — lineage must be
    /// acyclic. On rejection the method throws and does <b>not</b> add or dispose the dataset, so
    /// <b>ownership stays with the caller</b> (the caller must dispose the rejected dataset).
    /// </summary>
    public void Add(AfmDataset dataset)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dataset);

        if (_byId.ContainsKey(dataset.Id))
        {
            throw new InvalidOperationException($"A dataset with id '{dataset.Id}' is already in the workspace.");
        }

        EnsureNoCycle(dataset);

        _byId.Add(dataset.Id, dataset);
        _order.Add(dataset.Id);
        DatasetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rejects an add whose provenance parent chain (through datasets already present) reaches the new id.</summary>
    private void EnsureNoCycle(AfmDataset dataset)
    {
        var target = dataset.Id;
        var walked = new HashSet<DatasetId>();
        var parent = dataset.Provenance.ParentId;
        while (parent is { } p)
        {
            if (p == target)
            {
                throw new InvalidOperationException(
                    $"Adding dataset '{target}' would create a provenance cycle.");
            }

            if (!walked.Add(p) || !_byId.TryGetValue(p, out var parentDataset))
            {
                return; // chain ends (parent absent) or an existing loop we don't extend
            }

            parent = parentDataset.Provenance.ParentId;
        }
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
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Undefined removal policy.");
        }

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
                DisposeOrDefer(dataset); // the workspace owned it
            }
        }

        PruneActiveContext(toRemove);
        DatasetsChanged?.Invoke(this, EventArgs.Empty);
        return RemoveResult.Succeeded(toRemove);
    }

    /// <summary>
    /// Replaces this workspace's contents with <paramref name="source"/>'s, <b>moving</b> ownership of the
    /// datasets and the active context over: the current datasets are disposed, and <paramref name="source"/>
    /// is left <b>empty</b> so disposing it afterwards is a no-op. Used by workspace Open — the store returns
    /// a freshly-restored Workspace, and the session's singleton adopts it in place, keeping every subscriber
    /// bound (no re-wiring of view-models). Lineage is derived from provenance, so it is restored for free.
    /// </summary>
    public void ReplaceWith(Workspace source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, this))
        {
            return;
        }

        source.ThrowIfDisposed();

        // Dispose the datasets this workspace owned, then move source's over (source is emptied so it will
        // not dispose the datasets we just adopted).
        foreach (var dataset in _byId.Values)
        {
            DisposeOrDefer(dataset);
        }

        _byId.Clear();
        _order.Clear();
        foreach (var id in source._order)
        {
            _byId.Add(id, source._byId[id]);
            _order.Add(id);
        }

        var newActive = source._active;
        source._byId.Clear();
        source._order.Clear();
        source._active = ActiveContext.Empty;

        var previous = _active;
        _active = newActive;
        DatasetsChanged?.Invoke(this, EventArgs.Empty);
        ActiveContextChanged?.Invoke(this, new ActiveContextChangedEventArgs(previous, newActive));
    }

    /// <summary>
    /// Keeps the storage of <paramref name="ids"/> alive until the returned handle is disposed.
    /// <para>
    /// The workspace disposes a dataset the moment it is removed, which was safe only while every reader held
    /// the thread that could remove it. An operation now runs off that thread, so a removal can land in the
    /// middle of one — and a <c>ScanBuffer</c> view must not outlive its owner. A cancellation token does not
    /// close this: it asks the reader to stop, it does not wait for it, and <see cref="Remove"/> disposes before
    /// anything downstream has even heard that the active context changed.
    /// </para>
    /// <para>
    /// So a leased dataset is removed from the workspace immediately — it is gone from the UI the moment the
    /// user says so — but its storage is disposed only once the last reader lets go.
    /// </para>
    /// </summary>
    public IDisposable Lease(IEnumerable<DatasetId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var held = ids.Distinct().ToArray();
        lock (_gate)
        {
            foreach (var id in held)
            {
                _leases[id] = _leases.GetValueOrDefault(id) + 1;
            }
        }

        return new Handle(this, held);
    }

    /// <summary>Whether anything is still reading <paramref name="id"/>. For tests and diagnostics.</summary>
    public bool IsLeased(DatasetId id)
    {
        lock (_gate)
        {
            return _leases.ContainsKey(id);
        }
    }

    // Disposes now, or hands the dataset to whoever holds the last lease on it.
    private void DisposeOrDefer(AfmDataset dataset)
    {
        lock (_gate)
        {
            if (_leases.ContainsKey(dataset.Id))
            {
                _deferred.Add(dataset);
                return;
            }
        }

        dataset.Dispose();
    }

    private void Release(IReadOnlyList<DatasetId> ids)
    {
        var due = new List<AfmDataset>();
        lock (_gate)
        {
            foreach (var id in ids)
            {
                int count = _leases.GetValueOrDefault(id);
                if (count <= 1)
                {
                    _leases.Remove(id);
                    for (int i = _deferred.Count - 1; i >= 0; i--)
                    {
                        if (_deferred[i].Id == id)
                        {
                            due.Add(_deferred[i]);
                            _deferred.RemoveAt(i);
                        }
                    }
                }
                else
                {
                    _leases[id] = count - 1;
                }
            }
        }

        // Outside the lock: Dispose is the dataset's business, not this type's, and it must not run under a lock
        // that a reader on another thread is waiting on.
        foreach (var dataset in due)
        {
            dataset.Dispose();
        }
    }

    private sealed class Handle(Workspace workspace, IReadOnlyList<DatasetId> ids) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            workspace.Release(ids);
        }
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
            DisposeOrDefer(dataset);
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
        if (_active.Equals(next))
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
