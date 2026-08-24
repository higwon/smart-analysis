namespace SmartAnalysis.Application.Operations;

/// <summary>How the generic editor should render a parameter field (projected from the CLR parameter type).</summary>
public enum ParameterFieldKind
{
    /// <summary>A real-valued number (double/float/decimal), optionally bounded.</summary>
    Number,

    /// <summary>An integer number, optionally bounded (rendered as a stepper).</summary>
    Integer,

    /// <summary>A closed set of named choices (an enum) — rendered as a segmented/select control.</summary>
    Choice,

    /// <summary>A boolean toggle.</summary>
    Boolean,

    /// <summary>Free text.</summary>
    Text,
}

/// <summary>One selectable value of a <see cref="ParameterFieldKind.Choice"/> field (the raw name + its label).</summary>
public sealed record ParameterFieldOption(string Value, string Label);

/// <summary>
/// A single generic-editor field projected from an operation's <c>ParameterDescriptor</c>. Carries only
/// UI-facing primitives (no Analysis types): the CLR-type decision is already resolved into
/// <see cref="Kind"/>, and an enum's members into <see cref="Options"/>. The submitted value is coerced
/// back to the real parameter type by the Application before the run.
/// </summary>
public sealed record ParameterFieldDescriptor(
    string Name,
    string Label,
    ParameterFieldKind Kind,
    object? Default,
    double? Min,
    double? Max,
    IReadOnlyList<ParameterFieldOption> Options,
    string? Unit,
    string Help);

/// <summary>
/// The generic editor model for one operation: its identity/summary plus the projected parameter fields.
/// A form with zero fields is a parameterless operation (run directly). Used only when no operation-specific
/// <i>semantic</i> editor is registered for the id (e.g. Flatten keeps its hand-built editor).
/// </summary>
public sealed record OperationForm(
    string Id,
    string DisplayName,
    string Summary,
    OperationCategory Category,
    IReadOnlyList<ParameterFieldDescriptor> Fields,
    bool DerivesImage = false,
    bool DerivesCurve = false);
