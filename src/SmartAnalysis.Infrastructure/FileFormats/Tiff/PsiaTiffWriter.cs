using System.Buffers.Binary;
using System.Text;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Infrastructure.FileFormats.Tiff;

/// <summary>
/// Writes a <see cref="ScanImageDataset"/> back to a Park Systems PSIA-TIFF (the write counterpart of
/// <see cref="PsiaTiffReader"/>, ADR-015). Hand-emits the little-endian TIFF container + PSIA private tags
/// (MagicNumber 0xC500, Data 0xC502, Header 0xC503) so the reader parses it back, plus a standard
/// <c>ImageDescription</c> (0x010E) carrying the <see cref="TiffDomainSidecar"/> JSON (identity + provenance).
/// Pixels are written as float32 with <c>DataGain=1</c>/<c>ZOffset=0</c>, so the reader's
/// <c>physical = raw·gain + offset</c> returns the same values (lossless within float precision). No TIFF
/// library and no WPF (the PSIA layout is trivial to emit directly, keeping byte-for-byte reader compatibility).
/// <para>
/// X/Y scan geometry is <b>canonicalized to micrometres</b> — the fixed unit the PSIA header (and the reader)
/// use — so the physical coordinates survive even for a non-µm axis (e.g. nm); a non-length axis is rejected.
/// </para>
/// </summary>
public sealed class PsiaTiffWriter : IScanFileWriter
{
    private const int HeaderBytes = PsiaTiff.MinHeaderBytes; // 352
    private const ushort TagImageDescription = 0x010E;       // standard TIFF ASCII tag → the domain side-car JSON
    private const uint PsiaMagicValue = 0x0E031301;

    public bool CanWrite(AfmDataset dataset) => dataset is ScanImageDataset;

    public Task<FileWriteResult> WriteAsync(AfmDataset dataset, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(FileWriteResult.Failure(FileWriteErrorKind.Io, "Output path is empty."));
        }

        if (dataset is not ScanImageDataset image)
        {
            return Task.FromResult(FileWriteResult.Failure(
                FileWriteErrorKind.Unsupported,
                $"PSIA-TIFF writer supports {nameof(ScanImageDataset)} only; got {dataset.GetType().Name}."));
        }

        // The PSIA header stores X/Y scan size and offset in micrometres (the reader always reads them as µm),
        // so canonicalize the axes to µm here — otherwise a non-µm axis (e.g. nm) would be silently rescaled.
        if (!TryToMicrometre(image.X, out double xOriginUm, out double xStepUm)
            || !TryToMicrometre(image.Y, out double yOriginUm, out double yStepUm))
        {
            return Task.FromResult(FileWriteResult.Failure(
                FileWriteErrorKind.Unsupported,
                "PSIA-TIFF stores X/Y in micrometres; the dataset's scan axes are not length-dimensioned."));
        }

        try
        {
            byte[] description = BuildDescription(image);
            byte[] header = BuildHeader(image, xOriginUm, xStepUm, yOriginUm, yStepUm);
            byte[] pixels = PackFloatPixels(image.Data.Memory.Span);
            WriteFile(path, description, header, pixels);
            return Task.FromResult(FileWriteResult.Success(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or PathTooLongException or DirectoryNotFoundException)
        {
            return Task.FromResult(FileWriteResult.Failure(FileWriteErrorKind.Io, $"Failed to write '{path}': {ex.Message}"));
        }
    }

    private static byte[] BuildDescription(ScanImageDataset image)
    {
        // UTF-8 JSON side-car + NUL terminator (a valid ASCII/UTF-8 ImageDescription). Padded to an even
        // length so the following BYTE blocks start on a word boundary (TIFF requires even value offsets).
        var bytes = Encoding.UTF8.GetBytes(TiffDomainSidecar.Serialize(image));
        int length = bytes.Length + 1;      // + NUL terminator
        if (length % 2 != 0)
        {
            length++;                        // word-align the block
        }

        var block = new byte[length];
        Array.Copy(bytes, block, bytes.Length);
        return block;
    }

    // Converts an axis to the PSIA header's micrometre coordinate system: the origin is an affine coordinate
    // conversion (offset-aware), the step is a delta so only the scale ratio applies (no offset). Fails for a
    // non-length axis (not convertible to µm).
    private static bool TryToMicrometre(Axis axis, out double originUm, out double stepUm)
    {
        var conversion = new PhysicalValue(axis.Origin, axis.Unit).TryConvertTo(StandardUnits.Micrometre);
        if (!conversion.Success)
        {
            originUm = 0.0;
            stepUm = 0.0;
            return false;
        }

        originUm = conversion.Value.Value;
        stepUm = axis.Step * axis.Unit.ScaleToBase / StandardUnits.Micrometre.ScaleToBase;
        return true;
    }

    private static byte[] BuildHeader(ScanImageDataset image, double xOriginUm, double xStepUm, double yOriginUm, double yStepUm)
    {
        int width = image.X.Count;
        int height = image.Y.Count;

        // Field order mirrors the reader's sequential parse exactly (352 bytes). Numerics are little-endian
        // (BinaryWriter) and fixed strings are UTF-16LE, matching PsiaImageHeader.TryParse.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode);

        w.Write((int)PsiaTiff.ImageType.Scan2DMappedImage);
        WriteFixedString(w, image.Channel.Key, 32);              // SourceName ← channel key
        WriteFixedString(w, image.Metadata.InstrumentModel, 8);  // ImageMode  ← instrument model
        w.Write(0.0);            // LowPassFilterStrength
        w.Write(0);              // AutoFlatten
        w.Write(0);              // FlattenOrder
        w.Write(width);
        w.Write(height);
        w.Write(0.0);            // Angle
        w.Write(0);              // SineScan
        w.Write(0.0);            // OverScan
        w.Write(0);              // FastScanDir
        w.Write(0);              // SlowScanDirection
        w.Write(0);              // XYSwap
        w.Write(xStepUm * width);        // XScanSize = X.Step · Width, in µm
        w.Write(yStepUm * height);       // YScanSize = Y.Step · Height, in µm
        w.Write(xOriginUm);              // XOffset, in µm
        w.Write(yOriginUm);              // YOffset, in µm
        w.Write(0.0);            // ScanRate
        w.Write(0.0);            // SetPoint
        WriteFixedString(w, string.Empty, 8); // SetPointUnit
        w.Write(0.0);            // TipBias
        w.Write(0.0);            // SampleBias
        w.Write(1.0);            // DataGain = 1 (lossless float write)
        w.Write(0.0);            // ZScale
        w.Write(0.0);            // ZOffset = 0
        WriteFixedString(w, image.Channel.Unit.Symbol, 8);       // Unit ← channel unit symbol
        w.Write(0);              // DataMin
        w.Write(0);              // DataMax
        w.Write(0);              // DataAvg
        w.Write(0);              // Compression
        w.Write(0);              // LogScale
        w.Write(0);              // Square
        w.Write(0.0);            // ZServoGain
        w.Write(0.0);            // ZScannerRange
        WriteFixedString(w, string.Empty, 8); // XYVoltageMode
        WriteFixedString(w, string.Empty, 8); // ZVoltageMode
        WriteFixedString(w, string.Empty, 8); // XYServoMode
        w.Write((int)PsiaTiff.DataType.Float);

        w.Flush();
        var bytes = ms.ToArray();
        if (bytes.Length != HeaderBytes)
        {
            throw new InvalidOperationException($"PSIA header is {bytes.Length} bytes, expected {HeaderBytes}.");
        }

        return bytes;
    }

    private static byte[] PackFloatPixels(ReadOnlySpan<float> pixels)
    {
        var bytes = new byte[pixels.Length * sizeof(float)];
        for (int i = 0; i < pixels.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), pixels[i]);
        }

        return bytes;
    }

    private static void WriteFile(string path, byte[] description, byte[] header, byte[] data)
    {
        const int ifdOffset = 8;
        const int entryCount = 4; // ImageDescription, MagicNumber, Data, Header
        int ifdSize = 2 + (entryCount * 12) + 4;
        int descriptionOffset = ifdOffset + ifdSize;
        int headerOffset = descriptionOffset + description.Length;
        int dataOffset = headerOffset + header.Length;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // TIFF header (little-endian "II", magic 42, first IFD offset).
        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)ifdOffset);

        // IFD — entries MUST ascend by tag id: 0x010E < 0xC500 < 0xC502 < 0xC503.
        w.Write((ushort)entryCount);
        WriteEntry(w, TagImageDescription, type: 2 /*ASCII*/, count: (uint)description.Length, valueOrOffset: (uint)descriptionOffset);
        WriteEntry(w, PsiaTiff.TagMagicNumber, type: 4 /*LONG*/, count: 1, valueOrOffset: PsiaMagicValue); // inline value
        WriteEntry(w, PsiaTiff.TagData, type: 1 /*BYTE*/, count: (uint)data.Length, valueOrOffset: (uint)dataOffset);
        WriteEntry(w, PsiaTiff.TagHeader, type: 1 /*BYTE*/, count: (uint)header.Length, valueOrOffset: (uint)headerOffset);
        w.Write((uint)0); // next IFD

        // Data blocks in the same order as the offsets above.
        w.Write(description);
        w.Write(header);
        w.Write(data);

        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteEntry(BinaryWriter w, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        w.Write(tag);
        w.Write(type);
        w.Write(count);
        w.Write(valueOrOffset);
    }

    private static void WriteFixedString(BinaryWriter w, string s, int charCount)
    {
        var buffer = new byte[charCount * 2];
        if (!string.IsNullOrEmpty(s))
        {
            var encoded = Encoding.Unicode.GetBytes(s);
            Array.Copy(encoded, buffer, Math.Min(encoded.Length, buffer.Length - 2)); // leave a NUL terminator
        }

        w.Write(buffer);
    }
}
