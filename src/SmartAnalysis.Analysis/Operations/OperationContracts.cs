using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Analysis.Operations;

/// <summary>Input to an operation: a primary dataset and optional secondary datasets (binary ops).</summary>
public sealed class OperationInput
{
    public OperationInput(AfmDataset primary, IReadOnlyList<AfmDataset>? secondary = null)
    {
        Primary = AnalysisGuard.NotNull(primary, nameof(primary));
        if (secondary is null || secondary.Count == 0)
        {
            Secondary = [];
        }
        else
        {
            var copy = new AfmDataset[secondary.Count];
            for (var i = 0; i < secondary.Count; i++)
            {
                copy[i] = secondary[i] ?? throw new ArgumentException("Secondary inputs must not contain null.", nameof(secondary));
            }

            Secondary = Array.AsReadOnly(copy);
        }
    }

    public AfmDataset Primary { get; }

    public IReadOnlyList<AfmDataset> Secondary { get; }
    // NOTE: RegionOfInterest is added with D02; omitted here (MVP operates on whole datasets).
}

/// <summary>Progress report from a running operation (0..1 plus optional message).</summary>
public readonly record struct OperationProgress(double Fraction, string? Message = null);

/// <summary>Typed result of precondition/parameter validation — failures are values, not exceptions.</summary>
public sealed class ValidationResult
{
    public static ValidationResult Success { get; } = new(true, []);

    private ValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<string> Errors { get; }

    public static ValidationResult Fail(params string[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("A failed validation must carry at least one error.", nameof(errors));
        }

        return new ValidationResult(false, Array.AsReadOnly((string[])errors.Clone()));
    }
}

/// <summary>
/// The result of an operation run: exactly one output (a derived dataset or an artifact), the typed
/// warnings, and the emitted <see cref="ProvenanceStep"/> the caller records (doc 13).
/// </summary>
public sealed class OperationResult
{
    private OperationResult(
        AfmDataset? derivedDataset,
        AnalysisArtifact? artifact,
        ProvenanceStep provenance,
        IReadOnlyList<OperationWarning> warnings)
    {
        DerivedDataset = derivedDataset;
        Artifact = artifact;
        Provenance = provenance;
        Warnings = warnings;
    }

    public AfmDataset? DerivedDataset { get; }

    public AnalysisArtifact? Artifact { get; }

    public ProvenanceStep Provenance { get; }

    public IReadOnlyList<OperationWarning> Warnings { get; }

    public static OperationResult Derived(AfmDataset dataset, ProvenanceStep provenance, IReadOnlyList<OperationWarning>? warnings = null)
        => new(AnalysisGuard.NotNull(dataset, nameof(dataset)), null, AnalysisGuard.NotNull(provenance, nameof(provenance)), Copy(warnings));

    public static OperationResult Measurement(AnalysisArtifact artifact, ProvenanceStep provenance, IReadOnlyList<OperationWarning>? warnings = null)
        => new(null, AnalysisGuard.NotNull(artifact, nameof(artifact)), AnalysisGuard.NotNull(provenance, nameof(provenance)), Copy(warnings));

    private static IReadOnlyList<OperationWarning> Copy(IReadOnlyList<OperationWarning>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return [];
        }

        var copy = new OperationWarning[warnings.Count];
        for (var i = 0; i < warnings.Count; i++)
        {
            copy[i] = warnings[i] ?? throw new ArgumentException("Warnings must not contain null.", nameof(warnings));
        }

        return Array.AsReadOnly(copy);
    }
}

/// <summary>
/// Static, self-describing metadata for an operation — the basis for UI menus (<c>ApplicableTo</c>)
/// and AI discovery (<c>Summary</c>/<c>Tags</c>). Immutable. Adding an operation never edits a
/// central enum/switch (ADR-003).
/// </summary>
public sealed record OperationDescriptor
{
    public OperationDescriptor(
        string id,
        int version,
        string displayName,
        string summary,
        IReadOnlyList<DataKind> acceptedInputs,
        ParameterSchema parameters,
        OutputKind output,
        bool isDeterministic = true,
        IReadOnlyList<string>? tags = null)
    {
        Id = AnalysisGuard.Text(id, nameof(id));
        Version = AnalysisGuard.NonNegative(version, nameof(version));
        DisplayName = AnalysisGuard.Text(displayName, nameof(displayName));
        Summary = AnalysisGuard.Text(summary, nameof(summary));
        Parameters = AnalysisGuard.NotNull(parameters, nameof(parameters));
        Output = AnalysisGuard.DefinedEnum(output, nameof(output));
        IsDeterministic = isDeterministic;

        ArgumentNullException.ThrowIfNull(acceptedInputs);
        if (acceptedInputs.Count == 0)
        {
            throw new ArgumentException("An operation must accept at least one DataKind.", nameof(acceptedInputs));
        }

        AcceptedInputs = Array.AsReadOnly(acceptedInputs.Distinct().ToArray());
        Tags = tags is null ? [] : Array.AsReadOnly(tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray());
    }

    public string Id { get; }

    public int Version { get; }

    public string DisplayName { get; }

    public string Summary { get; }

    public IReadOnlyList<DataKind> AcceptedInputs { get; }

    public ParameterSchema Parameters { get; }

    public OutputKind Output { get; }

    public bool IsDeterministic { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool Accepts(DataKind kind) => AcceptedInputs.Contains(kind);
}

/// <summary>
/// The single contract every analysis operation implements (doc 13). Headless: no UI/viz/commercial
/// types. Registered by explicit per-module DI (ADR-005); a run always emits a <see cref="ProvenanceStep"/>.
/// </summary>
public interface IAnalysisOperation
{
    OperationDescriptor Descriptor { get; }

    /// <summary>Checks input applicability + parameters. Returns typed failures (never throws for expected invalidity).</summary>
    ValidationResult Validate(OperationInput input, IParameterSet parameters);

    /// <summary>Runs the operation headlessly, honoring cancellation/progress, emitting provenance.</summary>
    Task<OperationResult> RunAsync(
        OperationInput input,
        IParameterSet parameters,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Discovers registered operations. Populated by explicit per-module DI (ADR-005).</summary>
public interface IOperationRegistry
{
    IReadOnlyList<OperationDescriptor> All { get; }

    bool TryGet(string id, out IAnalysisOperation operation);

    /// <summary>Descriptors of operations that accept the given data kind (drives menus + AI search).</summary>
    IEnumerable<OperationDescriptor> ApplicableTo(DataKind kind);
}
