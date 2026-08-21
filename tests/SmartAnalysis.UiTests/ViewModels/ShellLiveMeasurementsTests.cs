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

        public StatisticsResult? GetMeasurement(DatasetId artifactId) => null;
    }

    // ---- fakes ----
    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

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
        ];

        public OperationForm? GetForm(string operationId) => null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());
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
