namespace SmartAnalysis.UI.ViewModels;

/// <summary>Outcome of a provenance step, mapped to icon + color by the view (theme-aware via DynamicResource).</summary>
public enum HistoryStatus
{
    Done,
    Running,
    Failed,
}

/// <summary>
/// One row in the History / Provenance panel: a recorded step of the active dataset (doc 22/24). The
/// view-model holds a semantic <see cref="Status"/> only — never a WPF resource key — so the view maps it
/// to a live <c>DynamicResource</c> brush/icon that follows the theme swap (fixing the earlier converter
/// that captured a one-time brush). Selecting a row (U02+) shows the step's op + params read-only; it never
/// changes the active dataset (a ProvenanceStep is not a navigable dataset — doc 22).
/// </summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(int order, string operation, string parameters, HistoryStatus status)
    {
        Order = order;
        Operation = operation;
        Parameters = parameters;
        Status = status;
    }

    public int Order { get; }

    public string Operation { get; }

    public string Parameters { get; }

    public HistoryStatus Status { get; }
}
