namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// Stable identity of a dataset. This — <b>not a file path</b> — is how originals and derived results
/// are referenced (fixing legacy H1, where the file path was the de-facto identity). Value type.
/// </summary>
public readonly record struct DatasetId(Guid Value)
{
    /// <summary>Creates a fresh, unique id.</summary>
    public static DatasetId New() => new(Guid.NewGuid());

    /// <summary>True for <c>default(DatasetId)</c> / <see cref="Guid.Empty"/> — not a valid identity.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}
