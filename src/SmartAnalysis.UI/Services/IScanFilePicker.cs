namespace SmartAnalysis.UI.Services;

/// <summary>
/// Abstraction over the "choose a scan file" dialog so the shell view-model stays headless-testable
/// (a fake picker in tests/render harness; the WPF <c>OpenFileDialog</c> at runtime).
/// </summary>
public interface IScanFilePicker
{
    /// <summary>Returns the chosen scan-file path, or <c>null</c> if the user cancelled.</summary>
    string? PickScanFile();
}
