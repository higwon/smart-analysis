using System.Collections.Generic;
using SmartAnalysis.Application.Operations;
using SmartAnalysis.UI.Mvvm;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// One editable field in the generic operation form, projected from a <see cref="ParameterFieldDescriptor"/>.
/// The kind booleans drive which control the view shows; <see cref="Value"/> holds the raw UI primitive
/// (enum member name, number-as-text, or bool) that the Application coerces back to the parameter's CLR type.
/// </summary>
public sealed class ParameterFieldViewModel : ObservableObject
{
    private object? _value;
    private bool _isRelevant = true;

    public ParameterFieldViewModel(ParameterFieldDescriptor descriptor)
    {
        Name = descriptor.Name;
        Label = descriptor.Label;
        Help = descriptor.Help;
        Unit = descriptor.Unit;
        Options = descriptor.Options;
        Kind = descriptor.Kind;
        RelevantWhen = descriptor.RelevantWhen;

        _value = descriptor.Kind switch
        {
            ParameterFieldKind.Choice => descriptor.Default as string ?? (Options.Count > 0 ? Options[0].Value : null),
            ParameterFieldKind.Boolean => descriptor.Default ?? false,
            _ => descriptor.Default,
        };
    }

    public string Name { get; }

    public string Label { get; }

    public string Help { get; }

    public string? Unit { get; }

    public IReadOnlyList<ParameterFieldOption> Options { get; }

    public ParameterFieldKind Kind { get; }

    public bool IsChoice => Kind == ParameterFieldKind.Choice;
    public bool IsNumber => Kind is ParameterFieldKind.Number or ParameterFieldKind.Integer;
    public bool IsBoolean => Kind == ParameterFieldKind.Boolean;
    public bool IsText => Kind == ParameterFieldKind.Text;

    /// <summary>When set, this field is only used for some settings of another one.</summary>
    public ParameterFieldRelevance? RelevantWhen { get; }

    /// <summary>
    /// Whether the current settings actually use this field. An irrelevant field keeps its value and still
    /// submits — it simply cannot change the result, and the form shows that rather than letting the user tune
    /// a control that does nothing.
    /// </summary>
    public bool IsRelevant
    {
        get => _isRelevant;
        set => SetProperty(ref _isRelevant, value);
    }

    /// <summary>The current raw value (submitted to the Application, which coerces it to the CLR type).</summary>
    public object? Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
