using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;

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
    /// Runs <paramref name="operationId"/> on the active dataset with <paramref name="values"/> WITHOUT committing
    /// anything to the workspace, and projects its (image) result to an <b>owned</b> <see cref="ImageRenderInput"/>
    /// for a live settings preview — the generic counterpart of the semantic Flatten preview. Returns <c>null</c>
    /// when there is nothing to show (no active image, unknown op, invalid parameters, a run failure, or a non-image
    /// result): a preview is best-effort, so a bad setting shows no PREVIEW pane rather than an error. A default
    /// no-op keeps non-registry implementations (test doubles) simple.
    /// </summary>
    Task<ImageRenderInput?> PreviewAsync(string operationId, IReadOnlyDictionary<string, object?> values, Colormap colormap, ValueRange? range, CancellationToken cancellationToken = default)
        => Task.FromResult<ImageRenderInput?>(null);

    /// <summary>
    /// The curve counterpart of <see cref="PreviewAsync"/>: runs a curve→curve <paramref name="operationId"/> on the
    /// active curve WITHOUT committing anything, and projects its result to an <b>owned</b> <see cref="CurveRenderInput"/>
    /// ("PREVIEW") the shell overlays on the source curve. Returns <c>null</c> when there is nothing to show (no active
    /// curve, unknown op, invalid parameters, a run failure, or a non-curve result). A default no-op keeps test doubles simple.
    /// </summary>
    Task<CurveRenderInput?> PreviewCurveAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        => Task.FromResult<CurveRenderInput?>(null);

    /// <summary>
    /// Why <paramref name="operationId"/> cannot run on the active dataset with <paramref name="values"/>, or
    /// <c>null</c> when it can.
    /// <para>
    /// <see cref="PreviewAsync"/> is best-effort and swallows the reason, which is right when the preview is a
    /// side pane the user can ignore. It is wrong when the preview <b>is</b> the stage: the picture would simply
    /// stop responding, with a setting on screen that does not describe what is being shown. A default of
    /// <c>null</c> keeps test doubles simple.
    /// </para>
    /// </summary>
    string? Explain(string operationId, IReadOnlyDictionary<string, object?> values) => null;

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
