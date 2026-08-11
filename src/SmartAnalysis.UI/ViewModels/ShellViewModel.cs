using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Mvvm;
using SmartAnalysis.UI.Services;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// The shell view-model (U01): one workspace, one active context (doc 22). Binds the command bar,
/// explorer lineage tree, active-view header, and history to the real <see cref="Workspace"/>. Import
/// reads a scan through the <see cref="IScanFileReader"/> port; theme is driven by <see cref="ThemeManager"/>.
/// The 2D viewer, parameter panel, and Save/Compare are wired in later tasks (U02 / P01-UI).
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly IScanFileReader _reader;
    private readonly ThemeManager _theme;
    private readonly IScanFilePicker _picker;

    private string _workspaceName = "Untitled workspace";
    private bool _hasUnsavedChanges;
    private string? _statusMessage;
    private string? _activeContextText;
    private string? _activeTitle;
    private string? _activeSubtitle;
    private string? _activeMeta;

    public ShellViewModel(Workspace workspace, IScanFileReader reader, ThemeManager theme, IScanFilePicker picker)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));

        ImportCommand = new AsyncRelayCommand(ImportAsync, onError: OnCommandError);
        OpenSampleCommand = new AsyncRelayCommand(OpenSampleAsync, () => SamplePath is not null, OnCommandError);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        SaveCommand = new RelayCommand(() => { }, () => false); // stub — persistence UI is a later task (P01)

        // Topology changes (datasets added/removed) rebuild the tree; an active/comparison change only
        // refreshes existing nodes' state — so selection + expansion in the TreeView are preserved.
        _workspace.DatasetsChanged += (_, _) => RebuildTopology();
        _workspace.ActiveContextChanged += (_, _) => RefreshActiveState();
        _theme.ThemeChanged += (_, _) => OnPropertyChanged(nameof(ThemeToggleLabel));
        RebuildTopology();
    }

    private readonly Dictionary<DatasetId, DatasetNodeViewModel> _nodesById = new();

    /// <summary>Path to the bundled sample scan (set by the composition root / render harness).</summary>
    public string? SamplePath { get; set; }

    public ObservableCollection<DatasetNodeViewModel> ExplorerNodes { get; } = new();

    public ObservableCollection<HistoryRowViewModel> HistoryRows { get; } = new();

    public ICommand ImportCommand { get; }
    public ICommand OpenSampleCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand SaveCommand { get; }

    public string WorkspaceName
    {
        get => _workspaceName;
        private set => SetProperty(ref _workspaceName, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public bool IsEmpty => _workspace.Count == 0;
    public bool HasWorkspace => !IsEmpty;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public string? ActiveContextText
    {
        get => _activeContextText;
        private set => SetProperty(ref _activeContextText, value);
    }

    public bool HasActive => _activeTitle is not null;
    public string? ActiveTitle { get => _activeTitle; private set { if (SetProperty(ref _activeTitle, value)) OnPropertyChanged(nameof(HasActive)); } }
    public string? ActiveSubtitle { get => _activeSubtitle; private set => SetProperty(ref _activeSubtitle, value); }
    public string? ActiveMeta { get => _activeMeta; private set => SetProperty(ref _activeMeta, value); }

    public string ThemeToggleLabel => _theme.EffectiveTheme == AppTheme.Dark ? "Light" : "Dark";

    /// <summary>Sets the active dataset when a dataset node is selected (measurements never become active).</summary>
    public void Select(DatasetNodeViewModel? node)
    {
        if (node is not null && !node.IsMeasurement && _workspace.Contains(node.Id))
        {
            _workspace.SetActive(node.Id);
        }
    }

    private async Task ImportAsync()
    {
        var path = _picker.PickScanFile();
        if (!string.IsNullOrEmpty(path))
        {
            await LoadAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>Loads the bundled sample scan (if <see cref="SamplePath"/> is set). Public so the composition
    /// root and the render harness can trigger it directly, in addition to the command.</summary>
    public async Task OpenSampleAsync()
    {
        if (SamplePath is { } path && File.Exists(path))
        {
            await LoadAsync(path).ConfigureAwait(true);
        }
        else
        {
            StatusMessage = "The bundled sample scan could not be found.";
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private async Task LoadAsync(string path)
    {
        var result = await _reader.ReadAsync(path, ScanReadOptions.Default, CancellationToken.None).ConfigureAwait(true);
        if (result.IsSuccess && result.Dataset is { } dataset)
        {
            _workspace.Add(dataset);
            _workspace.SetActive(dataset.Id);
            WorkspaceName = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : "Workspace";
            HasUnsavedChanges = true;
            StatusMessage = null;
        }
        else
        {
            StatusMessage = result.Error?.Message ?? "The file could not be read.";
        }

        OnPropertyChanged(nameof(HasStatus));
    }

    private void ToggleTheme()
    {
        var next = _theme.EffectiveTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next);
    }

    private void OnCommandError(Exception ex)
    {
        StatusMessage = $"Operation failed: {ex.Message}";
        OnPropertyChanged(nameof(HasStatus));
    }

    // Topology: datasets added/removed. Rebuilds the node tree (and the id->node index used by refresh).
    private void RebuildTopology()
    {
        _nodesById.Clear();
        ExplorerNodes.Clear();
        foreach (var rootId in _workspace.Roots)
        {
            ExplorerNodes.Add(BuildNode(rootId));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasWorkspace));
        RefreshActiveState();
    }

    // Active/comparison changed only: update existing nodes in place + the active header + history.
    // No node objects are replaced, so TreeView selection and expansion survive an active change.
    private void RefreshActiveState()
    {
        var active = _workspace.Active;
        foreach (var (id, node) in _nodesById)
        {
            node.IsActive = active.ActiveId == id;
            node.IsInComparison = active.Comparison.Contains(id);
        }

        if (active.ActiveId is { } activeId && _workspace.TryGet(activeId, out var dataset))
        {
            ActiveTitle = DatasetLabel(dataset);
            ActiveContextText = $"Active ▸ {ActiveTitle}";
            (ActiveSubtitle, ActiveMeta) = Describe(dataset);
            BuildHistory(dataset);
        }
        else
        {
            ActiveTitle = null;
            ActiveContextText = null;
            ActiveSubtitle = null;
            ActiveMeta = null;
            HistoryRows.Clear();
        }
    }

    private DatasetNodeViewModel BuildNode(DatasetId id)
    {
        _workspace.TryGet(id, out var dataset);
        var node = new DatasetNodeViewModel(id, DatasetLabel(dataset!), IconKey(dataset!), isMeasurement: false)
        {
            IsActive = _workspace.Active.ActiveId == id,
            IsInComparison = _workspace.Active.Comparison.Contains(id),
        };
        _nodesById[id] = node;
        foreach (var childId in _workspace.ChildrenOf(id))
        {
            node.Children.Add(BuildNode(childId));
        }

        return node;
    }

    private void BuildHistory(AfmDataset dataset)
    {
        HistoryRows.Clear();
        if (dataset.Provenance.IsRoot)
        {
            var file = dataset.Source.OriginalFilePath is { } p ? Path.GetFileName(p) : dataset.Source.FormatId;
            HistoryRows.Add(new HistoryRowViewModel(1, "Import", file, HistoryStatus.Done));
            return;
        }

        var order = 1;
        foreach (var step in dataset.Provenance.Steps)
        {
            HistoryRows.Add(new HistoryRowViewModel(order++, FriendlyOp(step.OperationId), step.OperationId, HistoryStatus.Done));
        }
    }

    private static string DatasetLabel(AfmDataset dataset)
    {
        if (dataset.Provenance.IsRoot)
        {
            var path = dataset.Source.OriginalFilePath;
            return string.IsNullOrEmpty(path) ? "Imported scan" : Path.GetFileNameWithoutExtension(path);
        }

        var last = dataset.Provenance.Steps.Count > 0 ? dataset.Provenance.Steps[^1].OperationId : "derived";
        return FriendlyOp(last);
    }

    private static string IconKey(AfmDataset dataset)
        => dataset is ScanImageDataset ? "SA.Icon.Dataset" : "SA.Icon.Dataset";

    private static (string subtitle, string meta) Describe(AfmDataset dataset)
    {
        if (dataset is ScanImageDataset image)
        {
            var subtitle = $"2D scan · {image.X.Count} × {image.Y.Count} · {image.Channel.DisplayName}";
            var instrument = dataset.Metadata.InstrumentModel;
            var meta = string.IsNullOrWhiteSpace(instrument) || instrument == "unknown"
                ? dataset.Source.FormatId
                : instrument;
            return (subtitle, meta);
        }

        return (dataset.GetType().Name, dataset.Source.FormatId);
    }

    private static string FriendlyOp(string operationId)
    {
        var tail = operationId.Contains('.') ? operationId[(operationId.LastIndexOf('.') + 1)..] : operationId;
        return tail.Length == 0 ? operationId : char.ToUpperInvariant(tail[0]) + tail[1..];
    }
}
