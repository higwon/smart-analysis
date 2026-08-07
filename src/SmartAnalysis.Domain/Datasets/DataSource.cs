namespace SmartAnalysis.Domain.Datasets;

/// <summary>
/// Where a dataset came from — provenance-only, <b>not identity</b> (identity is <see cref="DatasetId"/>).
/// A moved/renamed source can be relinked by <see cref="ContentHash"/>; the file path is never the key.
/// </summary>
public sealed record DataSource
{
    /// <param name="formatId">Stable format identifier, e.g. "psia-tiff", "ps-ppt", "parksystems-hdf5", "derived".</param>
    /// <param name="originalFilePath">Original file path, if imported from a file (informational only).</param>
    /// <param name="contentHash">Optional content hash for relink/identity-by-content.</param>
    public DataSource(string formatId, string? originalFilePath = null, string? contentHash = null)
    {
        FormatId = DomainGuard.Text(formatId, nameof(formatId));
        OriginalFilePath = originalFilePath;
        ContentHash = contentHash;
    }

    public string FormatId { get; }

    public string? OriginalFilePath { get; }

    public string? ContentHash { get; }

    /// <summary>A source for a dataset produced by processing (no originating file).</summary>
    public static DataSource Derived { get; } = new("derived");
}
