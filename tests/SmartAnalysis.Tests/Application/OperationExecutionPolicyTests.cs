using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.Visualization.Colormaps;
using Xunit;

namespace SmartAnalysis.Tests.Application;

/// <summary>
/// Whose thread an operation runs on.
/// <para>
/// Every operation computes straight through and returns an already-completed task, so awaiting one holds the
/// caller's thread for the whole computation. On the UI thread that is a dead window: nothing repaints and
/// nothing responds. It scales with the data — a 64x64 force-volume map is 4096 curves, about 140 ms, and the Volume view recomputes on every
/// keystroke in its settings.
/// </para>
/// <para>
/// So the operation declares what it <b>is</b> and the Application layer decides where it runs. An operation that
/// waits on I/O rather than computing must not be handed to the thread pool — that would occupy a thread to do
/// nothing — so the flag is not a synonym for "run it elsewhere".
/// </para>
/// </summary>
public sealed class OperationExecutionPolicyTests
{
    private static ScanImageDataset Image()
        => new(
            DatasetId.New(),
            new DataSource("test", null),
            new Axis("X", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new Axis("Y", StandardUnits.Micrometre, 0.0, 1.0, 2),
            new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
            ScanBuffer<float>.Allocate(2, 2),
            ScanMetadata.Unknown,
            ProvenanceRecord.Root);

    private static (Workspace Workspace, IOperationLauncher Launcher) With(IAnalysisOperation operation)
    {
        var ws = new Workspace();
        var image = Image();
        ws.Add(image);
        ws.SetActive(image.Id);
        return (ws, new OperationLauncherUseCase(ws, new OperationRegistry([operation]), new MeasurementStore()));
    }

    [Fact]
    public void An_operation_computes_unless_it_says_otherwise()
    {
        // The safe default, read off a descriptor that does not pass the flag at all. Reading it off a fake that
        // passes it explicitly would only assert the fake's own default, which is what this test did first.
        var descriptor = new OperationDescriptor(
            id: "test.defaults",
            version: 1,
            displayName: "Defaults",
            summary: "Declares nothing about where it runs.",
            acceptedInputs: [DataKind.ScanImage],
            parameters: new ParameterSchema([]),
            output: OutputKind.DerivedDataset);

        Assert.True(descriptor.IsCpuBound);
    }

    [Fact]
    public async Task A_computing_operation_does_not_hold_the_thread_that_asked_for_it()
    {
        var operation = new BlockingOperation("test.cpu");
        var (_, launcher) = With(operation);

        var run = launcher.PreviewAsync("test.cpu", new Dictionary<string, object?>(), Colormap.Grayscale, null);

        // The caller is back before the operation is: this is the whole point, and asserting it on the returned
        // task rather than on elapsed time makes it a property rather than a race.
        Assert.False(run.IsCompleted);
        Assert.True(operation.Entered.Wait(TimeSpan.FromSeconds(5)));

        operation.Release();
        await run;
        Assert.NotEqual(Environment.CurrentManagedThreadId, operation.RanOnThread);
    }

    [Fact]
    public async Task An_operation_that_waits_rather_than_computes_stays_on_the_caller_s_thread()
    {
        // Handing an I/O wait to the thread pool spends a thread on doing nothing. The flag says which kind it
        // is, so this one must NOT be moved.
        var operation = new BlockingOperation("test.io", cpuBound: false, holds: false);
        var (_, launcher) = With(operation);

        await launcher.PreviewAsync("test.io", new Dictionary<string, object?>(), Colormap.Grayscale, null);

        Assert.Equal(Environment.CurrentManagedThreadId, operation.RanOnThread);
    }

    [Fact]
    public async Task A_running_operation_keeps_the_dataset_it_is_reading_alive()
    {
        // The barrier the offload removed. While an operation held the caller's thread, that thread could not
        // also remove the dataset; now it can, and a ScanBuffer view must not outlive its owner.
        var operation = new BlockingOperation("test.cpu");
        var (ws, launcher) = With(operation);
        var source = ws.Datasets.OfType<ScanImageDataset>().Single();

        var run = launcher.RunAsync("test.cpu", new Dictionary<string, object?>());
        Assert.True(operation.Entered.Wait(TimeSpan.FromSeconds(5)));

        var removed = ws.Remove(source.Id, RemovalPolicy.Cascade);
        Assert.True(removed.Removed);

        // Gone from the workspace at once — the user said remove and it is removed ...
        Assert.False(ws.TryGet(source.Id, out _));

        // ... but its storage is still there for the reader that is mid-way through it.
        Assert.True(ws.IsLeased(source.Id));
        _ = source.Data.Memory;

        operation.Release();
        await run;

        // And once the reader lets go, the disposal that was deferred actually happens.
        Assert.False(ws.IsLeased(source.Id));
        Assert.Throws<ObjectDisposedException>(() => source.Data.Memory);
    }

    [Fact]
    public async Task A_result_whose_source_was_removed_mid_run_is_not_committed()
    {
        // Adding it would leave a dataset whose provenance names a parent that is not there.
        var operation = new BlockingOperation("test.cpu");
        var (ws, launcher) = With(operation);
        var source = ws.Datasets.OfType<ScanImageDataset>().Single();

        var run = launcher.RunAsync("test.cpu", new Dictionary<string, object?>());
        Assert.True(operation.Entered.Wait(TimeSpan.FromSeconds(5)));
        ws.Remove(source.Id, RemovalPolicy.Cascade);
        operation.Release();

        var result = await run;

        Assert.False(result.Success);
        Assert.Contains("removed before it finished", result.Error);
        Assert.Empty(ws.Datasets);   // the source is gone and nothing orphaned took its place
    }

    [Fact]
    public void Replacing_a_workspace_defers_a_leased_dataset_too()
    {
        // Open is another disposal path, and so is disposing the workspace. Guarding only Remove would leave the
        // race in place for two of the three ways a dataset's storage goes away.
        var ws = new Workspace();
        var source = Image();
        ws.Add(source);

        using (ws.Lease([source.Id]))
        {
            using var replacement = new Workspace();
            ws.ReplaceWith(replacement);
            _ = source.Data.Memory;
        }

        Assert.Throws<ObjectDisposedException>(() => source.Data.Memory);
        ws.Dispose();
    }

    [Fact]
    public void Disposing_a_workspace_defers_a_leased_dataset_too()
    {
        var ws = new Workspace();
        var source = Image();
        ws.Add(source);

        using (ws.Lease([source.Id]))
        {
            ws.Dispose();
            _ = source.Data.Memory;
        }

        Assert.Throws<ObjectDisposedException>(() => source.Data.Memory);
    }

    [Fact]
    public void A_dataset_nothing_is_reading_is_disposed_at_once()
    {
        // The lease must not turn every removal into a deferred one.
        var ws = new Workspace();
        var source = Image();
        ws.Add(source);

        ws.Remove(source.Id, RemovalPolicy.Cascade);

        Assert.False(ws.IsLeased(source.Id));
        Assert.Throws<ObjectDisposedException>(() => source.Data.Memory);
        ws.Dispose();
    }

    /// <summary>An operation that holds until released, so "did the caller come back first" is answerable.</summary>
    private sealed class BlockingOperation(string id, bool cpuBound = true, bool holds = true) : IAnalysisOperation
    {
        private readonly ManualResetEventSlim _release = new(initialState: !holds);

        public ManualResetEventSlim Entered { get; } = new(initialState: false);

        public int RanOnThread { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            id: id,
            version: 1,
            displayName: "Blocking",
            summary: "Holds until released.",
            acceptedInputs: [DataKind.ScanImage],
            parameters: new ParameterSchema([]),
            output: OutputKind.DerivedDataset,
            derivedKind: DataKind.ScanImage,
            isCpuBound: cpuBound);

        public bool IsApplicableTo(AfmDataset dataset) => dataset is ScanImageDataset;

        public ValidationResult Validate(OperationInput input, IParameterSet parameters) => ValidationResult.Success;

        public Task<OperationResult> RunAsync(
            OperationInput input, IParameterSet parameters, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            RanOnThread = Environment.CurrentManagedThreadId;
            Entered.Set();
            // Bounded: a mutation that runs this on the caller's thread must fail the test, not hang it.
            _release.Wait(TimeSpan.FromSeconds(2), cancellationToken);
            return Task.FromResult(OperationResult.Derived(Image()));
        }

        public void Release() => _release.Set();
    }
}
