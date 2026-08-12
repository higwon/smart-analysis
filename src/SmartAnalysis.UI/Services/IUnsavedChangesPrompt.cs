namespace SmartAnalysis.UI.Services;

/// <summary>The user's answer to "save before discarding the current workspace?".</summary>
public enum UnsavedChangesChoice
{
    /// <summary>Save the current workspace, then continue.</summary>
    Save,

    /// <summary>Discard unsaved changes and continue.</summary>
    DontSave,

    /// <summary>Abort — change nothing.</summary>
    Cancel,
}

/// <summary>
/// Asks the user what to do about unsaved changes before a destructive action (e.g. Open replacing the
/// session). Abstracted so the shell view-model stays free of WPF <c>MessageBox</c> and headless-testable.
/// </summary>
public interface IUnsavedChangesPrompt
{
    UnsavedChangesChoice Ask(string workspaceName);
}
