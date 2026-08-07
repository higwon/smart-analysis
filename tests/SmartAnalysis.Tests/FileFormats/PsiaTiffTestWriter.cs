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
