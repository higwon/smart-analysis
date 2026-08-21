using System.Windows.Input;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// The contextual Flatten parameter panel (U02, doc 24 §4): the real four-parameter <c>image.flatten</c>
/// schema (scope · order 0–8 · orientation · basement). Apply runs the operation through the Application
/// <see cref="IImageAnalysisUseCase"/> — the UI never touches the Analysis operation contract (doc 11).
/// While the panel is open the shell shows a live source-vs-preview compare (PreviewFlattenAsync); Apply then just
/// materializes the derived dataset as the active image (no forced Before/After). Warnings/typed errors surface here.
/// </summary>
public sealed class FlattenPanelViewModel : ObservableObject
{
    private readonly IImageAnalysisUseCase _useCase;
    private readonly Func<DatasetId?> _activeSource;

    private FlattenScope _scope = FlattenOptions.Default.Scope;
    private int _order = FlattenOptions.Default.Order;
    private FlattenOrientation _orientation = FlattenOptions.Default.Orientation;
    private FlattenBasement _basement = FlattenOptions.Default.Basement;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _warnings;

    public FlattenPanelViewModel(IImageAnalysisUseCase useCase, Func<DatasetId?> activeSource)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _activeSource = activeSource ?? throw new ArgumentNullException(nameof(activeSource));
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy, ex => ErrorMessage = ex.Message);
        OrderUpCommand = new RelayCommand(() => Order = Math.Min(8, Order + 1));
        OrderDownCommand = new RelayCommand(() => Order = Math.Max(0, Order - 1));
    }

    /// <summary>Stepper commands for Order (clamped to the schema range 0–8).</summary>
    public ICommand OrderUpCommand { get; }
    public ICommand OrderDownCommand { get; }

    public IReadOnlyList<FlattenScope> ScopeOptions { get; } = Enum.GetValues<FlattenScope>();
    public IReadOnlyList<FlattenOrientation> OrientationOptions { get; } = Enum.GetValues<FlattenOrientation>();
    public IReadOnlyList<FlattenBasement> BasementOptions { get; } = Enum.GetValues<FlattenBasement>();
    public IReadOnlyList<int> OrderOptions { get; } = Enumerable.Range(0, 9).ToArray(); // 0..8 (schema range)

    public ICommand ApplyCommand { get; }

    public FlattenScope Scope
    {
        get => _scope;
        set { if (SetProperty(ref _scope, value)) OnPropertyChanged(nameof(OrientationEnabled)); }
    }

    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public FlattenOrientation Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }
    public FlattenBasement Basement { get => _basement; set => SetProperty(ref _basement, value); }

    /// <summary>Orientation has no meaning for a Surface fit; the UI disables it then (schema still submits it).</summary>
    public bool OrientationEnabled => Scope != FlattenScope.Surface;

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) (ApplyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string? Warnings { get => _warnings; private set { if (SetProperty(ref _warnings, value)) OnPropertyChanged(nameof(HasWarnings)); } }
    public bool HasWarnings => !string.IsNullOrEmpty(Warnings);

    private async Task ApplyAsync()
    {
        if (_activeSource() is not { } sourceId)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        Warnings = null;
        try
        {
            var outcome = await _useCase
                .ApplyFlattenAsync(sourceId, new FlattenOptions(Scope, Order, Orientation, Basement))
                .ConfigureAwait(true);

            if (!outcome.Success)
            {
                ErrorMessage = outcome.Error;
            }
            else if (outcome.Warnings.Count > 0)
            {
                Warnings = string.Join("; ", outcome.Warnings);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
