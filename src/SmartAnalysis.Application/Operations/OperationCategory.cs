namespace SmartAnalysis.Application.Operations;

/// <summary>
/// The launcher grouping an operation falls under (doc 26 command taxonomy). Derived from the operation's
/// <c>OutputKind</c> today (a derived dataset → <see cref="Process"/>, a measurement artifact →
/// <see cref="Measure"/>); <see cref="View"/>/<see cref="Output"/> are reserved for non-operation shell
/// actions (colormap/fit, export) that the launcher may host later.
/// </summary>
public enum OperationCategory
{
    Process,
    Measure,
    View,
    Output,
}
