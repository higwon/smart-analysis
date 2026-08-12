using Microsoft.Win32;

namespace SmartAnalysis.UI.Services;

/// <summary>WPF implementation of <see cref="IWorkspacePathPicker"/> using the folder dialogs.</summary>
public sealed class WpfWorkspacePathPicker : IWorkspacePathPicker
{
    public string? PickSaveFolder()
    {
        // A workspace is a directory-package (the store creates/overwrites it). The user picks (or creates,
        // via the dialog's New-folder button) the target folder.
        var dialog = new OpenFolderDialog { Title = "Save workspace to folder" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickOpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open workspace folder" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
