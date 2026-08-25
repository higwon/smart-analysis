using System.Text;
using SmartAnalysis.Application.FileFormats;

namespace SmartAnalysis.Infrastructure.FileFormats;

/// <summary>
/// The FF05 adapter: identifies a scan file by its leading bytes, falling back to the extension only when the content
/// says nothing. Reads at most a few bytes from the front of the file and never loads the payload.
/// <para>
/// Signatures used (all at offset 0):
/// <list type="bullet">
///   <item><b>TIFF</b> — <c>II*\0</c> (little-endian) or <c>MM\0*</c> (big-endian). Both byte orders are real: the
///   endianness marker <i>is</i> the first two bytes of the format.</item>
///   <item><b>PS-PPT</b> — the ASCII maker string the container opens with.</item>
///   <item><b>HDF5</b> — the standard <c>\x89HDF\r\n\x1a\n</c> signature, whose non-ASCII first byte and CR/LF pair
///   are there precisely so a corrupting transfer is detectable.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MagicByteFormatDetector : IScanFormatDetector
{
    /// <summary>The longest signature; only this many bytes are ever read.</summary>
    private const int ProbeLength = 8;

    private static readonly byte[] TiffLittleEndian = [0x49, 0x49, 0x2A, 0x00];              // II*\0
    private static readonly byte[] TiffBigEndian = [0x4D, 0x4D, 0x00, 0x2A];                 // MM\0*
    private static readonly byte[] Hdf5 = [0x89, 0x48, 0x44, 0x46, 0x0D, 0x0A, 0x1A, 0x0A];  // \x89HDF\r\n\x1a\n
    private static readonly byte[] PsPpt = Encoding.ASCII.GetBytes("PS-PPT/v");              // the maker string's stem

    public FormatDetection Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FormatDetection.Unknown;
        }

        // The bytes get the final say WHENEVER WE HAVE THEM. If the file could be opened and its start does not match
        // any known format, that is an answer — "unknown" — not an invitation to trust the name. Otherwise a text file
        // renamed .tiff would still be offered to the TIFF reader, which is exactly the legacy bug.
        if (TryReadProbe(path, out var probe))
        {
            return FromContent(probe) is { } content
                ? new FormatDetection(content, FormatEvidence.Content)
                : FormatDetection.Unknown;
        }

        // The content could not be examined at all (the file is missing, locked, or unreadable). Only now is the name
        // worth anything — and the caller is told that is all it was.
        return FromExtension(path) is { } byName
            ? new FormatDetection(byName, FormatEvidence.Extension)
            : FormatDetection.Unknown;
    }

    // True when the file could be opened and its start read (even if that start is empty or unrecognised); false when
    // the content could not be examined at all. The distinction is what lets an unrecognised-but-readable file be a
    // firm "unknown" while a missing one may still fall back to its name.
    private static bool TryReadProbe(string path, out byte[] probe)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[ProbeLength];
            int read = stream.ReadAtLeast(buffer, ProbeLength, throwOnEndOfStream: false);
            probe = read == ProbeLength ? buffer : buffer[..read]; // a short file is compared against what fits
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            probe = [];
            return false; // an unreadable file is not a crash — its content simply could not be examined
        }
    }

    private static ScanFileFormat? FromContent(ReadOnlySpan<byte> probe)
    {
        if (StartsWith(probe, TiffLittleEndian) || StartsWith(probe, TiffBigEndian))
        {
            return ScanFileFormat.Tiff;
        }

        if (StartsWith(probe, Hdf5))
        {
            return ScanFileFormat.Hdf5;
        }

        return StartsWith(probe, PsPpt) ? ScanFileFormat.PsPpt : null;
    }

    private static ScanFileFormat? FromExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        return extension.ToLowerInvariant() switch
        {
            ".tif" or ".tiff" => ScanFileFormat.Tiff,
            ".ppt" or ".psppt" => ScanFileFormat.PsPpt,
            ".h5" or ".hdf5" => ScanFileFormat.Hdf5,
            _ => null,
        };
    }

    // A signature only matches when the file is long enough to contain it: a 2-byte file must not "match" by prefix.
    private static bool StartsWith(ReadOnlySpan<byte> probe, ReadOnlySpan<byte> signature)
        => probe.Length >= signature.Length && probe[..signature.Length].SequenceEqual(signature);
}
