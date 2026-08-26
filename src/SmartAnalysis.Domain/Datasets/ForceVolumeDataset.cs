using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// Many force–distance curves measured in one acquisition — a force–volume map, or a set of hand-placed points.
/// Entity keyed by <c>Id</c>; owns <b>both</b> buffers.
/// <para>
/// Storage is one row per curve: both buffers are <c>SampleCount</c> wide and <c>PointCount</c> tall, so the
/// curves are contiguous and a point is a row slice. Every curve shares the two channel descriptors, because a
/// single acquisition sweeps the same two channels at every point — a file where that were not true would not
/// be one dataset.
/// </para>
/// <para>
/// <see cref="Geometry"/> is deliberately nullable: the instrument records a grid for a force–volume map and
/// nothing for arbitrary points, and fabricating one would place curves where nothing was measured. As with
/// <see cref="ForceCurveDataset"/>, the approach/retract split is <b>not</b> stored — it is a classifier's
/// opinion, computed per curve on demand (<b>ADR-020</b>).
/// </para>
/// </summary>
public sealed class ForceVolumeDataset : AfmDataset
{
    private readonly ScanBuffer<float> _separation;
    private readonly ScanBuffer<float> _force;

    public ForceVolumeDataset(
        DatasetId id,
        DataSource source,
        ScanBuffer<float> separation,
        ScanBuffer<float> force,
        ChannelDescriptor separationChannel,
        ChannelDescriptor forceChannel,
        ForceVolumeGeometry? geometry,
        ScanMetadata metadata,
        ProvenanceRecord provenance,
        SpectroscopyChannelSet? channels = null,
        ScanImageDataset? referenceImage = null,
        MapPointLayout? pointLayout = null)
        : base(id, source, metadata, provenance)
    {
        DomainGuard.NotNull(separation, nameof(separation));
        DomainGuard.NotNull(force, nameof(force));
        SeparationChannel = DomainGuard.NotNull(separationChannel, nameof(separationChannel));
        ForceChannel = DomainGuard.NotNull(forceChannel, nameof(forceChannel));

        if (ReferenceEquals(separation, force))
        {
            throw new ArgumentException("Separation and force must be distinct buffers (single-owner per buffer).", nameof(force));
        }

        if (separation.Width != force.Width || separation.Height != force.Height)
        {
            throw new ArgumentException(
                $"Separation and force must have the same shape (was {separation.Width}x{separation.Height} "
                + $"vs {force.Width}x{force.Height}).");
        }

        if (separation.Width < 2)
        {
            throw new ArgumentException($"A curve needs more than one sample (was {separation.Width}).");
        }

        if (separation.Height < 1)
        {
            throw new ArgumentException($"A map needs at least one curve (was {separation.Height}).");
        }

        // A grid that does not account for exactly the curves present is a header disagreeing with its payload,
        // which would silently misplace every point after the first mismatch.
        if (geometry is { } grid && grid.PointCount != separation.Height)
        {
            throw new ArgumentException(
                $"The grid describes {grid.PointCount} points but the map holds {separation.Height} curves.",
                nameof(geometry));
        }

        ReferenceImage = referenceImage;
        AttachLayout(pointLayout, separation.Height);
        AttachChannels(channels, separation.Height, separation.Width);

        _separation = separation;
        _force = force;
        Geometry = geometry;
    }

    /// <summary>How many curves the map holds.</summary>
    public int PointCount => _separation.Height;

    /// <summary>How many samples each curve has. Every curve in one acquisition has the same length.</summary>
    public int SampleCount => _separation.Width;

    public ChannelDescriptor SeparationChannel { get; }

    public ChannelDescriptor ForceChannel { get; }

    /// <summary>The map's grid, or null when the file placed its points without one.</summary>
    public ForceVolumeGeometry? Geometry { get; }

    /// <summary>Whether the curves lie on a regular grid rather than at arbitrary positions.</summary>
    public bool IsGrid => Geometry is not null;

    /// <summary>The separation samples of one curve.</summary>
    public ReadOnlyMemory<float> SeparationAt(int pointIndex) => _separation.Slice(Offset(pointIndex), SampleCount);

    /// <summary>The force samples of one curve.</summary>
    public ReadOnlyMemory<float> ForceAt(int pointIndex) => _force.Slice(Offset(pointIndex), SampleCount);

    /// <summary>
    /// Every channel the acquisition measured, when the reader kept them. The designated separation/force
    /// pair is what the analysis uses; this is what the <i>file</i> contained, so nothing measured is lost and
    /// a different pair can be chosen later.
    /// </summary>
    public SpectroscopyChannelSet? Channels { get; private set; }

    /// <summary>
    /// The surface the acquisition was measured on, when the file carried one. A PSIA spectroscopy file
    /// commonly embeds a 2D scan in the <b>same</b> IFD (tag <c>0xC502</c>) — the reference image the
    /// instrument showed while the points were placed. Owned by this dataset, so it lives and dies with it.
    /// </summary>
    public ScanImageDataset? ReferenceImage { get; }

    /// <summary>
    /// Where each curve was measured, as the file recorded it — in the same frame as
    /// <see cref="ReferenceImage"/>, so a point can be drawn on the surface directly. Null when the file
    /// recorded no positions, which is a real case and not a failure.
    /// </summary>
    public MapPointLayout? PointLayout { get; private set; }

    private void AttachLayout(MapPointLayout? layout, int pointCount)
    {
        if (layout is null)
        {
            return;
        }

        // One position per curve. A layout of a different length would mark the wrong place for every point
        // past the mismatch, and each mark would look just as authoritative as a correct one.
        if (layout.Count != pointCount)
        {
            throw new ArgumentException(
                $"The layout describes {layout.Count} points but this dataset holds {pointCount}.",
                nameof(layout));
        }

        PointLayout = layout;
    }

    private void AttachChannels(SpectroscopyChannelSet? channels, int pointCount, int sampleCount)
    {
        if (channels is null)
        {
            return;
        }

        // The channels must describe THIS acquisition. A set of a different shape would let a caller read a
        // curve that has nothing to do with the one the dataset designates.
        if (channels.PointCount != pointCount || channels.SampleCount != sampleCount)
        {
            throw new ArgumentException(
                $"The channel set covers {channels.PointCount}x{channels.SampleCount}, but this dataset is "
                + $"{pointCount}x{sampleCount}.", nameof(channels));
        }

        Channels = channels;
    }

    public override void Dispose()
    {
        _separation.Dispose();
        _force.Dispose(); // distinct instances guaranteed at construction
        Channels?.Dispose();
        ReferenceImage?.Dispose();
    }

    private int Offset(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex), pointIndex, $"The map holds {PointCount} curves.");
        }

        return pointIndex * SampleCount;
    }
}
