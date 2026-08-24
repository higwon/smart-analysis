using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Analysis;

/// <summary>The drawable shape of a <see cref="MeasurementRegion"/> — the two the image overlay can render.</summary>
public enum RegionOverlayShape
{
    Rectangle,
    Ellipse,
}

/// <summary>
/// Where on its source image a region measurement was taken, reconstructed from the measurement's provenance so the
/// shell can draw a read-only overlay when the measurement is selected ("this stat came from <i>here</i>"). Bounds are
/// pixel-index values in the source image's grid; <see cref="SourceId"/> identifies that image.
/// </summary>
public sealed record MeasurementRegion(
    DatasetId SourceId,
    RegionOverlayShape Shape,
    int Left,
    int Top,
    int Width,
    int Height);
