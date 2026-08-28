using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// TASK-UX10: doc 26 §5 has said since it was written that a line profile's Inspector shows dataset properties.
/// It showed "Select an image" instead — the same defect UX04 closed for a map and a curve, left standing for
/// the one dataset type this product derives most often.
/// </summary>
public sealed class ShellProfileInspectorTests
{
    private static LineProfileDataset Profile(int samples = 8, double step = 2.5)
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("Distance", StandardUnits.Nanometre, 0.0, step, samples),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre, "Height"),
            ScanBuffer<float>.Allocate(samples, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static ShellViewModel NewShell(Workspace ws, MeasurementLine? line = null)
        => new(
            ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(),
            new FakeImageAnalysis { Line = line }, new SpectroscopyParameterPreviewUseCase(), new FakeLauncher(),
            new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ShellViewModel WithActiveProfile(Workspace ws, LineProfileDataset profile, MeasurementLine? line = null)
    {
        var vm = NewShell(ws, line);
        ws.Add(profile);
        ws.SetActive(profile.Id);
        return vm;
    }

    [Fact]
    public void A_profile_is_not_told_to_select_an_image()
    {
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile());

        Assert.True(vm.IsProfile);
        Assert.False(vm.HasNothingToInspect);
    }

    [Fact]
    public void An_empty_workspace_still_has_nothing_to_inspect()
    {
        Assert.True(NewShell(new Workspace()).HasNothingToInspect);
    }

    [Fact]
    public void A_profile_describes_itself_as_one_rather_than_by_its_type_name()
    {
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile());

        Assert.Contains("Profile", vm.ActiveSubtitle);
        Assert.DoesNotContain("LineProfileDataset", vm.ActiveSubtitle);
    }

    [Fact]
    public void The_summary_says_how_far_the_profile_runs()
    {
        // A slice's length is the span from the first sample to the last — seven steps across eight samples,
        // not eight. The same off-by-one the map's grid spacing turns on.
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile(samples: 8, step: 2.5));

        Assert.Equal("8 samples · 17.5 nm", vm.ProfileSummary);
    }

    [Fact]
    public void A_single_sample_spans_nothing()
    {
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile(samples: 1));

        Assert.Equal("1 samples · 0 nm", vm.ProfileSummary);
    }

    [Fact]
    public void The_source_says_where_on_the_image_the_slice_was_taken()
    {
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile(), new MeasurementLine(DatasetId.New(), 1.0, 2.0, 30.0, 40.0));

        Assert.Equal("(1, 2) → (30, 40) px", vm.ProfileSource);
    }

    [Fact]
    public void A_profile_that_was_never_sliced_out_of_anything_says_so()
    {
        // A curve read straight from a file has no source line. Showing coordinates it does not have would be
        // the same invention the map refuses when it has no grid.
        var ws = new Workspace();
        var vm = WithActiveProfile(ws, Profile());

        Assert.Equal("not sampled from an image", vm.ProfileSource);
    }

    [Fact]
    public void Nothing_active_is_not_a_profile()
    {
        var vm = NewShell(new Workspace());

        Assert.False(vm.IsProfile);
        Assert.Equal(string.Empty, vm.ProfileSummary);
    }

    // --- fakes (this project keeps them per-file; see the other Shell* tests) ---

    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public MeasurementLine? Line { get; set; }

        public MeasurementLine? GetCurveSourceLine(DatasetId curveId) => Line;

        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

        public Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(null);

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());

        public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());

        public StatisticsResult? GetMeasurement(DatasetId artifactId) => null;
    }

    private sealed class FakeReader : IScanFileReader
    {
        public bool CanRead(string path) => false;

        public Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken ct = default)
            => Task.FromException<FileReadResult>(new NotImplementedException());
    }

    private sealed class FakeScanPicker : IScanFilePicker
    {
        public string? PickScanFile() => null;
    }

    private sealed class FakeLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();

        public OperationForm? GetForm(string operationId) => null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());
    }

    private sealed class FakePersistence : IWorkspacePersistence
    {
        public PersistenceOutcome Save(string path) => PersistenceOutcome.Ok;

        public PersistenceOutcome Open(string path) => PersistenceOutcome.Ok;
    }

    private sealed class FakePathPicker : IWorkspacePathPicker
    {
        public string? PickSaveFolder() => null;

        public string? PickOpenFolder() => null;
    }

    private sealed class FakePrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesChoice Ask(string workspaceName) => UnsavedChangesChoice.Cancel;
    }
}
