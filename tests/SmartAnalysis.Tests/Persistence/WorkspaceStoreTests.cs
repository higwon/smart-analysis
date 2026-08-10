using Microsoft.Extensions.DependencyInjection;
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

/// <summary>TASK-P01: workspace save/reopen restores datasets, buffers, provenance lineage, and active context.</summary>
public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly IUnitRegistry _units = StandardUnits.CreateRegistry();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ws_test_{Guid.NewGuid():N}");

    private DirectoryWorkspaceStore NewStore() => new(_units);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ScanImageDataset Original(DatasetId id, float[] pixels)
        => new(
            id,
            new DataSource("psia-tiff", "C:/scan.tiff", "ABCD1234"),
            new Axis("X", StandardUnits.Micrometre, 0.5, 0.1, 2),
            new Axis("Y", StandardUnits.Micrometre, 1.0, 0.2, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.TakeOwnership(pixels, 2, 2),
            new ScanMetadata("NX10", new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), new Dictionary<string, string> { ["scanRate"] = "1.0" }),
            ProvenanceRecord.Root);

    private static ScanImageDataset Derived(DatasetId id, DatasetId parent, float[] pixels)
    {
        var step = new ProvenanceStep(
            stepId: "step-0",
            inputDatasetId: parent,
            inputVersion: 0,
            operationId: "image.flatten",
            operationVersion: 1,
            order: 0,
            environment: new ExecutionEnvironment("1.2.3", "TestOS", "TestMachine", new DateTimeOffset(2026, 8, 10, 9, 5, 0, TimeSpan.Zero)),
            parameters: new Dictionary<string, PhysicalValue> { ["order"] = new(1, StandardUnits.One), ["cutoff"] = new(2.5, StandardUnits.Nanometre) },
            warnings: [new OperationWarning("flatten.note", "example")],
            parentResultId: id);

        return new ScanImageDataset(
            id,
            DataSource.Derived,
            new Axis("X", StandardUnits.Micrometre, 0.5, 0.1, 2),
            new Axis("Y", StandardUnits.Micrometre, 1.0, 0.2, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.TakeOwnership(pixels, 2, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.DerivedFrom(parent, [step]));
    }

    private (DatasetId origId, DatasetId derId) SaveSample(DirectoryWorkspaceStore store)
    {
        var origId = DatasetId.New();
        var derId = DatasetId.New();
        using var ws = new Workspace();
        ws.Add(Original(origId, [1f, 2f, 3f, 4f]));
        ws.Add(Derived(derId, origId, [0.1f, 0.2f, 0.3f, 0.4f]));
        ws.SetComparison([origId, derId]);
        ws.SetActive(derId);
        store.Save(ws, _dir);
        return (origId, derId);
    }

    [Fact]
    public void Round_trip_restores_datasets_buffers_lineage_and_active_context()
    {
        var store = NewStore();
        var (origId, derId) = SaveSample(store);

        var result = store.Open(_dir);

        Assert.True(result.IsSuccess, result.Error?.Message);
        using var ws = result.Workspace!;

        // Datasets + buffers.
        Assert.Equal(2, ws.Count);
        Assert.True(ws.TryGet(origId, out var orig));
        Assert.True(ws.TryGet(derId, out var der));
        var origImg = Assert.IsType<ScanImageDataset>(orig);
        Assert.Equal([1f, 2f, 3f, 4f], origImg.Data.Memory.ToArray());

        // Axes / channel / source / metadata.
        Assert.Equal("um", origImg.X.Unit.Symbol);
        Assert.Equal(0.1, origImg.X.Step, 10);
        Assert.Equal(ChannelKind.Topography, origImg.Channel.Kind);
        Assert.Equal("nm", origImg.Channel.Unit.Symbol);
        Assert.Equal("psia-tiff", origImg.Source.FormatId);
        Assert.Equal("ABCD1234", origImg.Source.ContentHash);
        Assert.Equal("1.0", origImg.Metadata.Extended["scanRate"]);
        Assert.True(origImg.Provenance.IsRoot);

        // Lineage restored from provenance.
        Assert.Equal(origId, ws.ParentOf(derId));
        Assert.Equal([derId], ws.ChildrenOf(origId));
        var step = Assert.Single(der.Provenance.Steps);
        Assert.Equal("image.flatten", step.OperationId);
        Assert.Equal(1.0, step.Parameters["order"].Value);
        Assert.Equal("nm", step.Parameters["cutoff"].Unit.Symbol);   // params keep their units
        Assert.Equal(2.5, step.Parameters["cutoff"].Value, 10);
        Assert.Equal("TestMachine", step.Environment.MachineName);
        Assert.Equal(derId, step.ParentResultId);
        Assert.Single(step.Warnings);

        // Active context restored.
        Assert.Equal(derId, ws.Active.ActiveId);
        Assert.Equal([origId, derId], ws.Active.Comparison);
    }

    [Fact]
    public void Unknown_schema_version_is_a_typed_failure()
    {
        var store = NewStore();
        SaveSample(store);
        var manifest = Path.Combine(_dir, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"1.0.0\"", "\"2.0.0\""));

        var result = store.Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.UnsupportedSchemaVersion, result.Error!.Kind);
    }

    [Fact]
    public void Missing_buffer_is_a_typed_corrupt_failure()
    {
        var store = NewStore();
        SaveSample(store);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(_dir, "buffers")))
        {
            File.Delete(f);
        }

        var result = store.Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public void A_dangling_active_context_reference_is_corrupt_not_silently_dropped()
    {
        var store = NewStore();
        SaveSample(store);
        var manifest = Path.Combine(_dir, "manifest.json");
        // Point the active id at a dataset that isn't in the package.
        File.WriteAllText(manifest, File.ReadAllText(manifest)
            .Replace(SaveSampleActiveIdMarker(manifest), Guid.NewGuid().ToString("D")));

        var result = store.Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.Corrupt, result.Error!.Kind);
    }

    // Reads the persisted ActiveId back out of the manifest so the test can swap it for a dangling id.
    private string SaveSampleActiveIdMarker(string manifestPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        return doc.RootElement.GetProperty("Active").GetProperty("ActiveId").GetString()!;
    }

    [Fact]
    public void A_buffer_with_trailing_bytes_is_corrupt()
    {
        var store = NewStore();
        SaveSample(store);
        var bufferFile = Directory.EnumerateFiles(Path.Combine(_dir, "buffers")).First();
        File.WriteAllBytes(bufferFile, File.ReadAllBytes(bufferFile).Concat(new byte[8]).ToArray());

        var result = store.Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public void A_buffer_file_name_with_a_path_component_is_rejected()
    {
        var store = NewStore();
        SaveSample(store);
        var manifest = Path.Combine(_dir, "manifest.json");
        File.WriteAllText(manifest, System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(manifest), "\"BufferFile\":\\s*\"[^\"]+\"", "\"BufferFile\": \"../escape.bin\"", System.Text.RegularExpressions.RegexOptions.None));

        var result = store.Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.Corrupt, result.Error!.Kind);
    }

    [Fact]
    public void A_directory_without_a_manifest_is_not_a_workspace()
    {
        Directory.CreateDirectory(_dir);
        var result = NewStore().Open(_dir);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.NotAWorkspace, result.Error!.Kind);
    }

    [Fact]
    public void A_missing_directory_is_an_io_failure()
    {
        var result = NewStore().Open(Path.Combine(_dir, "does-not-exist"));

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceOpenErrorKind.Io, result.Error!.Kind);
    }

    [Fact]
    public void A_failed_save_preserves_the_existing_package()
    {
        var store = NewStore();
        var (origId, derId) = SaveSample(store); // a good package already on disk at _dir

        // A workspace that will fail to save (contains an unsupported dataset kind).
        using var doomed = new Workspace();
        doomed.Add(Original(DatasetId.New(), [9f, 9f, 9f, 9f]));
        doomed.Add(new LineProfileDataset(
            DatasetId.New(),
            new DataSource("psia-tiff", "p.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0, 1, 3),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.TakeOwnership([1f, 2f, 3f], 3, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root));

        Assert.Throws<NotSupportedException>(() => store.Save(doomed, _dir));

        // The previously-saved package must still open intact — the failed save touched nothing.
        var result = store.Open(_dir);
        Assert.True(result.IsSuccess, result.Error?.Message);
        using var ws = result.Workspace!;
        Assert.Equal(2, ws.Count);
        Assert.True(ws.Contains(origId));
        Assert.True(ws.Contains(derId));
        Assert.Equal(derId, ws.Active.ActiveId);
        Assert.Equal([1f, 2f, 3f, 4f], ((ScanImageDataset)ws.Datasets.Single(d => d.Id == origId)).Data.Memory.ToArray());
    }

    [Fact]
    public void Overwriting_an_existing_workspace_replaces_it_cleanly()
    {
        var store = NewStore();
        SaveSample(store);

        // Save a different (smaller) workspace to the same path.
        var soloId = DatasetId.New();
        using (var ws2 = new Workspace())
        {
            ws2.Add(Original(soloId, [7f, 8f, 9f, 10f]));
            store.Save(ws2, _dir);
        }

        var result = store.Open(_dir);
        Assert.True(result.IsSuccess, result.Error?.Message);
        using var reopened = result.Workspace!;
        Assert.Equal(1, reopened.Count); // no stale datasets/buffers from the previous save
        Assert.True(reopened.Contains(soloId));
    }

    [Fact]
    public void Registers_via_di_and_binds_the_port()
    {
        using var provider = new ServiceCollection().AddWorkspaceStore().BuildServiceProvider();

        Assert.IsType<DirectoryWorkspaceStore>(provider.GetRequiredService<IWorkspaceStore>());
        Assert.NotNull(provider.GetService<IUnitRegistry>());
    }
}
