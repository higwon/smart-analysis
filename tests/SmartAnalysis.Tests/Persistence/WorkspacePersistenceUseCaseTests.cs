using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Infrastructure.Persistence.Workspace;
using Xunit;

namespace SmartAnalysis.Tests.Persistence;

/// <summary>
/// P01-UI: <see cref="Workspace.ReplaceWith"/> (the in-place adopt used by Open) and the
/// <see cref="WorkspacePersistenceUseCase"/> save/open flow over the real directory store.
/// </summary>
public sealed class WorkspacePersistenceUseCaseTests : IDisposable
{
    private readonly IUnitRegistry _units = StandardUnits.CreateRegistry();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ws_p01ui_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private DirectoryWorkspaceStore NewStore() => new(_units);

    private static ScanImageDataset Root(DatasetId id)
        => new(
            id,
            new DataSource("psia-tiff", "C:/scan.tiff", "ABCD1234"),
            new Axis("X", StandardUnits.Micrometre, 0.5, 0.1, 2),
            new Axis("Y", StandardUnits.Micrometre, 1.0, 0.2, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.TakeOwnership([1f, 2f, 3f, 4f], 2, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static ScanImageDataset Derived(DatasetId id, DatasetId parent)
    {
        var step = new ProvenanceStep(
            stepId: "step-0",
            inputDatasetId: parent,
            inputVersion: 0,
            operationId: "image.flatten",
            operationVersion: 1,
            order: 0,
            environment: new ExecutionEnvironment("1.0.0", "os", "machine", DateTimeOffset.UnixEpoch),
            parameters: new Dictionary<string, PhysicalValue> { ["order"] = new(1, StandardUnits.One) },
            warnings: [],
            parentResultId: id);
        return new ScanImageDataset(
            id, DataSource.Derived,
            new Axis("X", StandardUnits.Micrometre, 0.5, 0.1, 2),
            new Axis("Y", StandardUnits.Micrometre, 1.0, 0.2, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.TakeOwnership([5f, 6f, 7f, 8f], 2, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.DerivedFrom(parent, [step]));
    }

    [Fact]
    public void ReplaceWith_moves_datasets_active_and_lineage_and_empties_the_source()
    {
        using var target = new Workspace();
        target.Add(Root(DatasetId.New())); // the old content — must be replaced

        var source = new Workspace();
        var rootId = DatasetId.New();
        var derivedId = DatasetId.New();
        source.Add(Root(rootId));
        source.Add(Derived(derivedId, rootId));
        source.SetActive(derivedId);
        source.SetComparison([rootId]);

        int datasetsChanged = 0, activeChanged = 0;
        target.DatasetsChanged += (_, _) => datasetsChanged++;
        target.ActiveContextChanged += (_, _) => activeChanged++;

        target.ReplaceWith(source);

        Assert.Equal(2, target.Count);
        Assert.True(target.Contains(rootId));
        Assert.True(target.Contains(derivedId));
        Assert.Equal(derivedId, target.Active.ActiveId);
        Assert.Contains(rootId, target.Active.Comparison);
        Assert.Contains(derivedId, target.ChildrenOf(rootId)); // lineage restored from provenance
        Assert.Equal(0, source.Count);
        Assert.Equal(1, datasetsChanged);
        Assert.Equal(1, activeChanged);

        source.Dispose(); // emptied → no-op, must not dispose the datasets target now owns
        Assert.True(target.TryGet(derivedId, out _));
    }

    [Fact]
    public void Save_then_Open_round_trips_the_workspace_into_the_session()
    {
        var id = DatasetId.New();
        using (var saved = new Workspace())
        {
            saved.Add(Root(id));
            saved.SetActive(id);
            var save = new WorkspacePersistenceUseCase(saved, NewStore()).Save(_dir);
            Assert.True(save.Success, save.Error);
        }

        using var session = new Workspace();
        var open = new WorkspacePersistenceUseCase(session, NewStore()).Open(_dir);

        Assert.True(open.Success, open.Error);
        Assert.Equal(1, session.Count);
        Assert.True(session.Contains(id));
        Assert.Equal(id, session.Active.ActiveId);
    }

    [Fact]
    public void Open_a_non_workspace_path_returns_a_typed_failure_and_leaves_the_session_untouched()
    {
        using var session = new Workspace();
        session.Add(Root(DatasetId.New()));

        var outcome = new WorkspacePersistenceUseCase(session, NewStore())
            .Open(Path.Combine(Path.GetTempPath(), $"not_a_ws_{Guid.NewGuid():N}"));

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal(1, session.Count); // unchanged
    }
}
