using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A single force–distance curve: paired <see cref="Force"/> and <see cref="Separation"/> samples
/// (same length, 1D). Entity keyed by <c>Id</c>; owns <b>both</b> buffers.
/// <para>
/// On success the ctor takes ownership of both buffers (dispose the dataset). If the ctor throws,
/// ownership stays with the caller. Passing the <b>same</b> buffer instance for both roles is rejected
/// so each buffer has exactly one owner. Units come from the channel descriptors
/// (<c>SeparationChannel.Unit</c>, <c>ForceChannel.Unit</c>). The approach/retract split is deliberately NOT
/// stored here: it is the output of a classifier mode + parameters, so it is computed on demand by
/// <c>ApproachRetractSegmentation</c> into a <c>CurveSegmentation</c> — a curve is never frozen to one
/// classifier's opinion (<b>ADR-020</b>, D03 / EPIC-SPEC01).
/// </para>
/// </summary>
public sealed class ForceCurveDataset : AfmDataset
{
    public ForceCurveDataset(
        DatasetId id,
        DataSource source,
        ScanBuffer<float> separation,
        ScanBuffer<float> force,
        ChannelDescriptor separationChannel,
        ChannelDescriptor forceChannel,
        ScanMetadata metadata,
        ProvenanceRecord provenance)
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

        if (separation.Height != 1 || force.Height != 1)
        {
            throw new ArgumentException("Force-curve buffers must be 1D (height 1).");
        }

        if (separation.Length != force.Length)
        {
            throw new ArgumentException(
                $"Separation and force must have equal length (was {separation.Length} vs {force.Length}).");
        }

        Separation = separation;
        Force = force;
    }

    public ScanBuffer<float> Separation { get; }

    public ScanBuffer<float> Force { get; }

    public ChannelDescriptor SeparationChannel { get; }

    public ChannelDescriptor ForceChannel { get; }

    /// <summary>Number of samples in the curve.</summary>
    public int Length => Force.Length;

    public override void Dispose()
    {
        Separation.Dispose();
        Force.Dispose(); // distinct instances guaranteed at construction
    }
}
