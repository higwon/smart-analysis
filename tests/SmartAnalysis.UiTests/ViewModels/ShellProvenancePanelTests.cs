using System;
using System.Collections.Generic;
using System.Linq;
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
/// U05 provenance/history panel: the active dataset's recorded steps expose their real, auditable detail from
/// F05 — the parameters that were applied (name + value with unit) and any warnings — not just the op name.
/// Selecting a step shows it read-only and never changes the active dataset. ShellViewModel is plain (no STA).
/// </summary>
public sealed class ShellProvenancePanelTests
{
    private static ShellViewModel NewShell(Workspace ws)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ScanImageDataset Root()
        => new(
            DatasetId.New(),
            new DataSource("psia-tiff", "C:\\scans\\sample.tiff"),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static ScanImageDataset DerivedFrom(ScanImageDataset parent)
    {
        var step = new ProvenanceStep(
            stepId: "s1",
            inputDatasetId: parent.Id,
            inputVersion: 0,
            operationId: "image.fourier",
            operationVersion: 1,
            order: 0,
            environment: ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue>
            {
                ["lowCutoff"] = new(0.25, StandardUnits.One),
                ["highCutoff"] = new(0.75, StandardUnits.One),
            },
            warnings: [new OperationWarning("fourier.padded", "The image was mean-padded to 8×8 for the FFT.")]);

        return new ScanImageDataset(
            DatasetId.New(),
            DataSource.Derived,
            parent.X,
            parent.Y,
            parent.Channel,
            ScanBuffer<float>.Allocate(4, 4),
            ScanMetadata.Unknown,
            ProvenanceRecord.DerivedFrom(parent.Id, [step]));
    }

    [Fact]
    public void A_derived_step_exposes_its_parameters_with_units_and_warnings()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var root = Root();
        var derived = DerivedFrom(root);
        ws.Add(root);
        ws.Add(derived);
        ws.SetActive(derived.Id);

        var step = Assert.Single(vm.HistoryRows);
        Assert.Equal("Fourier", step.Operation);
        Assert.Equal("image.fourier", step.OperationId);

        Assert.True(step.HasParameters);
        var low = Assert.Single(step.Parameters, p => p.Name == "lowCutoff");
        Assert.Equal("0.25", low.Value); // dimensionless → no unit symbol
        Assert.Contains(step.Parameters, p => p.Name == "highCutoff" && p.Value == "0.75");

        Assert.True(step.HasWarnings);
        Assert.Contains("mean-padded", Assert.Single(step.Warnings));

        // The compact strip summary lists the applied parameters.
        Assert.Contains("lowCutoff 0.25", step.Summary);
    }

    [Fact]
    public void A_physical_parameter_keeps_its_unit_symbol()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var root = Root();

        var step = new ProvenanceStep(
            stepId: "s1",
            inputDatasetId: root.Id,
            inputVersion: 0,
            operationId: "image.pixelmath",
            operationVersion: 1,
            order: 0,
            environment: ExecutionEnvironment.Unknown,
            parameters: new Dictionary<string, PhysicalValue> { ["amount"] = new(2.5, StandardUnits.Nanometre) });
        var derived = new ScanImageDataset(
            DatasetId.New(), DataSource.Derived, root.X, root.Y, root.Channel,
            ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown,
            ProvenanceRecord.DerivedFrom(root.Id, [step]));

        ws.Add(root);
        ws.Add(derived);
        ws.SetActive(derived.Id);

        var amount = Assert.Single(Assert.Single(vm.HistoryRows).Parameters);
        Assert.Equal("amount", amount.Name);
        Assert.Equal("2.5 nm", amount.Value);
    }

    [Fact]
    public void The_import_row_shows_the_source_file_and_no_parameters()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var root = Root();
        ws.Add(root);
        ws.SetActive(root.Id);

        var import = Assert.Single(vm.HistoryRows);
        Assert.Equal("Import", import.Operation);
        Assert.Equal("sample.tiff", import.Summary);
        Assert.False(import.HasParameters);
        Assert.False(import.HasWarnings);
    }

    [Fact]
    public void Selecting_a_step_shows_it_read_only_and_leaves_the_active_dataset_unchanged()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var root = Root();
        var derived = DerivedFrom(root);
        ws.Add(root);
        ws.Add(derived);
        ws.SetActive(derived.Id);

        vm.SelectStep(vm.HistoryRows[0]);

        Assert.True(vm.RoleIsStep);
        Assert.Same(vm.HistoryRows[0], vm.SelectedStep);
        Assert.Equal(derived.Id, ws.Active.ActiveId); // a recorded step is not navigable

        vm.SelectStep(null);
        Assert.False(vm.RoleIsStep);
        Assert.Null(vm.SelectedStep);
    }

    // ---- minimal fakes (construction only) ----
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

    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken ct = default)
            => Task.FromException<FlattenOutcome>(new NotImplementedException());

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());

        public StatisticsResult? GetMeasurement(DatasetId artifactId) => null;
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
