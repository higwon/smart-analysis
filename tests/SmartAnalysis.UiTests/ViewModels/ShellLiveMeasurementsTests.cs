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
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
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

    // ---- fakes ----
    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
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
