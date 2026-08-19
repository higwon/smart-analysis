namespace SmartAnalysis.Application.Operations;

/// <summary>
/// The Application service behind the UX02 operation launcher (doc 26). It projects the Analysis operation
/// registry into UI-facing DTOs and runs a chosen operation on the active dataset, applying the right
/// workspace policy by output kind. The UI depends only on this (never on the Analysis operation contract,
/// doc 11), so adding an operation (A03+) surfaces it in the launcher with <b>no shell edits</b>.
/// </summary>
public interface IOperationLauncher
{
    /// <summary>
    /// The operations applicable to the current active dataset (via <c>ApplicableTo(kind)</c>), projected to
    /// launcher items and ordered by category then name. Empty when there is no active dataset.
    /// </summary>
    IReadOnlyList<OperationLauncherItem> ApplicableToActive();

    /// <summary>
    /// The generic editor form for <paramref name="operationId"/> (parameter fields projected from its
    /// schema), or <c>null</c> if unknown. Used when no operation-specific semantic editor is registered.
    /// </summary>
    OperationForm? GetForm(string operationId);

    /// <summary>
    /// Runs <paramref name="operationId"/> on the active dataset with <paramref name="values"/> (UI
    /// primitives, coerced back to the schema's CLR types). A derived-dataset output applies the transform
    /// policy (derived active, source → comparison); an artifact output is attached to the source. Invalid
    /// parameters / a run failure come back as a typed <see cref="OperationRunResult.Error"/>.
    /// </summary>
    Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default);

    /// <summary>
    /// The display name of an <b>enum</b> parameter value for <paramref name="operationId"/> — e.g. "BandStop"
    /// for <c>kind = 3</c> — so provenance/history shows the member name instead of the raw code. Returns
    /// <c>null</c> (the caller then formats the number) when the parameter is not a known enum on that operation,
    /// the value is not an in-range integer, or the recorded <paramref name="operationVersion"/> does not match the
    /// current descriptor — a past step must not be relabelled with a newer schema's (possibly different) enum
    /// meaning; an unknown version is shown as the raw number, which is safe. A default no-op keeps non-registry
    /// implementations (test doubles) simple.
    /// </summary>
    string? EnumParameterLabel(string operationId, int operationVersion, string parameterName, double value) => null;
}
