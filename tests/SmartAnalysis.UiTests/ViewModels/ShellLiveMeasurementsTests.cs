using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
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
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// Inline basic measurements: activating an image shows its statistics directly on the default (Dataset) Inspector
/// — read on the main screen, not run from Analyze — and Statistics is dropped from the Analyze launcher.
/// </summary>
public sealed class ShellLiveMeasurementsTests
{
    private static ShellViewModel NewShell(Workspace ws)
        => NewShell(ws, new FakeImageAnalysis());

    private static ShellViewModel NewShell(Workspace ws, IImageAnalysisUseCase analysis)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), analysis,
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ShellViewModel NewShell(Workspace ws, IOperationLauncher launcher)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
               launcher, new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ImageRenderInput MakeInput()
        => RenderInputFactory.ForImage(Image(), Colormap.Grayscale, range: null);

    private static ScanImageDataset Image()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static LineProfileDataset Curve()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("Frequency", StandardUnits.PerMetre, 1.0, 1.0, 8),
            new ChannelDescriptor("psd", ChannelKind.Unknown, StandardUnits.One, "PSD"),
            ScanBuffer<float>.Allocate(8, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

    [Fact]
    public async Task An_active_image_shows_inline_measurements()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        await vm.LiveMeasurementsSettled; // the auto-computation is async

        Assert.True(vm.HasLiveMeasurements);
        Assert.Contains(vm.LiveMeasurements!.Readouts, r => r.Name == "Sq (RMS)");
        Assert.Equal(InspectorRole.DatasetProperties, vm.InspectorRole); // shown on the default panel, not a Result card
    }

    [Fact]
    public void Statistics_is_not_offered_as_an_analyze_action()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        Assert.DoesNotContain(vm.LauncherItems, i => i.Id == "image.statistics"); // it's inline instead
        Assert.Contains(vm.LauncherItems, i => i.Id == "image.flatten");           // other ops still offered
    }

    [Fact]
    public async Task A_non_image_active_dataset_has_no_inline_measurements()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        var curve = Curve();
        ws.Add(image);
        ws.Add(curve);

        ws.SetActive(image.Id);
        await vm.LiveMeasurementsSettled;
        Assert.True(vm.HasLiveMeasurements);

        ws.SetActive(curve.Id); // a curve is not an image → the inline measurements clear
        await vm.LiveMeasurementsSettled;
        Assert.False(vm.HasLiveMeasurements);
    }

    [Fact]
    public async Task Opening_the_flatten_editor_enters_a_source_vs_preview_compare()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        vm.LauncherItems.Single(i => i.Id == "image.flatten").LaunchCommand.Execute(null); // open the Flatten editor
        await vm.OperationPreviewSettled;

        Assert.True(vm.IsOperationPreview);
        Assert.True(vm.ShowComparePanes);
        Assert.False(vm.ShowSingle2D);              // the single stage yields to the preview split
        Assert.NotNull(vm.OperationPreviewInput);     // the uncommitted preview result (AFTER pane)
        Assert.Equal("SOURCE", vm.CompareBeforeLabel);
        Assert.Equal("PREVIEW", vm.CompareAfterLabel);
        Assert.Equal(1, ws.Count);                  // preview committed nothing
    }

    [Fact]
    public async Task Leaving_the_flatten_editor_returns_to_the_single_stage()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var a = Image();
        var b = Image();
        ws.Add(a);
        ws.Add(b);
        ws.SetActive(a.Id);

        vm.LauncherItems.Single(i => i.Id == "image.flatten").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;
        Assert.True(vm.IsOperationPreview);

        ws.SetActive(b.Id); // a new active dataset closes the editor → back to the single stage
        Assert.False(vm.IsOperationPreview);
        Assert.False(vm.ShowComparePanes);
        Assert.Null(vm.OperationPreviewInput);
    }

    [Fact]
    public async Task Opening_a_generic_process_form_enters_a_source_vs_preview_compare()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        vm.LauncherItems.Single(i => i.Id == "image.deglitch").LaunchCommand.Execute(null); // a generic Process op
        await vm.OperationPreviewSettled;

        Assert.True(vm.IsOperationPreview);         // the preview extends beyond the semantic Flatten editor
        Assert.True(vm.ShowComparePanes);
        Assert.False(vm.ShowSingle2D);
        Assert.NotNull(vm.OperationPreviewInput);
        Assert.Equal("SOURCE", vm.CompareBeforeLabel);
        Assert.Equal("PREVIEW", vm.CompareAfterLabel);
        Assert.Equal(1, ws.Count);                  // preview committed nothing
    }

    [Fact]
    public async Task Opening_a_generic_measure_form_stays_on_the_single_stage()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        vm.LauncherItems.Single(i => i.Id == "image.grains").LaunchCommand.Execute(null); // a Measure op derives no image
        await vm.OperationPreviewSettled;

        Assert.False(vm.IsOperationPreview);        // no source-vs-preview compare for a measurement
        Assert.False(vm.ShowComparePanes);
    }

    [Fact]
    public async Task Opening_an_image_to_curve_process_form_stays_on_the_single_stage()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        // Power Spectral Density is a Process op (OutputKind.DerivedDataset) but derives a CURVE, not an image — so it
        // must NOT enter the SOURCE/PREVIEW compare mode (that would open an empty PREVIEW pane). DerivesImage is false.
        vm.LauncherItems.Single(i => i.Id == "image.psd").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;

        Assert.False(vm.IsOperationPreview);        // Process ≠ derives-an-image
        Assert.False(vm.ShowComparePanes);
        Assert.True(vm.ShowSingle2D);               // the single image stage is retained
        Assert.Equal(1, ws.Count);                  // workspace/active unchanged
        Assert.Equal(image.Id, ws.Active.ActiveId);
    }

    [Fact]
    public async Task A_stale_preview_completing_late_does_not_overwrite_the_newest()
    {
        var ws = new Workspace();
        var launcher = new QueuedPreviewLauncher();
        var vm = NewShell(ws, launcher);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        vm.LauncherItems.Single(i => i.Id == "image.deglitch").LaunchCommand.Execute(null); // request #1 (open)
        var firstSettle = vm.OperationPreviewSettled;

        var form = Assert.IsType<ParameterFormViewModel>(vm.OperationEditor);
        form.Fields.Single(f => f.Name == "threshold").Value = 5.0; // request #2 supersedes #1 (same active image)
        var secondSettle = vm.OperationPreviewSettled;

        Assert.Equal(2, launcher.Pending.Count);
        var older = MakeInput();
        var newer = MakeInput();

        launcher.Pending[1].SetResult(newer); // the NEWEST request finishes first
        await secondSettle;
        Assert.Same(newer, vm.OperationPreviewInput);

        launcher.Pending[0].SetResult(older); // the stale request finishes LATE
        await firstSettle;
        Assert.Same(newer, vm.OperationPreviewInput); // its result is dropped by the generation guard — newest stands
    }

    [Fact]
    public async Task Changing_a_generic_process_field_recomputes_the_preview()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        vm.LauncherItems.Single(i => i.Id == "image.deglitch").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;
        var first = vm.OperationPreviewInput;

        var form = Assert.IsType<ParameterFormViewModel>(vm.OperationEditor);
        form.Fields.Single(f => f.Name == "threshold").Value = 5.0; // a setting change re-runs the uncommitted preview
        await vm.OperationPreviewSettled;

        Assert.NotNull(vm.OperationPreviewInput);
        Assert.NotSame(first, vm.OperationPreviewInput); // a fresh preview was computed for the new setting
    }

    [Fact]
    public async Task A_late_failure_for_a_replaced_image_does_not_clear_the_current_measurements()
    {
        var ws = new Workspace();
        var analysis = new ControlledImageAnalysis();
        var vm = NewShell(ws, analysis);
        var a = Image();
        var b = Image();
        ws.Add(a);
        ws.Add(b);

        ws.SetActive(a.Id);            // starts A's (still-pending) computation
        var taskA = vm.LiveMeasurementsSettled;
        ws.SetActive(b.Id);            // clears the panel, starts B's computation
        var taskB = vm.LiveMeasurementsSettled;

        analysis.Succeed(b.Id, "B-stat"); // B (the current image) completes first → shown
        await taskB;
        Assert.True(vm.HasLiveMeasurements);
        Assert.Contains(vm.LiveMeasurements!.Readouts, r => r.Name == "B-stat");

        analysis.Fail(a.Id);          // A (a since-replaced image) fails late
        await taskA;

        Assert.True(vm.HasLiveMeasurements); // B's measurements survive — the stale failure must not clear them
        Assert.Contains(vm.LiveMeasurements!.Readouts, r => r.Name == "B-stat");
    }

    // A per-id, test-controlled statistics use case: each request stays pending until Succeed/Fail is called.
    private sealed class ControlledImageAnalysis : IImageAnalysisUseCase
    {
        private readonly Dictionary<DatasetId, TaskCompletionSource<StatisticsResult>> _pending = new();

        public void Succeed(DatasetId id, string readoutName)
            => _pending[id].SetResult(new StatisticsResult(true, "img", new[] { new StatisticsReadout(readoutName, 1.0, "nm") }, Array.Empty<int>(), null));

        public void Fail(DatasetId id) => _pending[id].SetException(new InvalidOperationException("stale"));

        public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<StatisticsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[sourceId] = tcs;
            return tcs.Task;
        }

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
            => ComputeStatisticsPreviewAsync(sourceId, ct);

        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

        public Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(null);

        public StatisticsResult? GetMeasurement(DatasetId artifactId) => null;
    }

    // ---- fakes ----
    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

        public Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(RenderInputFactory.ForImage(Image(), colormap, range)); // a canned preview

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default) => Result();

        public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken ct = default) => Result();

        private static Task<StatisticsResult> Result()
            => Task.FromResult(new StatisticsResult(
                true, "img",
                new[] { new StatisticsReadout("Sq (RMS)", 1.23, "nm"), new StatisticsReadout("Sa", 0.98, "nm") },
                new[] { 3, 1, 2 }, null));

        public StatisticsResult? GetMeasurement(DatasetId artifactId) => null;
    }

    private sealed class FakeLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() =>
        [
            new OperationLauncherItem("image.statistics", "Statistics", "Basic stats", OperationCategory.Measure),
            new OperationLauncherItem("image.flatten", "Flatten", "Level the surface", OperationCategory.Process),
            new OperationLauncherItem("image.deglitch", "Deglitch", "Remove spikes", OperationCategory.Process),
            new OperationLauncherItem("image.grains", "Grains", "Detect grains", OperationCategory.Measure),
            new OperationLauncherItem("image.psd", "Power Spectral Density", "Image → curve", OperationCategory.Process),
        ];

        // A generic image→image Process form (DerivesImage), an image→curve Process form (Process but NOT DerivesImage),
        // and a generic Measure form; unknown ids have no form.
        public OperationForm? GetForm(string operationId) => operationId switch
        {
            "image.deglitch" => new OperationForm("image.deglitch", "Deglitch", "Remove spikes", OperationCategory.Process,
                [new ParameterFieldDescriptor("threshold", "Threshold", ParameterFieldKind.Number, 1.0, 0.0, null, Array.Empty<ParameterFieldOption>(), null, "help")], DerivesImage: true),
            "image.psd" => new OperationForm("image.psd", "Power Spectral Density", "Image → curve", OperationCategory.Process,
                [new ParameterFieldDescriptor("window", "Window", ParameterFieldKind.Number, 1.0, 0.0, null, Array.Empty<ParameterFieldOption>(), null, "help")], DerivesImage: false),
            "image.grains" => new OperationForm("image.grains", "Grains", "Detect grains", OperationCategory.Measure,
                [new ParameterFieldDescriptor("minArea", "Min Area", ParameterFieldKind.Number, 1.0, 0.0, null, Array.Empty<ParameterFieldOption>(), null, "help")]),
            _ => null,
        };

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());

        // An image→image Process op previews a (fresh, canned) image; an image→curve op or a Measure op derives no
        // image → nothing to compare (the shell won't even call this for those, but mirror the real contract).
        public Task<ImageRenderInput?> PreviewAsync(string operationId, IReadOnlyDictionary<string, object?> values, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(operationId == "image.deglitch"
                ? RenderInputFactory.ForImage(Image(), colormap, range)
                : null);
    }

    // A launcher whose image→image preview completion is controlled by the test (to force out-of-order finishes).
    private sealed class QueuedPreviewLauncher : IOperationLauncher
    {
        public List<TaskCompletionSource<ImageRenderInput?>> Pending { get; } = new();

        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() =>
            [new OperationLauncherItem("image.deglitch", "Deglitch", "Remove spikes", OperationCategory.Process)];

        public OperationForm? GetForm(string operationId) => operationId == "image.deglitch"
            ? new OperationForm("image.deglitch", "Deglitch", "Remove spikes", OperationCategory.Process,
                [new ParameterFieldDescriptor("threshold", "Threshold", ParameterFieldKind.Number, 1.0, 0.0, null, Array.Empty<ParameterFieldOption>(), null, "help")], DerivesImage: true)
            : null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());

        public Task<ImageRenderInput?> PreviewAsync(string operationId, IReadOnlyDictionary<string, object?> values, Colormap colormap, ValueRange? range, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ImageRenderInput?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(tcs);
            return tcs.Task;
        }
    }

    private sealed class FakeReader : IScanFileReader
    {
        public bool CanRead(string path) => false;

        public Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken ct)
            => Task.FromException<FileReadResult>(new NotImplementedException());
    }

    private sealed class FakeScanPicker : IScanFilePicker
    {
        public string? PickScanFile() => null;
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
