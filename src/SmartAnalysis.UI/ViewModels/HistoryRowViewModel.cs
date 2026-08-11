namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// One row in the History / Provenance panel: a recorded step of the active dataset (doc 22/24). Selecting
/// a row (U02+) shows the step's op + params read-only; it never changes the active dataset (a
/// ProvenanceStep is not a navigable dataset — doc 22).
/// </summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(int order, string operation, string parameters, string statusIconKey, string statusBrushKey)
    {
        Order = order;
        Operation = operation;
        Parameters = parameters;
        StatusIconKey = statusIconKey;
        StatusBrushKey = statusBrushKey;
    }

    public int Order { get; }

    public string Operation { get; }

    public string Parameters { get; }

    /// <summary>An <c>SA.Icon.*</c> key (e.g. Check) resolved by the view.</summary>
    public string StatusIconKey { get; }

    /// <summary>An <c>SA.Brush.*</c> key for the status icon color.</summary>
    public string StatusBrushKey { get; }
}
