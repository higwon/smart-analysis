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
/// V04 shell 3D toggle: the single image can be shown 2D or as a 3D surface, mutually exclusive, and the toggle
/// requests a re-render. The mode is ignored for Before/After and curves. ShellViewModel is plain (no STA).
/// </summary>
public sealed class ShellSurfaceToggleTests
{
    private static ShellViewModel NewShell(Workspace ws)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(), new SpectroscopyParameterPreviewUseCase(),
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ScanImageDataset Image()
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 4),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(4, 4),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    [Fact]
    public void Toggling_3d_swaps_the_single_view_and_requests_a_re_render()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        Assert.True(vm.ShowSingle2D);
        Assert.False(vm.ShowSingle3D);

        int renders = 0;
        vm.ImagesChanged += (_, _) => renders++;

        vm.Is3D = true;

        Assert.False(vm.ShowSingle2D);
        Assert.True(vm.ShowSingle3D);
        Assert.Equal(1, renders); // a single re-render into the newly shown view
    }

    [Fact]
    public void The_3d_mode_persists_across_active_image_changes()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var a = Image();
        var b = Image();
        ws.Add(a);
        ws.Add(b);
        ws.SetActive(a.Id);
        vm.Is3D = true;

        ws.SetActive(b.Id);

        Assert.True(vm.Is3D);          // the chosen view mode sticks
        Assert.True(vm.ShowSingle3D);
    }

    [Fact]
    public void Enabling_the_roi_while_3d_forces_2d_and_disabling_restores_3d()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);
        vm.Is3D = true;
        Assert.True(vm.ShowSingle3D);

        // The ROI overlay lives on the 2D view, so enabling it must force 2D even with the 3D preference on.
        vm.RoiEnabled = true;

        Assert.True(vm.ShowSingle2D);
        Assert.False(vm.ShowSingle3D);
        Assert.True(vm.Is3D);        // …the preference is retained
        Assert.False(vm.CanToggle3D); // the 3D toggle is hidden while the ROI is on

        // Turning the ROI off returns to the retained 3D view.
        vm.RoiEnabled = false;

        Assert.True(vm.ShowSingle3D);
        Assert.False(vm.ShowSingle2D);
        Assert.True(vm.CanToggle3D);
    }

    [Fact]
    public void A_curve_is_never_shown_as_a_surface_even_in_3d_mode()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        var curve = new LineProfileDataset(
            DatasetId.New(), new DataSource("test", null),
            new Axis("Distance", StandardUnits.Micrometre, 0.0, 1.0, 8),
            new ChannelDescriptor("psd", ChannelKind.Unknown, StandardUnits.One, "PSD"),
            ScanBuffer<float>.Allocate(8, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);
        ws.Add(image);
        ws.Add(curve);
        ws.SetActive(image.Id);
        vm.Is3D = true;

        ws.SetActive(curve.Id);

        Assert.False(vm.ShowSingle3D); // a curve routes to the curve view regardless of 3D mode
        Assert.False(vm.ShowSingle2D);
        Assert.True(vm.IsSingleCurve);
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

        public Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken ct = default)
            => Task.FromResult<ImageRenderInput?>(null);

        public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken ct = default)
            => Task.FromException<StatisticsResult>(new NotImplementedException());

        public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken ct = default)
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
