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
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// Two defects found on a real 8x8 map, both of them the Inspector losing track of what the Stage was showing.
/// <para>
/// A form launched over a map opened asking for the point number, with the channel pickers at the no-channels
/// sentinel, while the toolbar right above it read <c>Point 24 of 64</c>. Doc 26 SS22.2 is explicit that the
/// Inspector holds the selection so nothing has to be typed; the <i>Extract</i> button already worked that way
/// and the launcher path did not.
/// </para>
/// <para>
/// And leaving the Volume view cleared the operation editor while the Inspector was still in the Operation role,
/// which draws nothing at all — the whole panel went blank with no way back to the map's own properties.
/// </para>
/// </summary>
public sealed class ShellMapSelectionSyncTests
{
    private const int Samples = 4;
    private const string ExtractId = "force-volume.extract-point";
    private const string VolumeId = "force-volume.volume-image";
    private const string CropId = "image.crop";

    private static ShellViewModel NewShell(Workspace ws, IOperationLauncher launcher)
        => new(
            ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
            new SpectroscopyParameterPreviewUseCase(), launcher, new MeasurementStore(), new FakePersistence(),
            new FakePathPicker(), new FakePrompt());

    private static ForceVolumeDataset Map(int points, SpectroscopyChannelSet? channels = null)
    {
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        for (int point = 0; point < points; point++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(point * Samples) + i] = Samples - i;
                force[(point * Samples) + i] = point;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(),
            new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            new ForceVolumeGeometry(4, 2, 3.0, 1.0, -1.5, -0.5, StandardUnits.Micrometre),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root,
            channels);
    }

    /// <summary>Three channels over <paramref name="points"/> points, so the pickers have something to pick.</summary>
    private static SpectroscopyChannelSet ChannelSet(int points)
        => new(
            [
                new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
                new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
                new ChannelDescriptor("Current", ChannelKind.Current, StandardUnits.Nanoampere, "Current"),
            ],
            points,
            ScanBuffer<float>.TakeOwnership(new float[3 * points * Samples], Samples, 3 * points));

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

    private static ShellViewModel WithActive(Workspace ws, AfmDataset dataset, IOperationLauncher launcher)
    {
        var vm = NewShell(ws, launcher);
        ws.Add(dataset);
        ws.SetActive(dataset.Id);
        return vm;
    }

    private static void Launch(ShellViewModel vm, string operationId)
        => vm.LauncherItems.Single(i => i.Id == operationId).LaunchCommand.Execute(null);

    private static object? Field(ShellViewModel vm, string name)
        => ((ParameterFormViewModel)vm.OperationEditor!).Fields.Single(f => f.Name == name).Value;

    [Fact]
    public void A_form_launched_over_a_map_opens_on_the_point_already_selected()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8), new FormLauncher());
        vm.SelectedMapPoint = 5;

        Launch(vm, ExtractId);

        // The toolbar above the form said "Point 6 of 8". Asking for the number again is asking the viewer to
        // read it off the screen and retype it.
        Assert.Equal(5, Field(vm, "point"));
    }

    [Fact]
    public void Stepping_the_point_moves_an_open_form_with_it()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8), new FormLauncher());

        Launch(vm, ExtractId);
        Assert.Equal(0, Field(vm, "point"));

        vm.StepMapPoint(3);

        Assert.Equal(3, Field(vm, "point"));
    }

    [Fact]
    public void The_channel_pair_on_screen_is_the_pair_the_form_would_extract()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8, ChannelSet(8)), new FormLauncher());
        vm.SelectedXChannel = 0;
        vm.SelectedYChannel = 2;

        Launch(vm, ExtractId);

        Assert.Equal(0, Field(vm, "xChannel"));
        Assert.Equal(2, Field(vm, "yChannel"));
    }

    [Fact]
    public void Changing_the_channel_moves_an_open_form_with_it()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8, ChannelSet(8)), new FormLauncher());

        Launch(vm, ExtractId);
        vm.SelectedYChannel = 2;

        Assert.Equal(2, Field(vm, "yChannel"));
    }

    [Fact]
    public void A_map_that_kept_no_channels_sends_the_sentinel_rather_than_an_index()
    {
        // An index into a set that does not exist would name whichever channel happened to share the number.
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8), new FormLauncher());

        Launch(vm, ExtractId);

        Assert.Equal(-1, Field(vm, "xChannel"));
        Assert.Equal(-1, Field(vm, "yChannel"));
    }

    [Fact]
    public void A_form_over_something_that_is_not_a_map_keeps_its_own_defaults()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Image(), new FormLauncher());

        Launch(vm, CropId);

        // The seeding is about a map point. An image operation that happens to have a field of the same name
        // must not be quietly overwritten with a selection that means nothing to it.
        Assert.Equal(7, Field(vm, "point"));
    }

    [Fact]
    public void Leaving_the_volume_view_shows_the_map_rather_than_an_empty_panel()
    {
        var ws = new Workspace();
        var vm = WithActive(ws, Map(8), new FormLauncher());

        Launch(vm, VolumeId);
        Assert.True(vm.IsVolumeView);
        Assert.True(vm.RoleIsOperation);

        vm.ShowSurfaceCommand.Execute(null);

        // The Operation role draws its editor and nothing else, so a null editor left the whole Inspector blank
        // — no properties, no placeholder, and no control that could put anything back.
        Assert.Null(vm.OperationEditor);
        Assert.False(vm.RoleIsOperation);
        Assert.True(vm.RoleIsDataset);
    }

    // --- fakes (this project keeps them per-file; see the other Shell* tests) ---

    /// <summary>Offers the two map operations and an image one, with the fields the real descriptors carry.</summary>
    private sealed class FormLauncher : IOperationLauncher
    {
        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() =>
        [
            new(ExtractId, "Extract Map Point", "Takes one curve out of a map.", OperationCategory.Process),
            new(VolumeId, "Volume Image", "Lays a measure out on the grid.", OperationCategory.Process),
            new(CropId, "Crop", "Cuts a rectangle out.", OperationCategory.Process),
        ];

        public OperationForm? GetForm(string operationId) => operationId switch
        {
            ExtractId => new OperationForm(
                ExtractId, "Extract Map Point", "Takes one curve out of a map.", OperationCategory.Process,
                [Integer("point", null), Integer("xChannel", -1), Integer("yChannel", -1)],
                DerivesCurve: true),
            VolumeId => new OperationForm(
                VolumeId, "Volume Image", "Lays a measure out on the grid.", OperationCategory.Process,
                [Integer("threshold", 50)],
                DerivesImage: true),
            CropId => new OperationForm(
                CropId, "Crop", "Cuts a rectangle out.", OperationCategory.Process,
                [Integer("point", 7)],
                DerivesImage: true),
            _ => null,
        };

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromException<OperationRunResult>(new NotImplementedException());

        private static ParameterFieldDescriptor Integer(string name, object? @default)
            => new(name, name, ParameterFieldKind.Integer, @default, null, null, [], null, string.Empty);
    }

    private sealed class FakeImageAnalysis : IImageAnalysisUseCase
    {
        public MeasurementLine? GetCurveSourceLine(DatasetId curveId) => null;

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
