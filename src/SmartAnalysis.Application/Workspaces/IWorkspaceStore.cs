namespace SmartAnalysis.Application.Workspaces;

/// <summary>
/// Persists and restores a <see cref="Workspace"/> — datasets + provenance lineage + active context
/// (doc 16, ADR-017). A <b>port</b> (ADR-010): defined in Application, referencing Domain + the
/// Workspace only; no serializer/format type crosses this boundary. Infrastructure supplies the
/// adapter (directory-package). <see cref="Open"/> returns expected failures (not-a-workspace, unknown
/// schema, corrupt) as a <see cref="WorkspaceOpenResult"/> value; <see cref="Save"/> throws on I/O or
/// an unsupported dataset kind (a deliberate action, not an expected data condition).
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>Writes the workspace to <paramref name="path"/> (a directory-package, created/overwritten).</summary>
    void Save(Workspace workspace, string path);

    /// <summary>Reads a workspace from <paramref name="path"/>, restoring datasets, lineage, and active context.</summary>
    WorkspaceOpenResult Open(string path);
}

/// <summary>Kinds of expected workspace-open failure (typed, never a silent loss — fixes legacy H1).</summary>
public enum WorkspaceOpenErrorKind
{
    /// <summary>The path could not be read (missing, access, I/O).</summary>
    Io,

    /// <summary>The path is not a workspace package (no/invalid manifest).</summary>
    NotAWorkspace,

    /// <summary>The manifest schema version is not supported by this build (migration is P03).</summary>
    UnsupportedSchemaVersion,

    /// <summary>The package is structurally broken (bad entry, missing/short buffer, unknown unit).</summary>
    Corrupt,
}

/// <summary>A typed workspace-open failure: its <see cref="Kind"/> plus a human-readable context message.</summary>
public sealed record WorkspaceOpenError(WorkspaceOpenErrorKind Kind, string Message);

/// <summary>
/// The outcome of opening a workspace: either the restored <see cref="Workspace"/> or a typed
/// <see cref="Error"/>, never both. On success the caller owns the returned workspace (it owns the
/// datasets' buffers and is <see cref="IDisposable"/>).
/// </summary>
public sealed class WorkspaceOpenResult
{
    private WorkspaceOpenResult(Workspace? workspace, WorkspaceOpenError? error)
    {
        Workspace = workspace;
        Error = error;
    }

    public bool IsSuccess => Workspace is not null;

    public Workspace? Workspace { get; }

    public WorkspaceOpenError? Error { get; }

    public static WorkspaceOpenResult Success(Workspace workspace)
        => new(workspace ?? throw new ArgumentNullException(nameof(workspace)), null);

    public static WorkspaceOpenResult Failure(WorkspaceOpenErrorKind kind, string message)
        => new(null, new WorkspaceOpenError(kind, string.IsNullOrWhiteSpace(message) ? kind.ToString() : message));
}
