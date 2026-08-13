using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.UI.ViewModels;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// Part 2 palette state on the shell: the colormap picker (catalog-backed) and the Auto/Manual value range.
/// ShellViewModel is plain (no WPF Application/STA needed).
/// </summary>
public sealed class ShellPaletteTests
{
    private static ShellViewModel NewShell()
        => new(new Workspace(), new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
               new FakeLauncher(), new MeasurementStore(), new FakePersistence(), new FakePathPicker(), new FakePrompt());

    [Fact]
    public void Defaults_to_the_Gold_colormap_and_auto_range()
    {
        var vm = NewShell();

        Assert.Equal("Gold", vm.ColormapName);
        Assert.Same(ColormapCatalog.ByName("Gold"), vm.Colormap);
        Assert.Equal(ColormapCatalog.Names, vm.AvailableColormaps);
        Assert.True(vm.AutoRange);
        Assert.Null(vm.EffectiveRange); // auto → the view uses each image's data min/max
    }

    [Fact]
    public void Selecting_a_colormap_swaps_it_and_requests_a_re_render()
    {
        var vm = NewShell();
        int renders = 0;
        vm.ImagesChanged += (_, _) => renders++;

        vm.ColormapName = "Grayscale";

        Assert.Same(ColormapCatalog.ByName("Grayscale"), vm.Colormap);
        Assert.Equal(1, renders);
    }

    [Fact]
    public void A_manual_range_yields_an_explicit_effective_range_and_re_renders()
    {
        var vm = NewShell();
        int renders = 0;
        vm.ImagesChanged += (_, _) => renders++;

        vm.AutoRange = false;   // enter manual (re-render #1)
        vm.RangeMin = -5.0;     // #2
        vm.RangeMax = 5.0;      // #3

        Assert.True(vm.ManualRangeEnabled);
        Assert.Equal(new ValueRange(-5.0, 5.0), vm.EffectiveRange);
        Assert.Equal(3, renders);
    }

    [Fact]
    public void Dragging_the_palette_bar_sets_a_manual_range_and_re_renders_once()
    {
        var vm = NewShell();
        int renders = 0;
        vm.ImagesChanged += (_, _) => renders++;

        vm.SetManualRange(-3.0, 7.0); // as raised by a palette-bar handle drag commit

        Assert.False(vm.AutoRange);
        Assert.Equal(-3.0, vm.RangeMin, 9);
        Assert.Equal(7.0, vm.RangeMax, 9);
        Assert.Equal(new ValueRange(-3.0, 7.0), vm.EffectiveRange);
        Assert.Equal(1, renders); // a single re-render on commit, not per drag step
    }

    [Fact]
    public void An_invalid_manual_range_falls_back_to_auto()
    {
        var vm = NewShell();
        vm.AutoRange = false;
        vm.RangeMin = 5.0;
        vm.RangeMax = 5.0; // max <= min → not a usable range

        Assert.Null(vm.EffectiveRange);
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
