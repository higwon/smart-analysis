using System;
using System.Collections.Generic;
using System.Linq;
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
/// V04 × V06/V07/V08: an interactive image-overlay editor (a region crop/ROI, or a line profile) draws on the
/// <b>2D</b> image view, so opening one must force the 2D stage even when 3D is the preference (otherwise the
/// surface hides the overlay the user is meant to drag). Closing it returns to the retained 3D preference, and
/// the 3D toggle is hidden while editing. ShellViewModel is plain (no STA).
/// </summary>
public sealed class ShellSurfaceEditingTests
{
    private static ParameterFieldDescriptor Num(string name) => new(name, name, ParameterFieldKind.Number, 0.0, null, null, [], null, "");
    private static ParameterFieldDescriptor Int(string name) => new(name, name, ParameterFieldKind.Integer, 0, null, null, [], null, "");

    private static OperationForm LineForm() => new(
        "image.line-profile", "Line Profile (free)", "", OperationCategory.Process,
        [Num("x0"), Num("y0"), Num("x1"), Num("y1"), Int("samples")]);

    private static OperationForm RegionForm() => new(
        "image.crop", "Crop", "", OperationCategory.Process,
        [Int("left"), Int("top"), Int("width"), Int("height")]);

    private static ShellViewModel NewShell(Workspace ws, OperationForm form)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(), new SpectroscopyParameterPreviewUseCase(),
               new FormLauncher(form), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

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
    public void Opening_a_line_profile_editor_forces_2d_and_closing_it_restores_3d()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, LineForm());
        var image = Image();
        var next = Image();
        ws.Add(image);
        ws.Add(next);
        ws.SetActive(image.Id);
        vm.Is3D = true;
        Assert.True(vm.ShowSingle3D);

        // Open the line-profile editor → the overlay lives on the 2D view, so the stage is forced to 2D.
        vm.LauncherItems.Single(i => i.Id == "image.line-profile").LaunchCommand.Execute(null);

        Assert.True(vm.IsInteractiveImageEditing);
        Assert.True(vm.ShowSingle2D);
        Assert.False(vm.ShowSingle3D);
        Assert.False(vm.CanToggle3D); // the 3D toggle is hidden while editing
        Assert.True(vm.Is3D);         // …but the preference is retained

        // Closing the editor (active moves on) restores the retained 3D preference.
        ws.SetActive(next.Id);

        Assert.False(vm.IsInteractiveImageEditing);
        Assert.True(vm.ShowSingle3D);
        Assert.False(vm.ShowSingle2D);
        Assert.True(vm.CanToggle3D);
    }

    [Fact]
    public void A_region_editor_forces_2d_too()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, RegionForm());
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);
        vm.Is3D = true;

        vm.LauncherItems.Single(i => i.Id == "image.crop").LaunchCommand.Execute(null);

        Assert.True(vm.IsInteractiveImageEditing); // same 2D-only policy as the line editor
        Assert.True(vm.ShowSingle2D);
        Assert.False(vm.ShowSingle3D);
    }

    // ---- fakes ----
    private sealed class FormLauncher : IOperationLauncher
    {
        private readonly OperationForm _form;

        public FormLauncher(OperationForm form) => _form = form;

        public IReadOnlyList<OperationLauncherItem> ApplicableToActive()
            => [new(_form.Id, _form.DisplayName, _form.Summary, _form.Category)];

        public OperationForm? GetForm(string operationId) => operationId == _form.Id ? _form : null;

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
