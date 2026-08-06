# Analysis Operation Contract

The single standard by which **all** analysis/preprocessing/measurement functions are exposed,
so that UI, the workflow engine, and the AI layer call them uniformly, and adding an operation
never edits a central switch (fixes legacy H4). Grounded in the ~75 operations catalogued in
[`../legacy-analysis/03-analysis-algorithm-inventory.md`](../legacy-analysis/03-analysis-algorithm-inventory.md).

> C# is an illustrative sketch. Names provisional.

## Separation of concerns (per operation)

Each legacy feature decomposes into these distinct parts — keep them separate:

```
<Feature>
├─ Pure numeric algorithm      (Analysis, headless, unit-tested)   ← reuse grade A/B/C code
├─ Parameter model             (typed, schema, defaults, ranges, units)
├─ Operation                   (IAnalysisOperation: validate → run → provenance)
├─ Provenance contribution     (records op id/version/params/units/order)
├─ UI parameter input          (UI layer only)
└─ Before/After visualization  (Viz adapter only)
```

Example (Flatten, from doc 03 §B and doc 01 §4.5):

```
Flatten
├─ WholeFlatten/LineFlatten/SurfaceFlatten numeric (double[] in/out; reuse PolynomialLeastSquaresRegression, MultiplePolynomialRegression — grade A)
├─ FlattenParameters { Scope, Orientation, RegressionOrder, ZeroBasement, Region }
├─ FlattenOperation : IAnalysisOperation
├─ writes Provenance step { op="flatten", v=1, params..., units }
├─ UI: flatten panel (scope/order/region pickers)
└─ Viz: before/after 2D image + histogram
```

## The contract

```csharp
public interface IAnalysisOperation
{
    OperationDescriptor Descriptor { get; }                    // static metadata (below)
    ValidationResult Validate(OperationInput input, IParameterSet p);   // preconditions + param check
    Task<OperationResult> RunAsync(OperationInput input, IParameterSet p,
        IProgress<OperationProgress>? progress, CancellationToken ct);
}

public sealed record OperationDescriptor(
    string Id,                       // stable, e.g. "image.flatten"
    int Version,                     // algorithm version (bump on numeric change) — provenance
    string DisplayName,
    string Summary,                  // human + AI-readable description of what/why
    IReadOnlyList<DataKind> AcceptedInputs,   // e.g. ScanImage; applicability, not a switch
    ParameterSchema Parameters,      // typed params: name, type, default, range, unit, help
    OutputKind Output,               // DerivedDataset | Artifact | InPlaceView
    bool IsDeterministic,            // reproducibility flag
    IReadOnlyList<string> Tags);     // for search / AI discovery

public sealed record OperationInput(AfmDataset Primary, IReadOnlyList<AfmDataset> Secondary,
    RegionOfInterest? Region);       // secondary for binary ops (arithmetic, difference, matching)

public sealed record OperationResult(
    AfmDataset? DerivedDataset,      // for transforms
    AnalysisArtifact? Artifact,      // for measurements
    IReadOnlyList<OperationWarning> Warnings,   // typed, not swallowed
    ProvenanceStep Provenance,       // what ran, with what, in what units
    QualityMetrics? Quality);        // optional (fit residual, SNR, etc.)
```

Requirements the contract enforces (maps to the task spec fields, doc 33):

- **Input type validation** — `AcceptedInputs` + `Validate`.
- **Parameter schema** with defaults, allowed ranges, and **units** (a param carries a `Unit`).
- **Preconditions** — `Validate` returns typed failures (e.g. "requires ≥2 datasets",
  "channel must be Force").
- **Output type** — declared, so callers/AI know what they get.
- **Errors vs warnings** — errors fail `Validate`/`Run`; warnings are non-fatal typed values.
- **Progress & cancellation** — every potentially-slow op honors `ct` and reports `progress`.
- **Determinism + version** — for reproducibility and regression testing (doc 19).
- **Provenance** — `RunAsync` always emits a `ProvenanceStep` (doc 16). No result without it.
- **UI-independent** — the whole contract references only Domain types.
- **AI-readable metadata** — `Summary`, `Tags`, `ParameterSchema.help` let the AI layer
  discover and explain operations (doc 14) without executing arbitrary code.

## Registry (no central switch)

```csharp
public interface IOperationRegistry {
    IReadOnlyList<OperationDescriptor> All { get; }
    bool TryGet(string id, out IAnalysisOperation op);
    IEnumerable<OperationDescriptor> ApplicableTo(DataKind kind);   // drives UI menus + AI search
}
```

Operations self-register (assembly scan / DI). UI builds menus from `ApplicableTo(...)`; the AI
layer searches `All` by `Summary`/`Tags`. Adding an operation = add one class + register; no
enum, no switch, no God-VM edit.

## Worked examples (legacy → new)

### 1. Image Flatten (transform → derived dataset)
- Numeric: reuse `PolynomialLeastSquaresRegression`/`MultiplePolynomialRegression` (grade A);
  drop WPF `Point[]` from signatures (legacy grade C is only due to `Point[]`) → pass a
  `RegionOfInterest` domain type.
- `Output = DerivedDataset`; provenance step `{ "image.flatten" v1, scope, order, orientation }`.
- Validate: input is `ScanImage`; order in range; region within bounds.

### 2. Roughness / statistics (measurement → artifact)
- Numeric: reuse `RoughnessCalculator`/`SummaryStatisticsCalculator` (grade A/B); take
  `ScanBuffer`/`Axis` instead of `PhysicalValueCollection` bound to legacy quantity singletons.
- `Output = Artifact` (Sq/Sa/… as `PhysicalValue`s). Deterministic. No secondary input.

### 3. Spectrum matching (comparison → artifact)
- Numeric: reuse the 4 matchers + 7 preprocessors + `SpectrumMatchingService` (grade A/B, doc 03 §A.6).
- Input: primary spectrum + secondary reference set (or a persisted spectrum-library query).
- `Output = Artifact` (ranked matches with scores); warnings for non-overlapping ranges.

## Failure & boundary conditions (must be modeled, not swallowed)

From legacy defects (doc 07 M5): out-of-range interpolation, NaN/Infinity, empty data,
reversed axes, non-overlapping spectra, unit mismatch. These become typed `ValidationResult`
failures or `OperationWarning`s — never a silent `0` or `null`.
