namespace SmartAnalysis.Application.Operations;

/// <summary>
/// A UI-facing launcher entry projected from an <c>OperationDescriptor</c> that is applicable to the
/// active dataset. The UI groups these by <see cref="Category"/> and, when one is chosen, resolves an
/// editor for it (a semantic override or the generic schema form) — the shell never enumerates operations
/// itself, so a new operation (A03+) appears here with no shell edits.
/// </summary>
public sealed record OperationLauncherItem(string Id, string DisplayName, string Summary, OperationCategory Category);
