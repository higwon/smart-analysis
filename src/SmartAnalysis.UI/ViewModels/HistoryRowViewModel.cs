using System.Collections.Generic;

namespace SmartAnalysis.UI.ViewModels;

/// <summary>Outcome of a provenance step, mapped to icon + color by the view (theme-aware via DynamicResource).</summary>
public enum HistoryStatus
{
    Done,
    Running,
    Failed,
}

/// <summary>One recorded parameter of a provenance step: a name and its already-formatted value (with unit).</summary>
public sealed class StepParameterViewModel
{
    public StepParameterViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

/// <summary>
/// One row in the History / Provenance panel (U05): a recorded step of the active dataset (doc 22/24). Carries
/// the step's real, auditable detail from F05 — the <see cref="Parameters"/> that were applied (name + value with
/// unit) and any <see cref="Warnings"/> — plus a compact <see cref="Summary"/> for the strip. The view-model holds
/// a semantic <see cref="Status"/> only — never a WPF resource key — so the view maps it to a live
/// <c>DynamicResource</c> brush/icon that follows the theme swap. Selecting a row shows the step read-only; it
/// never changes the active dataset (a ProvenanceStep is not a navigable dataset — doc 22).
/// </summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(
        int order,
        string operation,
        string summary,
        HistoryStatus status,
        IReadOnlyList<StepParameterViewModel>? parameters = null,
        IReadOnlyList<string>? warnings = null,
        string? operationId = null)
    {
        Order = order;
        Operation = operation;
        Summary = summary;
        Status = status;
        Parameters = parameters ?? [];
        Warnings = warnings ?? [];
        OperationId = operationId ?? string.Empty;
    }

    public int Order { get; }

    public string Operation { get; }

    /// <summary>A compact one-line summary shown in the strip (e.g. the applied parameters, or the import source).</summary>
    public string Summary { get; }

    public HistoryStatus Status { get; }

    /// <summary>The recorded parameters (name + formatted value) shown in the step inspector.</summary>
    public IReadOnlyList<StepParameterViewModel> Parameters { get; }

    public bool HasParameters => Parameters.Count > 0;

    /// <summary>Warnings recorded when the step ran (e.g. clamped region, skipped non-finite lines).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>The raw operation id (e.g. "image.psd"); empty for the Import row.</summary>
    public string OperationId { get; }
}
