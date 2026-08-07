using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// A single force–distance curve: paired <see cref="Force"/> and <see cref="Separation"/> samples
/// (same length, 1D). Immutable.
/// <para>
/// F03 stores the raw curve only. The <b>approach/retract segmentation</b> and contact model are
/// added in <b>D03 / EPIC-SPEC01</b> (doc 12 OPEN: stored vs recomputed).
/// </para>
/// </summary>
public sealed record ForceCurveDataset : AfmDataset
{
    public ForceCurveDataset(
        DatasetId id,
        DataSource source,
        ScanBuffer<float> separation,
        ScanBuffer<float> force,
        Unit separationUnit,
        Unit forceUnit)
        : base(id, source)
    {
        Separation = DomainGuard.NotNull(separation, nameof(separation));
        Force = DomainGuard.NotNull(force, nameof(force));
        SeparationUnit = DomainGuard.NotNull(separationUnit, nameof(separationUnit));
        ForceUnit = DomainGuard.NotNull(forceUnit, nameof(forceUnit));

        if (separation.Height != 1 || force.Height != 1)
        {
            throw new ArgumentException("Force-curve buffers must be 1D (height 1).");
        }

        if (separation.Length != force.Length)
        {
            throw new ArgumentException(
                $"Separation and force must have equal length (was {separation.Length} vs {force.Length}).");
        }
    }

    public ScanBuffer<float> Separation { get; }

    public ScanBuffer<float> Force { get; }

    public Unit SeparationUnit { get; }

    public Unit ForceUnit { get; }

    /// <summary>Number of samples in the curve.</summary>
    public int Length => Force.Length;
}
