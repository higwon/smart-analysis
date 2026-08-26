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
using SmartAnalysis.Domain.Spectroscopy;
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

        if (header.ImageType == (int)PsiaTiff.ImageType.SpectroscopyImage)
        {
            return ReadSpectroscopy(path, ifd, fieldReader, header, contentHash, ct);
        }

        if (header.ImageType != (int)PsiaTiff.ImageType.Scan2DMappedImage)
        {
            var kind = System.Enum.IsDefined(typeof(PsiaTiff.ImageType), header.ImageType)
                ? ((PsiaTiff.ImageType)header.ImageType).ToString()
                : $"ImageType={header.ImageType}";
            return FileReadResult.Failure(FileReadErrorKind.UnsupportedImageType,
                $"PSIA image type '{kind}' is not supported yet (FF01 reads 2D scan images and force curves).");
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

    /// <summary>
    /// Reads a PSIA spectroscopy image (ImageType 2) as a <see cref="ForceCurveDataset"/>. Which channel is the
    /// abscissa is the <b>file's</b> answer (the per-line X/Y axis flags), not a guess from the source name; the pair
    /// is accepted only when its units are a length against a force, so a non-force spectrum (an IR/PiFM
    /// wavenumber sweep, say) is refused rather than silently presented as a force curve.
    /// </summary>
    private FileReadResult ReadSpectroscopy(
        string path,
        TiffImageFileDirectory ifd,
        TiffFieldReader fieldReader,
        PsiaImageHeader header,
        string contentHash,
        CancellationToken ct)
    {
        var headerEntry = ifd.FindEntry((TiffTag)PsiaTiff.TagSpectroscopyHeader);
        if (headerEntry.Tag == TiffTag.None || headerEntry.ValueCount == 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, "PSIA spectroscopy header tag (0xC506) is missing.");
        }

        var spectroscopy = PsiaSpectroscopyHeader.TryParse(ReadByteField(fieldReader, headerEntry));
        if (spectroscopy is null)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                "PSIA spectroscopy header is too short to hold the sample counts.");
        }

        ct.ThrowIfCancellationRequested();

        if (spectroscopy.DataPoints <= 0 || spectroscopy.SourceCount <= 0 || spectroscopy.SpectroscopyPoints <= 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                $"Invalid spectroscopy counts (points={spectroscopy.DataPoints}, sources={spectroscopy.SourceCount}, "
                + $"spectra={spectroscopy.SpectroscopyPoints}).");
        }

        if (spectroscopy.SourceCount > spectroscopy.Lines.Count)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt,
                $"Spectroscopy header declares {spectroscopy.SourceCount} sources but holds only "
                + $"{spectroscopy.Lines.Count} channel slots.");
        }

        // The spectroscopy payload carries its OWN element type: a processed file (an offset-adjusted curve, say)
        // is written as float while the 2D header it inherited still says short. The 2D header is only a fallback for
        // a header too short to hold the field.
        int dataType = spectroscopy.DataType ?? header.DataType;
        if (!System.Enum.IsDefined(typeof(PsiaTiff.DataType), dataType))
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, $"Unsupported PSIA data type {dataType}.");
        }

        int xIndex = FindAxis(spectroscopy, isXAxis: true);
        if (xIndex < 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, "No spectroscopy channel is flagged as the X axis.");
        }

        int yIndex = FindAxis(spectroscopy, isXAxis: false);
        if (yIndex < 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Corrupt, "No spectroscopy channel is flagged as the Y axis.");
        }

        var xLine = spectroscopy.Lines[xIndex];
        var yLine = spectroscopy.Lines[yIndex];

        var separationUnit = ResolveUnit(xLine.Unit, out bool xKnown);
        var ordinateUnit = ResolveUnit(yLine.Unit, out bool yKnown);
        bool isDeflection = IsDeflectionVoltage(yLine, ordinateUnit);
        if (!xKnown || !yKnown || separationUnit.Dimension != StandardUnits.Length
            || (ordinateUnit.Dimension != StandardUnits.Force && !isDeflection))
        {
            return FileReadResult.Failure(FileReadErrorKind.UnsupportedImageType,
                $"Spectroscopy '{xLine.SourceName}' [{xLine.Unit}] vs '{yLine.SourceName}' [{yLine.Unit}] is not a "
                + "force-distance curve (a length abscissa against a force or cantilever-deflection ordinate).");
        }

        // Most instruments never store a force: they store what the photodiode measured, a deflection voltage. The
        // curve is still force-distance, but the force has to be recovered from the probe calibration the file
        // recorded with it — and without that calibration the force is simply not knowable from this file.
        CantileverCalibration? calibration = null;
        if (isDeflection)
        {
            if (!CantileverCalibration.TryCreate(
                    spectroscopy.ForceConstantNewtonPerMetre, spectroscopy.SensitivityVoltPerMicrometre, out var cal))
            {
                return FileReadResult.Failure(FileReadErrorKind.UnsupportedImageType,
                    $"Spectroscopy '{yLine.SourceName}' is a deflection voltage, but the file carries no usable probe "
                    + $"calibration (spring constant {spectroscopy.ForceConstantNewtonPerMetre} N/m, sensitivity "
                    + $"{spectroscopy.SensitivityVoltPerMicrometre} V/um), so its force cannot be recovered.");
            }

            calibration = cal;
        }

        var dataEntry = ifd.FindEntry((TiffTag)PsiaTiff.TagSpectroscopyData);
        if (dataEntry.Tag == TiffTag.None || dataEntry.ValueCount == 0)
        {
            return FileReadResult.Failure(FileReadErrorKind.Truncated, "PSIA spectroscopy data tag (0xC507) is missing.");
        }

        int bytesPerValue = dataType switch
        {
            (int)PsiaTiff.DataType.Short => sizeof(short),
            (int)PsiaTiff.DataType.Int => sizeof(int),
            _ => sizeof(float),
        };

        // The payload is channel-planar, so a mis-read would still land inside the buffer and yield a plausible-looking
        // curve. The size must match EXACTLY: a BYTE tag's ValueCount is the payload's logical length, with no
        // alignment padding to allow for. A larger payload is the dangerous direction — it means the header and the
        // data disagree, most likely several spectra whose SpectPoints was misread as one, and accepting it would
        // read the leading planes and hand back a plausible curve. That is the partial consumption of an unverified
        // layout this reader exists to refuse.
        long expected = spectroscopy.ExpectedDataBytes(bytesPerValue);
        if (dataEntry.ValueCount != expected)
        {
            bool short_ = dataEntry.ValueCount < expected;
            return FileReadResult.Failure(
                short_ ? FileReadErrorKind.Truncated : FileReadErrorKind.Corrupt,
                $"Spectroscopy payload ({dataEntry.ValueCount} bytes) is {(short_ ? "smaller" : "larger")} than the "
                + $"declared {spectroscopy.DataPoints}x{spectroscopy.SourceCount}x{spectroscopy.SpectroscopyPoints}"
                + $"x{bytesPerValue} = {expected} bytes.");
        }

        ct.ThrowIfCancellationRequested();

        byte[] dataBytes = ReadByteField(fieldReader, dataEntry);
        int count = spectroscopy.DataPoints;
        int points = spectroscopy.SpectroscopyPoints;
        float[] separationValues = ReadPlanes(
            dataBytes, spectroscopy, xIndex, dataType, bytesPerValue, xLine.DataGain, spectroscopy.Offsets[xIndex]);
        float[] forceValues = ReadPlanes(
            dataBytes, spectroscopy, yIndex, dataType, bytesPerValue, yLine.DataGain, spectroscopy.Offsets[yIndex]);

        var forceUnit = ordinateUnit;
        if (calibration is { } probe)
        {
            // The stored gain and offset already put the ordinate in its own unit, so the volts are scaled to the
            // base volt before the calibration is applied — a channel recorded in mV must not be read as V.
            for (int i = 0; i < forceValues.Length; i++)
            {
                forceValues[i] = (float)probe.ForceNanonewtons(forceValues[i] * ordinateUnit.ScaleToBase);
            }

            forceUnit = _units.GetUnit(StandardUnits.Nanonewton.Symbol);
        }

        var separationChannel = new ChannelDescriptor(
            Key(xLine.SourceName, "separation"), ChannelKind.Topography, separationUnit, displayName: xLine.SourceName);
        var forceChannel = new ChannelDescriptor(
            Key(yLine.SourceName, "force"), ChannelKind.Force, forceUnit, displayName: yLine.SourceName);

        var metadata = BuildSpectroscopyMetadata(
            header, spectroscopy, xLine, yLine, dataType, calibration, contentHash);
        var source = new DataSource("psia-tiff", originalFilePath: path, contentHash: contentHash);

        // Everything the acquisition measured, not just the two the file flagged as axes. Half of a typical
        // file is other channels, and some of them — a populated Separation, say — are better than the flagged
        // ones for the analysis that follows.
        var channelSet = ReadChannelSet(dataBytes, spectroscopy, dataType, bytesPerValue, calibration, ordinateUnit, yIndex);

        var separation = ScanBuffer<float>.TakeOwnership(separationValues, count, points);
        ScanBuffer<float>? force = null;
        try
        {
            force = ScanBuffer<float>.TakeOwnership(forceValues, count, points);

            // One spectrum is a curve; several are a map. The layout is the same either way — the map is just the
            // curve case repeated — so the only thing that changes is which type the caller receives.
            AfmDataset dataset = points == 1
                ? new ForceCurveDataset(
                    DatasetId.New(), source, separation, force, separationChannel, forceChannel, metadata, ProvenanceRecord.Root, channelSet)
                : new ForceVolumeDataset(
                    DatasetId.New(), source, separation, force, separationChannel, forceChannel,
                    BuildGeometry(spectroscopy), metadata, ProvenanceRecord.Root, channelSet);

            return FileReadResult.Success(dataset);
        }
        catch
        {
            separation.Dispose(); // ownership only transfers on a successful ctor (ADR-011/012)
            force?.Dispose();
            channelSet?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The map's grid, or null when the file did not record one. `VolumeImage` is the instrument's own answer to
    /// "was this a grid": a set of hand-placed points leaves it clear and writes sentinel extents, and turning
    /// those into a grid would place curves where nothing was measured.
    /// </summary>
    private ForceVolumeGeometry? BuildGeometry(PsiaSpectroscopyHeader header)
    {
        if (!header.IsVolumeImage || header.PointsPerX <= 0)
        {
            return null;
        }

        // A grid has to account for exactly the spectra present; a width that does not divide them means the
        // header and the payload disagree about the shape, and a partial last row would misplace every point in it.
        if (header.SpectroscopyPoints % header.PointsPerX != 0)
        {
            return null;
        }

        if (!(header.ScanSizeX > 0) || !(header.ScanSizeY > 0)
            || !double.IsFinite(header.ScanSizeX) || !double.IsFinite(header.ScanSizeY)
            || !double.IsFinite(header.OffsetX) || !double.IsFinite(header.OffsetY))
        {
            return null;
        }

        return new ForceVolumeGeometry(
            header.PointsPerX,
            header.SpectroscopyPoints / header.PointsPerX,
            header.ScanSizeX,
            header.ScanSizeY,
            header.OffsetX,
            header.OffsetY,
            _units.GetUnit(StandardUnits.Micrometre.Symbol)); // scan extents are µm, as in the 2D header
    }

    /// <summary>
    /// Reads every declared channel into one set. The ordinate is converted the same way it is for the
    /// designated pair, so a deflection channel reads in force whichever way the caller reaches it.
    /// </summary>
    private SpectroscopyChannelSet? ReadChannelSet(
        byte[] data,
        PsiaSpectroscopyHeader header,
        int dataType,
        int bytesPerValue,
        CantileverCalibration? calibration,
        Unit ordinateUnit,
        int ordinateIndex)
    {
        int count = header.DataPoints;
        int points = header.SpectroscopyPoints;
        var descriptors = new ChannelDescriptor[header.SourceCount];
        var samples = new float[checked(header.SourceCount * points * count)];

        for (int c = 0; c < header.SourceCount; c++)
        {
            var line = header.Lines[c];
            var unit = ResolveUnit(line.Unit, out bool known);
            var plane = ReadPlanes(data, header, c, dataType, bytesPerValue, line.DataGain, header.Offsets[c]);

            if (c == ordinateIndex && calibration is { } probe)
            {
                for (int i = 0; i < plane.Length; i++)
                {
                    plane[i] = (float)probe.ForceNanonewtons(plane[i] * ordinateUnit.ScaleToBase);
                }

                unit = _units.GetUnit(StandardUnits.Nanonewton.Symbol);
                known = true;
            }

            plane.CopyTo(samples, c * points * count);
            descriptors[c] = new ChannelDescriptor(
                Key(line.SourceName, $"channel{c}"),
                known ? KindFromDimension(unit.Dimension.Name) : ChannelKind.Unknown,
                unit,
                displayName: line.SourceName);
        }

        var buffer = ScanBuffer<float>.TakeOwnership(samples, count, header.SourceCount * points);
        try
        {
            return new SpectroscopyChannelSet(descriptors, points, buffer);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether an ordinate is the cantilever's vertical deflection, and so a force in disguise. A voltage alone is
    /// not enough: a lock-in or piezoresponse amplitude swept against Z is a different detector entirely, and the
    /// probe calibration sitting in the header is no evidence about WHICH channel was selected - it describes the
    /// probe, not the ordinate. Legacy identifies the same channel the same way (<c>ForceConstantViewModel</c>:
    /// <c>SourceName.Contains("Vertical")</c>); the name real files carry is "Vertical (A-B)", the vertical
    /// photodiode segment difference.
    /// </summary>
    private static bool IsDeflectionVoltage(PsiaSpectroscopyLine line, Unit unit)
        => unit.Dimension == StandardUnits.Voltage
           && line.SourceName.Contains("Vertical", StringComparison.OrdinalIgnoreCase);

    /// <summary>Index of the first declared source the file flags as the requested axis, or -1.</summary>
    private static int FindAxis(PsiaSpectroscopyHeader header, bool isXAxis)
    {
        for (int i = 0; i < header.SourceCount; i++)
        {
            if (isXAxis ? header.Lines[i].IsXAxis : header.Lines[i].IsYAxis)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Lifts one channel out of the payload for every spectrum, converted to physical units and laid out one
    /// curve after another. The payload is a sequence of spectra, each channel-planar, so the samples of one
    /// channel for spectrum k start at ((k * sources) + channel) * points — which for a single spectrum is
    /// just that channel plane.
    /// </summary>
    private static float[] ReadPlanes(
        byte[] data, PsiaSpectroscopyHeader header, int planeIndex, int dataType, int bytesPerValue, double gain, double offset)
    {
        int count = header.DataPoints;
        var values = new float[checked(count * header.SpectroscopyPoints)];
        for (int k = 0; k < header.SpectroscopyPoints; k++)
        {
            int origin = ((k * header.SourceCount) + planeIndex) * count * bytesPerValue;
            int target = k * count;
            for (int i = 0; i < count; i++)
            {
                values[target + i] = ToPhysical(data, origin + (i * bytesPerValue), bytesPerValue, dataType, gain, offset);
            }
        }

        return values;
    }

    private static float ToPhysical(byte[] data, int at, int bytesPerValue, int dataType, double gain, double offset)
    {
        {
            var span = data.AsSpan(at, bytesPerValue);
            double raw = dataType switch
            {
                (int)PsiaTiff.DataType.Short => BinaryPrimitives.ReadInt16LittleEndian(span),
                (int)PsiaTiff.DataType.Int => BinaryPrimitives.ReadInt32LittleEndian(span),
                _ => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
            };
            return (float)((raw * gain) + offset);
        }
    }

    private static string Key(string sourceName, string fallback)
        => string.IsNullOrWhiteSpace(sourceName) ? fallback : sourceName;

    private static ScanMetadata BuildSpectroscopyMetadata(
        PsiaImageHeader header,
        PsiaSpectroscopyHeader spectroscopy,
        PsiaSpectroscopyLine xLine,
        PsiaSpectroscopyLine yLine,
        int dataType,
        CantileverCalibration? calibration,
        string contentHash)
    {
        string model = string.IsNullOrWhiteSpace(header.ImageMode) ? "unknown" : header.ImageMode;
        var extended = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["psia.imageType"] = header.ImageType.ToString(CultureInfo.InvariantCulture),
            ["psia.dataType"] = dataType.ToString(CultureInfo.InvariantCulture), // the payload's, not the 2D header's
            ["psia.spect.dataPoints"] = spectroscopy.DataPoints.ToString(CultureInfo.InvariantCulture),
            ["psia.spect.sources"] = spectroscopy.SourceCount.ToString(CultureInfo.InvariantCulture),
            ["psia.spect.xSource"] = xLine.SourceName,
            ["psia.spect.xUnitRaw"] = xLine.Unit,
            ["psia.spect.ySource"] = yLine.SourceName,
            ["psia.spect.yUnitRaw"] = yLine.Unit,
            ["source.contentHash"] = contentHash,
        };

        // Zero means the writer left it unset, which is not the same as a zero-stiffness cantilever.
        if (double.IsFinite(spectroscopy.ForceConstantNewtonPerMetre) && spectroscopy.ForceConstantNewtonPerMetre > 0)
        {
            extended["psia.spect.forceConstant_N_per_m"] =
                spectroscopy.ForceConstantNewtonPerMetre.ToString(CultureInfo.InvariantCulture);
        }

        // A force this reader COMPUTED must never be indistinguishable from one the instrument stored:
        // record that it was derived, and the two numbers it was derived with.
        if (calibration is { } probe)
        {
            extended["psia.spect.forceDerivedFrom"] = $"{yLine.SourceName} [{yLine.Unit}]";
            extended["psia.spect.springConstant_N_per_m"] =
                probe.SpringConstantNewtonPerMetre.ToString(CultureInfo.InvariantCulture);
            extended["psia.spect.sensitivity_V_per_um"] =
                probe.SensitivityVoltPerMicrometre.ToString(CultureInfo.InvariantCulture);
        }

        return new ScanMetadata(model, DateTimeOffset.MinValue, extended);
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
        if (!string.IsNullOrWhiteSpace(symbol)
            && (_units.TryGetUnit(symbol, out var unit) || _units.TryGetUnit(NormalizeUnitSymbol(symbol), out unit)))
        {
            known = true;
            return unit;
        }

        known = false;
        return _units.GetUnit(StandardUnits.One.Symbol); // dimensionless fallback for an unrecognized symbol
    }

    // Real PSIA files write the micro sign (and some writers the Greek mu) where the registry holds the ASCII
    // "um"; the registry deliberately leaves such input-file variants to the parser that met them.
    private static string NormalizeUnitSymbol(string symbol)
        => symbol.Replace('\u00B5', 'u').Replace('\u03BC', 'u');

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
