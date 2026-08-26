using System.Text;

namespace SmartAnalysis.Tests.FileFormats;

/// <summary>
/// Hand-writes a minimal, deterministic little-endian PSIA-TIFF for tests — the PSIA private tags
/// (MagicNumber 0xC500, Data 0xC502, Header 0xC503) laid out so the real TiffLibrary parser (used by
/// <c>PsiaTiffReader</c>) reads them back. The header byte layout mirrors the reader's sequential
/// parse exactly, so a round-trip validates the reader's tag IO, header parse, endianness, pixel
/// decode, and domain mapping without any committed binary fixture or customer data (ADR-015).
/// </summary>
internal static class PsiaTiffTestWriter
{
    public const ushort TagMagicNumber = 0xC500;
    public const ushort TagData = 0xC502;
    public const ushort TagHeader = 0xC503;

    private const int HeaderBytes = 352; // matches PsiaTiff.MinHeaderBytes

    /// <summary>Builds the PSIA header block (352 bytes) in the exact field order the reader parses.</summary>
    public static byte[] BuildHeader(
        int imageType,
        int width,
        int height,
        double xScanSize,
        double yScanSize,
        double xOffset,
        double yOffset,
        double dataGain,
        double zOffset,
        string unit,
        string sourceName,
        string imageMode,
        int dataType)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode);

        w.Write(imageType);
        WriteFixedString(w, sourceName, 32);
        WriteFixedString(w, imageMode, 8);
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
        w.Write(xScanSize);
        w.Write(yScanSize);
        w.Write(xOffset);
        w.Write(yOffset);
        w.Write(0.0);            // ScanRate
        w.Write(0.0);            // SetPoint
        WriteFixedString(w, string.Empty, 8); // SetPointUnit
        w.Write(0.0);            // TipBias
        w.Write(0.0);            // SampleBias
        w.Write(dataGain);
        w.Write(0.0);            // ZScale
        w.Write(zOffset);
        WriteFixedString(w, unit, 8);
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
        w.Write(dataType);

        w.Flush();
        var bytes = ms.ToArray();
        if (bytes.Length != HeaderBytes)
        {
            throw new InvalidOperationException($"Test header is {bytes.Length} bytes, expected {HeaderBytes}.");
        }

        return bytes;
    }

    /// <summary>
    /// Writes a PSIA-TIFF file to <paramref name="path"/>. With <paramref name="includeMagic"/> false the
    /// MagicNumber tag is omitted (to exercise the not-a-PSIA-TIFF path).
    /// </summary>
    public static void WriteFile(string path, byte[] header, byte[] data, uint magic = 0x0E031301, bool includeMagic = true)
    {
        const int ifdOffset = 8;
        int entryCount = includeMagic ? 3 : 2;
        int ifdSize = 2 + entryCount * 12 + 4;
        int headerBlockOffset = ifdOffset + ifdSize;
        int dataBlockOffset = headerBlockOffset + header.Length;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // TIFF header (little-endian "II", magic 42, first IFD offset).
        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)ifdOffset);

        // IFD — entries MUST be ascending by tag id (0xC500 < 0xC502 < 0xC503).
        w.Write((ushort)entryCount);
        if (includeMagic)
        {
            WriteEntry(w, TagMagicNumber, type: 4 /*LONG*/, count: 1, valueOrOffset: magic); // inline value
        }

        WriteEntry(w, TagData, type: 1 /*BYTE*/, count: (uint)data.Length, valueOrOffset: (uint)dataBlockOffset);
        WriteEntry(w, TagHeader, type: 1 /*BYTE*/, count: (uint)header.Length, valueOrOffset: (uint)headerBlockOffset);
        w.Write((uint)0); // next IFD

        // Data blocks (order matches the offsets above).
        w.Write(header);
        w.Write(data);

        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>Packs a raw pixel array into little-endian bytes for the given PSIA data type (0=short,1=int,2=float).</summary>
    public static byte[] PackPixels(double[] raw, int dataType)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var v in raw)
        {
            switch (dataType)
            {
                case 0: w.Write((short)v); break;
                case 1: w.Write((int)v); break;
                default: w.Write((float)v); break;
            }
        }

        w.Flush();
        return ms.ToArray();
    }


    public const ushort TagSpectroscopyHeader = 0xC506;
    public const ushort TagSpectroscopyData = 0xC507;

    /// <summary>One channel slot for <see cref="BuildSpectroscopyHeader"/>.</summary>
    internal readonly record struct SpectroscopyLine(
        string SourceName, string Unit, double DataGain, bool IsXAxis, bool IsYAxis, double Offset = 0);

    /// <summary>Bytes the writer emits for tag 0xC506 — through ForceConstant and Sensitivity.</summary>
    public const int SpectroscopyHeaderBytes = 992;

    /// <summary>A real 0xC506 header's size, which runs past Sensitivity.</summary>
    public const int FullSpectroscopyHeaderBytes = 1220;

    /// <summary>Where the payload's element type sits in a full header.</summary>
    public const int PayloadDataTypeOffset = 1108;

    /// <summary>Builds a spectroscopy header (tag 0xC506) with the eight fixed channel slots.</summary>
    public static byte[] BuildSpectroscopyHeader(
        SpectroscopyLine[] lines,
        int dataPoints,
        int spectroscopyPoints = 1,
        double forceConstant = 0,
        double sensitivity = 0,
        bool volumeImage = false,
        int pointsPerX = 0,
        double scanSizeX = 0,
        double scanSizeY = 0,
        double offsetX = 0,
        double offsetY = 0,
        int? sourceCountOverride = null,
        int? payloadDataType = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode);

        for (int i = 0; i < 8; i++)
        {
            var line = i < lines.Length ? lines[i] : default;
            WriteFixedString(w, line.SourceName ?? string.Empty, 32);
            WriteFixedString(w, line.Unit ?? string.Empty, 8);
            w.Write(line.DataGain);
            w.Write(line.IsXAxis ? 1 : 0);
            w.Write(line.IsYAxis ? 1 : 0);
        }

        w.Write(sourceCountOverride ?? lines.Length); // SpectSources
        w.Write(0);                                    // Average
        w.Write(dataPoints);
        w.Write(spectroscopyPoints);
        w.Write(0);                                    // DrivingSourceIndex
        for (int i = 0; i < 4; i++)
        {
            w.Write(0f);                               // drive periods / speeds
        }

        w.Write(volumeImage ? 1 : 0);
        for (int i = 0; i < 8; i++)
        {
            w.Write(i < lines.Length ? lines[i].Offset : 0.0);
        }

        for (int i = 0; i < 16; i++)
        {
            w.Write(0);                                // LogScale[8] + Square[8]
        }

        w.Write(pointsPerX);
        w.Write(0);                                    // ReferenceImage
        w.Write(scanSizeX);
        w.Write(scanSizeY);
        w.Write(offsetX);
        w.Write(offsetY);

        w.Write(forceConstant);
        w.Write(sensitivity);

        w.Flush();
        var bytes = ms.ToArray();
        if (bytes.Length != SpectroscopyHeaderBytes)
        {
            throw new InvalidOperationException($"Test spectroscopy header is {bytes.Length} bytes, expected {SpectroscopyHeaderBytes}.");
        }

        if (payloadDataType is not { } dataType)
        {
            return bytes;
        }

        // A real header runs on past Sensitivity; the payload's element type sits at a known offset inside that tail.
        var full = new byte[FullSpectroscopyHeaderBytes];
        bytes.CopyTo(full, 0);
        BitConverter.GetBytes(dataType).CopyTo(full, PayloadDataTypeOffset);
        return full;
    }

    /// <summary>
    /// Writes a PSIA spectroscopy TIFF: the 2D header (which carries the image type and the payload's element
    /// width) plus tags 0xC506/0xC507. The payload is channel-planar, exactly as the instrument writes it.
    /// </summary>
    public static void WriteSpectroscopyFile(string path, byte[] imageHeader, byte[] spectroscopyHeader, byte[] data)
    {
        const int ifdOffset = 8;
        const int entryCount = 4;
        const int ifdSize = 2 + (entryCount * 12) + 4;
        int imageHeaderOffset = ifdOffset + ifdSize;
        int spectroscopyHeaderOffset = imageHeaderOffset + imageHeader.Length;
        int dataOffset = spectroscopyHeaderOffset + spectroscopyHeader.Length;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)ifdOffset);

        // Entries must be ascending by tag id (0xC500 < 0xC503 < 0xC506 < 0xC507).
        w.Write((ushort)entryCount);
        WriteEntry(w, TagMagicNumber, type: 4, count: 1, valueOrOffset: 0x0E031301);
        WriteEntry(w, TagHeader, type: 1, count: (uint)imageHeader.Length, valueOrOffset: (uint)imageHeaderOffset);
        WriteEntry(w, TagSpectroscopyHeader, type: 1, count: (uint)spectroscopyHeader.Length, valueOrOffset: (uint)spectroscopyHeaderOffset);
        WriteEntry(w, TagSpectroscopyData, type: 1, count: (uint)data.Length, valueOrOffset: (uint)dataOffset);
        w.Write((uint)0);

        w.Write(imageHeader);
        w.Write(spectroscopyHeader);
        w.Write(data);

        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>
    /// A spectroscopy TIFF that also carries the 2D scan the points were placed on — tag 0xC502 in the SAME
    /// IFD, which is how real files store the reference image.
    /// </summary>
    public static void WriteSpectroscopyFileWithSurface(
        string path, byte[] imageHeader, byte[] spectroscopyHeader, byte[] data, byte[] surfacePixels)
    {
        const int ifdOffset = 8;
        const int entryCount = 5;
        const int ifdSize = 2 + (entryCount * 12) + 4;
        int surfaceOffset = ifdOffset + ifdSize;
        int imageHeaderOffset = surfaceOffset + surfacePixels.Length;
        int spectroscopyHeaderOffset = imageHeaderOffset + imageHeader.Length;
        int dataOffset = spectroscopyHeaderOffset + spectroscopyHeader.Length;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)ifdOffset);

        // Ascending by tag id: 0xC500 < 0xC502 < 0xC503 < 0xC506 < 0xC507.
        w.Write((ushort)entryCount);
        WriteEntry(w, TagMagicNumber, type: 4, count: 1, valueOrOffset: 0x0E031301);
        WriteEntry(w, TagData, type: 1, count: (uint)surfacePixels.Length, valueOrOffset: (uint)surfaceOffset);
        WriteEntry(w, TagHeader, type: 1, count: (uint)imageHeader.Length, valueOrOffset: (uint)imageHeaderOffset);
        WriteEntry(w, TagSpectroscopyHeader, type: 1, count: (uint)spectroscopyHeader.Length, valueOrOffset: (uint)spectroscopyHeaderOffset);
        WriteEntry(w, TagSpectroscopyData, type: 1, count: (uint)data.Length, valueOrOffset: (uint)dataOffset);
        w.Write((uint)0);

        w.Write(surfacePixels);
        w.Write(imageHeader);
        w.Write(spectroscopyHeader);
        w.Write(data);

        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>Writes a spectroscopy TIFF without the 0xC506 tag, to exercise the missing-header path.</summary>
    public static void WriteSpectroscopyFileWithoutHeader(string path, byte[] imageHeader, byte[] data)
    {
        const int ifdOffset = 8;
        const int entryCount = 3;
        const int ifdSize = 2 + (entryCount * 12) + 4;
        int imageHeaderOffset = ifdOffset + ifdSize;
        int dataOffset = imageHeaderOffset + imageHeader.Length;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)ifdOffset);

        w.Write((ushort)entryCount);
        WriteEntry(w, TagMagicNumber, type: 4, count: 1, valueOrOffset: 0x0E031301);
        WriteEntry(w, TagHeader, type: 1, count: (uint)imageHeader.Length, valueOrOffset: (uint)imageHeaderOffset);
        WriteEntry(w, TagSpectroscopyData, type: 1, count: (uint)data.Length, valueOrOffset: (uint)dataOffset);
        w.Write((uint)0);

        w.Write(imageHeader);
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
