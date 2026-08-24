using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Where on its source image a line-profile curve was sampled, reconstructed from the curve's provenance so the shell
/// can draw a read-only line back on the source image ("this profile runs <i>here</i>") beside the curve. Endpoints are
/// pixel coordinates in the source image's grid; <see cref="SourceId"/> identifies that image.
/// </summary>
public sealed record MeasurementLine(
    DatasetId SourceId,
    double X0,
    double Y0,
    double X1,
    double Y1);
