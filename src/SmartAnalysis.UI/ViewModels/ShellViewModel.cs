using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Input;
using System.Linq;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;
using SmartAnalysis.UI.DesignSystem.Theming;
using SmartAnalysis.UI.Mvvm;
using SmartAnalysis.UI.Services;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

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
    private readonly ISpectroscopyParameterPreview _parameterPreview;
    private readonly IOperationLauncher _launcher;
    private readonly MeasurementStore _measurements;
    private readonly IWorkspacePersistence _persistence;
    private readonly IWorkspacePathPicker _workspacePicker;
    private readonly IUnsavedChangesPrompt _unsavedPrompt;
    private readonly AsyncRelayCommand _runStatistics;
    private readonly AsyncRelayCommand _extractPoint;
    private readonly RelayCommand _showSurface;
    private readonly RelayCommand _showVolume;
    private readonly RelayCommand _save;
    private string? _workspacePath;   // where this workspace was last saved/opened (Save writes here silently)
    private bool _suppressDirty;      // guards the dirty flag during an in-place Open

    // The one piece of operation-specific knowledge left in the shell (doc 26 / U08): the semantic-editor
    // override registry. An id here bypasses the generic schema form for a hand-built editor / direct run;
    // everything else falls through to the generic parameter form. Adding a new operation needs no entry.
    private const string FlattenId = "image.flatten";
    private const string StatisticsId = "image.statistics";
    private const string VolumeImageId = "force-volume.volume-image";

    private string _workspaceName = "Untitled workspace";
    private bool _hasUnsavedChanges;
    private string? _statusMessage;
    private string? _activeContextText;
    private string? _activeTitle;
    private string? _activeSubtitle;
    private string? _activeMeta;
    private ScanImageDataset? _activeImage;
    private ScanImageDataset? _beforeImage;
    private LineProfileDataset? _activeCurve;
    private ForceCurveDataset? _activeForceCurve;
    private ForceVolumeDataset? _activeForceVolume;
    private int _selectedMapPoint;
    private int _selectedXChannel;
    private int _selectedYChannel;
    private int _designatedXChannel;
    private int _designatedYChannel;
    private bool _is3D;
    private bool _isInteractiveImageEditing;
    private bool _roiEnabled;
    private bool _roiIsEllipse;
    private InspectorRole _inspectorRole = InspectorRole.DatasetProperties;
    private bool _isLauncherOpen;
    private object? _operationEditor;
    private StatisticsResultViewModel? _statistics;
    private MeasurementRegion? _selectedRegion;
    private DatasetId? _selectedMeasurementId;
    private MeasurementLine? _curveSourceLine;
    private ScanImageDataset? _curveSourceImage;
    private StatisticsResultViewModel? _liveMeasurements;
    private Task _liveMeasurementsTask = Task.CompletedTask;
    private bool _isOperationPreview;
    private ImageRenderInput? _operationPreviewInput;
    private string? _volumeUnavailable;
    private ThresholdWindow? _window;
    private bool _windowComputed;
    private CurveRenderInput? _operationPreviewCurve;
    private Task _operationPreviewTask = Task.CompletedTask;
    private Func<DatasetId, CancellationToken, Task<PreviewOutput>>? _computePreview;
    private CancellationTokenSource? _previewCts;
    private int _previewGeneration;
    private HistoryRowViewModel? _selectedStep;
    private Colormap _colormap = ColormapCatalog.Default.Map;
    private string _colormapName = ColormapCatalog.Default.Name;
    private bool _autoRange = true;
    private double _rangeMin;
    private double _rangeMax = 1.0;

    public ShellViewModel(Workspace workspace, IScanFileReader reader, ThemeManager theme, IScanFilePicker picker, IImageAnalysisUseCase imageAnalysis, ISpectroscopyParameterPreview parameterPreview, IOperationLauncher launcher, MeasurementStore measurements, IWorkspacePersistence persistence, IWorkspacePathPicker workspacePicker, IUnsavedChangesPrompt unsavedPrompt)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _imageAnalysis = imageAnalysis ?? throw new ArgumentNullException(nameof(imageAnalysis));
        _parameterPreview = parameterPreview ?? throw new ArgumentNullException(nameof(parameterPreview));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _workspacePicker = workspacePicker ?? throw new ArgumentNullException(nameof(workspacePicker));
        _unsavedPrompt = unsavedPrompt ?? throw new ArgumentNullException(nameof(unsavedPrompt));

        FlattenPanel = new FlattenPanelViewModel(imageAnalysis, () => _workspace.Active.ActiveId);
        // While the Flatten editor is open, re-run the uncommitted preview whenever a setting changes.
        FlattenPanel.PropertyChanged += (_, e) =>
        {
            if (_isOperationPreview && e.PropertyName is nameof(FlattenPanelViewModel.Scope)
                or nameof(FlattenPanelViewModel.Order) or nameof(FlattenPanelViewModel.Orientation)
                or nameof(FlattenPanelViewModel.Basement))
            {
                RefreshOperationPreview();
            }
        };

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
        _extractPoint = new AsyncRelayCommand(ExtractPointAsync, () => IsForceVolume, OnCommandError);
        // Closing the editor is what leaves the volume view: the picture is the preview, so there is no separate
        // "off" state to keep in sync.
        _showSurface = new RelayCommand(() => OperationEditor = null, () => IsVolumeView);
        _showVolume = new RelayCommand(() => LaunchOperation(VolumeImageId), () => CanShowVolume && !IsVolumeView);
        ExitCompareCommand = new RelayCommand(() => _workspace.SetComparison([]), () => IsBeforeAfter);

        // Topology changes (datasets added/removed) rebuild the tree; an active/comparison change only
        // refreshes existing nodes' state — so selection + expansion in the TreeView are preserved.
        _workspace.DatasetsChanged += (_, _) => RebuildTopology();
        _workspace.ActiveContextChanged += (_, _) => RefreshActiveState();
        // Any dataset change (import, a derived op, or a removal) marks the workspace unsaved — except during
        // an in-place Open (suppressed). Startup raises no DatasetsChanged, so no Count guard is needed (and a
        // change that empties the workspace must still count as dirty).
        _workspace.DatasetsChanged += (_, _) => { if (!_suppressDirty) HasUnsavedChanges = true; };
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

    /// <summary>Shows a status banner message (e.g. a failed export the view performed on the shell's behalf).</summary>
    public void ShowStatus(string? message)
    {
        StatusMessage = message;
        OnPropertyChanged(nameof(HasStatus));
    }

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
    public ICommand ExitCompareCommand { get; }

    /// <summary>The registry-driven launcher entries applicable to the active dataset (grouped in the view).</summary>
    public ObservableCollection<OperationLauncherItemViewModel> LauncherItems { get; } = new();

    /// <summary>The current Operation-role editor: a semantic editor (e.g. <see cref="FlattenPanel"/>) or a
    /// generic <see cref="ParameterFormViewModel"/>; null when the Operation role is not showing an editor.</summary>
    public object? OperationEditor
    {
        get => _operationEditor;
        private set
        {
            // Set the stage mode FIRST: an interactive image operation (region crop/ROI, or a line profile) edits
            // an overlay that lives on the 2D image view, so its editor forces the 2D stage even when 3D is the
            // preference — and doing this before the editor change means the 2D image is re-rendered before the
            // shell seeds the overlay onto it (a seed onto a not-yet-rendered view would find no image).
            IsInteractiveImageEditing = IsImageOverlayEditor(value);
            SetProperty(ref _operationEditor, value);
            SetOperationPreview(value);

            // An Operation role with no editor draws nothing at all — the Inspector goes blank with no way back.
            if (value is null && _inspectorRole == InspectorRole.Operation)
            {
                InspectorRole = InspectorRole.DatasetProperties;
            }

            RaiseVolumeViewChanged();
        }
    }

    /// <summary>Whether an operation's settings preview owns the stage (source-vs-preview split, uncommitted).</summary>
    public bool IsOperationPreview => _isOperationPreview;

    /// <summary>The compare panes show for a real Before/After OR an IMAGE operation settings preview (a curve preview
    /// overlays on the curve view instead — see <see cref="OperationPreviewCurve"/>).</summary>
    public bool ShowComparePanes => IsBeforeAfter || (_isOperationPreview && HasActiveImage);

    /// <summary>Left/right pane captions: source-vs-preview while previewing, else the before/after comparison.</summary>
    public string CompareBeforeLabel => _isOperationPreview ? "SOURCE" : "BEFORE";
    public string CompareAfterLabel => _isOperationPreview ? "PREVIEW" : "AFTER";

    /// <summary>The owned render input of the live IMAGE operation preview (the PREVIEW pane); null when not previewing.</summary>
    public ImageRenderInput? OperationPreviewInput => _operationPreviewInput;

    /// <summary>The owned render input of the live CURVE operation preview (overlaid as "PREVIEW" on the source curve);
    /// null when not previewing a curve op.</summary>
    public CurveRenderInput? OperationPreviewCurve => _operationPreviewCurve;

    /// <summary>Awaitable settle of the in-flight preview computation (deterministic tests).</summary>
    public Task OperationPreviewSettled => _operationPreviewTask;

    // The result of one preview compute — an image OR a curve render input (whichever the op derives).
    private readonly record struct PreviewOutput(ImageRenderInput? Image, CurveRenderInput? Curve);

    // Resolves the preview strategy for the editor being shown: the semantic Flatten panel, a generic form that
    // derives an IMAGE (image→image, active image), or one that derives a CURVE (curve→curve, active curve). Process
    // alone means "derives a dataset", not a specific kind — so the gate is form.DerivesImage / form.DerivesCurve,
    // decided before running. Measure forms and the image-overlay editors (crop/line, own live preview) are excluded.
    // A null strategy leaves the stage in its single view.
    private void SetOperationPreview(object? editor)
    {
        _computePreview = editor switch
        {
            FlattenPanelViewModel when HasActiveImage
                => async (id, ct) => new PreviewOutput(await _imageAnalysis.PreviewFlattenAsync(id, CurrentFlattenOptions(), _colormap, EffectiveRange, ct).ConfigureAwait(true), null),
            ParameterFormViewModel form when HasActiveImage && form.DerivesImage && !IsImageOverlayEditor(form)
                => async (id, ct) => new PreviewOutput(await _launcher.PreviewAsync(form.Id, form.Values, _colormap, EffectiveRange, ct).ConfigureAwait(true), null),
            // A map is not an image, but a map -> image operation still previews: the picture recomputes in place
            // as the measure's parameters change, and nothing enters the workspace until Apply (doc 26 SS22.3).
            ParameterFormViewModel form when _activeForceVolume is not null && form.DerivesImage
                => async (id, ct) => new PreviewOutput(await _launcher.PreviewAsync(form.Id, form.Values, _colormap, EffectiveRange, ct).ConfigureAwait(true), null),
            ParameterFormViewModel form when _activeCurve is not null && form.DerivesCurve && !IsImageOverlayEditor(form) && !IsProfileRangeEditor(form)
                => async (id, ct) => new PreviewOutput(null, await _launcher.PreviewCurveAsync(form.Id, form.Values, ct).ConfigureAwait(true)),
            _ => null,
        };

        if (_computePreview is null)
        {
            SetOperationPreview(false);
            return;
        }

        if (_isOperationPreview)
        {
            // Already previewing, but the editor (and its strategy) changed — a previewable op → another previewable
            // op. The on/off toggle wouldn't re-run, so drop the stale preview now and recompute for the NEW op, else
            // the previous op's PREVIEW lingers until the user first touches a parameter.
            _operationPreviewInput = null;
            _operationPreviewCurve = null;
            OnPropertyChanged(nameof(OperationPreviewInput));
            OnPropertyChanged(nameof(OperationPreviewCurve));
            ImagesChanged?.Invoke(this, EventArgs.Empty); // clear the stale overlay immediately; the new one follows
            RefreshOperationPreview();
            return;
        }

        SetOperationPreview(true);
    }

    private void SetOperationPreview(bool on)
    {
        if (_isOperationPreview == on)
        {
            return;
        }

        _isOperationPreview = on;
        _operationPreviewInput = null; // clear the old preview; a fresh one is computed below when turning on
        SetVolumeUnavailable(null);
        _operationPreviewCurve = null;
        OnPropertyChanged(nameof(IsOperationPreview));
        OnPropertyChanged(nameof(ShowComparePanes));
        OnPropertyChanged(nameof(CompareBeforeLabel));
        OnPropertyChanged(nameof(CompareAfterLabel));
        OnPropertyChanged(nameof(ShowSingle2D));
        OnPropertyChanged(nameof(ShowSingle3D));
        RaiseVolumeViewChanged();

        OnPropertyChanged(nameof(OperationPreviewInput));
        OnPropertyChanged(nameof(OperationPreviewCurve));

        if (on)
        {
            RefreshOperationPreview();
        }
        else
        {
            CancelOperationPreview(); // drop any in-flight preview so a late result can't paint the closed compare
            ImagesChanged?.Invoke(this, EventArgs.Empty); // back to the single view
        }
    }

    private FlattenOptions CurrentFlattenOptions()
        => new(FlattenPanel.Scope, FlattenPanel.Order, FlattenPanel.Orientation, FlattenPanel.Basement);

    // Each refresh supersedes the last: a new generation + a fresh cancellation token. A rapid A→B parameter change
    // cancels A's in-flight compute AND — even if A still completes — the generation guard drops its stale result, so
    // a slower A can never overwrite a newer B (an ActiveId-only guard misses this: both A and B are the same image).
    private void RefreshOperationPreview()
    {
        if (_computePreview is not { } compute || _workspace.Active.ActiveId is not { } id)
        {
            return;
        }

        CancelOperationPreview();
        _previewCts = new CancellationTokenSource();
        var generation = ++_previewGeneration;
        _operationPreviewTask = ComputeOperationPreviewAsync(compute, id, generation, _previewCts.Token);
    }

    private void CancelOperationPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private async Task ComputeOperationPreviewAsync(Func<DatasetId, CancellationToken, Task<PreviewOutput>> compute, DatasetId id, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var output = await compute(id, cancellationToken).ConfigureAwait(true);

            // Apply only if this is still the newest request for the still-previewed active dataset. The generation
            // check is what defeats out-of-order completion; the ActiveId check drops a preview for a replaced dataset.
            if (_isOperationPreview && generation == _previewGeneration && _workspace.Active.ActiveId == id)
            {
                _operationPreviewInput = output.Image;
                _operationPreviewCurve = output.Curve;

                // An attempt that produced nothing is not the same as no attempt yet. On the Volume view the
                // preview IS the stage, so leaving the previous picture up would show one set of settings while
                // another is on screen beside it.
                SetVolumeUnavailable(
                    IsVolumeView && output.Image is null
                        // Only the launcher can name a cause. A preview also fails on an unexpected error,
                        // which is not the settings' fault, so the fallback says what happened and no more.
                        ? ExplainVolume() ?? "No picture could be computed for this map."
                        : null);

                OnPropertyChanged(nameof(OperationPreviewInput));
                OnPropertyChanged(nameof(OperationPreviewCurve));
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // Best-effort preview: a cancellation or failure just shows no PREVIEW, never an error banner.
        }
    }

    /// <summary>Whether an interactive image-overlay operation (region or line profile) editor is open. While it
    /// is, the 2D stage is forced (the overlay lives on the 2D view) and the 3D toggle is hidden.</summary>
    public bool IsInteractiveImageEditing
    {
        get => _isInteractiveImageEditing;
        private set
        {
            if (SetProperty(ref _isInteractiveImageEditing, value))
            {
                OnPropertyChanged(nameof(ShowSingle2D));
                OnPropertyChanged(nameof(ShowSingle3D));
                OnPropertyChanged(nameof(CanToggle3D));
                OnPropertyChanged(nameof(CanUseRoi));
                ImagesChanged?.Invoke(this, EventArgs.Empty);
                RoiChanged?.Invoke(this, EventArgs.Empty); // an editor opening/closing re-evaluates the ROI overlay
            }
        }
    }

    // The overlay editors are recognized by their parameter shape: a region (left/top/width/height) or a line
    // (x0/y0/x1/y1) — the same fields the shell draws overlays for.
    private static bool IsImageOverlayEditor(object? editor)
    {
        if (editor is not ParameterFormViewModel form)
        {
            return false;
        }

        bool Has(params string[] names) => Array.TrueForAll(names, n => form.Fields.Any(f => f.Name == n));
        return Has("left", "top", "width", "height") || Has("x0", "y0", "x1", "y1");
    }

    // A profile-range editor (Crop Profile) is recognized by its start/count fields: instead of a source-vs-preview
    // overlay it draws the kept [start, count) range as vertical markers on the source curve (the view handles it).
    private static bool IsProfileRangeEditor(object? editor)
        => editor is ParameterFormViewModel form
            && form.Fields.Any(f => f.Name == "start")
            && form.Fields.Any(f => f.Name == "count");

    /// <summary>Which role the Inspector shows (doc 26 §13).</summary>
    public InspectorRole InspectorRole
    {
        get => _inspectorRole;
        private set
        {
            if (SetProperty(ref _inspectorRole, value))
            {
                // The measurement selection belongs to the Result role: any move off it (a step, an operation
                // form, the dataset properties) drops it, so "export the measurement I am looking at" can never
                // export a stale one. Callers entering the Result role set the id AFTER the role, so this never
                // clears their own selection.
                if (value != InspectorRole.Result)
                {
                    SelectedMeasurementId = null;
                }

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

    /// <summary>
    /// The basic statistics of the active image, shown <b>inline</b> on the default Inspector (Dataset role) — a
    /// simple measurement is read directly on the main screen, not run from Analyze. Null when no image is active.
    /// </summary>
    public StatisticsResultViewModel? LiveMeasurements
    {
        get => _liveMeasurements;
        private set
        {
            if (SetProperty(ref _liveMeasurements, value))
            {
                OnPropertyChanged(nameof(HasLiveMeasurements));
            }
        }
    }

    /// <summary>Whether inline basic measurements are available for the active image.</summary>
    public bool HasLiveMeasurements => _liveMeasurements is not null;

    /// <summary>The in-flight (or completed) inline-measurement computation — awaitable for deterministic refresh.</summary>
    public Task LiveMeasurementsSettled => _liveMeasurementsTask;

    /// <summary>The selected provenance step (Step role); null otherwise.</summary>
    public HistoryRowViewModel? SelectedStep { get => _selectedStep; private set => SetProperty(ref _selectedStep, value); }

    /// <summary>The active AFM data colormap (theme-independent), resolved from <see cref="ColormapName"/>.</summary>
    public Colormap Colormap => _colormap;

    /// <summary>The predefined colormap names for the palette picker.</summary>
    public IReadOnlyList<string> AvailableColormaps => ColormapCatalog.Names;

    /// <summary>The selected colormap by name; setting it re-resolves the colormap and re-renders.</summary>
    public string ColormapName
    {
        get => _colormapName;
        set
        {
            if (!string.IsNullOrEmpty(value) && SetProperty(ref _colormapName, value))
            {
                _colormap = ColormapCatalog.ByName(value);
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Palette range mode. <c>true</c> = auto (each image's own data min/max); <c>false</c> = the manual
    /// <see cref="RangeMin"/>/<see cref="RangeMax"/>. Switching to auto reseeds the shown range from the
    /// active image so the numbers stay meaningful.
    /// </summary>
    public bool AutoRange
    {
        get => _autoRange;
        set
        {
            if (SetProperty(ref _autoRange, value))
            {
                if (value)
                {
                    SeedRangeFromActive();
                }

                OnPropertyChanged(nameof(ManualRangeEnabled));
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether the manual min/max inputs are editable (i.e. not in auto mode).</summary>
    public bool ManualRangeEnabled => !_autoRange;

    /// <summary>Manual palette minimum (in the channel unit); only used when <see cref="AutoRange"/> is off.</summary>
    public double RangeMin
    {
        get => _rangeMin;
        set
        {
            if (SetProperty(ref _rangeMin, value) && !_autoRange)
            {
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Manual palette maximum (in the channel unit); only used when <see cref="AutoRange"/> is off.</summary>
    public double RangeMax
    {
        get => _rangeMax;
        set
        {
            if (SetProperty(ref _rangeMax, value) && !_autoRange)
            {
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// The range the view should render with: <c>null</c> = auto (the factory uses each image's data min/max);
    /// otherwise the manual range. A degenerate/invalid manual range falls back to auto rather than throwing.
    /// </summary>
    public ValueRange? EffectiveRange
        => _autoRange || !double.IsFinite(_rangeMin) || !double.IsFinite(_rangeMax) || _rangeMax <= _rangeMin
            ? null
            : new ValueRange(_rangeMin, _rangeMax);

    /// <summary>
    /// Applies a value window set by dragging the palette-bar handles: switch to manual and re-render once.
    /// Updates the toolbar (Auto unchecks, the min/max fields follow) without firing a render per keystroke.
    /// </summary>
    public void SetManualRange(double min, double max)
    {
        _autoRange = false;
        _rangeMin = min;
        _rangeMax = max;
        OnPropertyChanged(nameof(AutoRange));
        OnPropertyChanged(nameof(ManualRangeEnabled));
        OnPropertyChanged(nameof(RangeMin));
        OnPropertyChanged(nameof(RangeMax));
        ImagesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Reflects the active image's data min/max in the shown range (used while auto, and when switching to auto).
    private void SeedRangeFromActive()
    {
        var range = _activeImage is { } image
            ? ValueRange.FromData(image.Data.Memory.Span)
            : new ValueRange(0.0, 1.0);
        _rangeMin = range.Min;
        _rangeMax = range.Max;
        OnPropertyChanged(nameof(RangeMin));
        OnPropertyChanged(nameof(RangeMax));
    }

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
                    var editor = new ParameterFormViewModel(_launcher, form, OnGenericRunCompleted);
                    // While the form is open, re-run the uncommitted preview whenever a field changes.
                    editor.ParametersChanged += (_, _) =>
                    {
                        RaiseCurveMarkersChanged();
                        if (_isOperationPreview)
                        {
                            RefreshOperationPreview();
                        }
                    };
                    SeedMapSelection(editor);
                    OperationEditor = editor;
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
            // A measurement leaves active unchanged, so (unlike a transform) nothing else closes the parameter form.
            // Close it here so its draggable region overlay doesn't linger as an editable region over the result.
            OperationEditor = null;

            Statistics = new StatisticsResultViewModel(measurement);
            InspectorRole = InspectorRole.Result;

            // Show where the just-run measurement was taken (a region on the active image), same as re-selecting it.
            SelectedRegion = result.MeasurementId is { } id
                && _imageAnalysis.GetMeasurementRegion(id) is { } region
                && region.SourceId == _workspace.Active.ActiveId
                    ? region
                    : null;

            // The just-run measurement is what the Result card shows, so it is what Export offers — set AFTER the
            // role above (entering a non-Result role clears this).
            SelectedMeasurementId = result.MeasurementId;
        }
    }

    // Re-populates the launcher from the registry for the active dataset's kind (empty when none active).
    private void RebuildLauncherItems()
    {
        LauncherItems.Clear();
        foreach (var item in _launcher.ApplicableToActive())
        {
            // Basic statistics is a simple measurement shown inline on the main screen (LiveMeasurements),
            // so it is not offered as an Analyze action — Analyze is for parameter-setting/derived operations.
            if (item.Id == StatisticsId)
            {
                continue;
            }

            var id = item.Id;
            LauncherItems.Add(new OperationLauncherItemViewModel(item, () => LaunchOperation(id)));
        }
    }

    // Auto-computes the active image's basic statistics for the inline panel (fire-and-forget; failures are silent
    // since this is a passive readout). Guards against a stale result if the active dataset changed meanwhile.
    private void RefreshLiveMeasurements()
    {
        LiveMeasurements = null; // clear the previous image's readouts up front so stale stats never show
        if (_workspace.Active.ActiveId is { } id && HasActiveImage)
        {
            _liveMeasurementsTask = ComputeLiveMeasurementsAsync(id);
        }
        else
        {
            _liveMeasurementsTask = Task.CompletedTask; // no in-flight refresh once the active dataset isn't an image
        }
    }

    private async Task ComputeLiveMeasurementsAsync(DatasetId id)
    {
        try
        {
            var result = await _imageAnalysis.ComputeStatisticsPreviewAsync(id).ConfigureAwait(true); // ephemeral: no saved node
            // Only touch the panel if THIS image is still active — otherwise a slow request (success OR failure)
            // for a since-replaced image must not clobber the current image's already-shown measurements.
            if (_workspace.Active.ActiveId == id && result.Success)
            {
                LiveMeasurements = new StatisticsResultViewModel(result);
            }
        }
        catch when (_workspace.Active.ActiveId == id)
        {
            LiveMeasurements = null; // a passive readout must never surface an error banner (only for the active image)
        }
        catch
        {
            // A stale image's failure — leave the current image's measurements untouched.
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

    /// <summary>The active dataset when it is a 2D scan image (drives the viewer); null otherwise.</summary>
    public ScanImageDataset? ActiveImage
    {
        get => _activeImage;
        private set
        {
            if (SetProperty(ref _activeImage, value) && _autoRange)
            {
                SeedRangeFromActive(); // keep the shown auto range in step with the active image
            }
        }
    }

    /// <summary>The comparison "before" image (the source) when in Before/After; null otherwise.</summary>
    public ScanImageDataset? BeforeImage { get => _beforeImage; private set => SetProperty(ref _beforeImage, value); }

    /// <summary>The active dataset when it is a 1D curve (profile/spectrum, e.g. a PSD); drives the curve view.</summary>
    public LineProfileDataset? ActiveCurve { get => _activeCurve; private set => SetProperty(ref _activeCurve, value); }

    /// <summary>The active dataset when it is a force curve (spectroscopy); drives the force-distance view.</summary>
    public ForceCurveDataset? ActiveForceCurve { get => _activeForceCurve; private set => SetProperty(ref _activeForceCurve, value); }

    /// <summary>Whether the stage shows a force–distance plot (force against separation).</summary>
    public bool IsSingleForceCurve => _activeForceCurve is not null;

    /// <summary>The active dataset when it is a force–volume map; the stage shows one of its curves at a time.</summary>
    public ForceVolumeDataset? ActiveForceVolume { get => _activeForceVolume; private set => SetProperty(ref _activeForceVolume, value); }

    /// <summary>Whether the stage shows a curve taken from a force–volume map.</summary>
    public bool IsForceVolume => _activeForceVolume is not null;

    /// <summary>How many curves the active map holds; zero when the active dataset is not a map.</summary>
    public int MapPointCount => _activeForceVolume?.PointCount ?? 0;

    /// <summary>The largest valid point index — what a selector's upper bound must be, not the count.</summary>
    public int MapPointMaxIndex => Math.Max(0, MapPointCount - 1);

    /// <summary>
    /// Which curve of the active map is on the stage. Clamped to the map, so a stale index from a previous
    /// dataset can never index past the current one.
    /// </summary>
    public int SelectedMapPoint
    {
        get => _selectedMapPoint;
        set
        {
            int clamped = MapPointCount == 0 ? 0 : Math.Clamp(value, 0, MapPointCount - 1);
            if (SetProperty(ref _selectedMapPoint, clamped))
            {
                OnPropertyChanged(nameof(MapPointLabel));
                OnPropertyChanged(nameof(SpectroscopyLabel));
                OnPropertyChanged(nameof(ShowSpectroscopyToolbar));
                OnPropertyChanged(nameof(CanStepMapPointBack));
                OnPropertyChanged(nameof(CanStepMapPointForward));
                RaiseCurveMarkersChanged();
                SeedMapSelection(OperationEditor as ParameterFormViewModel);
                MapPointChanged?.Invoke();
            }
        }
    }

    /// <summary>Raised when the stage should redraw because a different curve of the map was selected.</summary>
    public event Action? MapPointChanged;

    public bool CanStepMapPointBack => MapPointCount > 0 && _selectedMapPoint > 0;

    public bool CanStepMapPointForward => MapPointCount > 0 && _selectedMapPoint < MapPointCount - 1;

    /// <summary>
    /// What the viewer is looking at: which curve, and where it was measured. The position comes from the
    /// recorded layout whenever the file kept one, because that is the frame the markers on the stage are drawn
    /// in — a toolbar quoting a different frame would have the stage contradict itself about one point. The
    /// reconstructed grid is a fallback, and the label names whichever frame it is speaking in.
    /// </summary>
    public string MapPointLabel
    {
        get
        {
            if (_activeForceVolume is not { } map)
            {
                return string.Empty;
            }

            string label = $"Point {_selectedMapPoint + 1} of {map.PointCount}";
            if (map.Geometry is { } cells)
            {
                int column = (_selectedMapPoint % cells.Columns) + 1;
                int row = (_selectedMapPoint / cells.Columns) + 1;
                label += $" · col {column}/{cells.Columns}, row {row}/{cells.Rows}";
            }

            if (map.PointLayout is { } layout && _selectedMapPoint < layout.Count)
            {
                var p = layout[_selectedMapPoint];
                return $"{label} · surface {Position(p.X, p.Y, layout.LengthUnit)}";
            }

            if (map.Geometry is { } grid)
            {
                var (x, y) = grid.PositionOf(_selectedMapPoint);
                return $"{label} · scan {Position(x, y, grid.LengthUnit)}";
            }

            return $"{label} · no recorded position";
        }
    }

    private static string Position(double x, double y, Unit unit)
        => $"({x.ToString("0.###", CultureInfo.InvariantCulture)}, "
            + $"{y.ToString("0.###", CultureInfo.InvariantCulture)}) {unit.Symbol}";

    /// <summary>Why the Volume view has no picture for the current settings, or null when it has one.</summary>
    public string? VolumeUnavailable => _volumeUnavailable;

    public bool HasVolumeUnavailable => _volumeUnavailable is not null;

    private void SetVolumeUnavailable(string? reason)
    {
        if (_volumeUnavailable == reason)
        {
            return;
        }

        _volumeUnavailable = reason;
        OnPropertyChanged(nameof(VolumeUnavailable));
        OnPropertyChanged(nameof(HasVolumeUnavailable));
        RaiseCurveMarkersChanged();
    }

    private string? ExplainVolume()
        => OperationEditor is ParameterFormViewModel form ? _launcher.Explain(form.Id, form.Values) : null;

    /// <summary>
    /// Whether the Inspector curve shows whatever pair the channel picker was left on.
    /// <para>
    /// False in the Volume view, where the curve's job changed: it is no longer somewhere to explore an
    /// acquisition's channels but the explanation of the picture on the stage. The marks on it are a force level
    /// and two separations, so drawing them over (say) a Voltage-against-Z pair would be a confident explanation
    /// of a measurement nothing made.
    /// </para>
    /// </summary>
    public bool CurveFollowsChannelPicker => !IsVolumeView;

    /// <summary>
    /// Separations at which to mark the selected point's curve: where the threshold window begins and ends.
    /// Empty unless the Volume view is showing, because the marks belong to ITS settings (doc 26 §22.6).
    /// </summary>
    public IReadOnlyList<double> CurveVerticalMarkers
        => CurrentWindow() is { } w ? [w.PeakSeparation, w.WindowSeparation] : [];

    /// <summary>Force levels to mark: the non-contact level every force is measured from, and what the threshold means.</summary>
    public IReadOnlyList<double> CurveHorizontalMarkers
        => CurrentWindow() is { } w ? [w.Baseline, w.ThresholdForce] : [];

    // Both marker lists describe one window, and a drag will ask for them many times a second. Computed once
    // per refresh and dropped whenever anything it depends on moves.
    private ThresholdWindow? CurrentWindow()
    {
        if (!_windowComputed)
        {
            _window = Window();
            _windowComputed = true;
        }

        return _window;
    }

    // A point with no window comes back with NaN separations, which the render input drops — so "nothing to
    // measure here" draws as a curve with no window on it, which is the explanation for that pixel being a hole.
    private ThresholdWindow? Window()
    {
        if (!IsVolumeView || _activeForceVolume is not { } map || OperationEditor is not ParameterFormViewModel form)
        {
            return null;
        }

        return _parameterPreview.Locate(
            map,
            _selectedMapPoint,
            phaseIsApproach: Choice(form, "phase") is not "Retract",
            thresholdPercent: Number(form, "threshold") ?? 50.0,
            baselinePercent: Number(form, "baseline") ?? 20.0);
    }

    private static string? Choice(ParameterFormViewModel form, string name)
        => form.Fields.FirstOrDefault(f => f.Name == name)?.Value as string;

    private static double? Number(ParameterFormViewModel form, string name)
        => form.Fields.FirstOrDefault(f => f.Name == name)?.Value is { } v
            && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    // Convertibility is checked before the loop, so this cannot fail here.
    private static double OnAxis(double value, Unit from, Unit to)
        => new PhysicalValue(value, from).TryConvertTo(to).Value.Value;

    private void RaiseVolumeViewChanged()
    {
        OnPropertyChanged(nameof(IsVolumeView));
        OnPropertyChanged(nameof(CanShowVolume));
        OnPropertyChanged(nameof(ShowSpectroscopyImage));
        OnPropertyChanged(nameof(ShowCurveOnStage));
        OnPropertyChanged(nameof(ShowCurveBelowStage));
        OnPropertyChanged(nameof(CurveFollowsChannelPicker));
        OnPropertyChanged(nameof(PointMarkers));
        OnPropertyChanged(nameof(VolumeUnavailable));
        OnPropertyChanged(nameof(HasVolumeUnavailable));
        _showSurface.RaiseCanExecuteChanged();
        _showVolume.RaiseCanExecuteChanged();
    }

    private void RaiseCurveMarkersChanged()
    {
        _windowComputed = false;
        _window = null;
        OnPropertyChanged(nameof(CurveVerticalMarkers));
        OnPropertyChanged(nameof(CurveHorizontalMarkers));
    }

    /// <summary>
    /// Selects the map point a volume-image pixel was computed from.
    /// <para>
    /// Only in the Volume view, and only there because that is the one picture whose pixels ARE the map's points
    /// — one each, laid out on its grid. A pixel of the reference surface is not a measurement position: a
    /// 128x128 surface carries an 8x8 map, so treating one as the other would silently select whichever point
    /// happened to share an index.
    /// </para>
    /// </summary>
    public void SelectMapPointAt(int column, int row)
    {
        if (!IsVolumeView || _activeForceVolume?.Geometry is not { } grid)
        {
            return;
        }

        if (column < 0 || column >= grid.Columns || row < 0 || row >= grid.Rows)
        {
            return;
        }

        SelectedMapPoint = (row * grid.Columns) + column;
    }

    public void StepMapPoint(int delta) => SelectedMapPoint = _selectedMapPoint + delta;

    // Switching maps resets the selection: point 7 of the map you were looking at has nothing to do with
    // point 7 of the next one, and a stale index would silently show an unrelated curve.
    private void SetActiveForceVolume(ForceVolumeDataset? map)
    {
        bool changed = !ReferenceEquals(_activeForceVolume, map);
        ActiveForceVolume = map;
        if (changed)
        {
            _selectedMapPoint = 0;
            OnPropertyChanged(nameof(SelectedMapPoint));
        }

        OnPropertyChanged(nameof(IsForceVolume));
        OnPropertyChanged(nameof(MapPointCount));
        OnPropertyChanged(nameof(MapPointMaxIndex));
        OnPropertyChanged(nameof(MapPointLabel));
        OnPropertyChanged(nameof(SpectroscopyLabel));
        OnPropertyChanged(nameof(ShowSpectroscopyToolbar));
        OnPropertyChanged(nameof(CanStepMapPointBack));
        OnPropertyChanged(nameof(CanStepMapPointForward));
    }

    /// <summary>
    /// The surface the active spectroscopy dataset was measured on, when the file carried one. Most PSIA
    /// spectroscopy files embed the 2D scan alongside the curves, so a map can be shown against the sample it
    /// came from rather than as a bare grid of indices.
    /// </summary>
    public ScanImageDataset? SpectroscopyReferenceImage
        => _activeForceVolume?.ReferenceImage ?? _activeForceCurve?.ReferenceImage;

    /// <summary>Whether there is a reference surface to draw.</summary>
    public bool HasReferenceSurface => SpectroscopyReferenceImage is not null;

    /// <summary>Whether the stage shows spectroscopy at all — a single force curve or a map.</summary>
    public bool IsSpectroscopy => IsSingleForceCurve || IsForceVolume;

    /// <summary>
    /// Whether the curve itself takes the Stage. A spectroscopy dataset that came with a surface shows the
    /// surface (doc 26 §22.1) and keeps the curve in the Inspector; one without a surface has nothing spatial
    /// to show, so the curve takes the Stage rather than leaving it blank.
    /// </summary>
    public bool ShowCurveOnStage => IsSpectroscopy && !HasReferenceSurface && !IsVolumeView;

    /// <summary>
    /// The selected point's curve sits under the picture, across the Stage, only when the picture is what the
    /// Stage is showing — a map with no surface already has the curve ON the stage, and drawing it twice says
    /// nothing new.
    /// </summary>
    public bool ShowCurveBelowStage => IsForceVolume && !ShowCurveOnStage;

    /// <summary>
    /// The placeholder is for having nothing to inspect, not for the active dataset being something other than
    /// an image. A map or a curve has properties; telling its viewer to "select an image" is just wrong.
    /// </summary>
    public bool HasNothingToInspect => !HasActiveImage && !IsSpectroscopy && _activeCurve is null;

    /// <summary>
    /// Derives a force curve from the point the viewer has selected (A39) — the explicit step from inspecting a
    /// curve to working on it. The Inspector holds the selection, so nothing has to be typed into a form; the
    /// channel pair goes along, so what was on screen is what gets analysed. A map that kept no channels sends
    /// the sentinel instead of an index into a set that does not exist.
    /// </summary>
    public ICommand ExtractPointCommand => _extractPoint;

    private async Task ExtractPointAsync()
    {
        if (_activeForceVolume is null)
        {
            return;
        }

        bool kept = SpectroscopyChannels is not null;
        var result = await _launcher.RunAsync(
            "force-volume.extract-point",
            new Dictionary<string, object?>
            {
                ["point"] = _selectedMapPoint,
                ["xChannel"] = kept ? _selectedXChannel : -1,
                ["yChannel"] = kept ? _selectedYChannel : -1,
            }).ConfigureAwait(true);

        if (!result.Success)
        {
            StatusMessage = result.Error;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    /// <summary>
    /// Fills a form's map-point fields from the selection the Stage already holds (doc 26 SS22.2).
    /// <para>
    /// The toolbar arrows and the map markers ARE the point selector, so a form launched over a map must not ask
    /// for the number again — it opened on a point the viewer had already chosen. Fields the form does not have
    /// are skipped, so this is a no-op for every operation that is not about a map point.
    /// </para>
    /// </summary>
    private void SeedMapSelection(ParameterFormViewModel? form)
    {
        if (form is null || _activeForceVolume is null)
        {
            return;
        }

        bool kept = SpectroscopyChannels is not null;
        Set("point", _selectedMapPoint);
        Set("xChannel", kept ? _selectedXChannel : -1);
        Set("yChannel", kept ? _selectedYChannel : -1);

        void Set(string name, int value)
        {
            if (form.Fields.FirstOrDefault(f => f.Name == name) is { } field)
            {
                field.Value = value;
            }
        }
    }

    /// <summary>A profile is a slice: how far it runs, in its own axis unit.</summary>
    public string ProfileSummary
    {
        get
        {
            if (_activeCurve is not { } profile)
            {
                return string.Empty;
            }

            double span = Math.Abs(profile.X.Step) * Math.Max(0, profile.X.Count - 1);
            return FormattableString.Invariant(
                $"{profile.X.Count} samples · {span:0.###} {profile.X.Unit.Symbol}");
        }
    }

    /// <summary>
    /// Where on its source image the profile was sampled, in that image's pixels.
    /// <para>
    /// A profile with no recorded line says so rather than showing a position it does not have — the same rule
    /// the map's point position follows. A curve read straight from a file was never sliced out of anything.
    /// </para>
    /// </summary>
    public string ProfileSource
        => CurveSourceLine is { } line
            ? FormattableString.Invariant(
                $"({line.X0:0.#}, {line.Y0:0.#}) → ({line.X1:0.#}, {line.Y1:0.#}) px")
            : "not sampled from an image";

    public bool IsProfile => _activeCurve is not null;

    /// <summary>How much of the sample the map covers, in its own terms: the grid when it has one, else points.</summary>
    public string MapSummary
    {
        get
        {
            if (_activeForceVolume is not { } map)
            {
                return string.Empty;
            }

            string points = $"{map.PointCount} point{(map.PointCount == 1 ? string.Empty : "s")}";
            return map.Geometry is { } grid ? $"{grid.Columns} × {grid.Rows} grid · {points}" : $"{points} · no grid";
        }
    }

    /// <summary>
    /// The Stage is showing the map as a picture rather than the surface it was measured on (doc 26 SS22.3). It is
    /// a <b>view</b>, not a dataset: the parameters live in the Inspector and the picture recomputes in place, and
    /// only <i>Keep as image</i> puts one in the workspace. Materialising one per threshold tweak would bury the
    /// workspace in near-identical images and make provenance meaningless.
    /// </summary>
    public bool IsVolumeView
        => _isOperationPreview && IsForceVolume && OperationEditor is ParameterFormViewModel { Id: VolumeImageId };

    /// <summary>Offered only for a map that could produce a picture: without a grid there is no shape to draw.</summary>
    public bool CanShowVolume => _activeForceVolume?.Geometry is not null;

    /// <summary>The image view takes the Stage for the surface and for the volume picture alike.</summary>
    public bool ShowSpectroscopyImage => HasReferenceSurface || IsVolumeView;

    public ICommand ShowSurfaceCommand => _showSurface;

    public ICommand ShowVolumeCommand => _showVolume;

    /// <summary>An empty bar is worse than no bar: a plain curve of the designated pair has nothing to say.</summary>
    public bool ShowSpectroscopyToolbar => IsSpectroscopy && (IsForceVolume || SpectroscopyLabel.Length > 0);

    /// <summary>
    /// Where to mark each measured point on the surface, in surface <b>pixels</b> — the overlay draws in image
    /// space. Empty when the file recorded no positions or carried no surface, so nothing is marked on a
    /// picture that cannot place it.
    /// </summary>
    public IReadOnlyList<(double X, double Y, int Point)> PointMarkers
    {
        get
        {
            // The Volume view marks ONE point: the selected one, at the centre of its own pixel.
            //
            // Not all of them — on a picture where every pixel is already a point that would be noise drawn on
            // top of the thing it marks. But not none either: the mark is the only thing on screen saying which
            // of the curves the panel beside it describes, and, since a click on a pixel is how that curve is
            // chosen, the only confirmation that the click landed where the viewer meant.
            //
            // In the picture's OWN pixels. The recorded positions below are in the surface's, which the volume
            // image does not share — mixing them is what put a stray mark in the far corner.
            if (IsVolumeView)
            {
                return _activeForceVolume?.Geometry is { } cells
                    && _selectedMapPoint >= 0 && _selectedMapPoint < cells.Columns * cells.Rows
                        ? [((_selectedMapPoint % cells.Columns) + 0.5, (_selectedMapPoint / cells.Columns) + 0.5, _selectedMapPoint)]
                        : [];
            }

            var layout = _activeForceVolume?.PointLayout ?? _activeForceCurve?.PointLayout;
            if (layout is null || SpectroscopyReferenceImage is not { } surface)
            {
                return [];
            }

            double stepX = surface.X.Step;
            double stepY = surface.Y.Step;
            if (!(stepX > 0) || !(stepY > 0))
            {
                return [];
            }

            // A recorded position and a surface axis are both lengths, but not necessarily the SAME length: the
            // reader that exists happens to give micrometres for both, which is luck, not a Domain invariant. One
            // that recorded nanometres would put every marker within a thousandth of the corner — a picture that
            // looks like a tight cluster of measurements rather than like a bug.
            //
            // A layout that cannot be expressed on these axes at all places nothing: a marker somewhere arbitrary
            // is a claim about where a curve was measured.
            var from = layout.LengthUnit;
            if (!from.IsConvertibleTo(surface.X.Unit) || !from.IsConvertibleTo(surface.Y.Unit))
            {
                return [];
            }

            var markers = new List<(double X, double Y, int Point)>(layout.Count);
            for (int i = 0; i < layout.Count; i++)
            {
                var p = layout.Positions[i];
                markers.Add((
                    OnAxis(p.X, from, surface.X.Unit) / stepX,
                    OnAxis(p.Y, from, surface.Y.Unit) / stepY,
                    i));
            }

            return markers;
        }
    }

    /// <summary>
    /// What the viewer is looking at. A map adds which curve and where; a plot of some pair other than the one
    /// the file designated says so, because that curve is not the force curve the analysis operates on.
    /// </summary>
    public string SpectroscopyLabel
    {
        get
        {
            if (!IsSpectroscopy)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (IsForceVolume)
            {
                parts.Add(MapPointLabel);
            }

            if (!IsDesignatedChannelPair)
            {
                parts.Add("not the designated pair");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Every channel the active spectroscopy dataset measured, when it kept them. Both a single curve and a map
    /// can carry a set; a derived dataset (an approach/retract phase) carries none, and then there is nothing to
    /// choose between.
    /// </summary>
    public SpectroscopyChannelSet? SpectroscopyChannels
        => _activeForceVolume?.Channels ?? _activeForceCurve?.Channels;

    /// <summary>What a channel picker lists, in the order the instrument declared them.</summary>
    public IReadOnlyList<string> ChannelChoices { get; private set; } = [];

    /// <summary>Whether there is more than one channel to plot, and so anything worth choosing between.</summary>
    public bool CanPickChannels => ChannelChoices.Count > 1;

    /// <summary>Which channel is on the abscissa. Defaults to the pair the file designated.</summary>
    public int SelectedXChannel
    {
        get => _selectedXChannel;
        set => SetChannel(ref _selectedXChannel, value, nameof(SelectedXChannel));
    }

    /// <summary>Which channel is on the ordinate. Defaults to the pair the file designated.</summary>
    public int SelectedYChannel
    {
        get => _selectedYChannel;
        set => SetChannel(ref _selectedYChannel, value, nameof(SelectedYChannel));
    }

    /// <summary>
    /// Whether the plotted pair is still the one the file designated. A curve plotted from some other pair is
    /// not the force curve the analysis operates on, and the viewer should be told.
    /// </summary>
    public bool IsDesignatedChannelPair
        => SpectroscopyChannels is null
           || (_selectedXChannel == _designatedXChannel && _selectedYChannel == _designatedYChannel);

    // A selector writes SelectedIndex = -1 whenever its item source is swapped. Coercing that back into range is
    // not enough: unless the rejection is announced, the control never re-reads and sits at -1 — an empty combo
    // beside a populated one, with the view-model holding a perfectly good value the whole time.
    private void SetChannel(ref int field, int value, string name)
    {
        if (SpectroscopyChannels is not { } set)
        {
            OnPropertyChanged(name);
            return;
        }

        int clamped = Math.Clamp(value, 0, set.ChannelCount - 1);
        if (!SetProperty(ref field, clamped, name))
        {
            if (clamped != value)
            {
                OnPropertyChanged(name);
            }

            return;
        }

        OnPropertyChanged(nameof(IsDesignatedChannelPair));
        OnPropertyChanged(nameof(SpectroscopyLabel));
        OnPropertyChanged(nameof(ShowSpectroscopyToolbar));
        SeedMapSelection(OperationEditor as ParameterFormViewModel);
        MapPointChanged?.Invoke(); // the stage redraws for a channel change the same way it does for a point
    }

    // A channel selection belongs to one dataset. Carrying an index across would plot whatever channel happened
    // to sit at that position in the next file — a different physical quantity, with no sign that it changed.
    private void ResetChannelSelection()
    {
        var set = SpectroscopyChannels;
        ChannelChoices = set is null
            ? []
            : [.. set.Channels.Select(c => $"{c.DisplayName} [{c.Unit.Symbol}]")];

        // The file's own designated pair is the starting point; a set whose keys do not match falls back to the
        // first two channels rather than to an arbitrary single one.
        _designatedXChannel = FindDesignated(set, SeparationChannelKey(), 0);
        _designatedYChannel = FindDesignated(set, ForceChannelKey(), Math.Min(1, (set?.ChannelCount ?? 1) - 1));
        _selectedXChannel = _designatedXChannel;
        _selectedYChannel = _designatedYChannel;

        OnPropertyChanged(nameof(SpectroscopyChannels));
        OnPropertyChanged(nameof(ChannelChoices));
        OnPropertyChanged(nameof(CanPickChannels));
        OnPropertyChanged(nameof(SelectedXChannel));
        OnPropertyChanged(nameof(SelectedYChannel));
        OnPropertyChanged(nameof(IsDesignatedChannelPair));
        OnPropertyChanged(nameof(IsSpectroscopy));
        OnPropertyChanged(nameof(SpectroscopyReferenceImage));
        OnPropertyChanged(nameof(HasReferenceSurface));
        OnPropertyChanged(nameof(ShowCurveOnStage));
        OnPropertyChanged(nameof(ShowCurveBelowStage));
        OnPropertyChanged(nameof(HasNothingToInspect));
        OnPropertyChanged(nameof(MapSummary));
        OnPropertyChanged(nameof(PointMarkers));
        OnPropertyChanged(nameof(SpectroscopyLabel));
        OnPropertyChanged(nameof(ShowSpectroscopyToolbar));
    }

    private static int FindDesignated(SpectroscopyChannelSet? set, string? key, int fallback)
    {
        if (set is null)
        {
            return 0;
        }

        int found = key is null ? -1 : set.IndexOf(key);
        return found >= 0 ? found : Math.Clamp(fallback, 0, set.ChannelCount - 1);
    }

    private string? SeparationChannelKey()
        => _activeForceVolume?.SeparationChannel.Key ?? _activeForceCurve?.SeparationChannel.Key;

    private string? ForceChannelKey()
        => _activeForceVolume?.ForceChannel.Key ?? _activeForceCurve?.ForceChannel.Key;

    /// <summary>The source image an active line-profile curve was sampled from (to render beside the curve with the
    /// read-only line), or <c>null</c> when there is none / it is no longer in the workspace.</summary>
    public ScanImageDataset? CurveSourceImage { get => _curveSourceImage; private set => SetProperty(ref _curveSourceImage, value); }

    /// <summary>Where the active line-profile curve was sampled on its source image (for the read-only line beside
    /// the curve), or <c>null</c> when the active curve has no reconstructable source line (e.g. a PSD).</summary>
    public MeasurementLine? CurveSourceLine
    {
        get => _curveSourceLine;
        private set
        {
            if (SetProperty(ref _curveSourceLine, value))
            {
                OnPropertyChanged(nameof(ShowCurveSourceImage));
                OnPropertyChanged(nameof(ShowSourceImagePane));
                OnPropertyChanged(nameof(IsSingleCurve));
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(ProfileSource));
        OnPropertyChanged(nameof(HasNothingToInspect));
            }
        }
    }

    public bool HasActiveImage => _activeImage is not null;
    public bool IsBeforeAfter => _activeImage is not null && _beforeImage is not null;
    public bool IsSingleImage => _activeImage is not null && _beforeImage is null;

    /// <summary>A line-profile curve whose sampling line can be shown back on its source image: the stage pairs the
    /// source image (with a read-only line) above the profile curve, instead of the full-screen curve.</summary>
    public bool ShowCurveSourceImage => _activeCurve is not null && _curveSourceLine is not null;

    /// <summary>The full-screen curve view — a curve with no reconstructable source line (e.g. a PSD frequency curve).</summary>
    public bool IsSingleCurve => _activeCurve is not null && _curveSourceLine is null;

    /// <summary>Whether the 2D image control is shown — for an active image, or as the source pane beside a curve.</summary>
    public bool ShowSourceImagePane => ShowSingle2D || ShowCurveSourceImage;

    /// <summary>Whether the single image is shown as a 3D surface (V04) rather than the 2D view. Persists across
    /// active-image changes so the chosen view mode sticks; ignored for Before/After and curves.</summary>
    public bool Is3D
    {
        get => _is3D;
        set
        {
            if (SetProperty(ref _is3D, value))
            {
                OnPropertyChanged(nameof(ShowSingle2D));
                OnPropertyChanged(nameof(ShowSingle3D));
                ImagesChanged?.Invoke(this, EventArgs.Empty); // re-render into the newly shown view
            }
        }
    }

    // An overlay editor OR a drawn ROI forces 2D even when 3D is preferred (both live on the 2D view); turning
    // them off returns to the retained 3D preference.
    public bool ShowSingle2D => IsSingleImage && !_isOperationPreview && (!_is3D || _isInteractiveImageEditing || _roiEnabled);
    public bool ShowSingle3D => IsSingleImage && !_isOperationPreview && _is3D && !_isInteractiveImageEditing && !_roiEnabled;

    /// <summary>Whether the 3D toggle is offered — hidden while an overlay editor forces the 2D stage.</summary>
    public bool CanToggle3D => IsSingleImage && !_isInteractiveImageEditing && !_roiEnabled;

    /// <summary>Whether a persistent region of interest is drawn on the image; a region-capable op (e.g. Roughness)
    /// then applies to it instead of the whole image. Independent of any operation form.</summary>
    public bool RoiEnabled
    {
        get => _roiEnabled;
        set
        {
            if (SetProperty(ref _roiEnabled, value))
            {
                // The ROI forces the 2D stage (its overlay lives on the 2D view); swap + re-render BEFORE the ROI
                // is drawn so it lands on a rendered image, then return to the 3D preference when the ROI is off.
                OnPropertyChanged(nameof(ShowSingle2D));
                OnPropertyChanged(nameof(ShowSingle3D));
                OnPropertyChanged(nameof(CanToggle3D));
                ImagesChanged?.Invoke(this, EventArgs.Empty);
                RoiChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether the drawn ROI is an ellipse (else a rectangle).</summary>
    public bool RoiIsEllipse
    {
        get => _roiIsEllipse;
        set { if (SetProperty(ref _roiIsEllipse, value)) RoiChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Whether the ROI toggle is offered — single 2D image, and not while an overlay editor is open.</summary>
    public bool CanUseRoi => IsSingleImage && !_isInteractiveImageEditing;

    /// <summary>Raised when the ROI enable/shape changes so the shell re-draws the overlay + updates the region.</summary>
    public event EventHandler? RoiChanged;

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
            // Explorer selection is distinct from the active context. Selecting a dataset node is a transition OUT of
            // a measurement selection, so drop its read-only overlay + Result card — even when the node is already
            // active (SetActive is a no-op for the same id, so RefreshActiveState wouldn't fire to clear them).
            bool alreadyActive = _workspace.Active.ActiveId == node.Id;
            _workspace.SetActive(node.Id);
            if (alreadyActive)
            {
                SelectedRegion = null;
                Statistics = null;
                SelectedMeasurementId = null;
                SelectedStep = null;
                InspectorRole = InspectorRole.DatasetProperties;
            }
        }
    }

    /// <summary>The region a selected measurement was taken over — drawn read-only on the source image so the user
    /// sees where the stat came from — or <c>null</c> when the selection has no drawable region on the active image.</summary>
    public MeasurementRegion? SelectedRegion
    {
        get => _selectedRegion;
        private set
        {
            if (SetProperty(ref _selectedRegion, value))
            {
                RoiChanged?.Invoke(this, EventArgs.Empty); // the view re-evaluates the region overlay
            }
        }
    }

    /// <summary>Shows an attached measurement in the Inspector's Result role; the active dataset is unchanged.</summary>
    public void SelectMeasurement(DatasetId artifactId)
    {
        if (_imageAnalysis.GetMeasurement(artifactId) is not { } result)
        {
            return;
        }

        // A region measurement can only be drawn on its own source image (its bounds are that image's pixels). If the
        // source isn't the active dataset but is still in the workspace, switch to it so "this came from here" shows
        // wherever the measurement is selected from — not only when its source already happens to be active.
        var region = _imageAnalysis.GetMeasurementRegion(artifactId);
        if (region is not null && region.SourceId != _workspace.Active.ActiveId && _workspace.Contains(region.SourceId))
        {
            _workspace.SetActive(region.SourceId); // resets the Inspector via RefreshActiveState; re-shown below
        }

        Statistics = new StatisticsResultViewModel(result);
        SelectedStep = null;
        InspectorRole = InspectorRole.Result;
        SelectedRegion = region is not null && region.SourceId == _workspace.Active.ActiveId ? region : null;
        SelectedMeasurementId = artifactId;
    }

    /// <summary>The attached measurement currently shown in the Result role, so it can be exported; null when the
    /// Inspector is not showing one.</summary>
    public DatasetId? SelectedMeasurementId
    {
        get => _selectedMeasurementId;
        private set
        {
            if (SetProperty(ref _selectedMeasurementId, value))
            {
                OnPropertyChanged(nameof(HasSelectedMeasurement));
            }
        }
    }

    public bool HasSelectedMeasurement => _selectedMeasurementId is not null;

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

    private void SaveWorkspace() => TrySave();

    // Saves the workspace as a directory-package; returns whether it was actually saved. Silently re-saves to
    // the known folder after the first save/open; prompts for a folder otherwise (a cancelled picker → false).
    private bool TrySave()
    {
        var path = _workspacePath ?? _workspacePicker.PickSaveFolder();
        if (string.IsNullOrEmpty(path))
        {
            return false; // user cancelled the folder picker
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
        return outcome.Success;
    }

    // Opens a saved workspace, replacing the current session in place. Protects unsaved work first: if the
    // workspace is dirty, ask Save / Don't Save / Cancel — Cancel or a failed/cancelled Save aborts the open,
    // so an in-progress workspace is never silently discarded. The dirty flag is suppressed for the in-place
    // swap and always restored (a subscriber throwing must not leave dirty tracking off).
    private void OpenWorkspace()
    {
        if (HasUnsavedChanges)
        {
            switch (_unsavedPrompt.Ask(WorkspaceName))
            {
                case UnsavedChangesChoice.Cancel:
                    return;
                case UnsavedChangesChoice.Save when !TrySave():
                    return; // save was cancelled or failed — don't discard the current work
                case UnsavedChangesChoice.Save:
                case UnsavedChangesChoice.DontSave:
                    break;
            }
        }

        var path = _workspacePicker.PickOpenFolder();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        PersistenceOutcome outcome;
        _suppressDirty = true;
        try
        {
            outcome = _persistence.Open(path);
        }
        finally
        {
            _suppressDirty = false;
        }

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
            ActiveCurve = dataset as LineProfileDataset;
            ActiveForceCurve = dataset as ForceCurveDataset;
            SetActiveForceVolume(dataset as ForceVolumeDataset);
            ResetChannelSelection();
            BeforeImage = FirstComparisonImage(active);
            // A line-profile curve pairs with its source image + a read-only sampling line (when the source is still
            // in the workspace); any other curve (e.g. a PSD) has none and stays full-screen.
            CurveSourceLine = ActiveCurve is not null ? _imageAnalysis.GetCurveSourceLine(activeId) : null;
            CurveSourceImage = CurveSourceLine is { } line && _workspace.TryGet(line.SourceId, out var src) ? src as ScanImageDataset : null;
        }
        else
        {
            ActiveTitle = null;
            ActiveContextText = null;
            ActiveSubtitle = null;
            ActiveMeta = null;
            HistoryRows.Clear();
            ActiveImage = null;
            ActiveCurve = null;
            ActiveForceCurve = null;
            SetActiveForceVolume(null);
            ResetChannelSelection();
            BeforeImage = null;
            CurveSourceLine = null;
            CurveSourceImage = null;
        }

        // A new active dataset resets the Inspector to its properties (op editor / result / step are transient)
        // and re-populates the launcher from the registry for the new active dataset's kind.
        Statistics = null;
        SelectedMeasurementId = null;
        _selectedRegion = null; // cleared silently; the RoiChanged below already refreshes the overlay
        SelectedStep = null;
        OperationEditor = null;
        InspectorRole = InspectorRole.DatasetProperties;
        IsLauncherOpen = false;
        RebuildLauncherItems();
        RefreshLiveMeasurements(); // inline basic measurements for the new active image (Dataset role)

        OnPropertyChanged(nameof(HasActiveImage));
        OnPropertyChanged(nameof(IsBeforeAfter));
        OnPropertyChanged(nameof(ShowComparePanes));
        OnPropertyChanged(nameof(IsSingleImage));
        OnPropertyChanged(nameof(IsSingleCurve));
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(ProfileSource));
        OnPropertyChanged(nameof(HasNothingToInspect));
        OnPropertyChanged(nameof(IsSingleForceCurve));
        OnPropertyChanged(nameof(ShowCurveSourceImage));
        OnPropertyChanged(nameof(ShowSourceImagePane));
        OnPropertyChanged(nameof(ShowSingle2D));
        OnPropertyChanged(nameof(ShowSingle3D));
        OnPropertyChanged(nameof(CanToggle3D));
        OnPropertyChanged(nameof(CanUseRoi));
        RoiChanged?.Invoke(this, EventArgs.Empty);
        (ToggleLauncherCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _runStatistics.RaiseCanExecuteChanged();
        _extractPoint.RaiseCanExecuteChanged();
        RaiseVolumeViewChanged();
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
            HistoryRows.Add(new HistoryRowViewModel(1, "Import", file, HistoryStatus.Done, operationId: dataset.Source.FormatId));
            return;
        }

        var order = 1;
        foreach (var step in dataset.Provenance.Steps)
        {
            var parameters = new List<StepParameterViewModel>();
            var summaryParts = new List<string>();
            foreach (var (name, value) in step.Parameters)
            {
                // An enum parameter is recorded as its integer code; show the member name (e.g. "BandStop") instead —
                // via the op schema, or the shared ROI-shape discriminator for a recorded region (regionShape → "Ellipse").
                // Otherwise the Inspector shows the exact recorded value (round-trippable) and the strip may round.
                var enumLabel = _launcher.EnumParameterLabel(step.OperationId, step.OperationVersion, name, value.Value)
                    ?? (name == RegionProvenance.ShapeKey ? RegionProvenance.ShapeLabel(value.Value) : null);
                parameters.Add(new StepParameterViewModel(name, enumLabel ?? FormatValuePrecise(value)));
                summaryParts.Add($"{name} {enumLabel ?? FormatValueCompact(value)}");
            }

            var warnings = new List<string>();
            foreach (var warning in step.Warnings)
            {
                warnings.Add(warning.Message);
            }

            var status = step.Errors.Count > 0 ? HistoryStatus.Failed : HistoryStatus.Done;
            HistoryRows.Add(new HistoryRowViewModel(
                order++,
                FriendlyOp(step.OperationId),
                summaryParts.Count == 0 ? "no parameters" : string.Join(" · ", summaryParts),
                status,
                parameters,
                warnings,
                step.OperationId));
        }
    }

    // Inspector detail: the exact recorded value (shortest round-trippable form), so what is shown equals what ran.
    private static string FormatValuePrecise(PhysicalValue value)
    {
        var v = value.Value.ToString(CultureInfo.InvariantCulture);
        return value.Unit.Symbol == "1" ? v : $"{v} {value.Unit.Symbol}";
    }

    // Strip glance: a rounded value. Dimensionless values (unit "1") drop the symbol.
    private static string FormatValueCompact(PhysicalValue value)
        => value.Unit.Symbol == "1" ? $"{value.Value:G4}" : $"{value.Value:G4} {value.Unit.Symbol}";

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
            return (subtitle, Instrument(dataset));
        }

        if (dataset is ForceVolumeDataset map)
        {
            string extent = map.Geometry is { } grid ? $"{grid.Columns} × {grid.Rows}" : $"{map.PointCount} points";
            return ($"Force volume · {extent} · {map.ForceChannel.DisplayName}", Instrument(dataset));
        }

        if (dataset is ForceCurveDataset curve)
        {
            return ($"Force curve · {curve.Length} samples · {curve.ForceChannel.DisplayName}", Instrument(dataset));
        }

        if (dataset is LineProfileDataset profile)
        {
            return ($"Profile · {profile.X.Count} samples · {profile.Channel.DisplayName}", Instrument(dataset));
        }

        return (dataset.GetType().Name, dataset.Source.FormatId);
    }

    private static string Instrument(AfmDataset dataset)
    {
        var model = dataset.Metadata.InstrumentModel;
        return string.IsNullOrWhiteSpace(model) || model == "unknown" ? dataset.Source.FormatId : model;
    }

    private static string FriendlyOp(string operationId)
    {
        var tail = operationId.Contains('.') ? operationId[(operationId.LastIndexOf('.') + 1)..] : operationId;
        return tail.Length == 0 ? operationId : char.ToUpperInvariant(tail[0]) + tail[1..];
    }
}
