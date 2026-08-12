namespace SmartAnalysis.Application.Workspaces;

/// <summary>Result of a save/open the UI drives — success, or a human-readable error to surface.</summary>
public sealed record PersistenceOutcome(bool Success, string? Error)
{
    public static PersistenceOutcome Ok { get; } = new(true, null);

    public static PersistenceOutcome Failed(string error) => new(false, error);
}

/// <summary>
/// The Application use case the UI calls to save the current workspace and to open a saved one (P01-UI).
/// Wraps the <see cref="IWorkspaceStore"/> port and owns the session policy of an open: the store returns a
/// freshly-restored <see cref="Workspace"/>, which this replaces the session's workspace with <b>in place</b>
/// (<see cref="Workspace.ReplaceWith"/>) so every view-model stays bound. Expected failures come back as a
/// typed <see cref="PersistenceOutcome.Error"/>, never an exception into the UI.
/// </summary>
public interface IWorkspacePersistence
{
    /// <summary>Writes the current workspace to <paramref name="path"/> (a directory-package).</summary>
    PersistenceOutcome Save(string path);

    /// <summary>Opens the workspace at <paramref name="path"/>, replacing the current session workspace.</summary>
    PersistenceOutcome Open(string path);
}
