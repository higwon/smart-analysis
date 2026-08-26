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
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

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
        Assert.Contains("(0, 0.5)", vm.MapPointLabel);
        Assert.Contains("um", vm.MapPointLabel);
    }

    [Fact]
    public void A_map_without_a_grid_says_so_rather_than_showing_a_position_it_does_not_have()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(4)); // hand-placed points: no geometry

        Assert.Contains("Point 1 of 4", vm.MapPointLabel);
        Assert.Contains("no grid", vm.MapPointLabel);
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
    [Fact]
    public void A_grid_map_offers_a_cell_for_every_point()
    {
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        Assert.True(vm.HasMapGrid);
        Assert.Equal(6, vm.MapCells.Count);
        Assert.Equal(3, vm.MapGridColumns);

        // Cells run along X first, the same order the payload stores its spectra in.
        Assert.Equal((1, 1), (vm.MapCells[0].Column, vm.MapCells[0].Row));
        Assert.Equal((3, 1), (vm.MapCells[2].Column, vm.MapCells[2].Row));
        Assert.Equal((1, 2), (vm.MapCells[3].Column, vm.MapCells[3].Row));
    }

    [Fact]
    public void A_cell_says_where_on_the_sample_it_is()
    {
        // The whole point of picking spatially: the cell has to mean a place, not just an ordinal.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        Assert.Contains("(0, 0.5)", vm.MapCells[4].Tooltip);
        Assert.Contains("um", vm.MapCells[4].Tooltip);
    }

    [Fact]
    public void Exactly_one_cell_is_selected_and_it_is_the_one_on_the_stage()
    {
        // A picker highlighting a different point than the plot is worse than no picker.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(6, Grid(3, 2)));

        Assert.Equal(0, vm.MapCells.Count(c => c.IsSelected) - 1);
        Assert.True(vm.MapCells[0].IsSelected);

        vm.SelectedMapPoint = 4;

        Assert.Single(vm.MapCells.Where(c => c.IsSelected));
        Assert.True(vm.MapCells[4].IsSelected);
        Assert.False(vm.MapCells[0].IsSelected);
    }

    [Fact]
    public void A_map_with_no_grid_draws_no_picker()
    {
        // Hand-placed points have no layout. Laying them out in a rectangle would imply a spatial arrangement
        // the instrument never recorded — the same reason the geometry itself is nullable.
        var ws = new Workspace();
        var vm = WithActiveMap(ws, Map(4));

        Assert.False(vm.HasMapGrid);
        Assert.Empty(vm.MapCells);
        Assert.Equal(0, vm.MapGridColumns);
    }

    [Fact]
    public void The_picker_is_rebuilt_for_the_next_map()
    {
        var ws = new Workspace();
        var first = Map(6, Grid(3, 2));
        var second = Map(4, Grid(2, 2));
        var vm = NewShell(ws);
        ws.Add(first);
        ws.Add(second);

        ws.SetActive(first.Id);
        Assert.Equal(6, vm.MapCells.Count);

        ws.SetActive(second.Id);

        Assert.Equal(4, vm.MapCells.Count);
        Assert.Equal(2, vm.MapGridColumns);
        Assert.True(vm.MapCells[0].IsSelected);
    }
}
