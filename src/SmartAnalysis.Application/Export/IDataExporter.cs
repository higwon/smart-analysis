using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Export;

/// <summary>
/// Writes analysis results to a plain data file (V05) — a <b>port</b> (ADR-010): defined in Application over Domain
/// types only, so no serializer/format type crosses the boundary. Infrastructure supplies the adapter (CSV today;
/// JCAMP and friends later). Every method writes the caller-chosen <paramref name="path"/>, overwriting it, and
/// throws on I/O — exporting is a deliberate user action, not an expected data condition.
/// </summary>
public interface IDataExporter
{
    /// <summary>The file extension (no dot) this exporter writes, for the save dialog's filter.</summary>
    string Extension { get; }

    /// <summary>Writes a curve as one row per sample: the X position and the channel value, with unit-bearing headers.</summary>
    void ExportCurve(LineProfileDataset curve, string path);

    /// <summary>Writes an image's Z values as a grid (one row per scan row), with the axis extents in the header.</summary>
    void ExportImage(ScanImageDataset image, string path);

    /// <summary>
    /// Writes a measurement: its scalar readouts (name, value, unit), then its per-row table when it has one
    /// (e.g. a peak list), so a measurement's full result leaves the app — not just what fits on the card.
    /// </summary>
    void ExportMeasurement(AnalysisArtifact measurement, string path);
}
