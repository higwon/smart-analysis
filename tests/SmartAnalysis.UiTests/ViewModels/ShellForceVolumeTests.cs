using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.UI.ViewModels;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// TASK-FF09: a force–volume map is many curves, so the stage shows one at a time and the viewer must always be
/// able to tell <b>which</b> — a curve with no stated position is indistinguishable from any other point on the map.
/// </summary>
public sealed class ShellForceVolumeTests
{
    private const int Samples = 4;

    private static ShellViewModel NewShell(Workspace ws)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(), new SpectroscopyParameterPreviewUseCase(),
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    private static ShellViewModel NewShell(Workspace ws, IOperationLauncher launcher)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(), new SpectroscopyParameterPreviewUseCase(),
               launcher, new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    /// <summary>A map of <paramref name="points"/> curves; point k has force samples all equal to k.</summary>
    private static ForceVolumeDataset Map(int points, ForceVolumeGeometry? geometry = null)
    {
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        for (int point = 0; point < points; point++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(point * Samples) + i] = i;
                force[(point * Samples) + i] = point;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            geometry, ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    private static ForceVolumeGeometry Grid(int columns, int rows)
        => new(columns, rows, scanSizeX: 3.0, scanSizeY: 1.0, offsetX: -1.5, offsetY: -0.5, StandardUnits.Micrometre);

    private static ShellViewModel WithActiveMap(Workspace ws, ForceVolumeDataset map)
    {
        var vm = NewShell(ws);
        ws.Add(map);
        ws.SetActive(map.Id);
        return vm;
    }

    [Fact]
    public void An_active_map_routes_to_its_own_stage_and_starts_at_the_first_point()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        Assert.True(vm.IsForceVolume);
        Assert.Equal(6, vm.MapPointCount);
        Assert.Equal(0, vm.SelectedMapPoint);

        // A map is not a single curve: routing it to the force-curve stage would show point 0 with no way to
        // reach the other five, and nothing on screen saying they exist.
        Assert.False(vm.IsSingleForceCurve);
        Assert.Null(vm.ActiveForceCurve);
        Assert.False(vm.IsSingleImage);
    }

    [Fact]
    public void The_upper_bound_of_a_selector_is_the_last_index_not_the_count()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        // Binding a selector to the count would leave a dead position past the last curve.
        Assert.Equal(5, vm.MapPointMaxIndex);
    }

    [Fact]
    public void Stepping_moves_one_curve_at_a_time_and_stops_at_the_ends()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(3));

        Assert.False(vm.CanStepMapPointBack);
        Assert.True(vm.CanStepMapPointForward);

        vm.StepMapPoint(1);
        Assert.Equal(1, vm.SelectedMapPoint);
        Assert.True(vm.CanStepMapPointBack);

        vm.StepMapPoint(1);
        Assert.Equal(2, vm.SelectedMapPoint);
        Assert.False(vm.CanStepMapPointForward);

        vm.StepMapPoint(1); // past the end
        Assert.Equal(2, vm.SelectedMapPoint);

        vm.StepMapPoint(-5); // past the start
        Assert.Equal(0, vm.SelectedMapPoint);
    }

    [Fact]
    public void Switching_to_another_map_resets_the_selection()
    {
        // Point 7 of one map has nothing to do with point 7 of the next. Carrying the index over would show an
        // unrelated curve that looks entirely valid — and the next map may not even have that many points.
        var ws = new Workspace();
        var first = Map(8);
        var second = Map(3);
        var vm = NewShell(ws);
        ws.Add(first);
        ws.Add(second);

        ws.SetActive(first.Id);
        vm.SelectedMapPoint = 7;
        Assert.Equal(7, vm.SelectedMapPoint);

        ws.SetActive(second.Id);

        Assert.Equal(0, vm.SelectedMapPoint);
        Assert.Equal(3, vm.MapPointCount);
    }

    [Fact]
    public void A_selection_can_never_index_past_the_active_map()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(3));

        vm.SelectedMapPoint = 99;

        Assert.Equal(2, vm.SelectedMapPoint);
    }

    [Fact]
    public void The_label_says_where_on_the_sample_the_curve_was_measured()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        // Point 4 is column 2, row 2 of a 3x2 grid. With the scan size spanning first point to last, the columns
        // sit at -1.5 / 0 / 1.5 and the rows at -0.5 / 0.5.
        vm.SelectedMapPoint = 4;

        Assert.Contains("Point 5 of 6", vm.MapPointLabel);
        Assert.Contains("col 2/3", vm.MapPointLabel);
        Assert.Contains("row 2/2", vm.MapPointLabel);
        Assert.Contains("scan (0, 0.5)", vm.MapPointLabel);
        Assert.Contains("um", vm.MapPointLabel);
    }

    [Fact]
    public void A_map_without_a_grid_says_so_rather_than_showing_a_position_it_does_not_have()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(4)); // hand-placed points: no geometry

        Assert.Contains("Point 1 of 4", vm.MapPointLabel);
        Assert.Contains("no recorded position", vm.MapPointLabel);
    }

    [Fact]
    public void Selecting_a_point_asks_the_stage_to_redraw()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(3));
        int redraws = 0;
        vm.MapPointChanged += () => redraws++;

        vm.SelectedMapPoint = 1;
        Assert.Equal(1, redraws);

        vm.SelectedMapPoint = 1; // same point again
        Assert.Equal(1, redraws);

        vm.StepMapPoint(1);
        Assert.Equal(2, redraws);
    }

    [Fact]
    public void Leaving_a_map_clears_the_stage()
    {
        var ws = new Workspace();
        var map = Map(4);
        var curve = SingleCurve();
        var vm = NewShell(ws);
        ws.Add(map);
        ws.Add(curve);

        ws.SetActive(map.Id);
        Assert.True(vm.IsForceVolume);

        ws.SetActive(curve.Id);

        Assert.False(vm.IsForceVolume);
        Assert.Null(vm.ActiveForceVolume);
        Assert.Equal(0, vm.MapPointCount);
        Assert.Equal(string.Empty, vm.MapPointLabel);
        Assert.True(vm.IsSingleForceCurve);
    }

    private static ForceCurveDataset SingleCurve()
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership([0f, 1f, 2f], 3, 1),
            ScanBuffer<float>.TakeOwnership([0f, 10f, 20f], 3, 1),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

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

    private sealed class RecordingLauncher : IOperationLauncher
    {
        public string? RanOperation { get; private set; }

        public IReadOnlyDictionary<string, object?>? RanWith { get; private set; }

        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();

        public OperationForm? GetForm(string operationId) => null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
        {
            RanOperation = operationId;
            RanWith = values;
            return Task.FromResult(OperationRunResult.Derived(DatasetId.New(), []));
        }
    }

    private sealed class VolumeLauncher : IOperationLauncher
    {
        public int Previews { get; private set; }

        public IReadOnlyDictionary<string, object?>? LastPreviewValues { get; private set; }

        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();

        public OperationForm? GetForm(string operationId)
            => operationId == "force-volume.volume-image"
                ? new OperationForm(
                    operationId, "Volume Image", "one pixel per point", OperationCategory.Process,
                    [
                        new ParameterFieldDescriptor("threshold", "Threshold", ParameterFieldKind.Number, 50.0, 0.0, 100.0, [], null, ""),
                        new ParameterFieldDescriptor("baseline", "Baseline", ParameterFieldKind.Number, 20.0, 1.0, 100.0, [], null, ""),
                        new ParameterFieldDescriptor(
                            "phase", "Phase", ParameterFieldKind.Choice, "Approach", null, null,
                            [new ParameterFieldOption("Approach", "Approach"), new ParameterFieldOption("Retract", "Retract")],
                            null, ""),
                    ],
                    DerivesImage: true)
                : null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromResult(OperationRunResult.Derived(DatasetId.New(), []));

        /// <summary>When set, the launcher refuses these values the way a schema violation would.</summary>
        public string? Refusal { get; set; }

        public Task<ImageRenderInput?> PreviewAsync(string operationId, IReadOnlyDictionary<string, object?> values, Colormap colormap, ValueRange? range, CancellationToken ct = default)
        {
            Previews++;
            LastPreviewValues = values;
            return Task.FromResult<ImageRenderInput?>(null);
        }

        public string? Explain(string operationId, IReadOnlyDictionary<string, object?> values) => Refusal;
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
    /// <summary>Three channels over <paramref name="points"/> points; channel c at point p is all c*100+p.</summary>
    private static SpectroscopyChannelSet ChannelSet(int points)
    {
        var samples = new float[3 * points * Samples];
        for (int c = 0; c < 3; c++)
        {
            for (int p = 0; p < points; p++)
            {
                for (int i = 0; i < Samples; i++)
                {
                    samples[((((c * points) + p) * Samples)) + i] = (c * 100) + p;
                }
            }
        }

        return new SpectroscopyChannelSet(
            [
                new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
                new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
                new ChannelDescriptor("Current", ChannelKind.Current, StandardUnits.Nanoampere, "Current"),
            ],
            points,
            ScanBuffer<float>.TakeOwnership(samples, Samples, 3 * points));
    }

    private static ForceVolumeDataset MapWithChannels(int points)
    {
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root, ChannelSet(points));
    }

    [Fact]
    public void The_picker_starts_on_the_pair_the_file_designated()
    {
        // Not channel 0 and 1 by position — the pair the FILE flagged, found by key. Starting anywhere else
        // would show a different quantity than the one the analysis operates on, with nothing saying so.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithChannels(2));

        Assert.True(vm.CanPickChannels);
        Assert.Equal(3, vm.ChannelChoices.Count);
        Assert.Equal(0, vm.SelectedXChannel);   // Z Scan
        Assert.Equal(1, vm.SelectedYChannel);   // Force
        Assert.True(vm.IsDesignatedChannelPair);
    }

    [Fact]
    public void A_pair_other_than_the_designated_one_says_so()
    {
        // A plot of Current against Z is a perfectly good chart and is NOT the force curve A12/A13 fit. The
        // viewer has to be able to tell, because the axes alone look just as authoritative.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithChannels(2));

        vm.SelectedYChannel = 2;   // Current

        Assert.False(vm.IsDesignatedChannelPair);
        Assert.Contains("not the designated pair", vm.SpectroscopyLabel);
    }

    [Fact]
    public void Choosing_a_channel_asks_the_stage_to_redraw()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithChannels(2));
        int redraws = 0;
        vm.MapPointChanged += () => redraws++;

        vm.SelectedYChannel = 2;
        Assert.Equal(1, redraws);

        vm.SelectedYChannel = 2;   // the same channel again
        Assert.Equal(1, redraws);
    }

    [Fact]
    public void A_channel_choice_belongs_to_one_dataset()
    {
        // Index 2 in one file is a different physical quantity than index 2 in the next. Carrying the choice
        // over would silently plot something else under the same selection.
        var ws = new Workspace();
        var first = MapWithChannels(2);
        var second = MapWithChannels(2);
        var vm = NewShell(ws);
        ws.Add(first);
        ws.Add(second);

        ws.SetActive(first.Id);
        vm.SelectedYChannel = 2;
        Assert.False(vm.IsDesignatedChannelPair);

        ws.SetActive(second.Id);

        Assert.Equal(1, vm.SelectedYChannel);
        Assert.True(vm.IsDesignatedChannelPair);
    }

    [Fact]
    public void A_dataset_that_kept_no_channels_offers_no_choice()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(3));   // no channel set

        Assert.Null(vm.SpectroscopyChannels);
        Assert.False(vm.CanPickChannels);
        Assert.Empty(vm.ChannelChoices);
        Assert.True(vm.IsDesignatedChannelPair);   // nothing to diverge from
    }

    [Fact]
    public void A_channel_index_can_never_leave_the_set()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithChannels(2));

        vm.SelectedXChannel = 99;
        Assert.Equal(2, vm.SelectedXChannel);

        vm.SelectedXChannel = -5;
        Assert.Equal(0, vm.SelectedXChannel);
    }
    [Fact]
    public void The_designated_pair_is_found_by_key_not_by_position()
    {
        // Real files do not put the flagged pair first: this one declares Current, then Z Scan, then Force.
        // Defaulting to positions 0 and 1 would open on Current-against-Z and call it the designated pair.
        var points = 2;
        var samples = new float[3 * points * Samples];
        var set = new SpectroscopyChannelSet(
            [
                new ChannelDescriptor("Current", ChannelKind.Current, StandardUnits.Nanoampere, "Current"),
                new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
                new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            ],
            points,
            ScanBuffer<float>.TakeOwnership(samples, Samples, 3 * points));

        var map = new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(new float[points * Samples], Samples, points),
            ScanBuffer<float>.TakeOwnership(new float[points * Samples], Samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root, set);

        var ws = new Workspace();
        var vm = WithActiveMap(ws, map);

        Assert.Equal(1, vm.SelectedXChannel);   // Z Scan, not position 0
        Assert.Equal(2, vm.SelectedYChannel);   // Force, not position 1
        Assert.True(vm.IsDesignatedChannelPair);
    }
    private static ScanImageDataset Surface()
        => new(
            DatasetId.New(), new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("Z Height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(2, 2),
            ScanMetadata.Unknown, ProvenanceRecord.Root);

    private static ForceVolumeDataset MapWithSurface()
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(new float[4 * Samples], Samples, 4),
            ScanBuffer<float>.TakeOwnership(new float[4 * Samples], Samples, 4),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root, null, Surface());

    [Fact]
    public void A_map_that_came_with_a_surface_offers_it()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithSurface());

        Assert.True(vm.HasReferenceSurface);
        Assert.NotNull(vm.SpectroscopyReferenceImage);
        Assert.Equal("Z Height", vm.SpectroscopyReferenceImage!.Channel.Key);
    }

    [Fact]
    public void A_previous_files_surface_does_not_linger_beside_the_next_files_curves()
    {
        // The surface belongs to one file. Showing the last one beside a map that has none would place those
        // curves on a sample they were never measured on.
        var ws = new Workspace();
        var withSurface = MapWithSurface();
        var without = Map(3);
        var vm = NewShell(ws);
        ws.Add(withSurface);
        ws.Add(without);

        ws.SetActive(withSurface.Id);
        Assert.True(vm.HasReferenceSurface);

        ws.SetActive(without.Id);

        Assert.False(vm.HasReferenceSurface);
        Assert.Null(vm.SpectroscopyReferenceImage);
    }
    private static MapPointLayout Layout(params (double X, double Y)[] points)
        => Layout(StandardUnits.Micrometre, points);

    private static MapPointLayout Layout(Unit unit, params (double X, double Y)[] points)
        => new([.. points.Select(p => new MapPointPosition(p.X, p.Y))], unit);

    /// <summary>A map with a 4x4-pixel surface over 4x4 um, so one pixel is exactly 1 um.</summary>
    private static ForceVolumeDataset MapOnSurface(
        MapPointLayout? layout, int points = 2, ForceVolumeGeometry? geometry = null,
        Unit? surfaceUnit = null, Unit? surfaceUnitY = null)
        => new(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(new float[points * Samples], Samples, points),
            ScanBuffer<float>.TakeOwnership(new float[points * Samples], Samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            geometry, ScanMetadata.Unknown, ProvenanceRecord.Root, null,
            new ScanImageDataset(
                DatasetId.New(), new DataSource("test", null),
                new Axis("X", surfaceUnit ?? StandardUnits.Micrometre, 0.0, 1.0, 4),
                new Axis("Y", surfaceUnitY ?? surfaceUnit ?? StandardUnits.Micrometre, 0.0, 1.0, 4),
                new ChannelDescriptor("Z Height", ChannelKind.Topography, StandardUnits.Nanometre),
                ScanBuffer<float>.Allocate(4, 4), ScanMetadata.Unknown, ProvenanceRecord.Root),
            layout);

    [Fact]
    public void The_surface_takes_the_stage_and_the_curve_does_not()
    {
        // doc 26 §22.1: a map is many curves measured at PLACES, so the Stage is the surface. Putting one
        // curve there makes one of N the subject and hides that the others exist.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapOnSurface(Layout((1, 1), (3, 2))));

        Assert.True(vm.HasReferenceSurface);
        Assert.False(vm.ShowCurveOnStage);
    }

    [Fact]
    public void With_no_surface_the_curve_takes_the_stage_rather_than_leaving_it_blank()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(3));   // no surface

        Assert.False(vm.HasReferenceSurface);
        Assert.True(vm.ShowCurveOnStage);
    }

    [Fact]
    public void Markers_are_the_recorded_positions_converted_to_surface_pixels()
    {
        // The overlay draws in image space. Handing it micrometres would scatter the marks by the pixel size.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapOnSurface(Layout((1, 1), (3, 2))));

        Assert.Equal(2, vm.PointMarkers.Count);
        Assert.Equal((1.0, 1.0), vm.PointMarkers[0]);   // 1 um / 1 um-per-pixel
        Assert.Equal((3.0, 2.0), vm.PointMarkers[1]);
    }

    [Fact]
    public void Nothing_is_marked_on_a_surface_that_cannot_place_it()
    {
        // A map whose file recorded no positions has nothing to mark, and marking anywhere would be a claim
        // about where it was measured.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapOnSurface(layout: null));

        Assert.True(vm.HasReferenceSurface);
        Assert.Empty(vm.PointMarkers);
    }

    [Fact]
    public void A_map_with_positions_but_no_surface_marks_nothing()
    {
        var ws = new Workspace();
        var map = new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(new float[2 * Samples], Samples, 2),
            ScanBuffer<float>.TakeOwnership(new float[2 * Samples], Samples, 2),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Micrometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            null, ScanMetadata.Unknown, ProvenanceRecord.Root, null, null, Layout((1, 1), (3, 2)));
        var vm = WithActiveMap(new Workspace(), map);

        Assert.Empty(vm.PointMarkers);
    }

    [Fact]
    public void The_toolbar_names_the_place_the_marker_is_drawn()
    {
        // The stage draws the RECORDED position; a toolbar quoting the reconstructed grid instead would put two
        // coordinates on screen for one point, in two frames, with no way to tell which is which.
        var ws = new Workspace();
        var vm = WithActiveMap(
            ws,
            MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1)));

        vm.SelectedMapPoint = 1;

        var surface = vm.SpectroscopyReferenceImage!;
        var (markerX, markerY) = vm.PointMarkers[1];
        string place = FormattableString.Invariant(
            $"({markerX * surface.X.Step:0.###}, {markerY * surface.Y.Step:0.###})");

        Assert.Contains(place, vm.MapPointLabel);
        Assert.Contains("surface", vm.MapPointLabel);

        // The grid puts point 1 at (1.5, -0.5). That number must not reach the label.
        Assert.DoesNotContain("1.5", vm.MapPointLabel);
    }

    [Fact]
    public void Without_recorded_positions_the_label_says_which_frame_it_fell_back_to()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        vm.SelectedMapPoint = 4;

        Assert.Contains("scan", vm.MapPointLabel);
        Assert.DoesNotContain("surface", vm.MapPointLabel);
    }

    [Fact]
    public void A_spectroscopy_dataset_is_never_told_to_select_an_image()
    {
        // doc 26 §22.2: a map and a curve both have properties. The placeholder is for having nothing to
        // inspect, not for the active dataset being something other than a 2D image.
        var empty = NewShell(new Workspace());
        Assert.True(empty.HasNothingToInspect);

        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapOnSurface(Layout((1, 1), (3, 2))));
        Assert.False(vm.HasNothingToInspect);
    }

    [Fact]
    public void The_map_props_say_how_much_of_the_sample_the_map_covers()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        Assert.Equal("3 × 2 grid · 6 points", vm.MapSummary);
    }

    [Fact]
    public void A_map_with_no_grid_counts_its_points_rather_than_inventing_a_shape()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(4));

        Assert.Equal("4 points · no grid", vm.MapSummary);
    }

    [Fact]
    public void The_inspector_previews_the_curve_only_when_the_stage_is_showing_the_surface()
    {
        // Drawing the same curve on the stage AND in the Inspector says nothing the stage did not already say.
        var onSurface = WithActiveMap(new Workspace(), MapOnSurface(Layout((1, 1), (3, 2))));
        Assert.True(onSurface.ShowCurveInInspector);

        var noSurface = WithActiveMap(new Workspace(), Map(3));
        Assert.True(noSurface.ShowCurveOnStage);
        Assert.False(noSurface.ShowCurveInInspector);
    }

    [Fact]
    public void Extracting_a_point_carries_the_selection_and_the_pair_on_screen()
    {
        // The Inspector holds the selection (§22.2), so the point index is never typed into a form — and the
        // curve that comes out is the one the viewer was looking at, channels included.
        var launcher = new RecordingLauncher();
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = MapWithChannels(4);
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.SelectedMapPoint = 2;
        vm.SelectedYChannel = 2;

        // The recording launcher completes synchronously, so the command has run by the time Execute returns.
        vm.ExtractPointCommand.Execute(null);

        Assert.Equal("force-volume.extract-point", launcher.RanOperation);
        Assert.Equal(2, launcher.RanWith!["point"]);
        Assert.Equal(vm.SelectedXChannel, launcher.RanWith["xChannel"]);
        Assert.Equal(2, launcher.RanWith["yChannel"]);
    }

    [Fact]
    public void A_map_that_kept_no_channels_extracts_the_pair_it_designates()
    {
        // -1 is the operation's sentinel for "keep the designated pair". Sending 0 would be an index into a
        // channel set the map does not have.
        var launcher = new RecordingLauncher();
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = Map(4);
        ws.Add(map);
        ws.SetActive(map.Id);

        // The recording launcher completes synchronously, so the command has run by the time Execute returns.
        vm.ExtractPointCommand.Execute(null);

        Assert.Null(vm.SpectroscopyChannels);
        Assert.Equal(-1, launcher.RanWith!["xChannel"]);
        Assert.Equal(-1, launcher.RanWith["yChannel"]);
    }

    [Fact]
    public void The_volume_view_is_offered_only_for_a_map_that_could_produce_a_picture()
    {
        // Without a grid there is no shape to lay the points out in, so the operation refuses. Offering the
        // toggle anyway would put a control on the toolbar whose only outcome is a validation error.
        Assert.True(WithActiveMap(new Workspace(), Map(6, Grid(3, 2))).CanShowVolume);
        Assert.False(WithActiveMap(new Workspace(), Map(4)).CanShowVolume);
        Assert.False(NewShell(new Workspace()).CanShowVolume);
    }

    [Fact]
    public void Showing_the_volume_hands_the_stage_to_the_picture()
    {
        // doc 26 SS22.3: the picture is a VIEW of the same Stage, so it replaces the surface rather than opening
        // a compare pane or a second stage.
        var launcher = new VolumeLauncher();
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.False(vm.IsVolumeView);

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.IsVolumeView);
        Assert.True(vm.ShowSpectroscopyImage);
        Assert.False(vm.ShowCurveOnStage);
        Assert.False(vm.ShowComparePanes);   // a view mode, not a before/after
        Assert.Equal(InspectorRole.Operation, vm.InspectorRole);
    }

    [Fact]
    public void Nothing_enters_the_workspace_while_the_picture_is_only_previewed()
    {
        // Materialising an image per threshold tweak would bury the workspace in near-identical pictures and make
        // provenance meaningless. Only Keep as image commits one.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);
        int before = ws.Datasets.Count;

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.IsVolumeView);
        Assert.Equal(before, ws.Datasets.Count);
    }

    [Fact]
    public void Leaving_the_volume_view_gives_the_surface_back()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);
        vm.ShowSurfaceCommand.Execute(null);

        Assert.False(vm.IsVolumeView);
        Assert.True(vm.HasReferenceSurface);
        Assert.True(vm.ShowSpectroscopyImage);
    }

    [Fact]
    public void The_curve_stays_in_the_inspector_when_the_volume_takes_the_stage()
    {
        // doc 26 SS22.6: the volume image parameters are statements ABOUT a curve — "50% of the maximum force"
        // is a place on one. Hiding the curve when you switch to the view that sets them leaves the user typing
        // a number with no referent, and a pixel that comes out as a hole with no explanation.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.True(vm.ShowCurveInInspector);

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.IsVolumeView);
        Assert.True(vm.ShowCurveInInspector);
    }

    [Fact]
    public void A_surfaceless_map_shows_the_curve_beside_the_picture_rather_than_twice()
    {
        // With no surface the curve owns the stage, so the Inspector does not repeat it. Entering the Volume
        // view hands the stage to the picture — and that is exactly when the Inspector has to pick the curve up.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));   // no reference surface
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.True(vm.ShowCurveOnStage);
        Assert.False(vm.ShowCurveInInspector);

        vm.ShowVolumeCommand.Execute(null);

        Assert.False(vm.ShowCurveOnStage);
        Assert.True(vm.ShowCurveInInspector);
    }

    [Fact]
    public void Entering_the_volume_view_announces_the_curve_moving()
    {
        // The binding only re-reads on a change notification; without one the panel keeps whatever it last drew.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ShowVolumeCommand.Execute(null);

        Assert.Contains(nameof(vm.ShowCurveInInspector), raised);
    }

    [Fact]
    public void A_map_with_no_surface_still_gets_the_picture_rather_than_the_curve()
    {
        // ShowCurveOnStage exists so a surface-less map is not blank. The picture is a better answer than the
        // curve when there is one, and both on the stage at once is nothing.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));   // no reference surface
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.True(vm.ShowCurveOnStage);

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.IsVolumeView);
        Assert.False(vm.ShowCurveOnStage);
        Assert.True(vm.ShowSpectroscopyImage);
    }

    [Fact]
    public void A_rejected_channel_index_is_pushed_back_so_the_combo_cannot_go_blank()
    {
        // A ComboBox writes SelectedIndex = -1 when its ItemsSource is swapped. The view-model coerces that back
        // into range — but when the coerced value is the one it already held, SetProperty raises nothing, the
        // control never re-reads, and it sits at -1 showing an empty box beside a populated one.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapWithChannels(2));
        vm.SelectedYChannel = 0;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedYChannel = -1;

        Assert.Equal(0, vm.SelectedYChannel);
        Assert.Contains(nameof(vm.SelectedYChannel), raised);
    }

    [Fact]
    public void A_setting_the_map_cannot_be_measured_with_says_so()
    {
        // The Volume view's preview IS the stage. Leaving the previous picture up would show one set of settings
        // on the stage and another in the panel beside it, with nothing saying which produced what.
        var launcher = new VolumeLauncher { Refusal = "Baseline must be at most 100." };
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.HasVolumeUnavailable);
        Assert.Equal("Baseline must be at most 100.", vm.VolumeUnavailable);
    }

    [Fact]
    public void A_refusal_with_no_reason_still_says_something()
    {
        // A launcher that declines to explain is not a licence to show nothing at all.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);

        Assert.True(vm.HasVolumeUnavailable);
        Assert.False(string.IsNullOrWhiteSpace(vm.VolumeUnavailable));
    }

    [Fact]
    public void Leaving_the_volume_view_takes_the_message_with_it()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher { Refusal = "nope" });
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);
        Assert.True(vm.HasVolumeUnavailable);

        vm.ShowSurfaceCommand.Execute(null);

        Assert.False(vm.HasVolumeUnavailable);
    }

    [Fact]
    public void The_curve_carries_no_marks_outside_the_volume_view()
    {
        // The marks belong to the Volume view's settings. On the Surface view the curve is what the selected
        // point measured, and there is no threshold on screen for a line to be explaining.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.Empty(vm.CurveVerticalMarkers);
        Assert.Empty(vm.CurveHorizontalMarkers);
    }

    [Fact]
    public void Entering_the_volume_view_announces_that_the_marks_moved()
    {
        // The curve is redrawn from a binding; without a notification it keeps whatever it last drew, which is
        // the curve with no settings on it.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ShowVolumeCommand.Execute(null);

        Assert.Contains(nameof(vm.CurveVerticalMarkers), raised);
        Assert.Contains(nameof(vm.CurveHorizontalMarkers), raised);
    }

    [Fact]
    public void Stepping_to_another_point_announces_that_the_marks_moved()
    {
        // Each point has its own baseline and its own window. Marks left from the previous point would explain
        // a number this one does not hold.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.StepMapPoint(1);

        Assert.Contains(nameof(vm.CurveHorizontalMarkers), raised);
    }

    /// <summary>
    /// A map of round trips: flat out of contact beyond separation 8, a push inside it. The retract pushes only
    /// half as hard, so the two halves are genuinely different curves and "which half" is a question this
    /// fixture can answer.
    /// </summary>
    private static ForceVolumeDataset RoundTripMap()
    {
        const int samples = 20;
        const int points = 2;
        var separation = new float[points * samples];
        var force = new float[points * samples];

        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < samples; i++)
            {
                bool approach = i < 10;
                float z = approach ? 10 - i : i - 9;
                separation[(p * samples) + i] = z;
                float push = z >= 8f ? 0f : (8f - z) * (8f - z) * (approach ? 1f : 0.5f);
                force[(p * samples) + i] = 267f + push;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, samples, points),
            ScanBuffer<float>.TakeOwnership(force, samples, points),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Nanometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            Grid(2, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);
    }

    [Fact]
    public void The_volume_view_puts_the_settings_on_the_curve()
    {
        // doc 26 §22.6 step 2. "50% of the maximum force" is a place on a curve; the panel says the number and
        // the curve has to say where it lands, or a hole in the picture has no explanation on screen.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = RoundTripMap();
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);

        // Two verticals: where the window begins and where it ends. Two horizontals: the non-contact level every
        // force is measured from, and the force the threshold percentage means.
        Assert.Equal(2, vm.CurveVerticalMarkers.Count);
        Assert.Equal(2, vm.CurveHorizontalMarkers.Count);

        // The baseline is the curve's own out-of-contact level, not zero.
        Assert.Equal(267.0, vm.CurveHorizontalMarkers[0], 3);
        Assert.True(vm.CurveHorizontalMarkers[1] > vm.CurveHorizontalMarkers[0]);

        // And the threshold line is where the BOX says, not where a default says.
        double atHalf = vm.CurveHorizontalMarkers[1];
        ((ParameterFormViewModel)vm.OperationEditor!).Fields.Single(f => f.Name == "threshold").Value = 25.0;

        Assert.True(vm.CurveHorizontalMarkers[1] < atHalf);
    }

    [Fact]
    public void The_marks_follow_the_half_the_picture_is_measured_on()
    {
        // The panel's Phase decides which half every pixel came from. Marks drawn on the other one would explain
        // a number the picture does not hold.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = RoundTripMap();
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        double onApproach = vm.CurveHorizontalMarkers[1];

        var phase = ((ParameterFormViewModel)vm.OperationEditor!).Fields.Single(f => f.Name == "phase");
        phase.Value = "Retract";

        // The retract pushes half as hard, so 50% of ITS peak is a lower force.
        Assert.True(vm.CurveHorizontalMarkers[1] < onApproach);
    }

    [Fact]
    public void The_baseline_mark_follows_the_box_that_sets_it()
    {
        // On a curve whose far end never flattens there is no single non-contact level, so how much of the
        // travel you average genuinely changes the answer. That is what makes this wiring visible at all — on a
        // properly flat tail the mark is meant NOT to move.
        const int samples = 20;
        var separation = new float[samples];
        var force = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            separation[i] = 20f - i;              // one long descent: contact from the first sample
            force[i] = 267f + (i * 5f);
        }

        var map = new ForceVolumeDataset(
            DatasetId.New(), new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, samples, 1),
            ScanBuffer<float>.TakeOwnership(force, samples, 1),
            new ChannelDescriptor("Z Scan", ChannelKind.Topography, StandardUnits.Nanometre, "Z Scan"),
            new ChannelDescriptor("Force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            Grid(1, 1), ScanMetadata.Unknown, ProvenanceRecord.Root);

        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        double narrow = vm.CurveHorizontalMarkers[0];

        var baseline = ((ParameterFormViewModel)vm.OperationEditor!).Fields.Single(f => f.Name == "baseline");
        baseline.Value = 60.0;

        // A wider window reaches further down the ramp, so the level it averages is higher up the force axis.
        Assert.True(vm.CurveHorizontalMarkers[0] > narrow);
    }

    [Fact]
    public void The_volume_view_pins_the_inspector_curve_to_the_pair_the_picture_was_measured_from()
    {
        // The marks are a force level and two separations. Drawn over some other channel pair — a voltage
        // against a bias, say — they would be nN and nm lines on axes that are neither, and they would look
        // exactly as authoritative as the correct ones. So in the Volume view the curve is not the picker's to
        // choose: it is the pair the picture came from.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapWithChannels(2);
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.True(vm.InspectorCurveFollowsChannelPicker);

        // Leave the picker somewhere other than the designated pair, the way UX04 lets a viewer do.
        vm.SelectedYChannel = 2;
        Assert.False(vm.IsDesignatedChannelPair);
        Assert.True(vm.InspectorCurveFollowsChannelPicker);   // the Surface view is still the picker's

        vm.ShowVolumeCommand.Execute(null);

        Assert.False(vm.InspectorCurveFollowsChannelPicker);
    }

    [Fact]
    public void Leaving_the_volume_view_gives_the_curve_back_to_the_picker()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);
        Assert.False(vm.InspectorCurveFollowsChannelPicker);

        vm.ShowSurfaceCommand.Execute(null);

        Assert.True(vm.InspectorCurveFollowsChannelPicker);
    }

    [Fact]
    public void A_position_recorded_in_another_length_is_converted_before_it_is_placed()
    {
        // The one reader that exists gives micrometres for both the layout and the surface axes, which is luck
        // rather than a rule. Dividing 1000 nm by a step of 1 um/pixel puts the marker a thousand pixels off the
        // image; dividing 1 um by it puts it where the curve was actually measured.
        var ws = new Workspace();
        var vm = WithActiveMap(
            ws,
            MapOnSurface(Layout(StandardUnits.Nanometre, (1000, 2000)), points: 1, surfaceUnit: StandardUnits.Micrometre));

        var (x, y) = Assert.Single(vm.PointMarkers);

        Assert.Equal(1.0, x, 6);   // 1000 nm = 1 um, one pixel along
        Assert.Equal(2.0, y, 6);
    }

    [Fact]
    public void The_same_unit_on_both_sides_still_places_the_marker_where_it_was()
    {
        // The conversion must be a no-op on the path that already worked, not a scale factor applied twice.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, MapOnSurface(Layout((1, 1), (3, 2))));

        Assert.Equal((1.0, 1.0), vm.PointMarkers[0]);
        Assert.Equal((3.0, 2.0), vm.PointMarkers[1]);
    }

    [Theory]
    [InlineData(false)]   // neither axis is a length
    [InlineData(true)]    // X is, Y is not — checking only the first would pass this one through
    public void A_layout_that_cannot_be_expressed_on_these_axes_places_nothing(bool xIsALength)
    {
        // A marker put somewhere arbitrary is a claim about where a curve was measured. There is no honest
        // position for a length on an axis that is not one — and a failed conversion reads as zero, which would
        // pile every marker into the corner rather than announce itself.
        var ws = new Workspace();
        var vm = WithActiveMap(
            ws,
            MapOnSurface(
                Layout((1, 1)),
                points: 1,
                surfaceUnit: xIsALength ? StandardUnits.Micrometre : StandardUnits.Volt,
                surfaceUnitY: StandardUnits.Volt));

        Assert.True(vm.HasReferenceSurface);
        Assert.Empty(vm.PointMarkers);
    }

    [Fact]
    public void Each_axis_is_converted_against_its_own_unit()
    {
        // An axis carries its own unit, so the two need not agree. Converting Y against the X axis is the kind
        // of slip that is invisible on every square, single-unit image and wrong by a thousand on the first one
        // that is not.
        var ws = new Workspace();
        var vm = WithActiveMap(
            ws,
            MapOnSurface(
                Layout(StandardUnits.Nanometre, (1000, 1000)),
                points: 1,
                surfaceUnit: StandardUnits.Micrometre,
                surfaceUnitY: StandardUnits.Nanometre));

        var (x, y) = Assert.Single(vm.PointMarkers);

        Assert.Equal(1.0, x, 6);      // 1000 nm on a um axis of 1 um/pixel
        Assert.Equal(1000.0, y, 6);   // 1000 nm on a nm axis of 1 nm/pixel
    }

    [Fact]
    public void Clicking_a_volume_pixel_selects_the_point_it_was_computed_from()
    {
        // The picture is the map laid out on its own grid, one pixel per point, so the mapping is exact — no
        // nearest-neighbour, no interpolation. This is the shortest route from a value on the picture to the
        // curve behind it, and from a hole to the reason it is one.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        // Row-major, and chosen so it cannot agree with the column-major reading: on a 3x2 grid (1,1) is point 4
        // going along X first and point 3 going down Y first.
        vm.SelectMapPointAt(column: 1, row: 1);

        Assert.Equal(4, vm.SelectedMapPoint);
    }

    [Fact]
    public void A_click_on_the_surface_is_not_a_point_selection()
    {
        // A 128x128 surface can carry an 8x8 map. Treating one of its pixels as a point would silently select
        // whichever point happened to share an index — a selection the viewer never made.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.False(vm.IsVolumeView);
        vm.SelectMapPointAt(column: 1, row: 0);

        Assert.Equal(0, vm.SelectedMapPoint);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    public void A_click_outside_the_grid_selects_nothing(int column, int row)
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);
        vm.SelectedMapPoint = 4;

        vm.SelectMapPointAt(column, row);

        Assert.Equal(4, vm.SelectedMapPoint);
    }

    [Fact]
    public void A_map_with_no_grid_has_no_pixel_to_click()
    {
        // Without a grid there is no volume image either, so there is nothing on screen to click — but the
        // guard belongs here rather than relying on that.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(4);
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.SelectMapPointAt(0, 0);

        Assert.Equal(0, vm.SelectedMapPoint);
    }

    [Fact]
    public void The_volume_picture_marks_the_selected_point_and_only_it()
    {
        // Not all of them: on a picture where every pixel is a point, a mark on each is noise drawn over the
        // thing it marks. Not none either: the mark is the only thing saying which of the curves the panel
        // beside it describes, and the only confirmation that a click landed where the viewer meant.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        vm.SelectMapPointAt(column: 1, row: 1);

        // The centre of that point's OWN pixel — the picture's coordinates, not the surface's.
        Assert.Equal((1.5, 1.5), Assert.Single(vm.PointMarkers));
        Assert.Equal(0, vm.SelectedPointMarker);
    }

    [Fact]
    public void The_surface_marks_every_point_and_says_which_is_selected()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.SelectedMapPoint = 1;

        Assert.Equal(2, vm.PointMarkers.Count);
        Assert.Equal(1, vm.SelectedPointMarker);
    }

    [Fact]
    public void The_mark_follows_the_point_through_the_volume_view()
    {
        // The redraw path asks again on every point change; a mark left behind would point at the previous
        // curve while the panel describes this one.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(6, Grid(3, 2));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        var before = Assert.Single(vm.PointMarkers);
        vm.StepMapPoint(1);

        Assert.NotEqual(before, Assert.Single(vm.PointMarkers));
    }

    [Fact]
    public void A_volume_view_of_a_map_with_no_grid_marks_nothing()
    {
        // Without a grid there is no picture either, so there is no pixel for a mark to sit on.
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = Map(4);
        ws.Add(map);
        ws.SetActive(map.Id);

        Assert.Empty(vm.PointMarkers);
    }

    [Fact]
    public void Leaving_the_volume_view_puts_the_markers_back()
    {
        var ws = new Workspace();
        var vm = NewShell(ws, new VolumeLauncher());
        var map = MapOnSurface(Layout((1, 1), (3, 2)), geometry: Grid(2, 1));
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);

        vm.ShowSurfaceCommand.Execute(null);

        Assert.Equal(2, vm.PointMarkers.Count);
    }
}
