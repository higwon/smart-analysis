using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Spectroscopy;

/// <summary>Where one curve of a spectroscopy acquisition was measured, on the surface's own frame.</summary>
public readonly record struct MapPointPosition(double X, double Y);

/// <summary>
/// The positions a spectroscopy file <b>recorded</b> for its points, one per curve.
/// <para>
/// This is not the same thing as <see cref="ForceVolumeGeometry"/>. A grid is a <i>rule</i> — first point,
/// spacing, count — from which positions can be reconstructed, and it only exists for a regular acquisition.
/// These are the positions the instrument actually wrote down, and they exist for hand-placed points too: one
/// sample file records three points on a diagonal, which no grid describes. Where both are present they agree,
/// but the recorded positions are what was measured and the grid is an inference from it.
/// </para>
/// <para>
/// Coordinates are in the <b>surface frame</b>: the same axes as the reference image, measured from its corner,
/// so a point can be drawn on it directly. The file stores them relative to the scan centre and rotated by the
/// scan angle; that transform is the reader's job, not this type's.
/// </para>
/// </summary>
public sealed class MapPointLayout
{
    private readonly MapPointPosition[] _positions;

    public MapPointLayout(IReadOnlyList<MapPointPosition> positions, Unit lengthUnit)
    {
        DomainGuard.NotNull(positions, nameof(positions));
        LengthUnit = DomainGuard.NotNull(lengthUnit, nameof(lengthUnit));

        if (positions.Count == 0)
        {
            throw new ArgumentException("A layout describes at least one point.", nameof(positions));
        }

        if (lengthUnit.Dimension != StandardUnits.Length)
        {
            throw new ArgumentException(
                $"A position is measured in a length, not '{lengthUnit.Symbol}'.", nameof(lengthUnit));
        }

        // A non-finite position cannot be drawn and cannot be compared. Accepting one would put a point
        // somewhere undefined on the surface, which looks like a place.
        var copy = new MapPointPosition[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
                throw new ArgumentException($"Point {i} has a non-finite position ({p.X}, {p.Y}).", nameof(positions));
            }

            copy[i] = p;
        }

        _positions = copy;
    }

    /// <summary>The positions, in the order the file recorded them — the same order as the curves.</summary>
    public IReadOnlyList<MapPointPosition> Positions => _positions;

    public int Count => _positions.Length;

    public Unit LengthUnit { get; }

    public MapPointPosition this[int pointIndex] => _positions[pointIndex];
}
