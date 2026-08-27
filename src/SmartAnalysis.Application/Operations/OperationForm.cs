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
/// <summary>
/// A field that only matters for some settings of another field, in the same UI primitives the fields carry
/// (an enum setting is its member name, as the choice values are). The form disables such a field rather than
/// hiding it: a control that appears and vanishes as you work makes it harder, not easier, to see what shapes
/// the result — a visibly inert one says "this exists, and right now it does nothing".
/// </summary>
public sealed record ParameterFieldRelevance(string Parameter, IReadOnlyList<object> Values);

public sealed record ParameterFieldDescriptor(
    string Name,
    string Label,
    ParameterFieldKind Kind,
    object? Default,
    double? Min,
    double? Max,
    IReadOnlyList<ParameterFieldOption> Options,
    string? Unit,
    string Help,
    ParameterFieldRelevance? RelevantWhen = null);

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
