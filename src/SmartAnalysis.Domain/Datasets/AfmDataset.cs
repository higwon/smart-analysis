namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// Base of every AFM dataset. Immutable and UI-free: no WPF types, no <c>INotifyPropertyChanged</c>,
/// no observable collections, no in-place mutation (fixing legacy C2). Concrete datasets are
/// <c>record</c>s composed from F01 types (<c>Unit</c>, <c>Axis</c>, <c>ScanBuffer&lt;T&gt;</c>).
/// <para>
/// Members added by later foundation tasks: <c>ChannelDescriptor</c>/<c>ScanMetadata</c> in <b>D01</b>;
/// <c>Provenance</c> in <b>F05</b> (every dataset will carry provenance — ADR-004). F03 provides the
/// identity + source + numeric structure.
/// </para>
/// </summary>
public abstract record AfmDataset
{
    protected AfmDataset(DatasetId id, DataSource source)
    {
        Id = id;
        Source = DomainGuard.NotNull(source, nameof(source));
    }

    /// <summary>Stable identity (never a file path).</summary>
    public DatasetId Id { get; }

    /// <summary>Where this dataset came from (provenance-only).</summary>
    public DataSource Source { get; }
}
