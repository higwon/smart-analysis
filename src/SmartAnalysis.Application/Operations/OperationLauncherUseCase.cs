using System.Globalization;
using System.Text;
using SmartAnalysis.Analysis.Operations;
using SmartAnalysis.Application.Analysis;
using SmartAnalysis.Application.Workspaces;
using SmartAnalysis.Domain.Datasets;

namespace SmartAnalysis.Application.Operations;

/// <summary>
/// Registry-driven implementation of <see cref="IOperationLauncher"/> (U08). It knows nothing operation-
/// specific: applicability, the editor form, and the run all flow from the <c>OperationDescriptor</c> and
/// <c>OutputKind</c>, so new operations need no code here. The transform/measurement workspace policy
/// mirrors <see cref="ImageAnalysisUseCase"/> (doc 22 §5) — the two are the single generic engine and the
/// semantic Flatten path respectively.
/// </summary>
public sealed class OperationLauncherUseCase : IOperationLauncher
{
    private readonly Workspace _workspace;
    private readonly IOperationRegistry _registry;
    private readonly MeasurementStore _measurements;
    private readonly RegionContext _region;

    public OperationLauncherUseCase(Workspace workspace, IOperationRegistry registry, MeasurementStore measurements, RegionContext? region = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
        _region = region ?? new RegionContext();
    }

    public IReadOnlyList<OperationLauncherItem> ApplicableToActive()
    {
        if (_workspace.Active.ActiveId is not { } id
            || !_workspace.TryGet(id, out var dataset)
            || KindOf(dataset) is not { } kind)
        {
            return [];
        }

        return _registry.ApplicableTo(kind)
            // Dataset-level applicability (beyond DataKind): an op can require more of the active dataset (e.g. a
            // wavelength filter needs a spatial profile), so it isn't offered where it could only fail to run.
            .Where(d => _registry.TryGet(d.Id, out var op) && op.IsApplicableTo(dataset))
            .Select(d => new OperationLauncherItem(d.Id, d.DisplayName, d.Summary, CategoryOf(d.Output)))
            .OrderBy(i => i.Category)
            .ThenBy(i => i.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public string? EnumParameterLabel(string operationId, int operationVersion, string parameterName, double value)
    {
        var descriptor = _registry.All.FirstOrDefault(d => d.Id == operationId);

        // Only relabel a step recorded by the CURRENT schema: a newer op version may have reassigned the enum
        // codes, so a past step's "3" could mean something else now — show the raw number rather than a wrong name.
        if (descriptor is null || descriptor.Version != operationVersion)
        {
            return null;
        }

        var parameter = descriptor.Parameters.Parameters.FirstOrDefault(p => p.Name == parameterName);
        if (parameter is null || !parameter.Type.IsEnum)
        {
            return null;
        }

        // Provenance stores the enum as its integer value; a fractional/non-finite value is corrupt → don't guess.
        if (!double.IsFinite(value) || value != Math.Floor(value))
        {
            return null;
        }

        int code = (int)value;
        return Enum.IsDefined(parameter.Type, code) ? Enum.GetName(parameter.Type, code) : null;
    }

    public OperationForm? GetForm(string operationId)
    {
        var descriptor = _registry.All.FirstOrDefault(d => d.Id == operationId);
        if (descriptor is null)
        {
            return null;
        }

        var fields = descriptor.Parameters.Parameters.Select(ToField).ToArray();
        return new OperationForm(descriptor.Id, descriptor.DisplayName, descriptor.Summary, CategoryOf(descriptor.Output), fields);
    }

    public async Task<OperationRunResult> RunAsync(string operationId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (_workspace.Active.ActiveId is not { } sourceId || !_workspace.TryGet(sourceId, out var source))
        {
            return OperationRunResult.Failed("There is no active dataset to run the operation on.");
        }

        if (!_registry.TryGet(operationId, out var operation))
        {
            return OperationRunResult.Failed($"Operation '{operationId}' is not registered.");
        }

        ParameterSet parameters;
        try
        {
            parameters = BuildParameters(operation.Descriptor.Parameters, values);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or OverflowException)
        {
            return OperationRunResult.Failed($"A parameter value could not be interpreted: {ex.Message}");
        }

        // Attach the active ROI only for a region-capable op; a whole-dataset op never sees it.
        var region = operation.Descriptor.UsesRegion ? _region.Current : null;
        var input = new OperationInput(source, region: region);
        var validation = operation.Validate(input, parameters);
        if (!validation.IsValid)
        {
            return OperationRunResult.Failed(string.Join("; ", validation.Errors));
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
            return OperationRunResult.Failed(ex.Message);
        }

        var warnings = result.Warnings.Select(w => w.Message).ToArray();

        if (result.DerivedDataset is { } derived)
        {
            // Transform policy (doc 22 §5): derived becomes active; the source enters the comparison set.
            // Ownership transfers to the workspace only on a successful Add; dispose on failure (W01).
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
            _workspace.SetComparison([sourceId]);
            return OperationRunResult.Derived(derived.Id, warnings);
        }

        if (result.Artifact is { } artifact)
        {
            // Measurement: preserve the real entity attached to its source; active unchanged (doc 22).
            _measurements.Attach(artifact);
            return OperationRunResult.Measured(ProjectMeasurement(artifact), warnings);
        }

        return OperationRunResult.Failed("The operation produced no output.");
    }

    // Coerces the UI's value primitives back to the schema's real CLR types (enum names → enum members,
    // widened numerics). Omitted values fall through to the schema's defaults / missing-required checks.
    private static ParameterSet BuildParameters(ParameterSchema schema, IReadOnlyDictionary<string, object?> values)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var descriptor in schema.Parameters)
        {
            if (!values.TryGetValue(descriptor.Name, out var raw) || raw is null)
            {
                continue;
            }

            dict[descriptor.Name] = Coerce(raw, descriptor.Type);
        }

        return new ParameterSet(dict);
    }

    private static object Coerce(object raw, Type target)
    {
        if (target.IsInstanceOfType(raw))
        {
            return raw;
        }

        if (target.IsEnum)
        {
            return Enum.Parse(target, raw.ToString() ?? string.Empty, ignoreCase: false);
        }

        return Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
    }

    private StatisticsResult ProjectMeasurement(AnalysisArtifact artifact)
    {
        // Generic projection: every scalar the operation emitted (no curated label map — that is the
        // semantic path's job, e.g. Statistics' Sq/Sa naming in ImageAnalysisUseCase).
        var readouts = artifact.Scalars
            .Select(kv => new StatisticsReadout(Humanize(kv.Key), kv.Value.Value, kv.Value.Unit.Symbol))
            .ToArray();
        var histogram = artifact.Histogram is { } h ? h.Counts.Select(c => (int)Math.Min(c, int.MaxValue)).ToArray() : [];
        var sourceLabel = _workspace.TryGet(artifact.SourceId, out var source) ? DatasetLabel(source) : null;
        return new StatisticsResult(true, sourceLabel, readouts, histogram, null, ProjectTable(artifact.Table));
    }

    // A table's unit is folded into each column header (peaks share a unit per column); cells carry the value.
    private static MeasurementTableDto? ProjectTable(MeasurementTable? table)
    {
        if (table is null)
        {
            return null;
        }

        var headers = new string[table.ColumnCount];
        for (int c = 0; c < table.ColumnCount; c++)
        {
            var unit = table.RowCount > 0 ? table.Rows[0][c].Unit.Symbol : string.Empty;
            headers[c] = unit is "" or "1" ? table.Columns[c] : $"{table.Columns[c]} ({unit})";
        }

        var rows = table.Rows
            .Select(r => (IReadOnlyList<string>)r.Select(cell => $"{cell.Value:G4}").ToArray())
            .ToArray();
        return new MeasurementTableDto(headers, rows);
    }

    private static ParameterFieldDescriptor ToField(ParameterDescriptor p)
    {
        var kind = FieldKind(p.Type);
        IReadOnlyList<ParameterFieldOption> options = p.Type.IsEnum
            ? Enum.GetNames(p.Type).Select(n => new ParameterFieldOption(n, n)).ToArray()
            : [];
        // Project the default to the UI primitive the control binds to (an enum default → its member name).
        var defaultValue = p.Default is { } d && p.Type.IsEnum ? d.ToString() : p.Default;
        return new ParameterFieldDescriptor(p.Name, Humanize(p.Name), kind, defaultValue, p.Min, p.Max, options, p.Unit?.Symbol, p.Help);
    }

    private static ParameterFieldKind FieldKind(Type t)
    {
        if (t.IsEnum)
        {
            return ParameterFieldKind.Choice;
        }

        if (t == typeof(bool))
        {
            return ParameterFieldKind.Boolean;
        }

        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(sbyte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(byte))
        {
            return ParameterFieldKind.Integer;
        }

        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal))
        {
            return ParameterFieldKind.Number;
        }

        return ParameterFieldKind.Text;
    }

    private static OperationCategory CategoryOf(OutputKind output)
        => output == OutputKind.DerivedDataset ? OperationCategory.Process : OperationCategory.Measure;

    private static DataKind? KindOf(AfmDataset dataset) => dataset switch
    {
        ScanImageDataset => DataKind.ScanImage,
        LineProfileDataset => DataKind.LineProfile,
        SpectrumDataset => DataKind.Spectrum,
        ForceCurveDataset => DataKind.ForceCurve,
        _ => null,
    };

    private static string DatasetLabel(AfmDataset dataset)
        => dataset.Provenance.IsRoot && dataset.Source.OriginalFilePath is { } p
            ? System.IO.Path.GetFileNameWithoutExtension(p)
            : dataset.Provenance.Steps.Count > 0 ? dataset.Provenance.Steps[^1].OperationId : "dataset";

    // "meanAbsoluteDeviation" → "Mean Absolute Deviation"; "order" → "Order". Best-effort label for a
    // camelCase key/name when no explicit label is provided.
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 4);
        sb.Append(char.ToUpperInvariant(name[0]));
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
