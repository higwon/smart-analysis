using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Analysis.Profiles;

/// <summary>
/// Shared construction of a line profile from an image + endpoints — used by both the <c>image.line-profile</c>
/// operation (with real provenance) and the live shell preview (transient), so the sampled values, the physical
/// arc-length axis, and the endpoint clamping are identical in both. The single <b>effective line</b> is the
/// request clamped into the image; <see cref="EffectiveLine"/> exposes it so callers record the same endpoints
/// they sampled. Pure/deterministic; the built dataset owns its buffer.
/// </summary>
public static class LineProfileBuilder
{
    /// <summary>The request clamped into <c>[0,width-1]×[0,height-1]</c> — the line that is displayed, dragged, executed, and recorded.</summary>
    public static (double X0, double Y0, double X1, double Y1) EffectiveLine(ScanImageDataset image, double x0, double y0, double x1, double y1)
    {
        ArgumentNullException.ThrowIfNull(image);
        double maxX = image.X.Count - 1, maxY = image.Y.Count - 1;
        return (Math.Clamp(x0, 0.0, maxX), Math.Clamp(y0, 0.0, maxY), Math.Clamp(x1, 0.0, maxX), Math.Clamp(y1, 0.0, maxY));
    }

    /// <summary>True when the effective (clamped) line collapses to a point.</summary>
    public static bool IsDegenerate(ScanImageDataset image, double x0, double y0, double x1, double y1)
    {
        var (ex0, ey0, ex1, ey1) = EffectiveLine(image, x0, y0, x1, y1);
        return ex0 == ex1 && ey0 == ey1;
    }

    /// <summary>
    /// Samples the image along the (clamped) line and builds a profile dataset of Z vs physical arc length. The
    /// caller supplies the identity + provenance (a derived step for the op, or <see cref="ProvenanceRecord.Root"/>
    /// for a transient preview). Clamps endpoints internally; <paramref name="samples"/> must be &gt;= 2.
    /// </summary>
    public static LineProfileDataset Build(
        ScanImageDataset image, double x0, double y0, double x1, double y1, int samples, DatasetId id, ProvenanceRecord provenance)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (ex0, ey0, ex1, ey1) = EffectiveLine(image, x0, y0, x1, y1);

        var line = LineSampler.Sample(image.Data.Memory.Span, image.X.Count, image.Y.Count, ex0, ey0, ex1, ey1, samples);

        // Physical arc length: convert the pixel deltas through each axis step to the base unit, then back to the
        // X unit — so a diagonal is measured correctly even when X and Y steps (or units) differ.
        double dxBase = (ex1 - ex0) * image.X.Step * image.X.Unit.ScaleToBase;
        double dyBase = (ey1 - ey0) * image.Y.Step * image.Y.Unit.ScaleToBase;
        double lengthInXUnit = Math.Sqrt((dxBase * dxBase) + (dyBase * dyBase)) / image.X.Unit.ScaleToBase;
        double stepInXUnit = lengthInXUnit / (samples - 1);

        var distanceAxis = new Axis("Distance", image.X.Unit, 0.0, stepInXUnit, samples);
        var buffer = ScanBuffer<float>.TakeOwnership(line, line.Length, 1);
        try
        {
            return new LineProfileDataset(id, DataSource.Derived, distanceAxis, image.Channel, buffer, image.Metadata, provenance);
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }
}
