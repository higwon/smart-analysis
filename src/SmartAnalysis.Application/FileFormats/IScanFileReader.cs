using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.FileFormats;

/// <summary>
/// Reads an instrument scan file into the immutable domain model. A <b>port</b> (ADR-010/015):
/// defined here in Application, referencing <b>Domain only</b> — no file-format-library types cross
/// this boundary. Infrastructure provides the adapter(s) (e.g. the PSIA-TIFF reader). Expected
/// failures (corrupt/truncated/unsupported) are returned as <see cref="FileReadResult"/> values, not
/// thrown (doc 13). A successful read returns an original/root dataset — its file origin lives on
/// <see cref="AfmDataset.Source"/>, its lineage is <c>ProvenanceRecord.Root</c> (ADR-013).
/// </summary>
public interface IScanFileReader
{
    /// <summary>Whether this reader recognizes the file (by extension, and later a content sniff).</summary>
    bool CanRead(string path);

    /// <summary>Reads the file headlessly, honoring cancellation. Never throws for expected invalidity.</summary>
    Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken cancellationToken);
}

/// <summary>Options for a read. <see cref="MetadataOnly"/> supports the legacy deferred/metadata-only mode.</summary>
public sealed record ScanReadOptions(bool MetadataOnly = false)
{
    public static ScanReadOptions Default { get; } = new();
}

/// <summary>Kinds of expected read failure (typed, never a silent null — doc 13, doc 07 M5).</summary>
public enum FileReadErrorKind
{
    /// <summary>The file could not be opened/read (I/O, missing, access).</summary>
    Io,

    /// <summary>The file is not a PSIA-TIFF (missing the PSIA magic tag).</summary>
    NotPsiaTiff,

    /// <summary>The file is structurally broken (bad/missing header, inconsistent dimensions).</summary>
    Corrupt,

    /// <summary>The pixel/data payload is missing or shorter than the header declares.</summary>
    Truncated,

    /// <summary>The image type is recognized but not yet supported by this reader.</summary>
    UnsupportedImageType,
}

/// <summary>A typed read failure: its <see cref="Kind"/> plus a human-readable context message.</summary>
public sealed record FileReadError(FileReadErrorKind Kind, string Message);

/// <summary>
/// The outcome of a read: either a <see cref="Dataset"/> (success) or a <see cref="Error"/> (failure),
/// never both. On success the caller owns the returned <see cref="AfmDataset"/> (it owns a buffer and
/// is <see cref="IDisposable"/>).
/// </summary>
public sealed class FileReadResult
{
    private FileReadResult(AfmDataset? dataset, FileReadError? error)
    {
        Dataset = dataset;
        Error = error;
    }

    public bool IsSuccess => Dataset is not null;

    public AfmDataset? Dataset { get; }

    public FileReadError? Error { get; }

    public static FileReadResult Success(AfmDataset dataset)
        => new(dataset ?? throw new ArgumentNullException(nameof(dataset)), null);

    public static FileReadResult Failure(FileReadErrorKind kind, string message)
        => new(null, new FileReadError(kind, string.IsNullOrWhiteSpace(message) ? kind.ToString() : message));
}
