using System.IO;

namespace SmartAnalysis.Application.Workspaces;

/// <inheritdoc cref="IWorkspacePersistence"/>
public sealed class WorkspacePersistenceUseCase : IWorkspacePersistence
{
    private readonly Workspace _workspace;
    private readonly IWorkspaceStore _store;

    public WorkspacePersistenceUseCase(Workspace workspace, IWorkspaceStore store)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public PersistenceOutcome Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PersistenceOutcome.Failed("No save location was chosen.");
        }

        try
        {
            _store.Save(_workspace, path);
            return PersistenceOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return PersistenceOutcome.Failed($"Could not save the workspace: {ex.Message}");
        }
    }

    public PersistenceOutcome Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PersistenceOutcome.Failed("No workspace was chosen.");
        }

        WorkspaceOpenResult result;
        try
        {
            result = _store.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PersistenceOutcome.Failed($"Could not open the workspace: {ex.Message}");
        }

        if (!result.IsSuccess)
        {
            return PersistenceOutcome.Failed(result.Error!.Message);
        }

        // Adopt the restored workspace in place; then dispose the now-empty source shell (ReplaceWith moved
        // its datasets out, so disposing it releases nothing we kept).
        var opened = result.Workspace!;
        try
        {
            _workspace.ReplaceWith(opened);
        }
        finally
        {
            opened.Dispose();
        }

        return PersistenceOutcome.Ok;
    }
}
