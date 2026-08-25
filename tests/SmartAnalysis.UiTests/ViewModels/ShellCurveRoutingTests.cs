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
/// A08 curve-view wiring: the shell routes the active dataset to the right stage. An image → the image view
/// (<c>IsSingleImage</c>), a 1D profile (e.g. a PSD) → the curve view (<c>IsSingleCurve</c>), and the two are
/// mutually exclusive. ShellViewModel is plain (no WPF Application/STA needed).
/// </summary>
public sealed class ShellCurveRoutingTests
{
    private static ShellViewModel NewShell(Workspace ws)
        => NewShell(ws, new FakeImageAnalysis());

    private static ShellViewModel NewShell(Workspace ws, IImageAnalysisUseCase analysis)
        => NewShell(ws, analysis, new FakeLauncher());

    private static ShellViewModel NewShell(Workspace ws, IImageAnalysisUseCase analysis, IOperationLauncher launcher)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), analysis,
               launcher, new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

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

    private static LineProfileDataset Curve()
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("Frequency", StandardUnits.PerMetre, 1.0, 1.0, 8),
            new ChannelDescriptor("psd", ChannelKind.Unknown, StandardUnits.One, "PSD"),
            ScanBuffer<float>.Allocate(8, 1),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    [Fact]
    public void An_active_curve_routes_to_the_curve_view_not_the_image_view()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var curve = Curve();
        ws.Add(curve);
        ws.SetActive(curve.Id);

        Assert.True(vm.IsSingleCurve);
        Assert.Same(curve, vm.ActiveCurve);
        Assert.False(vm.IsSingleImage);
        Assert.False(vm.HasActiveImage);
        Assert.Null(vm.ActiveImage);
    }

    private static ForceCurveDataset ForceCurve()
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([10f, 5f, 0f], 3, 1),
            ScanBuffer<float>.TakeOwnership([0f, 20f, 50f], 3, 1),
            new ChannelDescriptor("separation", ChannelKind.Unknown, StandardUnits.Nanometre, "Separation"),
            new ChannelDescriptor("force", ChannelKind.Unknown, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    [Fact]
    public void An_active_force_curve_routes_to_the_force_distance_view()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var curve = ForceCurve();
        ws.Add(curve);
        ws.SetActive(curve.Id);

        // A force curve is its own stage: force against separation, not a spatial profile and not an image.
        Assert.True(vm.IsSingleForceCurve);
        Assert.Same(curve, vm.ActiveForceCurve);
        Assert.False(vm.IsSingleCurve);
        Assert.False(vm.IsSingleImage);
        Assert.Null(vm.ActiveCurve);
        Assert.Null(vm.ActiveImage);
    }

    [Fact]
    public void Leaving_a_force_curve_clears_the_force_distance_stage()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var force = ForceCurve();
        var image = Image();
        ws.Add(force);
        ws.Add(image);

        ws.SetActive(force.Id);
        Assert.True(vm.IsSingleForceCurve);

        ws.SetActive(image.Id);
        Assert.False(vm.IsSingleForceCurve);
        Assert.Null(vm.ActiveForceCurve);
        Assert.True(vm.IsSingleImage);
    }

    [Fact]
    public void An_active_image_routes_to_the_image_view_not_the_curve_view()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);

        Assert.True(vm.IsSingleImage);
        Assert.Same(image, vm.ActiveImage);
        Assert.False(vm.IsSingleCurve);
        Assert.Null(vm.ActiveCurve);
    }

    [Fact]
    public void Switching_active_between_an_image_and_a_curve_swaps_the_stage()
    {
        var ws = new Workspace();
        var vm = NewShell(ws);
        var image = Image();
        var curve = Curve();
        ws.Add(image);
        ws.Add(curve);

        ws.SetActive(image.Id);
        Assert.True(vm.IsSingleImage);
        Assert.False(vm.IsSingleCurve);

        ws.SetActive(curve.Id);
        Assert.False(vm.IsSingleImage);
        Assert.True(vm.IsSingleCurve);
    }

    [Fact]
    public void A_line_profile_curve_pairs_with_its_source_image_and_line()
    {
        var ws = new Workspace();
        var source = Image();
        var curve = Curve();
        ws.Add(source);
        ws.Add(curve);

        var analysis = new FakeImageAnalysis { Line = new MeasurementLine(source.Id, 0, 2, 3, 2) };
        var vm = NewShell(ws, analysis);
        ws.SetActive(curve.Id);

        Assert.True(vm.ShowCurveSourceImage);          // the stage pairs the source image with the curve
        Assert.False(vm.IsSingleCurve);                // so the full-screen curve view yields
        Assert.True(vm.ShowSourceImagePane);           // the image control is shown (as the source pane)
        Assert.Same(source, vm.CurveSourceImage);      // and it renders the curve's source image
        Assert.NotNull(vm.CurveSourceLine);
    }

    [Fact]
    public void A_curve_with_no_source_line_stays_full_screen()
    {
        var ws = new Workspace();
        var curve = Curve();
        ws.Add(curve);

        var analysis = new FakeImageAnalysis { Line = null }; // e.g. a PSD — nothing to draw on an image
        var vm = NewShell(ws, analysis);
        ws.SetActive(curve.Id);

        Assert.True(vm.IsSingleCurve);
        Assert.False(vm.ShowCurveSourceImage);
        Assert.Null(vm.CurveSourceImage);
    }

    [Fact]
    public void Leaving_a_paired_curve_for_an_image_clears_the_source_pairing()
    {
        var ws = new Workspace();
        var source = Image();
        var curve = Curve();
        ws.Add(source);
        ws.Add(curve);

        var analysis = new FakeImageAnalysis { Line = new MeasurementLine(source.Id, 0, 2, 3, 2) };
        var vm = NewShell(ws, analysis);
        ws.SetActive(curve.Id);
        Assert.True(vm.ShowCurveSourceImage);

        ws.SetActive(source.Id); // back to an image → no curve pairing
        Assert.False(vm.ShowCurveSourceImage);
        Assert.Null(vm.CurveSourceLine);
        Assert.Null(vm.CurveSourceImage);
        Assert.True(vm.IsSingleImage);
    }

    [Fact]
    public async Task Opening_a_curve_process_form_previews_the_curve_without_image_panes()
    {
        var ws = new Workspace();
        var curve = Curve();
        ws.Add(curve);

        var vm = NewShell(ws, new FakeImageAnalysis(), new CurvePreviewLauncher());
        ws.SetActive(curve.Id);

        vm.LauncherItems.Single(i => i.Id == "profile.flatten").LaunchCommand.Execute(null); // a curve→curve Process op
        await vm.OperationPreviewSettled;

        Assert.True(vm.IsOperationPreview);
        Assert.False(vm.ShowComparePanes);        // a curve preview overlays on the curve view, not the image compare panes
        Assert.NotNull(vm.OperationPreviewCurve); // the PREVIEW curve to overlay on the source curve
    }

    [Fact]
    public async Task Leaving_a_curve_process_preview_clears_it()
    {
        var ws = new Workspace();
        var a = Curve();
        var b = Curve();
        ws.Add(a);
        ws.Add(b);

        var vm = NewShell(ws, new FakeImageAnalysis(), new CurvePreviewLauncher());
        ws.SetActive(a.Id);
        vm.LauncherItems.Single(i => i.Id == "profile.flatten").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;
        Assert.True(vm.IsOperationPreview);

        ws.SetActive(b.Id); // a new active dataset closes the editor → preview off
        Assert.False(vm.IsOperationPreview);
        Assert.Null(vm.OperationPreviewCurve);
    }

    [Fact]
    public async Task Switching_between_two_curve_process_forms_recomputes_the_preview()
    {
        var ws = new Workspace();
        var curve = Curve();
        ws.Add(curve);

        var vm = NewShell(ws, new FakeImageAnalysis(), new CurvePreviewLauncher());
        ws.SetActive(curve.Id);

        vm.LauncherItems.Single(i => i.Id == "profile.flatten").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;
        Assert.Equal("profile.flatten", vm.OperationPreviewCurve!.Series[0].Name);

        // Switch directly to another previewable op (both keep IsOperationPreview true) — the preview must recompute
        // for the NEW op immediately, not linger on the previous op's result until a parameter is touched.
        vm.LauncherItems.Single(i => i.Id == "profile.smooth").LaunchCommand.Execute(null);
        await vm.OperationPreviewSettled;

        Assert.Equal("profile.smooth", vm.OperationPreviewCurve!.Series[0].Name);
    }

    [Fact]
    public async Task Opening_a_crop_profile_form_does_not_enter_the_curve_preview()
    {
        var ws = new Workspace();
        var curve = Curve();
        ws.Add(curve);

        var vm = NewShell(ws, new FakeImageAnalysis(), new CurvePreviewLauncher());
        ws.SetActive(curve.Id);

        vm.LauncherItems.Single(i => i.Id == "profile.crop").LaunchCommand.Execute(null); // a range op (start/count)
        await vm.OperationPreviewSettled;

        // Crop derives a curve, but its start/count shape makes it a RANGE editor: the shell excludes it from the
        // preview strategy (IsProfileRangeEditor), so IsOperationPreview stays false — which is exactly what lets
        // RenderCurve fall through the preview branch to the vertical-marker branch (the markers are a live path).
        Assert.False(vm.IsOperationPreview);
        Assert.Null(vm.OperationPreviewCurve);
    }

    // A launcher offering two curve→curve Process ops; each previews a curve whose series is NAMED by its op id, so a
    // test can tell which op's preview is showing.
    private sealed class CurvePreviewLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() =>
        [
            new OperationLauncherItem("profile.flatten", "Flatten", "Detrend the profile", OperationCategory.Process),
            new OperationLauncherItem("profile.smooth", "Smooth", "Smooth the profile", OperationCategory.Process),
            new OperationLauncherItem("profile.crop", "Crop", "Crop the profile", OperationCategory.Process),
        ];

        public OperationForm? GetForm(string operationId) => operationId switch
        {
            "profile.flatten" or "profile.smooth" => new OperationForm(operationId, operationId, "curve op", OperationCategory.Process,
                [new ParameterFieldDescriptor("order", "Order", ParameterFieldKind.Integer, 1, 0, 8, Array.Empty<ParameterFieldOption>(), null, "help")], DerivesCurve: true),
            // Crop derives a curve too, but its start/count fields make it a range editor (markers), not a preview overlay.
            "profile.crop" => new OperationForm("profile.crop", "Crop", "Crop the profile", OperationCategory.Process,
            [
                new ParameterFieldDescriptor("start", "Start", ParameterFieldKind.Integer, 0, 0, null, Array.Empty<ParameterFieldOption>(), null, "help"),
                new ParameterFieldDescriptor("count", "Count", ParameterFieldKind.Integer, 4, 1, null, Array.Empty<ParameterFieldOption>(), null, "help"),
            ], DerivesCurve: true),
            _ => null,
        };

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());

        public Task<CurveRenderInput?> PreviewCurveAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromResult<CurveRenderInput?>(RenderInputFactory.ForLineProfile(Curve(), operationId)); // series named by op
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
