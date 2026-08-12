using System;
using System.Collections.Generic;
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
using SmartAnalysis.Domain.Units;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Services;
using SmartAnalysis.UI.ViewModels;
using Xunit;

namespace SmartAnalysis.UiTests.ViewModels;

/// <summary>
/// P01-UI data-loss guard: opening a workspace must not silently discard a <b>dirty</b> session. The shell
/// prompts Save / Don't Save / Cancel and only replaces the workspace when the user allows it.
/// (ShellViewModel is plain — no WPF Application/STA needed.)
/// </summary>
public sealed class ShellOpenGuardTests
{
    private static ShellViewModel NewShell(Workspace ws, FakePersistence persistence, FakePathPicker picker, FakePrompt prompt)
        => new(ws, new FakeReader(), new ThemeManager(), new FakeScanPicker(), new FakeImageAnalysis(),
               new FakeLauncher(), new MeasurementStore(), persistence, picker, prompt);

    private static ScanImageDataset Image() => new(
        DatasetId.New(),
        new DataSource("test", null),
        new Axis("X", StandardUnits.Nanometre, 0, 1, 2),
        new Axis("Y", StandardUnits.Nanometre, 0, 1, 2),
        new ChannelDescriptor("height", ChannelKind.Topography, StandardUnits.Nanometre),
        ScanBuffer<float>.Allocate(2, 2),
        ScanMetadata.Unknown,
        ProvenanceRecord.Root);

    [Fact]
    public void Open_on_a_clean_workspace_opens_without_prompting()
    {
        var ws = new Workspace();
        var persistence = new FakePersistence();
        var picker = new FakePathPicker { OpenFolder = "C:/ws" };
        var prompt = new FakePrompt();
        var vm = NewShell(ws, persistence, picker, prompt);

        vm.OpenWorkspaceCommand.Execute(null);

        Assert.Equal(0, prompt.Calls);                       // clean → no prompt
        Assert.Equal("C:/ws", Assert.Single(persistence.Opened));
    }

    [Fact]
    public void Open_on_a_dirty_workspace_and_Cancel_does_not_open_or_lose_work()
    {
        var ws = new Workspace();
        var persistence = new FakePersistence();
        var picker = new FakePathPicker { OpenFolder = "C:/ws" };
        var prompt = new FakePrompt { Choice = UnsavedChangesChoice.Cancel };
        var vm = NewShell(ws, persistence, picker, prompt);
        ws.Add(Image());                                     // makes it dirty
        Assert.True(vm.HasUnsavedChanges);

        vm.OpenWorkspaceCommand.Execute(null);

        Assert.Equal(1, prompt.Calls);
        Assert.Empty(persistence.Opened);                    // did NOT open
        Assert.Equal(1, ws.Count);                           // work preserved
        Assert.True(vm.HasUnsavedChanges);                   // still dirty
    }

    [Fact]
    public void Open_on_a_dirty_workspace_and_DontSave_opens_without_saving()
    {
        var ws = new Workspace();
        var persistence = new FakePersistence();
        var picker = new FakePathPicker { OpenFolder = "C:/ws" };
        var prompt = new FakePrompt { Choice = UnsavedChangesChoice.DontSave };
        var vm = NewShell(ws, persistence, picker, prompt);
        ws.Add(Image());

        vm.OpenWorkspaceCommand.Execute(null);

        Assert.Empty(persistence.Saved);
        Assert.Equal("C:/ws", Assert.Single(persistence.Opened));
    }

    [Fact]
    public void Open_on_a_dirty_workspace_and_Save_saves_then_opens()
    {
        var ws = new Workspace();
        var persistence = new FakePersistence();
        var picker = new FakePathPicker { SaveFolder = "C:/save", OpenFolder = "C:/open" };
        var prompt = new FakePrompt { Choice = UnsavedChangesChoice.Save };
        var vm = NewShell(ws, persistence, picker, prompt);
        ws.Add(Image());

        vm.OpenWorkspaceCommand.Execute(null);

        Assert.Equal("C:/save", Assert.Single(persistence.Saved));
        Assert.Equal("C:/open", Assert.Single(persistence.Opened));
    }

    [Fact]
    public void Open_on_a_dirty_workspace_where_Save_is_cancelled_aborts_the_open()
    {
        var ws = new Workspace();
        var persistence = new FakePersistence();
        var picker = new FakePathPicker { SaveFolder = null, OpenFolder = "C:/open" }; // save-folder picker cancelled
        var prompt = new FakePrompt { Choice = UnsavedChangesChoice.Save };
        var vm = NewShell(ws, persistence, picker, prompt);
        ws.Add(Image());

        vm.OpenWorkspaceCommand.Execute(null);

        Assert.Empty(persistence.Saved);   // nothing written (picker cancelled)
        Assert.Empty(persistence.Opened);  // open aborted — current work kept
    }

    // ---- minimal fakes ----
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
        public List<string> Saved { get; } = new();
        public List<string> Opened { get; } = new();
        public PersistenceOutcome SaveResult { get; init; } = PersistenceOutcome.Ok;
        public PersistenceOutcome OpenResult { get; init; } = PersistenceOutcome.Ok;

        public PersistenceOutcome Save(string path) { Saved.Add(path); return SaveResult; }

        public PersistenceOutcome Open(string path) { Opened.Add(path); return OpenResult; }
    }

    private sealed class FakePathPicker : IWorkspacePathPicker
    {
        public string? SaveFolder { get; init; }
        public string? OpenFolder { get; init; }

        public string? PickSaveFolder() => SaveFolder;

        public string? PickOpenFolder() => OpenFolder;
    }

    private sealed class FakePrompt : IUnsavedChangesPrompt
    {
        public int Calls { get; private set; }
        public UnsavedChangesChoice Choice { get; init; } = UnsavedChangesChoice.Cancel;

        public UnsavedChangesChoice Ask(string workspaceName) { Calls++; return Choice; }
    }
}
