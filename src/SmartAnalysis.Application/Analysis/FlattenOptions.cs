using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>Which regression the flatten subtracts (Application-level mirror of the operation's schema).</summary>
public enum FlattenScope
{
    Line = 0,
    Whole = 1,
    Surface = 2,
}

/// <summary>Line direction for Line/Whole flatten.</summary>
public enum FlattenOrientation
{
    FastAxis = 0,
    SlowAxis = 1,
}

/// <summary>Z-level handling after subtracting the regression.</summary>
public enum FlattenBasement
{
    RegressionToZero = 0,
    PreserveOriginalMidpoint = 1,
}

/// <summary>
/// The four flatten parameters, expressed in Application-level types so the UI drives the operation
/// through the Application use case without referencing the Analysis operation contract (doc 11:
/// UI → Application only). The use case maps these onto the real <c>image.flatten</c> parameter set.
/// </summary>
public sealed record FlattenOptions(
    FlattenScope Scope,
    int Order,
    FlattenOrientation Orientation,
    FlattenBasement Basement)
{
    /// <summary>The operation's schema defaults (Line · order 1 · FastAxis · RegressionToZero).</summary>
    public static FlattenOptions Default { get; } = new(FlattenScope.Line, 1, FlattenOrientation.FastAxis, FlattenBasement.RegressionToZero);
}

/// <summary>
/// The result of applying an operation from the UI: on success the derived dataset id (now active, with
/// the source in the comparison set) + any warnings; on failure a typed <see cref="Error"/> message.
/// </summary>
public sealed record FlattenOutcome(
    bool Success,
    DatasetId? DerivedId,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static FlattenOutcome Failed(string error) => new(false, null, [], error);
}
