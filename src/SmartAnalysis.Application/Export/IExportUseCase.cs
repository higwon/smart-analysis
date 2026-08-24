using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Export;

/// <summary>
/// The Application service behind the shell's data export (V05). The UI asks what the current selection can be
/// exported as (to build its save dialog), then asks for the write; it never sees a format or serializer type.
/// </summary>
public interface IExportUseCase
{
    /// <summary>The file extension (no dot) the configured exporter writes — for the save dialog's filter.</summary>
    string Extension { get; }

    /// <summary>What the active dataset can be exported as, or <c>null</c> when nothing exportable is active.</summary>
    ExportTarget? DescribeActive();

    /// <summary>What an attached measurement can be exported as, or <c>null</c> when it is not attached.</summary>
    ExportTarget? DescribeMeasurement(DatasetId measurementId);

    /// <summary>Writes the active dataset's data to <paramref name="path"/>; a typed failure never throws to the UI.</summary>
    ExportOutcome ExportActive(string path);

    /// <summary>Writes an attached measurement's readouts + table to <paramref name="path"/>.</summary>
    ExportOutcome ExportMeasurement(DatasetId measurementId, string path);
}
