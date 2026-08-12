namespace SmartAnalysis.UI.Services;

/// <summary>
/// Abstraction over the "choose a workspace folder" dialogs (a workspace is a directory-package), so the
/// shell view-model stays headless-testable (a fake picker in tests; the WPF folder dialog at runtime).
/// </summary>
public interface IWorkspacePathPicker
{
    /// <summary>Folder to write the workspace package to, or <c>null</c> if the user cancelled.</summary>
    string? PickSaveFolder();

    /// <summary>Workspace-package folder to open, or <c>null</c> if the user cancelled.</summary>
    string? PickOpenFolder();
}
