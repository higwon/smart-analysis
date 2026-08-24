using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// The re-select path: selecting an attached measurement node fills the Result role with the measurement's readouts
/// (the use case's projection is verified separately in the core suite; this pins the shell wiring).
/// </summary>
public sealed class ShellMeasurementSelectTests
{
    [Fact]
    public void Selecting_a_measurement_fills_the_result_role_with_readouts()
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        var vm = new ShellViewModel(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
            new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

        vm.SelectMeasurement(DatasetId.New()); // the exact call the tree makes for a measurement node

        Assert.True(vm.RoleIsResult);                 // Inspector switched to the MEASUREMENT card
        Assert.NotNull(vm.Statistics);
        Assert.NotEmpty(vm.Statistics!.Readouts);     // and it is NOT an empty card
        Assert.Contains(vm.Statistics.Readouts, r => r.Name == "Sq");
    }

    [Fact]
    public void Selecting_a_region_measurement_offers_its_region_on_the_active_image()
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(image.Id, RegionOverlayShape.Ellipse, 2, 3, 6, 4) };
        var vm = NewShell(ws, analysis);

        vm.SelectMeasurement(DatasetId.New());

        Assert.NotNull(vm.SelectedRegion);            // the "this came from here" overlay is offered
        Assert.Equal(RegionOverlayShape.Ellipse, vm.SelectedRegion!.Shape);
        Assert.Equal((2, 3, 6, 4), (vm.SelectedRegion.Left, vm.SelectedRegion.Top, vm.SelectedRegion.Width, vm.SelectedRegion.Height));
    }

    [Fact]
    public void A_region_measurement_for_another_image_offers_no_overlay()
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        // The recorded region belongs to a DIFFERENT image than the active one → its pixel bounds don't map here.
        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(DatasetId.New(), RegionOverlayShape.Rectangle, 2, 3, 6, 4) };
        var vm = NewShell(ws, analysis);

        vm.SelectMeasurement(DatasetId.New());

        Assert.Null(vm.SelectedRegion);
    }

    [Fact]
    public void Activating_a_dataset_clears_a_selected_region()
    {
        var ws = new Workspace();
        var image = Image();
        var other = Image();
        ws.Add(image);
        ws.Add(other);
        ws.SetActive(image.Id);

        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(image.Id, RegionOverlayShape.Rectangle, 1, 1, 3, 3) };
        var vm = NewShell(ws, analysis);
        vm.SelectMeasurement(DatasetId.New());
        Assert.NotNull(vm.SelectedRegion);

        ws.SetActive(other.Id); // moving off the source image drops the overlay
        Assert.Null(vm.SelectedRegion);
    }

    [Fact]
    public void Re_selecting_the_active_dataset_node_clears_a_selected_region()
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(image.Id, RegionOverlayShape.Rectangle, 1, 1, 3, 3) };
        var vm = NewShell(ws, analysis);
        vm.SelectMeasurement(DatasetId.New());
        Assert.NotNull(vm.SelectedRegion);

        // Re-selecting the (already-active) source image node is a transition out of the measurement selection, so the
        // overlay must drop even though the active dataset is unchanged (SetActive is a no-op for the same id).
        var node = vm.ExplorerNodes.Single(n => n.Id == image.Id);
        vm.Select(node);

        Assert.Null(vm.SelectedRegion);
        Assert.Equal(InspectorRole.DatasetProperties, vm.InspectorRole); // and the Result card yields back to properties
    }

    [Fact]
    public void Selecting_a_region_measurement_from_another_image_switches_to_its_source()
    {
        var ws = new Workspace();
        var source = Image();
        var other = Image();
        ws.Add(source);
        ws.Add(other);
        ws.SetActive(other.Id); // active is NOT the measurement's source

        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(source.Id, RegionOverlayShape.Rectangle, 2, 3, 6, 4) };
        var vm = NewShell(ws, analysis);

        vm.SelectMeasurement(DatasetId.New());

        Assert.Equal(source.Id, ws.Active.ActiveId); // switched to the image the region belongs to, so it can be drawn
        Assert.NotNull(vm.SelectedRegion);           // and the overlay now shows (source is active)
        Assert.True(vm.RoleIsResult);                // still showing the measurement result
    }

    [Fact]
    public void Running_a_measure_form_closes_it_and_shows_its_region()
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        var launcher = new MeasureRunLauncher();
        var analysis = new FakeImageAnalysis { Region = new MeasurementRegion(image.Id, RegionOverlayShape.Rectangle, 1, 1, 4, 4) };
        var vm = new ShellViewModel(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), analysis,
            launcher, new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

        vm.LauncherItems.Single(i => i.Id == "image.grains").LaunchCommand.Execute(null);
        var form = Assert.IsType<ParameterFormViewModel>(vm.OperationEditor);
        form.ApplyCommand.Execute(null); // a completed fake run continues synchronously → OnGenericRunCompleted

        Assert.Null(vm.OperationEditor);   // the Measure form closes so its draggable region overlay can't linger
        Assert.True(vm.RoleIsResult);
        Assert.NotNull(vm.SelectedRegion); // and the just-run measurement's region shows on the active image
    }

    private static ShellViewModel NewShell(Workspace ws, IImageAnalysisUseCase analysis)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), analysis,
            new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ScanImageDataset Image()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

    // Stands in for the use case's re-read: returns a roi-statistics-style result with readouts (the real projection
    // is covered by ImageAnalysisUseCaseTests.GetMeasurement_re_reads_a_non_statistics_measurement_in_full).
    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public MeasurementRegion? Region { get; set; }

        public MeasurementRegion? GetMeasurementRegion(DatasetId artifactId) => Region;

        public StatisticsResult? GetMeasurement(DatasetId artifactId)
            => new(true, "Cheese(1)", new[] { new StatisticsReadout("Sq", 1.0, "nm"), new StatisticsReadout("Pixel Count", 42, "1") }, Array.Empty<int>(), null);

        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());
        public Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(null);
        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());
        public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());
    }

    private sealed class FakeReader : IScanFileReader
    {
        public bool CanRead(string path) => false;
        public Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken ct)
            => Task.FromException<FileReadResult>(new NotImplementedException());
    }

    private sealed class FakeScanPicker : IScanFilePicker { public string? PickScanFile() => null; }

    private sealed class FakeLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();
        public OperationForm? GetForm(string operationId) => null;
        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());
    }

    // A launcher offering one generic Measure op whose run completes synchronously with an attached measurement id.
    private sealed class MeasureRunLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() =>
            [new OperationLauncherItem("image.grains", "Grains", "Detect grains", OperationCategory.Measure)];

        public OperationForm? GetForm(string operationId) => operationId == "image.grains"
            ? new OperationForm("image.grains", "Grains", "Detect grains", OperationCategory.Measure, Array.Empty<ParameterFieldDescriptor>())
            : null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromResult(OperationRunResult.Measured(
                new StatisticsResult(true, "img", new[] { new StatisticsReadout("Count", 1, "1") }, Array.Empty<int>(), null),
                Array.Empty<string>(), DatasetId.New()));
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
