using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.FileFormats;

/// <summary>
/// Writes a domain dataset back to an instrument scan file. The write counterpart of
/// <see cref="IScanFileReader"/> — a <b>port</b> (ADR-010/015) defined in Application, referencing
/// <b>Domain only</b>; Infrastructure provides the adapter (e.g. the PSIA-TIFF writer). Unlike a read,
/// a write embeds the dataset's <b>identity and provenance</b> so a written result round-trips its lineage
/// (F05), not just its pixels. Expected failures are returned as <see cref="FileWriteResult"/> values,
/// not thrown.
/// </summary>
public interface IScanFileWriter
{
    /// <summary>Whether this writer can serialize the given dataset (by its concrete kind).</summary>
    bool CanWrite(AfmDataset dataset);

    /// <summary>Writes the dataset to <paramref name="path"/> headlessly, honoring cancellation.</summary>
    Task<FileWriteResult> WriteAsync(AfmDataset dataset, string path, CancellationToken cancellationToken);
}

/// <summary>Kinds of expected write failure (typed, never a silent throw for known invalidity).</summary>
public enum FileWriteErrorKind
{
    /// <summary>The file could not be written (I/O, access, missing directory).</summary>
    Io,

    /// <summary>The dataset kind is not supported by this writer.</summary>
    Unsupported,
}

/// <summary>A typed write failure: its <see cref="Kind"/> plus a human-readable context message.</summary>
public sealed record FileWriteError(FileWriteErrorKind Kind, string Message);

/// <summary>
/// The outcome of a write: either success (carrying the written <see cref="Path"/>) or a
/// <see cref="Error"/>, never both.
/// </summary>
public sealed class FileWriteResult
{
    private FileWriteResult(string? path, FileWriteError? error)
    {
        Path = path;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public string? Path { get; }

    public FileWriteError? Error { get; }

    public static FileWriteResult Success(string path)
        => new(string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Path is required.", nameof(path)) : path, null);

    public static FileWriteResult Failure(FileWriteErrorKind kind, string message)
        => new(null, new FileWriteError(kind, string.IsNullOrWhiteSpace(message) ? kind.ToString() : message));
}
