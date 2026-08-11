using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using AnFlatten = SmartAnalysis.Analysis.Flattening;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Runs image operations against the workspace on the UI's behalf (Application → Analysis is allowed;
/// UI → Analysis is not — doc 11). Maps the UI-facing <see cref="FlattenOptions"/> onto the real
/// <c>image.flatten</c> parameter set, runs it via the registry, and applies the transform workspace policy
/// (add derived → active → comparison = [source]).
/// </summary>
public sealed class ImageAnalysisUseCase : IImageAnalysisUseCase
{
    private const string FlattenId = "image.flatten";
    private const string StatisticsId = "image.statistics";

    // Preferred readouts (operation scalar key → friendly label), in display order.
    private static readonly (string Key, string Label)[] StatKeys =
    [
        ("rms", "Sq (RMS)"), ("meanAbsoluteDeviation", "Sa"), ("mean", "Mean"),
        ("skewness", "Skewness"), ("kurtosis", "Kurtosis"),
    ];

    private readonly Workspace _workspace;
    private readonly IOperationRegistry _registry;
    private readonly MeasurementStore _measurements;

    public ImageAnalysisUseCase(Workspace workspace, IOperationRegistry registry, MeasurementStore measurements)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
    }

    public async Task<FlattenOutcome> ApplyFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Public contract: reject out-of-range (cast) enum values rather than silently defaulting them.
        if (!Enum.IsDefined(options.Scope) || !Enum.IsDefined(options.Orientation) || !Enum.IsDefined(options.Basement))
        {
            return FlattenOutcome.Failed("Flatten options contain an undefined scope/orientation/basement value.");
        }

        if (!_workspace.TryGet(sourceId, out var dataset) || dataset is not ScanImageDataset image)
        {
            return FlattenOutcome.Failed("The source is not an image dataset in the workspace.");
        }

        if (!_registry.TryGet(FlattenId, out var operation))
        {
            return FlattenOutcome.Failed($"Operation '{FlattenId}' is not registered.");
        }

        var parameters = new ParameterSet(new Dictionary<string, object?>
        {
            [FlattenOperation.ScopeParameter] = MapScope(options.Scope),
            [FlattenOperation.OrderParameter] = options.Order,
            [FlattenOperation.OrientationParameter] = MapOrientation(options.Orientation),
            [FlattenOperation.BasementParameter] = MapBasement(options.Basement),
        });

        var input = new OperationInput(image);
        var validation = operation.Validate(input, parameters);
        if (!validation.IsValid)
        {
            return FlattenOutcome.Failed(string.Join("; ", validation.Errors));
        }

        OperationResult result;
        try
        {
            result = await operation.RunAsync(input, parameters, progress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FlattenOutcome.Failed(ex.Message);
        }

        if (result.DerivedDataset is not { } derived)
        {
            return FlattenOutcome.Failed("Flatten produced no derived dataset.");
        }

        // Transform policy (doc 22 §5): derived becomes active; the source enters the comparison set.
        // Ownership transfers to the workspace only on a successful Add (W01); if Add throws (e.g. a
        // duplicate id / lineage-cycle guard), we still own the derived buffer and must dispose it.
        try
        {
            _workspace.Add(derived);
        }
        catch
        {
            derived.Dispose();
            throw;
        }

        // From here the workspace owns 'derived' — never dispose it on a later failure.
        _workspace.SetActive(derived.Id);
        _workspace.SetComparison([sourceId]);

        var warnings = result.Warnings.Select(w => w.Message).ToArray();
        return new FlattenOutcome(true, derived.Id, warnings, null);
    }

    public async Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken cancellationToken = default)
    {
        if (!_workspace.TryGet(sourceId, out var dataset) || dataset is not ScanImageDataset image)
        {
            return StatisticsResult.Failed("The source is not an image dataset in the workspace.");
        }

        if (!_registry.TryGet(StatisticsId, out var operation))
        {
            return StatisticsResult.Failed($"Operation '{StatisticsId}' is not registered.");
        }

        var input = new OperationInput(image);
        var validation = operation.Validate(input, ParameterSet.Empty);
        if (!validation.IsValid)
        {
            return StatisticsResult.Failed(string.Join("; ", validation.Errors));
        }

        OperationResult result;
        try
        {
            result = await operation.RunAsync(input, ParameterSet.Empty, progress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StatisticsResult.Failed(ex.Message);
        }

        if (result.Artifact is not { } artifact)
        {
            return StatisticsResult.Failed("Statistics produced no result.");
        }

        // Preserve the real measurement entity (Id/SourceId/OperationId/Provenance) attached to its source;
        // the active dataset is deliberately unchanged (doc 22 §Measurement). The workspace/ActiveContext are
        // NOT touched — a measurement is not a dataset — it lives in the parallel MeasurementStore.
        _measurements.Attach(artifact);
        return Map(artifact);
    }

    /// <summary>
    /// Re-reads a previously attached measurement (e.g. when its explorer node is selected) into the
    /// UI-facing DTO. Returns <c>null</c> if no measurement with that id is attached.
    /// </summary>
    public StatisticsResult? GetMeasurement(DatasetId artifactId)
        => _measurements.TryGet(artifactId, out var artifact) ? Map(artifact) : null;

    // Maps a stored artifact to the UI DTO (the scalar-key → label projection lives here, in Application).
    private StatisticsResult Map(AnalysisArtifact artifact)
    {
        var readouts = new List<StatisticsReadout>();
        foreach (var (key, label) in StatKeys)
        {
            if (artifact.Scalars.TryGetValue(key, out var pv))
            {
                readouts.Add(new StatisticsReadout(label, pv.Value, pv.Unit.Symbol));
            }
        }

        var histogram = artifact.Histogram is { } h ? h.Counts.Select(c => (int)Math.Min(c, int.MaxValue)).ToArray() : [];
        var sourceLabel = _workspace.TryGet(artifact.SourceId, out var source) ? DatasetLabel(source) : null;
        return new StatisticsResult(true, sourceLabel, readouts, histogram, null);
    }

    private static string DatasetLabel(AfmDataset d)
        => d.Provenance.IsRoot && d.Source.OriginalFilePath is { } p
            ? System.IO.Path.GetFileNameWithoutExtension(p)
            : d.Provenance.Steps.Count > 0 ? d.Provenance.Steps[^1].OperationId : "dataset";

    private static AnFlatten.FlattenScope MapScope(FlattenScope scope) => scope switch
    {
        FlattenScope.Line => AnFlatten.FlattenScope.Line,
        FlattenScope.Whole => AnFlatten.FlattenScope.Whole,
        FlattenScope.Surface => AnFlatten.FlattenScope.Surface,
        _ => AnFlatten.FlattenScope.Line,
    };

    private static AnFlatten.FlattenOrientation MapOrientation(FlattenOrientation orientation) => orientation switch
    {
        FlattenOrientation.FastAxis => AnFlatten.FlattenOrientation.FastAxis,
        FlattenOrientation.SlowAxis => AnFlatten.FlattenOrientation.SlowAxis,
        _ => AnFlatten.FlattenOrientation.FastAxis,
    };

    private static AnFlatten.BasementOption MapBasement(FlattenBasement basement) => basement switch
    {
        FlattenBasement.RegressionToZero => AnFlatten.BasementOption.RegressionToZero,
        FlattenBasement.PreserveOriginalMidpoint => AnFlatten.BasementOption.PreserveOriginalMidpoint,
        _ => AnFlatten.BasementOption.RegressionToZero,
    };
}
