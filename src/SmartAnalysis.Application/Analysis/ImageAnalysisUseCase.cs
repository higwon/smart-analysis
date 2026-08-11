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

    private readonly Workspace _workspace;
    private readonly IOperationRegistry _registry;

    public ImageAnalysisUseCase(Workspace workspace, IOperationRegistry registry)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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
