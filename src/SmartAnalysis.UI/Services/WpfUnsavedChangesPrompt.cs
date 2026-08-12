using System.Windows;

namespace SmartAnalysis.UI.Services;

/// <summary>WPF implementation of <see cref="IUnsavedChangesPrompt"/> using a Yes/No/Cancel message box.</summary>
public sealed class WpfUnsavedChangesPrompt : IUnsavedChangesPrompt
{
    public UnsavedChangesChoice Ask(string workspaceName)
    {
        var result = MessageBox.Show(
            $"Save changes to “{workspaceName}” before opening another workspace?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesChoice.Save,
            MessageBoxResult.No => UnsavedChangesChoice.DontSave,
            _ => UnsavedChangesChoice.Cancel,
        };
    }
}
