using System.Buffers.Binary;
using System.Text;

namespace SmartAnalysis.Infrastructure.FileFormats.Tiff;

/// <summary>
/// PSIA-TIFF format constants and the header parse (ADR-015). Derived by reading the legacy
/// <c>LIB.File.Tiff</c> (read-only) — the PSIA private tags and the fixed <c>PsiaHeaderStruct</c>
/// (<c>[StructLayout(Sequential, Pack=1)]</c>). Parsing is done with an explicit little-endian
/// <see cref="BinaryReader"/> (no <c>unsafe</c>, endianness explicit — fixing legacy's host-endian
/// <c>MemoryMarshal</c> assumption, doc 07 M3). Fixed-width strings are UTF-16LE (wchar) in the struct.
/// </summary>
internal static class PsiaTiff
{
    /// <summary>PSIA private TIFF tag ids (0xC500–0xC509).</summary>
    public const ushort TagMagicNumber = 0xC500; // LONG[1]
    public const ushort TagVersion = 0xC501;      // LONG[1]
    public const ushort TagData = 0xC502;         // BYTE[N] pixel payload
    public const ushort TagHeader = 0xC503;       // BYTE[HeaderSize] PsiaHeaderStruct
    public const ushort TagComments = 0xC504;     // ASCII
    public const ushort TagLineProfileHeader = 0xC505;
    public const ushort TagSpectroscopyHeader = 0xC506;
    public const ushort TagSpectroscopyData = 0xC507;
    public const ushort TagExtendedHeader = 0xC509; // XML

    /// <summary>Image type (<c>PsiaHeaderStruct.ImageType</c>, first field).</summary>
    public enum ImageType
    {
        Scan2DMappedImage = 0,
        LineProfileImage = 1,
        SpectroscopyImage = 2,
    }

    /// <summary>Pixel storage type (<c>PsiaHeaderStruct.DataType</c>).</summary>
    public enum DataType
    {
        Short = 0,
        Int = 1,
        Float = 2,
    }

    /// <summary>Bytes needed of the header to reach (and include) the <c>DataType</c> field — the MVP prefix.</summary>
    public const int MinHeaderBytes = 352;
}

/// <summary>
/// The subset of <c>PsiaHeaderStruct</c> the MVP 2D scan-image path needs, read sequentially in the
/// struct's field order with an explicit little-endian <see cref="BinaryReader"/>.
/// </summary>
internal sealed record PsiaImageHeader(
    int ImageType,
    string SourceName,
    string ImageMode,
    int Width,
    int Height,
    double XScanSize,
    double YScanSize,
    double XOffset,
    double YOffset,
    double DataGain,
    double ZScale,
    double ZOffset,
    string Unit,
    int DataType)
{
    /// <summary>
    /// Parses the header bytes (tag 0xC503). Returns null if the payload is shorter than
    /// <see cref="PsiaTiff.MinHeaderBytes"/> (treated as corrupt by the caller).
    /// </summary>
    public static PsiaImageHeader? TryParse(byte[] headerBytes)
    {
        if (headerBytes is null || headerBytes.Length < PsiaTiff.MinHeaderBytes)
        {
            return null;
        }

        using var ms = new MemoryStream(headerBytes, writable: false);
        using var r = new BinaryReader(ms, Encoding.Unicode); // BinaryReader numerics are little-endian

        int imageType = r.ReadInt32();
        string sourceName = ReadFixedString(r, 32);   // SourceName[32*2]
        string imageMode = ReadFixedString(r, 8);      // ImageMode[8*2]
        _ = r.ReadDouble();                             // LowPassFilterStrength
        _ = r.ReadInt32();                              // AutoFlatten
        _ = r.ReadInt32();                              // FlattenOrder
        int width = r.ReadInt32();
        int height = r.ReadInt32();
        _ = r.ReadDouble();                             // Angle
        _ = r.ReadInt32();                              // SineScan
        _ = r.ReadDouble();                             // OverScan
        _ = r.ReadInt32();                              // FastScanDir
        _ = r.ReadInt32();                              // SlowScanDirection
        _ = r.ReadInt32();                              // XYSwap
        double xScanSize = r.ReadDouble();
        double yScanSize = r.ReadDouble();
        double xOffset = r.ReadDouble();
        double yOffset = r.ReadDouble();
        _ = r.ReadDouble();                             // ScanRate
        _ = r.ReadDouble();                             // SetPoint
        _ = ReadFixedString(r, 8);                      // SetPointUnit[8*2]
        _ = r.ReadDouble();                             // TipBias
        _ = r.ReadDouble();                             // SampleBias
        double dataGain = r.ReadDouble();
        double zScale = r.ReadDouble();
        double zOffset = r.ReadDouble();
        string unit = ReadFixedString(r, 8);            // Unit[8*2]
        _ = r.ReadInt32();                              // DataMin
        _ = r.ReadInt32();                              // DataMax
        _ = r.ReadInt32();                              // DataAvg
        _ = r.ReadInt32();                              // Compression
        _ = r.ReadInt32();                              // LogScale
        _ = r.ReadInt32();                              // Square
        _ = r.ReadDouble();                             // ZServoGain
        _ = r.ReadDouble();                             // ZScannerRange
        _ = ReadFixedString(r, 8);                      // XYVoltageMode[8*2]
        _ = ReadFixedString(r, 8);                      // ZVoltageMode[8*2]
        _ = ReadFixedString(r, 8);                      // XYServoMode[8*2]
        int dataType = r.ReadInt32();

        return new PsiaImageHeader(
            imageType, sourceName, imageMode, width, height,
            xScanSize, yScanSize, xOffset, yOffset, dataGain, zScale, zOffset, unit, dataType);
    }

    /// <summary>Reads a fixed-width UTF-16LE string of <paramref name="charCount"/> chars, trimmed at the first NUL.</summary>
    private static string ReadFixedString(BinaryReader r, int charCount)
    {
        byte[] bytes = r.ReadBytes(charCount * 2);
        string s = Encoding.Unicode.GetString(bytes);
        int nul = s.IndexOf('\0');
        return (nul >= 0 ? s[..nul] : s).Trim();
    }
}

/// <summary>One of the eight fixed channel slots in <c>PsiaSpectroscopyHeaderStruct</c> (tag 0xC506).</summary>
/// <param name="IsXAxis">The file's own answer to "which channel is the abscissa" — not a guess from the name.</param>
internal readonly record struct PsiaSpectroscopyLine(
    string SourceName,
    string Unit,
    double DataGain,
    bool IsXAxis,
    bool IsYAxis);

/// <summary>
/// The spectroscopy header (tag 0xC506): eight channel slots followed by the sample counts. Read sequentially in
/// struct field order, like <see cref="PsiaImageHeader"/>.
/// <para>
/// The payload (tag 0xC507) is <b>channel-planar</b>: all <see cref="DataPoints"/> samples of source 0, then all of
/// source 1, and so on — <i>not</i> interleaved per point. Element width comes from the 2D header's
/// <c>DataType</c>, and <see cref="ExpectedDataBytes"/> exists so a wrong reading is caught by arithmetic rather
/// than producing plausible nonsense.
/// </para>
/// </summary>
internal sealed record PsiaSpectroscopyHeader(
    IReadOnlyList<PsiaSpectroscopyLine> Lines,
    int SourceCount,
    int DataPoints,
    int SpectroscopyPoints,
    IReadOnlyList<double> Offsets,
    double ForceConstantNewtonPerMetre,
    int? DataType)
{
    /// <summary>Channel slots in the struct, always written whether or not they are used.</summary>
    private const int LineSlots = 8;

    /// <summary>Bytes needed to reach the end of the sample counts — the minimum a usable header must have.</summary>
    private const int MinBytes = (LineSlots * 96) + (5 * sizeof(int));

    /// <summary>Bytes needed to also reach the end of the per-source <c>Offset</c> array.</summary>
    private const int OffsetsEndBytes = 872;

    /// <summary>Bytes needed to also reach <c>ForceConstantNewtonPerMeter</c>, which older writers may omit.</summary>
    private const int ForceConstantBytes = 984;

    /// <summary>
    /// Absolute offset of the payload's element type. The fields between it and <c>Sensitivity</c> are not mapped,
    /// so it is read by position rather than by inventing names for the bytes in between.
    /// </summary>
    private const int DataTypeOffset = 1108;

    public long ExpectedDataBytes(int bytesPerValue)
        => (long)DataPoints * SourceCount * SpectroscopyPoints * bytesPerValue;

    /// <summary>Parses tag 0xC506. Returns null when the payload is too short to hold the sample counts.</summary>
    public static PsiaSpectroscopyHeader? TryParse(byte[] headerBytes)
    {
        if (headerBytes is null || headerBytes.Length < MinBytes)
        {
            return null;
        }

        using var ms = new MemoryStream(headerBytes, writable: false);
        using var r = new BinaryReader(ms, Encoding.Unicode);

        var lines = new PsiaSpectroscopyLine[LineSlots];
        for (int i = 0; i < LineSlots; i++)
        {
            string sourceName = ReadFixedString(r, 32);  // SourceName[32*2]
            string unit = ReadFixedString(r, 8);          // Unit[8*2]
            double gain = r.ReadDouble();
            bool isX = r.ReadInt32() != 0;
            bool isY = r.ReadInt32() != 0;
            lines[i] = new PsiaSpectroscopyLine(sourceName, unit, gain, isX, isY);
        }

        int sourceCount = r.ReadInt32();
        _ = r.ReadInt32();                                // Average
        int dataPoints = r.ReadInt32();
        int spectPoints = r.ReadInt32();
        _ = r.ReadInt32();                                // DrivingSourceIndex

        var offsets = new double[LineSlots];
        double forceConstant = 0;
        // Each trailing field is gated on its own reach, so a header that stops short of ForceConstant still
        // yields the offsets that precede it rather than silently zeroing them.
        if (headerBytes.Length >= OffsetsEndBytes)
        {
            _ = r.ReadBytes(4 * sizeof(float));           // drive periods / speeds
            _ = r.ReadInt32();                            // VolumeImage
            for (int i = 0; i < LineSlots; i++)
            {
                offsets[i] = r.ReadDouble();
            }

            if (headerBytes.Length >= ForceConstantBytes)
            {
                _ = r.ReadBytes(LineSlots * sizeof(int)); // LogScale[8]
                _ = r.ReadBytes(LineSlots * sizeof(int)); // Square[8]
                _ = r.ReadInt32();                        // PerXPoint
                _ = r.ReadInt32();                        // ReferenceImage
                _ = r.ReadBytes(4 * sizeof(double));      // scan size / offset
                forceConstant = r.ReadDouble();
            }
        }

        int? dataType = headerBytes.Length >= DataTypeOffset + sizeof(int)
            ? BinaryPrimitives.ReadInt32LittleEndian(headerBytes.AsSpan(DataTypeOffset))
            : null;

        return new PsiaSpectroscopyHeader(lines, sourceCount, dataPoints, spectPoints, offsets, forceConstant, dataType);
    }

    private static string ReadFixedString(BinaryReader r, int charCount)
    {
        string s = Encoding.Unicode.GetString(r.ReadBytes(charCount * 2));
        int nul = s.IndexOf('\0');
        return (nul >= 0 ? s[..nul] : s).Trim();
    }
}
