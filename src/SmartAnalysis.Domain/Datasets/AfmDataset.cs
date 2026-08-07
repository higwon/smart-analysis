using SmartAnalysis.Domain.Metadata;

namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// Base of every AFM dataset. An <b>entity</b> keyed by <see cref="Id"/> (ADR-012): equality and hash
/// are by <see cref="Id"/> only, so a dataset reloaded with the same <see cref="DatasetId"/> compares
/// equal regardless of buffer instances (fixing legacy H1). Externally immutable and UI-free (no WPF
/// types, no <c>INotifyPropertyChanged</c>, no in-place mutation — fixing legacy C2).
/// <para>
/// A dataset <b>owns its buffer(s)</b> and is <see cref="IDisposable"/> (ADR-011/012):
/// <b>ownership of a <c>ScanBuffer</c> passed to a constructor transfers to the dataset on success</b>
/// (dispose the dataset, not the buffer); if a constructor throws, ownership stays with the caller.
/// </para>
/// <para>
/// Members added by later foundation tasks: <c>Provenance</c> in <b>F05</b> (ADR-004). F01/F03/D01
/// provide identity + source + metadata + numeric structure + typed channels.
/// </para>
/// </summary>
public abstract class AfmDataset : IEquatable<AfmDataset>, IDisposable
{
    protected AfmDataset(DatasetId id, DataSource source, ScanMetadata metadata)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("DatasetId must not be empty.", nameof(id));
        }

        Id = id;
        Source = DomainGuard.NotNull(source, nameof(source));
        Metadata = DomainGuard.NotNull(metadata, nameof(metadata));
    }

    /// <summary>Stable identity (never a file path).</summary>
    public DatasetId Id { get; }

    /// <summary>Where this dataset came from (provenance-only).</summary>
    public DataSource Source { get; }

    /// <summary>Acquisition metadata (D01). Use <see cref="ScanMetadata.Unknown"/> when none.</summary>
    public ScanMetadata Metadata { get; }

    /// <summary>Releases the buffer(s) this dataset owns.</summary>
    public abstract void Dispose();

    // --- Identity-based equality (ADR-012) ---

    public bool Equals(AfmDataset? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as AfmDataset);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(AfmDataset? left, AfmDataset? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(AfmDataset? left, AfmDataset? right) => !(left == right);
}
