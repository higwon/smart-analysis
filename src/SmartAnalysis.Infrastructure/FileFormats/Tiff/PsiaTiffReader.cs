using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SmartAnalysis.Application.FileFormats;
using SmartAnalysis.Domain.Axes;
using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Units;
using TiffLibrary;

namespace SmartAnalysis.Infrastructure.FileFormats.Tiff;

/// <summary>
/// Reads Park Systems PSIA-TIFF into the immutable domain (ADR-015). Uses <b>TiffLibrary (MIT)</b>,
/// isolated here in Infrastructure behind the <see cref="IScanFileReader"/> port — no TIFF-library
/// type crosses into Application/Domain. MVP scope is the <b>2D scan image</b>; line-profile and
/// spectroscopy are detected and routed to a typed <see cref="FileReadErrorKind.UnsupportedImageType"/>
/// (full mapping is a follow-up). Mapping (confirmed from legacy <c>LIB.File.Tiff</c> + <c>FW.Data.Scan</c>,
/// read-only): axis raw[0..W] → real[0..XScanSize] µm; value <c>physical = raw*DataGain + ZOffset</c>
/// in the header <c>Unit</c>; lineage <c>ProvenanceRecord.Root</c>; file origin on <c>Source</c> with a
/// content hash (ADR-013).
/// </summary>
public sealed class PsiaTiffReader : IScanFileReader
{
    private readonly IUnitRegistry _units;
    private readonly IScanFormatDetector _detector;

    public PsiaTiffReader(IUnitRegistry units, IScanFormatDetector? detector = null)
    {
        _units = units ?? throw new ArgumentNullException(nameof(units));
        _detector = detector ?? new MagicByteFormatDetector();
    }

    /// <summary>
    /// Whether this reader recognises the file. Identification is by <b>content</b> (FF05) with the extension only as
    /// a fallback — so a TIFF saved without an extension, or with the wrong one, is still offered to this reader, and
    /// a file merely NAMED <c>.tiff</c> whose bytes say otherwise is not.
    /// </summary>
    public bool CanRead(string path)
        => !string.IsNullOrWhiteSpace(path) && _detector.Detect(path).Format == ScanFileFormat.Tiff;

    public Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.FromResult(FileReadResult.Failure(FileReadErrorKind.Io, $"File not found: '{path}'."));
        }

        try
        {
            return Task.FromResult(Read(path, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected failures from the TIFF layer surface as a typed I/O failure, not a crash.
            return Task.FromResult(FileReadResult.Failure(FileReadErrorKind.Io, $"Failed to read '{path}': {ex.Message}"));
        }
    }

    private FileReadResult Read(string path, CancellationToken ct)
    {
        string contentHash = ComputeSha256(path);

        using var reader = TiffFileReader.Open(path);
        using var fieldReader = reader.CreateFieldReader();
        var ifd = reader.ReadImageFileDirectory();

        var magic = ifd.FindEntry((TiffTag)PsiaTiff.TagMagicNumber);
        if (magic.Tag == TiffTag.None)
        {
            // Presence-only check (matches legacy). Comparing the exact magic value is an FF01 open item.
            return FileReadResult.Failure(FileReadErrorKind.NotPsiaTiff, "Not a PSIA-TIFF: PSIA MagicNumber tag (0xC500) is absent.");
        }

        var headerEntry = ifd.FindEntry((TiffTag)PsiaTiff.TagHeader);
        if (headerEntry.Tag == TiffTag.None || headerEntry.ValueCount == 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, "PSIA header tag (0xC503) is missing.");
        }

        byte[] headerBytes = ReadByteField(fieldReader, headerEntry);
        var header = PsiaImageHeader.TryParse(headerBytes);
        if (header is null)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                $"PSIA header is shorter than the expected {PsiaTiff.MinHeaderBytes} bytes.");
        }

        ct.ThrowIfCancellationRequested();

        if (header.ImageType != (int)PsiaTiff.ImageType.Scan2DMappedImage)
        {
            var kind = System.Enum.IsDefined(typeof(PsiaTiff.ImageType), header.ImageType)
                ? ((PsiaTiff.ImageType)header.ImageType).ToString()
                : $"ImageType={header.ImageType}";
            return FileReadResult.Failure(FileReadErrorKind.UnsupportedImageType,
                $"PSIA image type '{kind}' is not supported yet (FF01 MVP reads 2D scan images only).");
        }

        if (header.Width <= 0 || header.Height <= 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                $"Invalid image dimensions {header.Width}x{header.Height}.");
        }

        if (!(header.XScanSize > 0) || !(header.YScanSize > 0)
            || !double.IsFinite(header.XScanSize) || !double.IsFinite(header.YScanSize))
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                $"Invalid scan size (X={header.XScanSize}, Y={header.YScanSize}).");
        }

        if (!System.Enum.IsDefined(typeof(PsiaTiff.DataType), header.DataType))
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, $"Unsupported PSIA data type {header.DataType}.");
        }

        var dataEntry = ifd.FindEntry((TiffTag)PsiaTiff.TagData);
        if (dataEntry.Tag == TiffTag.None || dataEntry.ValueCount == 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Truncated, "PSIA pixel data tag (0xC502) is missing.");
        }

        int count = header.Width * header.Height;
        int bytesPerValue = header.DataType switch
        {
            (int)PsiaTiff.DataType.Short => sizeof(short),
            (int)PsiaTiff.DataType.Int => sizeof(int),
            _ => sizeof(float),
        };

        if (dataEntry.ValueCount < (long)count * bytesPerValue)
        {
            return FileReadResult.Failure(FileReadErrorKind.Truncated,
                $"Pixel payload ({dataEntry.ValueCount} bytes) is smaller than {count}x{bytesPerValue} expected.");
        }

        ct.ThrowIfCancellationRequested();

        byte[] dataBytes = ReadByteField(fieldReader, dataEntry);
        float[] values = ToPhysicalFloats(dataBytes, count, header.DataType, header.DataGain, header.ZOffset);

        // Identity, axis directions and provenance come from the ImageDescription side-car when we wrote the
        // file (FF02); a real PSIA file (or any foreign description) has none, so it keeps the legacy fresh-id +
        // Root + Forward behaviour. The header has no faithful scan-direction field, so direction lives here.
        var domain = ReadDomainSidecar(ifd, fieldReader);

        var lengthUnit = _units.GetUnit(StandardUnits.Micrometre.Symbol); // scan size is µm (legacy MICRO_METER)
        var xAxis = new Axis("X", lengthUnit, origin: header.XOffset, step: header.XScanSize / header.Width, count: header.Width, direction: domain.XDirection);
        var yAxis = new Axis("Y", lengthUnit, origin: header.YOffset, step: header.YScanSize / header.Height, count: header.Height, direction: domain.YDirection);

        var channel = BuildChannel(header);
        var metadata = BuildMetadata(header, contentHash);
        var source = new DataSource("psia-tiff", originalFilePath: path, contentHash: contentHash);

        var buffer = ScanBuffer<float>.TakeOwnership(values, header.Width, header.Height);
        try
        {
            var dataset = new ScanImageDataset(domain.Id, source, xAxis, yAxis, channel, buffer, metadata, domain.Provenance);
            return FileReadResult.Success(dataset);
        }
        catch
        {
            buffer.Dispose(); // ctor failed → we still own the buffer (ADR-011/012)
            throw;
        }
    }

    // Standard TIFF ASCII tag; FF02 stores the identity + provenance JSON side-car here.
    private const ushort TagImageDescription = 0x010E;

    private TiffDomainInfo ReadDomainSidecar(TiffImageFileDirectory ifd, TiffFieldReader fieldReader)
    {
        var entry = ifd.FindEntry((TiffTag)TagImageDescription);
        if (entry.Tag != TiffTag.None && entry.ValueCount > 0)
        {
            byte[] bytes = ReadByteField(fieldReader, entry);
            int nul = System.Array.IndexOf(bytes, (byte)0);
            string json = Encoding.UTF8.GetString(bytes, 0, nul >= 0 ? nul : bytes.Length);
            if (TiffDomainSidecar.TryParse(json, _units, out var info))
            {
                return info;
            }
        }

        return new TiffDomainInfo(DatasetId.New(), ProvenanceRecord.Root, AxisDirection.Forward, AxisDirection.Forward);
    }

    private ChannelDescriptor BuildChannel(PsiaImageHeader header)
    {
        var unit = ResolveUnit(header.Unit, out bool known);
        var kind = known ? KindFromDimension(unit.Dimension.Name) : ChannelKind.Unknown;
        string key = string.IsNullOrWhiteSpace(header.SourceName) ? "channel" : header.SourceName;
        return new ChannelDescriptor(key, kind, unit, displayName: key);
    }

    private Unit ResolveUnit(string symbol, out bool known)
    {
        if (!string.IsNullOrWhiteSpace(symbol) && _units.TryGetUnit(symbol, out var unit))
        {
            known = true;
            return unit;
        }

        known = false;
        return _units.GetUnit(StandardUnits.One.Symbol); // dimensionless fallback for an unrecognized symbol
    }

    private static ChannelKind KindFromDimension(string dimensionName) => dimensionName switch
    {
        "Length" => ChannelKind.Topography,
        "Current" => ChannelKind.Current,
        "Voltage" => ChannelKind.Voltage,
        "Force" => ChannelKind.Force,
        "Frequency" => ChannelKind.Frequency,
        _ => ChannelKind.Unknown,
    };

    private static ScanMetadata BuildMetadata(PsiaImageHeader header, string contentHash)
    {
        string model = string.IsNullOrWhiteSpace(header.ImageMode) ? "unknown" : header.ImageMode;
        var extended = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["psia.imageType"] = header.ImageType.ToString(CultureInfo.InvariantCulture),
            ["psia.width"] = header.Width.ToString(CultureInfo.InvariantCulture),
            ["psia.height"] = header.Height.ToString(CultureInfo.InvariantCulture),
            ["psia.xScanSize_um"] = header.XScanSize.ToString(CultureInfo.InvariantCulture),
            ["psia.yScanSize_um"] = header.YScanSize.ToString(CultureInfo.InvariantCulture),
            ["psia.dataGain"] = header.DataGain.ToString(CultureInfo.InvariantCulture),
            ["psia.zOffset"] = header.ZOffset.ToString(CultureInfo.InvariantCulture),
            ["psia.dataType"] = header.DataType.ToString(CultureInfo.InvariantCulture),
            ["psia.unitRaw"] = header.Unit ?? string.Empty,
            ["source.contentHash"] = contentHash,
        };

        // AcquiredAt: the DateTime tag parse is deferred (FF01 open item); use a placeholder for now.
        return new ScanMetadata(model, DateTimeOffset.MinValue, extended);
    }

    private static float[] ToPhysicalFloats(byte[] raw, int count, int dataType, double dataGain, double zOffset)
    {
        // Pixel payload is little-endian PSIA data — read it explicitly (BinaryPrimitives), not with
        // host-endian BitConverter, so the decode matches ADR-015's "endianness explicit" rule.
        var result = new float[count];
        ReadOnlySpan<byte> span = raw;
        switch (dataType)
        {
            case (int)PsiaTiff.DataType.Short:
                for (int i = 0; i < count; i++)
                {
                    short v = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i * sizeof(short), sizeof(short)));
                    result[i] = (float)(v * dataGain + zOffset);
                }

                break;

            case (int)PsiaTiff.DataType.Int:
                for (int i = 0; i < count; i++)
                {
                    int v = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(i * sizeof(int), sizeof(int)));
                    result[i] = (float)(v * dataGain + zOffset);
                }

                break;

            default: // Float
                for (int i = 0; i < count; i++)
                {
                    float v = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(i * sizeof(float), sizeof(float)));
                    result[i] = (float)(v * dataGain + zOffset);
                }

                break;
        }

        return result;
    }

    private static byte[] ReadByteField(TiffFieldReader fieldReader, TiffImageFileDirectoryEntry entry)
    {
        byte[] bytes = new byte[checked((int)entry.ValueCount)];
        fieldReader.ReadByteField(entry, 0, bytes);
        return bytes;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
