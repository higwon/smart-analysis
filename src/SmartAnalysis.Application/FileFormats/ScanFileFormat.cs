namespace SmartAnalysis.Application.FileFormats;

/// <summary>A scan file format the product can recognise (FF05). <see cref="Unknown"/> is a real answer, not a gap.</summary>
public enum ScanFileFormat
{
    /// <summary>Nothing recognised it — neither its content nor its extension.</summary>
    Unknown,

    /// <summary>TIFF (little- or big-endian). The PSIA-specific tags are checked by the reader itself.</summary>
    Tiff,

    /// <summary>PS-PPT (Park Systems PinPoint) — a maker-string container.</summary>
    PsPpt,

    /// <summary>HDF5 (PiFM and friends).</summary>
    Hdf5,
}

/// <summary>How a format was decided — so a caller (and a log) can tell a real identification from a guess.</summary>
public enum FormatEvidence
{
    /// <summary>No evidence at all; the format is <see cref="ScanFileFormat.Unknown"/>.</summary>
    None,

    /// <summary>The file's own bytes identified it. Trustworthy regardless of what it is named.</summary>
    Content,

    /// <summary>Only the file name suggested it — the content was unreadable or unrecognised.</summary>
    Extension,
}

/// <summary>What a file was identified as, and on what basis.</summary>
public readonly record struct FormatDetection(ScanFileFormat Format, FormatEvidence Evidence)
{
    public static FormatDetection Unknown { get; } = new(ScanFileFormat.Unknown, FormatEvidence.None);

    /// <summary>Whether the file's own bytes settled it (rather than its name).</summary>
    public bool IsFromContent => Evidence == FormatEvidence.Content;
}

/// <summary>
/// Identifies a scan file's format (FF05) — a <b>port</b> (ADR-010) over paths and the enum above, so no format
/// library type crosses the boundary.
/// <para>
/// The legacy product decided by <b>file extension alone</b>, so a renamed or extension-less file was simply refused
/// and a mislabelled one was handed to the wrong parser. Detection here reads the file's own <b>magic bytes</b> first
/// and falls back to the extension only when the content says nothing — and it reports <b>which</b> of the two
/// decided, so "we recognised this file" is never confused with "the name looked right".
/// </para>
/// </summary>
public interface IScanFormatDetector
{
    /// <summary>
    /// Identifies <paramref name="path"/>. Never throws for an expected condition (missing file, no permission, an
    /// empty or truncated file) — those are <see cref="ScanFileFormat.Unknown"/>.
    /// </summary>
    FormatDetection Detect(string path);
}
