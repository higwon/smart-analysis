using SmartAnalysis.Domain.Datasets;
using SmartAnalysis.Domain.Geometry;
using SmartAnalysis.Domain.Provenance;

namespace SmartAnalysis.Analysis.Operations;

/// <summary>
/// Input to an operation: a primary dataset, optional secondary datasets (binary ops), and an optional
/// <see cref="Region"/> of interest (D02). A region-aware op restricts itself to the region when one is supplied
/// (e.g. roughness over a drawn ellipse) and operates on the whole dataset when it is <c>null</c>; an op that
/// ignores the region simply doesn't read it. The shell attaches the current overlay ROI for a region-capable op.
/// </summary>
public sealed class OperationInput
{
    public OperationInput(AfmDataset primary, IReadOnlyList<AfmDataset>? secondary = null, Roi? region = null)
    {
        Primary = AnalysisGuard.NotNull(primary, nameof(primary));
        Region = region;
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

    /// <summary>The region of interest to restrict the operation to, or <c>null</c> for the whole dataset (D02).</summary>
    public Roi? Region { get; }
}

/// <summary>Progress report from a running operation: a finite fraction in [0, 1] plus an optional message.</summary>
public readonly record struct OperationProgress
{
    public OperationProgress(double fraction, string? message = null)
    {
        if (!double.IsFinite(fraction) || fraction is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Progress fraction must be finite and within [0, 1].");
        }

        Fraction = fraction;
        Message = message;
    }

    public double Fraction { get; }

    public string? Message { get; }
}

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
/// The result of an operation run: exactly one output — a derived dataset or a measurement artifact —
/// plus typed warnings. The emitted provenance step is <b>not</b> duplicated here: the output object's
/// mandatory <see cref="ProvenanceRecord"/> is the single source of truth (ADR-004/013/014). Read the
/// step this run produced from <c>Artifact.Provenance.Steps[^1]</c> (or the derived dataset's), so a
/// result can never carry a step that disagrees with its output's lineage.
/// </summary>
public sealed class OperationResult
{
    private OperationResult(
        AfmDataset? derivedDataset,
        AnalysisArtifact? artifact,
        IReadOnlyList<OperationWarning> warnings)
    {
        DerivedDataset = derivedDataset;
        Artifact = artifact;
        Warnings = warnings;
    }

    public AfmDataset? DerivedDataset { get; }

    public AnalysisArtifact? Artifact { get; }

    public IReadOnlyList<OperationWarning> Warnings { get; }

    public static OperationResult Derived(AfmDataset dataset, IReadOnlyList<OperationWarning>? warnings = null)
        => new(AnalysisGuard.NotNull(dataset, nameof(dataset)), null, Copy(warnings));

    public static OperationResult Measurement(AnalysisArtifact artifact, IReadOnlyList<OperationWarning>? warnings = null)
        => new(null, AnalysisGuard.NotNull(artifact, nameof(artifact)), Copy(warnings));

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
        IReadOnlyList<string>? tags = null,
        bool usesRegion = false,
        DataKind? derivedKind = null,
        bool isCpuBound = true)
    {
        Id = AnalysisGuard.Text(id, nameof(id));
        Version = AnalysisGuard.NonNegative(version, nameof(version));
        DisplayName = AnalysisGuard.Text(displayName, nameof(displayName));
        Summary = AnalysisGuard.Text(summary, nameof(summary));
        Parameters = AnalysisGuard.NotNull(parameters, nameof(parameters));
        Output = AnalysisGuard.DefinedEnum(output, nameof(output));
        IsDeterministic = isDeterministic;
        UsesRegion = usesRegion;
        IsCpuBound = isCpuBound;

        // The derived kind is only meaningful for a dataset-deriving op — reject a kind on a measurement so the
        // metadata can't lie about what a Measure produces.
        if (derivedKind is { } dk)
        {
            AnalysisGuard.DefinedEnum(dk, nameof(derivedKind));
            if (output != OutputKind.DerivedDataset)
            {
                throw new ArgumentException("Only a DerivedDataset operation may declare a derivedKind.", nameof(derivedKind));
            }
        }

        DerivedKind = derivedKind;

        ArgumentNullException.ThrowIfNull(acceptedInputs);
        if (acceptedInputs.Count == 0)
        {
            throw new ArgumentException("An operation must accept at least one DataKind.", nameof(acceptedInputs));
        }

        foreach (var kind in acceptedInputs)
        {
            AnalysisGuard.DefinedEnum(kind, nameof(acceptedInputs));
        }

        AcceptedInputs = Array.AsReadOnly(acceptedInputs.Distinct().ToArray());
        Tags = tags is null ? [] : Array.AsReadOnly(tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray());
    }

    /// <summary>
    /// Whether <c>RunAsync</c> <b>computes</b> rather than waiting on something else.
    /// <para>
    /// Every operation here computes straight through and hands back an already-completed task, so awaiting one
    /// on the thread that asked for it holds that thread for the whole computation. On a UI thread that means
    /// nothing repaints and nothing responds, and it gets linearly worse with the data: a 64x64 force-volume map
    /// is 4096 curves. So the Application layer runs a CPU-bound operation off the caller's thread — and it is
    /// declared HERE because only the operation knows which it is, while only the caller knows whether it can
    /// afford to wait.
    /// </para>
    /// <para>
    /// An operation that genuinely awaits I/O sets this <c>false</c>: handing it to the thread pool would occupy
    /// a thread to do nothing, which is the opposite of the point.
    /// </para>
    /// </summary>
    public bool IsCpuBound { get; }

    public string Id { get; }

    public int Version { get; }

    public string DisplayName { get; }

    public string Summary { get; }

    public IReadOnlyList<DataKind> AcceptedInputs { get; }

    public ParameterSchema Parameters { get; }

    public OutputKind Output { get; }

    public bool IsDeterministic { get; }

    public IReadOnlyList<string> Tags { get; }

    /// <summary>Whether the op restricts itself to <see cref="OperationInput.Region"/> when a region is active
    /// (the shell attaches the drawn ROI for such ops); a whole-dataset op leaves this false.</summary>
    public bool UsesRegion { get; }

    /// <summary>The <see cref="DataKind"/> this operation derives, when <see cref="Output"/> is
    /// <see cref="OutputKind.DerivedDataset"/>; <c>null</c> for a measurement or when unspecified. Lets a caller
    /// tell an image→image transform (a live SOURCE/PREVIEW compare applies) from an image→curve one (it does not)
    /// <b>before</b> running.</summary>
    public DataKind? DerivedKind { get; }

    public bool Accepts(DataKind kind) => AcceptedInputs.Contains(kind);
}

/// <summary>
/// The single contract every analysis operation implements (doc 13). Headless: no UI/viz/commercial
/// types. Registered by explicit per-module DI (ADR-005); a run always emits a <see cref="ProvenanceStep"/>.
/// </summary>
public interface IAnalysisOperation
{
    OperationDescriptor Descriptor { get; }

    /// <summary>
    /// Whether the operation applies to <paramref name="dataset"/> <b>beyond</b> the coarse
    /// <see cref="OperationDescriptor.AcceptedInputs"/> DataKind — a dataset-level predicate the launcher uses to
    /// decide what to offer (e.g. a wavelength filter needs a <b>spatial</b> length-axis profile, not a PSD's
    /// frequency axis). Params aren't available yet, so this is coarse; the full check stays in
    /// <see cref="Validate"/>. Default: applicable whenever the DataKind matches.
    /// </summary>
    bool IsApplicableTo(AfmDataset dataset) => true;

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
