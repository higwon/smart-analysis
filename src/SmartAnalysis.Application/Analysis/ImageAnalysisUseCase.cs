using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Analysis.Operations.Image;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Visualization.Colormaps;
using SmartAnalysis.Visualization.Rendering;
using AnFlatten = SmartAnalysis.Analysis.Flattening;

namespace SmartAnalysis.Application.Analysis;

/// <summary>
/// Runs image operations against the workspace on the UI's behalf (Application → Analysis is allowed;
/// UI → Analysis is not — doc 11). Maps the UI-facing <see cref="FlattenOptions"/> onto the real
/// <c>image.flatten</c> parameter set, runs it via the registry, and applies the transform workspace policy
/// (add derived → active). Comparing the result to the source is a live settings preview (PreviewFlattenAsync),
/// not a post-apply Before/After split.
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

        var (result, error) = await RunFlattenAsync(sourceId, options, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return FlattenOutcome.Failed(error);
        }

        if (result!.DerivedDataset is not ScanImageDataset derived)
        {
            return FlattenOutcome.Failed("Flatten produced no derived dataset.");
        }

        // Apply is now a plain transform: the derived dataset is added and becomes active. It is NOT forced into a
        // Before/After comparison any more — comparing the result to the source happens live in the settings preview
        // (PreviewFlattenAsync) before applying. Ownership transfers to the workspace only on a successful Add (W01).
        try
        {
            _workspace.Add(derived);
        }
        catch
        {
            derived.Dispose();
            throw;
        }

        _workspace.SetActive(derived.Id);

        var warnings = result.Warnings.Select(w => w.Message).ToArray();
        return new FlattenOutcome(true, derived.Id, warnings, null);
    }

    public async Task<ImageRenderInput?> PreviewFlattenAsync(DatasetId sourceId, FlattenOptions options, Colormap colormap, ValueRange? range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(colormap);

        var (result, error) = await RunFlattenAsync(sourceId, options, cancellationToken).ConfigureAwait(false);
        if (error is not null || result!.DerivedDataset is not ScanImageDataset derived)
        {
            return null; // a preview is best-effort: a bad setting just shows nothing, never an error
        }

        // The derived is transient (never added to the workspace): project an OWNED render input, then dispose it.
        try
        {
            return RenderInputFactory.ForImageOwned(derived, colormap, range);
        }
        finally
        {
            derived.Dispose();
        }
    }

    // Validates the options + runs image.flatten WITHOUT committing anything to the workspace. On success the
    // OperationResult carries the (uncommitted) derived dataset; on any expected failure it returns a typed message.
    private async Task<(OperationResult? Result, string? Error)> RunFlattenAsync(DatasetId sourceId, FlattenOptions options, CancellationToken cancellationToken)
    {
        // Public contract: reject out-of-range (cast) enum values rather than silently defaulting them.
        if (!Enum.IsDefined(options.Scope) || !Enum.IsDefined(options.Orientation) || !Enum.IsDefined(options.Basement))
        {
            return (null, "Flatten options contain an undefined scope/orientation/basement value.");
        }

        if (!_workspace.TryGet(sourceId, out var dataset) || dataset is not ScanImageDataset image)
        {
            return (null, "The source is not an image dataset in the workspace.");
        }

        if (!_registry.TryGet(FlattenId, out var operation))
        {
            return (null, $"Operation '{FlattenId}' is not registered.");
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
            return (null, string.Join("; ", validation.Errors));
        }

        try
        {
            var result = await operation.RunAsync(input, parameters, progress: null, cancellationToken).ConfigureAwait(false);
            return (result, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, CancellationToken cancellationToken = default)
        => ComputeStatisticsAsync(sourceId, attach: true, cancellationToken);

    public Task<StatisticsResult> ComputeStatisticsPreviewAsync(DatasetId sourceId, CancellationToken cancellationToken = default)
        => ComputeStatisticsAsync(sourceId, attach: false, cancellationToken);

    private async Task<StatisticsResult> ComputeStatisticsAsync(DatasetId sourceId, bool attach, CancellationToken cancellationToken)
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
        // NOT touched — a measurement is not a dataset — it lives in the parallel MeasurementStore. A preview
        // (inline Dataset readout) skips the attach so it never accumulates saved measurement nodes.
        if (attach)
        {
            _measurements.Attach(artifact);
        }

        return Map(artifact);
    }

    /// <summary>
    /// Re-reads a previously attached measurement (e.g. when its explorer node is selected) into the
    /// UI-facing DTO. Returns <c>null</c> if no measurement with that id is attached.
    /// </summary>
    public StatisticsResult? GetMeasurement(DatasetId artifactId)
        => _measurements.TryGet(artifactId, out var artifact) ? Map(artifact) : null;

    // Maps a stored artifact to the UI DTO. Projects EVERY scalar so any measurement (roughness, peaks, grains,
    // region statistics, …) re-reads its full result when its node is re-selected — the friendly Sq/Sa naming is
    // used for the statistics keys, and every other key is humanized. The optional table (e.g. the peak list) is
    // carried too. (Kept consistent with the launcher's generic projection so a measurement looks the same however
    // it is viewed.)
    private StatisticsResult Map(AnalysisArtifact artifact)
    {
        var friendly = StatKeys.ToDictionary(k => k.Key, k => k.Label, StringComparer.Ordinal);
        var readouts = new List<StatisticsReadout>(artifact.Scalars.Count);

        // Statistics keys first, in their curated order + friendly labels …
        foreach (var (key, label) in StatKeys)
        {
            if (artifact.Scalars.TryGetValue(key, out var pv))
            {
                readouts.Add(new StatisticsReadout(label, pv.Value, pv.Unit.Symbol));
            }
        }

        // … then every remaining scalar, humanized (Sa/Sq/Sz/… are already display-ready; PeakCount → "Peak Count").
        foreach (var (key, pv) in artifact.Scalars)
        {
            if (!friendly.ContainsKey(key))
            {
                readouts.Add(new StatisticsReadout(Humanize(key), pv.Value, pv.Unit.Symbol));
            }
        }

        var histogram = artifact.Histogram is { } h ? h.Counts.Select(c => (int)Math.Min(c, int.MaxValue)).ToArray() : [];
        var sourceLabel = _workspace.TryGet(artifact.SourceId, out var source) ? DatasetLabel(source) : null;
        return new StatisticsResult(true, sourceLabel, readouts, histogram, null, ProjectTable(artifact.Table));
    }

    // A table's unit is folded into each column header (read from the column's own unit); cells carry the value.
    private static MeasurementTableDto? ProjectTable(MeasurementTable? table)
    {
        if (table is null)
        {
            return null;
        }

        var headers = new string[table.ColumnCount];
        for (int c = 0; c < table.ColumnCount; c++)
        {
            var unit = table.Columns[c].Unit.Symbol;
            headers[c] = unit is "" or "1" ? table.Columns[c].Name : $"{table.Columns[c].Name} ({unit})";
        }

        var rows = table.Rows
            .Select(r => (IReadOnlyList<string>)r.Select(cell => $"{cell.Value:G4}").ToArray())
            .ToArray();
        return new MeasurementTableDto(headers, rows);
    }

    // "meanAbsoluteDeviation" → "Mean Absolute Deviation"; "PeakCount" → "Peak Count". Best-effort camelCase label.
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new System.Text.StringBuilder(name.Length + 4);
        sb.Append(char.ToUpperInvariant(name[0]));
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(name[i]);
        }

        return sb.ToString();
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
