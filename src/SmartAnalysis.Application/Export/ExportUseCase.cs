using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Export;

/// <summary>What the current selection can be exported as, so the shell can offer only the real choices.</summary>
public enum ExportTargetKind
{
    /// <summary>The active image's Z grid.</summary>
    Image,

    /// <summary>The active curve's samples.</summary>
    Curve,

    /// <summary>A selected measurement's readouts + table.</summary>
    Measurement,
}

/// <summary>One offered export: what it covers, and the suggested file name (without extension).</summary>
public sealed record ExportTarget(ExportTargetKind Kind, string Label, string SuggestedName);

/// <summary>The outcome of an export: success, or a typed message (no exception crosses to the UI).</summary>
public sealed record ExportOutcome(bool Success, string? Error)
{
    public static ExportOutcome Ok { get; } = new(true, null);

    public static ExportOutcome Failed(string error) => new(false, error);
}

/// <summary>
/// Runs data export on the UI's behalf (V05): decides what the current selection can be exported as, then writes it
/// through the <see cref="IDataExporter"/> port. Reads the workspace + measurement store; mutates neither, and never
/// changes the active context — exporting is a read-only side trip.
/// </summary>
public sealed class ExportUseCase : IExportUseCase
{
    private readonly Workspace _workspace;
    private readonly MeasurementStore _measurements;
    private readonly IDataExporter _exporter;

    public ExportUseCase(Workspace workspace, MeasurementStore measurements, IDataExporter exporter)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public string Extension => _exporter.Extension;

    public ExportTarget? DescribeActive()
    {
        if (_workspace.Active.ActiveId is not { } id || !_workspace.TryGet(id, out var dataset))
        {
            return null;
        }

        var name = DatasetLabel(dataset);
        return dataset switch
        {
            ScanImageDataset => new ExportTarget(ExportTargetKind.Image, "Image data", name),
            LineProfileDataset => new ExportTarget(ExportTargetKind.Curve, "Curve data", name),
            _ => null, // no exporter for this dataset kind yet (spectrum/force curve follow their own tasks)
        };
    }

    public ExportTarget? DescribeMeasurement(DatasetId measurementId)
        => _measurements.TryGet(measurementId, out var artifact)
            ? new ExportTarget(ExportTargetKind.Measurement, "Measurement", artifact.OperationId.Replace('.', '-'))
            : null;

    public ExportOutcome ExportActive(string path)
    {
        if (_workspace.Active.ActiveId is not { } id || !_workspace.TryGet(id, out var dataset))
        {
            return ExportOutcome.Failed("There is no active dataset to export.");
        }

        try
        {
            switch (dataset)
            {
                case ScanImageDataset image:
                    _exporter.ExportImage(image, path);
                    return ExportOutcome.Ok;
                case LineProfileDataset curve:
                    _exporter.ExportCurve(curve, path);
                    return ExportOutcome.Ok;
                default:
                    return ExportOutcome.Failed("The active dataset cannot be exported as data yet.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExportOutcome.Failed(ex.Message);
        }
    }

    public ExportOutcome ExportMeasurement(DatasetId measurementId, string path)
    {
        if (!_measurements.TryGet(measurementId, out var artifact))
        {
            return ExportOutcome.Failed("That measurement is no longer attached.");
        }

        try
        {
            _exporter.ExportMeasurement(artifact, path);
            return ExportOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExportOutcome.Failed(ex.Message);
        }
    }

    private static string DatasetLabel(AfmDataset d)
        => d.Provenance.IsRoot && d.Source.OriginalFilePath is { } p
            ? Path.GetFileNameWithoutExtension(p)
            : d.Provenance.Steps.Count > 0 ? d.Provenance.Steps[^1].OperationId.Replace('.', '-') : "dataset";
}
