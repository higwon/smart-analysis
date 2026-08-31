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
/// caller's thread for the whole computation. On the UI thread that is a dead window: nothing repaints, nothing
/// responds, and the <c>IProgress</c> the operation is handed has no thread left to report on. It scales with the
/// data — a 64x64 force-volume map is 4096 curves, about 140 ms, and the Volume view recomputes on every
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
