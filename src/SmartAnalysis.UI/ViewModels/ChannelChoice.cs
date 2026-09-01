namespace SmartAnalysis.UI.ViewModels;

/// <summary>
/// One entry of a channel picker: which channel it is, and what to call it.
/// <para>
/// The index travels with the label so a selector can bind to the <b>item</b> rather than to a position. A
/// position into a list the shell replaces whenever the active dataset changes is not a selection the control
/// can hold on to — it kept the number and lost the thing behind it.
/// </para>
/// </summary>
public sealed record ChannelChoice(int Index, string Label);
