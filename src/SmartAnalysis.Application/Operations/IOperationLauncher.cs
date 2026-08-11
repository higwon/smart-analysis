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
}
