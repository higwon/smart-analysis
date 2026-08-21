using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// The generic, schema-driven operation editor (U08): built from an <see cref="OperationForm"/> for any
/// operation that has no hand-built semantic editor. Renders a field per parameter and, on Apply, submits
/// the collected values to <see cref="IOperationLauncher.RunAsync"/>. A derived-dataset run updates the
/// workspace (the shell reacts to that); a measurement run is handed back via <c>onCompleted</c>.
/// </summary>
public sealed class ParameterFormViewModel : ObservableObject
{
    private readonly IOperationLauncher _launcher;
    private readonly OperationForm _form;
    private readonly Action<OperationRunResult> _onCompleted;
    private string? _errorMessage;
    private string? _warnings;

    public ParameterFormViewModel(IOperationLauncher launcher, OperationForm form, Action<OperationRunResult> onCompleted)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        Fields = form.Fields.Select(f => new ParameterFieldViewModel(f)).ToArray();
        foreach (var field in Fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ParameterFieldViewModel.Value))
                {
                    ParametersChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, onError: ex => ErrorMessage = ex.Message);
    }

    /// <summary>Raised whenever a field value changes, so the shell can refresh a live settings preview.</summary>
    public event EventHandler? ParametersChanged;

    /// <summary>The operation id this form edits (e.g. <c>image.crop</c>) — lets the shell add a semantic preview.</summary>
    public string Id => _form.Id;

    public string DisplayName => _form.DisplayName;

    public string Summary => _form.Summary;

    /// <summary>Whether this operation derives a new dataset (Process) or measures (Measure).</summary>
    public OperationCategory Category => _form.Category;

    /// <summary>Whether this operation derives an <b>image</b> (image→image) — so a live SOURCE/PREVIEW compare
    /// applies. False for a measurement or an image→curve transform (e.g. Power Spectral Density).</summary>
    public bool DerivesImage => _form.DerivesImage;

    public IReadOnlyList<ParameterFieldViewModel> Fields { get; }

    public bool HasFields => Fields.Count > 0;

    /// <summary>The current field values as UI primitives (the Application coerces them to the schema's CLR types).</summary>
    public IReadOnlyDictionary<string, object?> Values => Fields.ToDictionary(f => f.Name, f => f.Value);

    public ICommand ApplyCommand { get; }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string? Warnings
    {
        get => _warnings;
        private set { if (SetProperty(ref _warnings, value)) OnPropertyChanged(nameof(HasWarnings)); }
    }

    public bool HasWarnings => !string.IsNullOrEmpty(Warnings);

    private async Task ApplyAsync()
    {
        var values = Fields.ToDictionary(f => f.Name, f => f.Value);
        var result = await _launcher.RunAsync(_form.Id, values).ConfigureAwait(true);
        if (!result.Success)
        {
            Warnings = null;
            ErrorMessage = result.Error;
            return;
        }

        ErrorMessage = null;
        Warnings = result.Warnings.Count > 0 ? string.Join("; ", result.Warnings) : null;
        _onCompleted(result);
    }
}
