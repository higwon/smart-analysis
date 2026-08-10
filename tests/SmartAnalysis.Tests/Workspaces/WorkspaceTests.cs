using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using Xunit;

namespace SmartAnalysis.Tests.Workspaces;

/// <summary>TASK-W01: the in-memory workspace + single explicit active context (doc 16/17).</summary>
public sealed class WorkspaceTests
{
    private static ScanImageDataset Image(DatasetId id, DatasetId? parent = null)
    {
        var provenance = parent is { } p
            ? ProvenanceRecord.DerivedFrom(p, [new ProvenanceStep("s0", p, 0, "test.derive", 1, 0, ExecutionEnvironment.Unknown)])
            : ProvenanceRecord.Root;

        return new ScanImageDataset(
            id,
            parent is null ? new DataSource("psia-tiff", "file.tiff") : DataSource.Derived,
            new Axis("X", StandardUnits.Nanometre, 0, 1, 2),
            new Axis("Y", StandardUnits.Nanometre, 0, 1, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(2, 2),
            ScanMetadata.Unknown,
            provenance);
    }

    // --- Add / identity ---

    [Fact]
    public void Add_holds_datasets_in_insertion_order()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        var b = DatasetId.New();
        ws.Add(Image(a));
        ws.Add(Image(b));

        Assert.Equal(2, ws.Count);
        Assert.True(ws.Contains(a));
        Assert.Equal([a, b], ws.Datasets.Select(d => d.Id));
    }

    [Fact]
    public void Add_rejects_duplicate_id()
    {
        using var ws = new Workspace();
        var id = DatasetId.New();
        ws.Add(Image(id));
        Assert.Throws<InvalidOperationException>(() => ws.Add(Image(id)));
    }

    [Fact]
    public void Add_failure_leaves_ownership_with_the_caller()
    {
        using var ws = new Workspace();
        var id = DatasetId.New();
        ws.Add(Image(id));

        var rejected = Image(id); // duplicate id → Add must reject
        Assert.Throws<InvalidOperationException>(() => ws.Add(rejected));

        // The rejected dataset was NOT taken over by the workspace → still usable; the caller disposes it.
        Assert.Equal(4, rejected.Data.Memory.Length);
        rejected.Dispose();
    }

    // --- Lineage must stay acyclic ---

    [Fact]
    public void Add_rejects_a_self_parent_cycle()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        var selfParented = Image(a, parent: a);
        Assert.Throws<InvalidOperationException>(() => ws.Add(selfParented));
        selfParented.Dispose(); // caller still owns it
    }

    [Fact]
    public void Add_rejects_a_cycle_completed_by_a_new_parent()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        var b = DatasetId.New();
        ws.Add(Image(a, parent: b)); // A derived-from B; B absent → A is a root for now
        var loop = Image(b, parent: a); // B derived-from A → adding B closes the loop
        Assert.Throws<InvalidOperationException>(() => ws.Add(loop));
        loop.Dispose();
    }

    [Fact]
    public void Add_allows_a_linear_chain()
    {
        using var ws = new Workspace();
        var root = DatasetId.New();
        var child = DatasetId.New();
        var grandchild = DatasetId.New();
        ws.Add(Image(root));
        ws.Add(Image(child, parent: root));
        ws.Add(Image(grandchild, parent: child)); // no cycle
        Assert.Equal(3, ws.Count);
    }

    // --- Lineage as a view over provenance ---

    [Fact]
    public void Lineage_is_derived_from_provenance_parent()
    {
        using var ws = new Workspace();
        var root = DatasetId.New();
        var child = DatasetId.New();
        var grandchild = DatasetId.New();
        ws.Add(Image(root));
        ws.Add(Image(child, parent: root));
        ws.Add(Image(grandchild, parent: child));

        Assert.Equal([root], ws.Roots);
        Assert.Equal([child], ws.ChildrenOf(root));
        Assert.Equal(root, ws.ParentOf(child));
        Assert.Equal([child, grandchild], ws.DescendantsOf(root));
        Assert.Empty(ws.ChildrenOf(grandchild));
    }

    [Fact]
    public void A_derived_dataset_whose_parent_is_absent_is_a_root()
    {
        using var ws = new Workspace();
        var child = DatasetId.New();
        ws.Add(Image(child, parent: DatasetId.New())); // parent not added

        Assert.Equal([child], ws.Roots);
        Assert.Null(ws.ParentOf(child));
    }

    // --- Active context (explicit + observable) ---

    [Fact]
    public void SetActive_updates_context_and_raises_event_once()
    {
        using var ws = new Workspace();
        var id = DatasetId.New();
        ws.Add(Image(id));

        int events = 0;
        ActiveContext? seen = null;
        ws.ActiveContextChanged += (_, e) => { events++; seen = e.Current; };

        ws.SetActive(id);
        ws.SetActive(id); // no change → no second event

        Assert.Equal(1, events);
        Assert.Equal(id, ws.Active.ActiveId);
        Assert.Equal(id, seen!.ActiveId);
    }

    [Fact]
    public void SetActive_rejects_unknown_id()
    {
        using var ws = new Workspace();
        Assert.Throws<ArgumentException>(() => ws.SetActive(DatasetId.New()));
    }

    [Fact]
    public void SetComparison_keeps_active_and_dedupes()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        var b = DatasetId.New();
        ws.Add(Image(a));
        ws.Add(Image(b));
        ws.SetActive(a);

        ws.SetComparison([a, b, a]);

        Assert.Equal(a, ws.Active.ActiveId);
        Assert.Equal([a, b], ws.Active.Comparison);
    }

    [Fact]
    public void ClearActive_keeps_comparison()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        ws.Add(Image(a));
        ws.SetActive(a);
        ws.SetComparison([a]);

        ws.ClearActive();

        Assert.Null(ws.Active.ActiveId);
        Assert.Equal([a], ws.Active.Comparison);
    }

    // --- Removal policy ---

    [Fact]
    public void Remove_block_refuses_when_children_exist()
    {
        using var ws = new Workspace();
        var root = DatasetId.New();
        var child = DatasetId.New();
        ws.Add(Image(root));
        ws.Add(Image(child, parent: root));

        var result = ws.Remove(root); // Block by default

        Assert.False(result.Removed);
        Assert.Equal([child], result.BlockedByChildren);
        Assert.Equal(2, ws.Count); // nothing removed
    }

    [Fact]
    public void Remove_cascade_removes_and_disposes_the_subtree()
    {
        using var ws = new Workspace();
        var root = DatasetId.New();
        var child = DatasetId.New();
        var rootDataset = Image(root);
        var childDataset = Image(child, parent: root);
        ws.Add(rootDataset);
        ws.Add(childDataset);

        var result = ws.Remove(root, RemovalPolicy.Cascade);

        Assert.True(result.Removed);
        Assert.Equal(2, result.RemovedIds.Count);
        Assert.Contains(root, result.RemovedIds);
        Assert.Contains(child, result.RemovedIds);
        Assert.Equal(0, ws.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = rootDataset.Data.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = childDataset.Data.Memory);
    }

    [Fact]
    public void Remove_prunes_active_and_comparison_and_raises_event()
    {
        using var ws = new Workspace();
        var a = DatasetId.New();
        var b = DatasetId.New();
        ws.Add(Image(a));
        ws.Add(Image(b));
        ws.SetActive(a);
        ws.SetComparison([a, b]);

        int activeEvents = 0;
        ws.ActiveContextChanged += (_, _) => activeEvents++;

        var result = ws.Remove(a);

        Assert.True(result.Removed);
        Assert.Null(ws.Active.ActiveId);       // a was active → cleared
        Assert.Equal([b], ws.Active.Comparison); // a pruned from comparison
        Assert.Equal(1, activeEvents);
    }

    [Fact]
    public void Remove_unknown_id_is_not_found()
    {
        using var ws = new Workspace();
        Assert.Same(RemoveResult.NotFound, ws.Remove(DatasetId.New()));
    }

    [Fact]
    public void Remove_rejects_an_undefined_policy()
    {
        using var ws = new Workspace();
        var id = DatasetId.New();
        ws.Add(Image(id));
        Assert.Throws<ArgumentOutOfRangeException>(() => ws.Remove(id, (RemovalPolicy)999));
        Assert.Equal(1, ws.Count); // not removed
    }

    // --- ActiveContext value equality ---

    [Fact]
    public void ActiveContext_has_structural_equality()
    {
        var x = DatasetId.New();
        var y = DatasetId.New();

        Assert.Equal(new ActiveContext(x, [x, y]), new ActiveContext(x, [x, y]));
        Assert.True(new ActiveContext(x, [x, y]) == new ActiveContext(x, [x, y]));
        Assert.Equal(new ActiveContext(x, [x, y]).GetHashCode(), new ActiveContext(x, [x, y]).GetHashCode());
        Assert.NotEqual(new ActiveContext(x, [x, y]), new ActiveContext(x, [y, x])); // order matters
        Assert.NotEqual(new ActiveContext(x, []), new ActiveContext(y, []));
    }

    // --- Disposal ---

    [Fact]
    public void Dispose_disposes_all_datasets()
    {
        var ws = new Workspace();
        var dataset = Image(DatasetId.New());
        ws.Add(dataset);

        ws.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = dataset.Data.Memory);
        Assert.Throws<ObjectDisposedException>(() => ws.SetActive(DatasetId.New()));
    }
}
