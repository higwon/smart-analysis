using Microsoft.Win32;

namespace SmartAnalysis.UI.Services;

/// <summary>WPF implementation of <see cref="IScanFilePicker"/> using the standard open-file dialog.</summary>
public sealed class WpfScanFilePicker : IScanFilePicker
{
    public string? PickScanFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open scan",
            Filter = "Scan files (*.tiff;*.tif)|*.tiff;*.tif|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
