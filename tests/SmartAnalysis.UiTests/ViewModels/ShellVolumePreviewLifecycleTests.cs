using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
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
/// What the Volume view shows while its picture is being computed, and when the picture arrives.
/// <para>
/// Every operation runs straight through and hands back an already-completed task, so a preview has always
/// landed inside the call that asked for it. That hid a state gap: entering the view clears the preview, and
/// until the first one arrives there is nothing to draw — so the Stage kept showing the <b>surface</b> while the
/// panel beside it described a volume image. On a 64x64 map the computation is ~140 ms, which is long enough to
/// read as nothing having happened.
/// </para>
/// <para>
/// These tests hold the preview open deliberately, so the view is exercised in the state the timing normally
/// skips past. What they pin is that the Stage follows the preview's <b>state</b> — not the order the call stack
/// happens to unwind in.
/// </para>
/// </summary>
public sealed class ShellVolumePreviewLifecycleTests
{
    private const string VolumeId = "force-volume.volume-image";
    private const int Samples = 8;

    private static ShellViewModel NewShell(Workspace ws, IOperationLauncher launcher)
        => new(
            ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
            new SpectroscopyParameterPreviewUseCase(), launcher, new MeasurementStore(), new FakePersistence(),
            new FakePathPicker(), new FakePrompt());

    private static ForceVolumeDataset Map()
    {
        const int points = 4;
        var separation = new float[points * Samples];
        var force = new float[points * Samples];
        int half = Samples / 2;
        for (int p = 0; p < points; p++)
        {
            for (int i = 0; i < Samples; i++)
            {
                separation[(p * Samples) + i] = i < half ? half - i : i - half + 1;
                force[(p * Samples) + i] = i == half - 1 ? p + 1 : 0f;
            }
        }

        return new ForceVolumeDataset(
            DatasetId.New(),
            new DataSource("test", null),
            ScanBuffer<float>.TakeOwnership(separation, Samples, points),
            ScanBuffer<float>.TakeOwnership(force, Samples, points),
            new ChannelDescriptor("separation", ChannelKind.Topography, StandardUnits.Micrometre, "Z"),
            new ChannelDescriptor("force", ChannelKind.Force, StandardUnits.Nanonewton, "Force"),
            new ForceVolumeGeometry(2, 2, 1.0, 1.0, 0.0, 0.0, StandardUnits.Micrometre),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);
    }

    private static ImageRenderInput Picture(float value)
        => new(
            new float[] { value, value, value, value },
            2, 2,
            new ValueRange(0, 1),
            Colormap.Grayscale,
            new AxisView("X", "um", 0, 1, 2),
            new AxisView("Y", "um", 0, 1, 2),
            "nN");

    private static (ShellViewModel Shell, HeldLauncher Launcher) InVolumeView()
    {
        var launcher = new HeldLauncher();
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = Map();
        ws.Add(map);
        ws.SetActive(map.Id);
        vm.ShowVolumeCommand.Execute(null);
        return (vm, launcher);
    }

    [Fact]
    public void Entering_the_volume_view_says_it_is_measuring_rather_than_leaving_the_stage_alone()
    {
        var (vm, launcher) = InVolumeView();

        Assert.True(vm.IsVolumeView);
        Assert.Single(launcher.Pending);
        Assert.True(vm.IsVolumeComputing);
        Assert.Null(vm.OperationPreviewInput);
        Assert.False(vm.HasVolumeUnavailable);   // not "cannot be computed" — not computed YET
    }

    [Fact]
    public void The_picture_arrives_when_the_preview_completes_later()
    {
        var (vm, launcher) = InVolumeView();
        int renders = 0;
        vm.ImagesChanged += (_, _) => renders++;

        launcher.Complete(0, Picture(1f));

        Assert.False(vm.IsVolumeComputing);
        Assert.NotNull(vm.OperationPreviewInput);
        Assert.Equal(1, renders);   // the stage is told, rather than left to find out
    }

    [Fact]
    public void A_preview_that_completes_at_once_leaves_the_same_state()
    {
        // The path everything took until now. It must end where the delayed one ends, or the two disagree about
        // what the view shows and only the timing decides which is right.
        var launcher = new HeldLauncher { CompleteAtOnce = Picture(2f) };
        var ws = new Workspace();
        var vm = NewShell(ws, launcher);
        var map = Map();
        ws.Add(map);
        ws.SetActive(map.Id);

        vm.ShowVolumeCommand.Execute(null);

        Assert.False(vm.IsVolumeComputing);
        Assert.NotNull(vm.OperationPreviewInput);
        Assert.False(vm.HasVolumeUnavailable);
    }

    [Fact]
    public void A_superseded_preview_never_reaches_the_stage()
    {
        // Two requests in flight, the older finishing last. Applying it would put the previous settings' picture
        // on a stage whose panel shows the current ones.
        var (vm, launcher) = InVolumeView();
        var form = (ParameterFormViewModel)vm.OperationEditor!;
        form.Fields.Single(f => f.Name == "threshold").Value = 25.0;

        Assert.Equal(2, launcher.Pending.Count);

        launcher.Complete(1, Picture(9f));   // the newer one first
        var newer = vm.OperationPreviewInput;
        launcher.Complete(0, Picture(1f));   // then the stale one

        Assert.Same(newer, vm.OperationPreviewInput);
    }

    [Fact]
    public void A_preview_that_yields_nothing_says_why_rather_than_staying_pending()
    {
        var (vm, launcher) = InVolumeView();
        launcher.Refusal = "The threshold is not on this curve.";

        launcher.Complete(0, null);

        Assert.False(vm.IsVolumeComputing);
        Assert.True(vm.HasVolumeUnavailable);
        Assert.Equal("The threshold is not on this curve.", vm.VolumeUnavailable);
    }

    [Fact]
    public void A_preview_that_throws_is_not_left_pending_either()
    {
        // Best-effort: a failed run shows no picture, never an error banner. But it must still leave the pending
        // state, or the view says it is measuring something that stopped being measured.
        var (vm, launcher) = InVolumeView();

        launcher.Fail(0, new InvalidOperationException("boom"));

        Assert.False(vm.IsVolumeComputing);
        Assert.Null(vm.OperationPreviewInput);
        Assert.True(vm.HasVolumeUnavailable);
    }

    [Fact]
    public void Leaving_the_volume_view_while_a_preview_is_in_flight_is_not_still_measuring()
    {
        var (vm, launcher) = InVolumeView();

        vm.ShowSurfaceCommand.Execute(null);

        Assert.False(vm.IsVolumeView);
        Assert.False(vm.IsVolumeComputing);

        // And a preview that lands afterwards has no stage to claim.
        launcher.Complete(0, Picture(1f));
        Assert.False(vm.IsVolumeComputing);
    }

    // --- fakes (this project keeps them per-file; see the other Shell* tests) ---

    /// <summary>A launcher that holds each preview open until the test says otherwise.</summary>
    private sealed class HeldLauncher : IOperationLauncher
    {
        public List<TaskCompletionSource<ImageRenderInput?>> Pending { get; } = new();

        /// <summary>When set, previews complete synchronously with this picture — the old behaviour.</summary>
        public ImageRenderInput? CompleteAtOnce { get; set; }

        public string? Refusal { get; set; }

        public void Complete(int index, ImageRenderInput? picture) => Pending[index].SetResult(picture);

        public void Fail(int index, Exception error) => Pending[index].SetException(error);

        public IReadOnlyList<OperationLauncherItem> ApplicableToActive() => Array.Empty<OperationLauncherItem>();

        public OperationForm? GetForm(string operationId)
            => operationId == VolumeId
                ? new OperationForm(
                    operationId, "Volume Image", "one pixel per point", OperationCategory.Process,
                    [
                        new ParameterFieldDescriptor("threshold", "Threshold", ParameterFieldKind.Number, 50.0, 0.0, 100.0, [], null, ""),
                    ],
                    DerivesImage: true)
                : null;

        public Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
            => Task.FromResult(OperationRunResult.Derived(DatasetId.New(), []));

        public Task<ImageRenderInput?> PreviewAsync(string operationId, IReadOnlyDictionary<string, object?> values, Colormap colormap, ValueRange? range, CancellationToken ct = default)
        {
            if (CompleteAtOnce is { } now)
            {
                return Task.FromResult<ImageRenderInput?>(now);
            }

            // Continuations run inline on SetResult: what these tests need is that the preview did not finish
            // inside PreviewAsync, not that it finished on another thread.
            var pending = new TaskCompletionSource<ImageRenderInput?>();
            Pending.Add(pending);
            return pending.Task;
        }

        public string? Explain(string operationId, IReadOnlyDictionary<string, object?> values) => Refusal;
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
