using SmartAnalysis.Domain.Buffers;
using SmartAnalysis.Domain.Channels;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Metadata;
using SmartAnalysis.Domain.Provenance;
using SmartAnalysis.Domain.Spectroscopy;
using SmartAnalysis.Domain.Units;

namespace SmartAnalysis.Analysis.Operations.Spectroscopy;

/// <summary>
/// One point of a force–volume map as a curve of its own (A39), on the F04 contract.
/// <para>
/// A map is viewable a point at a time, but nothing can <b>analyse</b> a point: every spectroscopy operation —
/// the approach/retract split, the force-distance measures, the modulus fit, the separation correction — takes a
/// <see cref="ForceCurveDataset"/>. This is the bridge, and it is what lets a map reach the correction that
/// <b>LD-11</b> is about.
/// </para>
/// <para>
/// The extracted curve gets its own identity and a provenance step naming the point — and, when the map has a
/// grid, <b>where on the sample it was measured</b>. A curve pulled out of a map and then fitted is otherwise
/// indistinguishable from any other point's, which is the same "which curve am I looking at" problem the map
/// view solves on screen.
/// </para>
/// <para>
/// By default the pair the map designates is extracted. A different pair may be named by index into the map's
/// kept channels (FF10), so what the viewer is looking at is what they can analyse.
/// </para>
/// Deterministic; DI-only (ADR-005).
/// </summary>
public sealed class MapPointExtractOperation : IAnalysisOperation
{
    public const string PointParameter = "point";
    public const string XChannelParameter = "xChannel";
    public const string YChannelParameter = "yChannel";
    public const string ColumnParameter = "column";
    public const string RowParameter = "row";
    public const string PositionXParameter = "positionX";
    public const string PositionYParameter = "positionY";

    private readonly IExecutionEnvironmentProvider _environment;

    public MapPointExtractOperation(IExecutionEnvironmentProvider environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public OperationDescriptor Descriptor { get; } = new(
        id: "force-volume.extract-point",
        version: 1,
        displayName: "Extract Map Point",
        summary: "Takes one curve out of a force-volume map so the spectroscopy operations can work on it.",
        acceptedInputs: [DataKind.ForceVolume],
        parameters: new ParameterSchema(
        [
            new ParameterDescriptor(PointParameter, typeof(int), defaultValue: null, min: 0, help: "Which curve of the map, counted along X first."),
            new ParameterDescriptor(XChannelParameter, typeof(int), defaultValue: -1, min: -1, help: "Abscissa channel index; -1 keeps the pair the map designates."),
            new ParameterDescriptor(YChannelParameter, typeof(int), defaultValue: -1, min: -1, help: "Ordinate channel index; -1 keeps the pair the map designates."),
        ]),
        output: OutputKind.DerivedDataset,
        isDeterministic: true,
        tags: ["spectroscopy", "force-volume", "map", "extract"],
        derivedKind: DataKind.ForceCurve);

    public ValidationResult Validate(OperationInput input, IParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);

        var schema = Descriptor.Parameters.Validate(parameters);
        if (!schema.IsValid)
        {
            return schema;
        }

        if (input.Primary is not ForceVolumeDataset map)
        {
            return ValidationResult.Fail($"'{Descriptor.Id}' requires a {nameof(ForceVolumeDataset)} as its primary input.");
        }

        int point = parameters.Get<int>(PointParameter);
        if (point >= map.PointCount)
        {
            return ValidationResult.Fail($"The map holds {map.PointCount} curves, so point {point} does not exist.");
        }

        var (x, y) = ReadChannels(parameters);
        if (x < 0 && y < 0)
        {
            return CheckIsAForceCurve(map, x, y); // the designated pair still has to BE a force curve
        }

        // Naming a channel only means something when the map kept them. A derived map carries none, and an index
        // into a set that is not there would silently fall back to the designated pair under a different name.
        if (map.Channels is not { } channels)
        {
            return ValidationResult.Fail("This map kept no channels, so only the pair it designates can be extracted.");
        }

        if (x >= channels.ChannelCount || y >= channels.ChannelCount)
        {
            return ValidationResult.Fail(
                $"The map has {channels.ChannelCount} channels, so channel {Math.Max(x, y)} does not exist.");
        }

        return CheckIsAForceCurve(map, x, y);
    }

    /// <summary>
    /// A <see cref="ForceCurveDataset"/> is not a generic XY curve: in this domain it <b>means</b> a
    /// force–distance curve, and anything typed as one is classified <c>DataKind.ForceCurve</c> and offered to
    /// the spectroscopy pipeline. Extracting, say, Z against Current into that type would make the type itself
    /// lie — downstream operations would list it as a candidate, and the ones that re-check dimensions would
    /// only be catching a mistake made here. Plotting any two channels is fine (FF11); <i>typing</i> them as a
    /// force curve is not.
    /// </summary>
    private static ValidationResult CheckIsAForceCurve(ForceVolumeDataset map, int x, int y)
    {
        var (_, abscissa) = ResolveChannel(map, x, abscissa: true);
        var (_, ordinate) = ResolveChannel(map, y, abscissa: false);

        if (abscissa.Unit.Dimension != StandardUnits.Length)
        {
            return ValidationResult.Fail(
                $"A force curve needs a length abscissa; '{abscissa.DisplayName}' is "
                + $"{abscissa.Unit.Dimension.Name} ({abscissa.Unit.Symbol}).");
        }

        if (ordinate.Unit.Dimension != StandardUnits.Force)
        {
            return ValidationResult.Fail(
                $"A force curve needs a force ordinate; '{ordinate.DisplayName}' is "
                + $"{ordinate.Unit.Dimension.Name} ({ordinate.Unit.Symbol}).");
        }

        return ValidationResult.Success;
    }

    // The channel that will actually be used, and its index in the kept set when there is one. A designated
    // channel still has an index, so provenance can name it rather than recording a bare -1.
    private static (int Index, ChannelDescriptor Channel) ResolveChannel(
        ForceVolumeDataset map, int requested, bool abscissa)
    {
        if (requested >= 0 && map.Channels is { } named && requested < named.ChannelCount)
        {
            return (requested, named.Channels[requested]);
        }

        var designated = abscissa ? map.SeparationChannel : map.ForceChannel;
        return (map.Channels?.IndexOf(designated.Key) ?? -1, designated);
    }

    // -1 on either axis means "the pair the map designates". Naming one and not the other is not a partial
    // choice: the unnamed one still comes from the designated pair, which is what the caller asked for.
    private static (int X, int Y) ReadChannels(IParameterSet parameters)
        => (parameters.TryGet<int>(XChannelParameter, out int x) ? x : -1,
            parameters.TryGet<int>(YChannelParameter, out int y) ? y : -1);

    public Task<OperationResult> RunAsync(
        OperationInput input,
        IParameterSet parameters,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();

        // Validate is the contract; reaching here with a bad input means it was not honoured (F04).
        var map = (ForceVolumeDataset)input.Primary!;
        int point = parameters.Get<int>(PointParameter);
        var (xChannel, yChannel) = ReadChannels(parameters);

        progress?.Report(new OperationProgress(0.0, "Extracting curve..."));

        var (xIndex, separationChannel) = ResolveChannel(map, xChannel, abscissa: true);
        var (yIndex, forceChannel) = ResolveChannel(map, yChannel, abscissa: false);
        var separationSamples = Take(map, xIndex, point, abscissa: true);
        var forceSamples = Take(map, yIndex, point, abscissa: false);

        cancellationToken.ThrowIfCancellationRequested();

        var artifactId = DatasetId.New();
        var recorded = new Dictionary<string, PhysicalValue>
        {
            [PointParameter] = new(point, StandardUnits.One),
            // The index actually used, not the -1 that asked for the designated pair — provenance should say
            // which channel was read without needing the parent map to interpret it.
            [XChannelParameter] = new(xIndex, StandardUnits.One),
            [YChannelParameter] = new(yIndex, StandardUnits.One),
        };

        // Where on the sample, when the map knows. A curve pulled out of a map and then fitted is otherwise
        // indistinguishable from any other point's — the index alone does not survive being looked at later.
        if (map.Geometry is { } grid)
        {
            var (px, py) = grid.PositionOf(point);
            recorded[ColumnParameter] = new((point % grid.Columns) + 1, StandardUnits.One);
            recorded[RowParameter] = new((point / grid.Columns) + 1, StandardUnits.One);
            recorded[PositionXParameter] = new(px, grid.LengthUnit);
            recorded[PositionYParameter] = new(py, grid.LengthUnit);
        }

        var step = new ProvenanceStep(
            stepId: Guid.NewGuid().ToString("D"),
            inputDatasetId: map.Id,
            inputVersion: 0,
            operationId: Descriptor.Id,
            operationVersion: Descriptor.Version,
            order: 0,
            environment: _environment.Capture(),
            parameters: recorded,
            parentResultId: artifactId);

        var separationBuffer = ScanBuffer<float>.TakeOwnership(separationSamples, separationSamples.Length, 1);
        ScanBuffer<float>? forceBuffer = null;
        try
        {
            forceBuffer = ScanBuffer<float>.TakeOwnership(forceSamples, forceSamples.Length, 1);

            // No channel set on the extracted curve: it is one point's samples, so a set covering the whole map
            // would no longer describe it (the same rule the approach/retract split follows).
            var derived = new ForceCurveDataset(
                artifactId,
                DataSource.Derived,
                separationBuffer,
                forceBuffer,
                separationChannel,
                forceChannel,
                map.Metadata,
                ProvenanceRecord.DerivedFrom(map.Id, [step]));

            progress?.Report(new OperationProgress(1.0, "Done."));
            return Task.FromResult(OperationResult.Derived(derived));
        }
        catch
        {
            // The dataset ctor did not take ownership, so both buffers are still ours (ADR-011/012).
            separationBuffer.Dispose();
            forceBuffer?.Dispose();
            throw;
        }
    }

    private static float[] Take(ForceVolumeDataset map, int channelIndex, int point, bool abscissa)
    {
        if (channelIndex >= 0 && map.Channels is { } channels)
        {
            return channels.At(channelIndex, point).ToArray();
        }

        return abscissa ? map.SeparationAt(point).ToArray() : map.ForceAt(point).ToArray();
    }
}
