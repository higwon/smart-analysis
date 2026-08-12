using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Mvvm;
using SmartAnalysis.UI.Services;
using SmartAnalysis.Visualization.Colormaps;

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
    private readonly IImageAnalysisUseCase _imageAnalysis;
    private readonly IOperationLauncher _launcher;
    private readonly MeasurementStore _measurements;
    private readonly IWorkspacePersistence _persistence;
    private readonly IWorkspacePathPicker _workspacePicker;
    private readonly AsyncRelayCommand _runStatistics;
    private readonly RelayCommand _save;
    private string? _workspacePath;   // where this workspace was last saved/opened (Save writes here silently)
    private bool _suppressDirty;      // guards the dirty flag during an in-place Open

    // The one piece of operation-specific knowledge left in the shell (doc 26 / U08): the semantic-editor
    // override registry. An id here bypasses the generic schema form for a hand-built editor / direct run;
    // everything else falls through to the generic parameter form. Adding a new operation needs no entry.
    private const string FlattenId = "image.flatten";
    private const string StatisticsId = "image.statistics";

    private string _workspaceName = "Untitled workspace";
    private bool _hasUnsavedChanges;
    private string? _statusMessage;
    private string? _activeContextText;
    private string? _activeTitle;
    private string? _activeSubtitle;
    private string? _activeMeta;
    private ScanImageDataset? _activeImage;
    private ScanImageDataset? _beforeImage;
    private InspectorRole _inspectorRole = InspectorRole.DatasetProperties;
    private bool _isLauncherOpen;
    private object? _operationEditor;
    private StatisticsResultViewModel? _statistics;
    private HistoryRowViewModel? _selectedStep;
    private Colormap _colormap = Colormap.AfmGold;
    private string _colormapName = "AFM Gold";

    public ShellViewModel(Workspace workspace, IScanFileReader reader, ThemeManager theme, IScanFilePicker picker, IImageAnalysisUseCase imageAnalysis, IOperationLauncher launcher, MeasurementStore measurements, IWorkspacePersistence persistence, IWorkspacePathPicker workspacePicker)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _imageAnalysis = imageAnalysis ?? throw new ArgumentNullException(nameof(imageAnalysis));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _workspacePicker = workspacePicker ?? throw new ArgumentNullException(nameof(workspacePicker));

        FlattenPanel = new FlattenPanelViewModel(imageAnalysis, () => _workspace.Active.ActiveId);

        ImportCommand = new AsyncRelayCommand(ImportAsync, onError: OnCommandError);
        OpenSampleCommand = new AsyncRelayCommand(OpenSampleAsync, () => SamplePath is not null, OnCommandError);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        _save = new RelayCommand(SaveWorkspace, () => HasWorkspace);
        OpenWorkspaceCommand = new RelayCommand(OpenWorkspace);
        // Enabled by the launcher's own state — whether ANY operation is applicable to the active dataset —
        // not by the dataset being an image. So a future Profile/Spectrum operation registered in the
        // registry opens the launcher with no shell edits (the U08 goal); an empty launcher stays disabled.
        ToggleLauncherCommand = new RelayCommand(() => IsLauncherOpen = !IsLauncherOpen, () => LauncherItems.Count > 0);
        _runStatistics = new AsyncRelayCommand(RunStatisticsAsync, () => HasActiveImage, OnCommandError);
        CycleColormapCommand = new RelayCommand(CycleColormap);
        ExitCompareCommand = new RelayCommand(() => _workspace.SetComparison([]), () => IsBeforeAfter);

        // Topology changes (datasets added/removed) rebuild the tree; an active/comparison change only
        // refreshes existing nodes' state — so selection + expansion in the TreeView are preserved.
        _workspace.DatasetsChanged += (_, _) => RebuildTopology();
        _workspace.ActiveContextChanged += (_, _) => RefreshActiveState();
        // Any dataset change (import, a derived op) marks the workspace unsaved — except during an in-place
        // Open (suppressed) and on the empty startup workspace.
        _workspace.DatasetsChanged += (_, _) => { if (!_suppressDirty && _workspace.Count > 0) HasUnsavedChanges = true; };
        // A new/removed measurement re-surfaces the attached nodes without disturbing the active context
        // or the current Inspector role (so the just-shown result card survives its own attach).
        _measurements.MeasurementsChanged += (_, _) => RebuildNodes();
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
    public ICommand SaveCommand => _save;
    public ICommand OpenWorkspaceCommand { get; }

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

    /// <summary>The contextual Flatten parameter panel (shown when the active dataset is an image).</summary>
    public FlattenPanelViewModel FlattenPanel { get; }

    public ICommand ToggleLauncherCommand { get; }
    public ICommand CycleColormapCommand { get; }
    public ICommand ExitCompareCommand { get; }

    /// <summary>The registry-driven launcher entries applicable to the active dataset (grouped in the view).</summary>
    public ObservableCollection<OperationLauncherItemViewModel> LauncherItems { get; } = new();

    /// <summary>The current Operation-role editor: a semantic editor (e.g. <see cref="FlattenPanel"/>) or a
    /// generic <see cref="ParameterFormViewModel"/>; null when the Operation role is not showing an editor.</summary>
    public object? OperationEditor { get => _operationEditor; private set => SetProperty(ref _operationEditor, value); }

    /// <summary>Which role the Inspector shows (doc 26 §13).</summary>
    public InspectorRole InspectorRole
    {
        get => _inspectorRole;
        private set
        {
            if (SetProperty(ref _inspectorRole, value))
            {
                OnPropertyChanged(nameof(RoleIsDataset));
                OnPropertyChanged(nameof(RoleIsOperation));
                OnPropertyChanged(nameof(RoleIsResult));
                OnPropertyChanged(nameof(RoleIsStep));
            }
        }
    }

    public bool RoleIsDataset => _inspectorRole == InspectorRole.DatasetProperties;
    public bool RoleIsOperation => _inspectorRole == InspectorRole.Operation;
    public bool RoleIsResult => _inspectorRole == InspectorRole.Result;
    public bool RoleIsStep => _inspectorRole == InspectorRole.Step;

    /// <summary>The operation launcher popover (Analyze ▾) open state.</summary>
    public bool IsLauncherOpen
    {
        get => _isLauncherOpen;
        set => SetProperty(ref _isLauncherOpen, value);
    }

    /// <summary>The current measurement result card (Result role); null otherwise.</summary>
    public StatisticsResultViewModel? Statistics { get => _statistics; private set => SetProperty(ref _statistics, value); }

    /// <summary>The selected provenance step (Step role); null otherwise.</summary>
    public HistoryRowViewModel? SelectedStep { get => _selectedStep; private set => SetProperty(ref _selectedStep, value); }

    /// <summary>The active AFM data colormap (theme-independent). Name is shown on the viewer toolbar.</summary>
    public Colormap Colormap => _colormap;
    public string ColormapName { get => _colormapName; private set => SetProperty(ref _colormapName, value); }

    /// <summary>Selecting a provenance step shows it read-only in the Inspector — the active dataset never changes.</summary>
    public void SelectStep(HistoryRowViewModel? step)
    {
        SelectedStep = step;
        InspectorRole = step is null ? InspectorRole.DatasetProperties : InspectorRole.Step;
    }

    // Resolves an editor strategy for a launcher choice (doc 26 / U08): a semantic override for the two
    // known ids, else the generic schema form. The shell holds no other operation-specific knowledge.
    private void LaunchOperation(string operationId)
    {
        IsLauncherOpen = false;
        switch (operationId)
        {
            case FlattenId:
                OperationEditor = FlattenPanel;         // hand-built semantic editor
                InspectorRole = InspectorRole.Operation;
                break;
            case StatisticsId:
                _runStatistics.Execute(null);           // parameterless measurement → run directly
                break;
            default:
                if (_launcher.GetForm(operationId) is { } form)
                {
                    OperationEditor = new ParameterFormViewModel(_launcher, form, OnGenericRunCompleted);
                    InspectorRole = InspectorRole.Operation;
                }

                break;
        }
    }

    // A generic run completed. A derived output already moved the active context (the shell reacts to that
    // via ActiveContextChanged); a measurement output is shown in the Result role, active unchanged.
    private void OnGenericRunCompleted(OperationRunResult result)
    {
        if (result.Measurement is { } measurement)
        {
            Statistics = new StatisticsResultViewModel(measurement);
            InspectorRole = InspectorRole.Result;
        }
    }

    // Re-populates the launcher from the registry for the active dataset's kind (empty when none active).
    private void RebuildLauncherItems()
    {
        LauncherItems.Clear();
        foreach (var item in _launcher.ApplicableToActive())
        {
            var id = item.Id;
            LauncherItems.Add(new OperationLauncherItemViewModel(item, () => LaunchOperation(id)));
        }
    }

    private async Task RunStatisticsAsync()
    {
        IsLauncherOpen = false;
        if (_workspace.Active.ActiveId is not { } id)
        {
            return;
        }

        var result = await _imageAnalysis.ComputeStatisticsAsync(id).ConfigureAwait(true);
        if (result.Success)
        {
            Statistics = new StatisticsResultViewModel(result);
            InspectorRole = InspectorRole.Result; // attached to the active dataset — active is unchanged
        }
        else
        {
            StatusMessage = result.Error;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private void CycleColormap()
    {
        var toGold = !ReferenceEquals(_colormap, Colormap.AfmGold);
        _colormap = toGold ? Colormap.AfmGold : Colormap.Grayscale;
        ColormapName = toGold ? "AFM Gold" : "Grayscale";
        ImagesChanged?.Invoke(this, EventArgs.Empty); // re-render with the new colormap
    }

    /// <summary>The active dataset when it is a 2D scan image (drives the viewer); null otherwise.</summary>
    public ScanImageDataset? ActiveImage { get => _activeImage; private set => SetProperty(ref _activeImage, value); }

    /// <summary>The comparison "before" image (the source) when in Before/After; null otherwise.</summary>
    public ScanImageDataset? BeforeImage { get => _beforeImage; private set => SetProperty(ref _beforeImage, value); }

    public bool HasActiveImage => _activeImage is not null;
    public bool IsBeforeAfter => _activeImage is not null && _beforeImage is not null;
    public bool IsSingleImage => _activeImage is not null && _beforeImage is null;

    /// <summary>
    /// Raised when the images to display change. The <b>view</b> handles rendering: it builds a fresh
    /// <c>ImageRenderInput</c> from <see cref="ActiveImage"/>/<see cref="BeforeImage"/> and calls
    /// <c>AfmImageView.Render(...)</c> — the render input (which borrows the dataset buffer) is never held
    /// by the view-model (ADR-011 / V02 lifetime contract).
    /// </summary>
    public event EventHandler? ImagesChanged;

    public string ThemeToggleLabel => _theme.EffectiveTheme == AppTheme.Dark ? "Light" : "Dark";

    /// <summary>
    /// Handles an explorer selection. A dataset node becomes active; a measurement node instead shows its
    /// result read-only in the Inspector (attached to its source — the active dataset never changes).
    /// </summary>
    public void Select(DatasetNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.IsMeasurement)
        {
            SelectMeasurement(node.Id);
        }
        else if (_workspace.Contains(node.Id))
        {
            _workspace.SetActive(node.Id);
        }
    }

    /// <summary>Shows an attached measurement in the Inspector's Result role; the active dataset is unchanged.</summary>
    public void SelectMeasurement(DatasetId artifactId)
    {
        if (_imageAnalysis.GetMeasurement(artifactId) is { } result)
        {
            Statistics = new StatisticsResultViewModel(result);
            SelectedStep = null;
            InspectorRole = InspectorRole.Result;
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

    // Saves the workspace as a directory-package. Silently re-saves to the known folder after the first
    // save/open; prompts for a folder otherwise.
    private void SaveWorkspace()
    {
        var path = _workspacePath ?? _workspacePicker.PickSaveFolder();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var outcome = _persistence.Save(path);
        if (outcome.Success)
        {
            _workspacePath = path;
            WorkspaceName = FolderName(path);
            HasUnsavedChanges = false;
            StatusMessage = null;
        }
        else
        {
            StatusMessage = outcome.Error;
        }

        OnPropertyChanged(nameof(HasStatus));
    }

    // Opens a saved workspace, replacing the current session in place (ReplaceWith fires the workspace events
    // that rebuild the tree + active state). The dirty flag is suppressed for that in-place swap.
    private void OpenWorkspace()
    {
        var path = _workspacePicker.PickOpenFolder();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _suppressDirty = true;
        var outcome = _persistence.Open(path);
        _suppressDirty = false;

        if (outcome.Success)
        {
            _workspacePath = path;
            WorkspaceName = FolderName(path);
            HasUnsavedChanges = false;
            StatusMessage = null;
        }
        else
        {
            StatusMessage = outcome.Error;
        }

        OnPropertyChanged(nameof(HasStatus));
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? "Workspace" : name;
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

    // Topology: datasets added/removed. Rebuilds the node tree, then refreshes the active header/history.
    private void RebuildTopology()
    {
        RebuildNodes();
        RefreshActiveState();
    }

    // Rebuilds the explorer node tree (datasets + their attached measurements) and the id->node index the
    // in-place active refresh uses. Deliberately does NOT touch the active header or the Inspector role.
    private void RebuildNodes()
    {
        _nodesById.Clear();
        ExplorerNodes.Clear();
        foreach (var rootId in _workspace.Roots)
        {
            ExplorerNodes.Add(BuildNode(rootId));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasWorkspace));
        _save.RaiseCanExecuteChanged(); // Save is enabled once the workspace has content
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
            ActiveImage = dataset as ScanImageDataset;
            BeforeImage = FirstComparisonImage(active);
        }
        else
        {
            ActiveTitle = null;
            ActiveContextText = null;
            ActiveSubtitle = null;
            ActiveMeta = null;
            HistoryRows.Clear();
            ActiveImage = null;
            BeforeImage = null;
        }

        // A new active dataset resets the Inspector to its properties (op editor / result / step are transient)
        // and re-populates the launcher from the registry for the new active dataset's kind.
        Statistics = null;
        SelectedStep = null;
        OperationEditor = null;
        InspectorRole = InspectorRole.DatasetProperties;
        IsLauncherOpen = false;
        RebuildLauncherItems();

        OnPropertyChanged(nameof(HasActiveImage));
        OnPropertyChanged(nameof(IsBeforeAfter));
        OnPropertyChanged(nameof(IsSingleImage));
        (ToggleLauncherCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _runStatistics.RaiseCanExecuteChanged();
        (ExitCompareCommand as RelayCommand)?.RaiseCanExecuteChanged();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
    }

    // The first comparison-set member that is an image in the workspace (the Before of Before/After).
    private ScanImageDataset? FirstComparisonImage(ActiveContext active)
    {
        foreach (var id in active.Comparison)
        {
            if (_workspace.TryGet(id, out var d) && d is ScanImageDataset image)
            {
                return image;
            }
        }

        return null;
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

        // Attached measurements (doc 22 §Measurement): shown under their source, never independently active.
        foreach (var artifact in _measurements.ForSource(id))
        {
            node.Children.Add(new DatasetNodeViewModel(
                artifact.Id, FriendlyOp(artifact.OperationId), "SA.Icon.Statistics", isMeasurement: true));
        }

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
