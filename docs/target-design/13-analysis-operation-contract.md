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

Operations are registered by **explicit per-module DI** — **not** reflection/attribute assembly
scan, static-ctor side effects, or a central list (**ADR-005**). Each analysis module exposes an
`AddXxxAnalysis(IServiceCollection)` that calls `services.AddAnalysisOperation<TOp>()`; the
composition root calls each module explicitly. UI builds menus from `ApplicableTo(...)`; the AI
layer searches `All` by `Summary`/`Tags`. Adding an operation = add one class + one line in its
module's `Add*()`; **no enum, no switch, no magic reflection, no God-VM edit.** Duplicate operation
ids are rejected at registration; an unregistered operation cannot be executed.

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

## Implementation status (TASK-F04)

The contract, registry, explicit-DI registration, and a reference operation are implemented in
`SmartAnalysis.Analysis` (Domain + `Microsoft.Extensions.DependencyInjection.Abstractions` only).
Deltas from the sketch above, and why:

- **Provenance has a single source of truth (ADR-014).** `OperationResult` carries **no**
  `ProvenanceStep` field — only the output object (`AfmDataset`/`AnalysisArtifact`) holds the mandatory
  `ProvenanceRecord` (ADR-004/013). A result therefore cannot carry a step that disagrees with its
  output's lineage. Read the step a run produced from `result.Artifact.Provenance.Steps[^1]` (or the
  derived dataset's). `OperationResult` = `{ DerivedDataset?, Artifact?, Warnings }`.
- **Parameter value/unit convention (ADR-014).** The runtime value in an `IParameterSet` is the **raw
  CLR value** of `ParameterDescriptor.Type`; `Unit` is descriptor **metadata** naming the unit that
  value is expressed in (so a `Unit`, and any `Min`/`Max` range, is valid only on a numeric parameter
  type). An operation pairs value + `Unit` into a `PhysicalValue` when it records provenance — so the
  binding rule is fixed once here, not re-decided per operation.
- **Schemas validate their own invariants + the values against them.** `ParameterDescriptor` rejects
  an inconsistent schema at construction (default of the wrong type, default outside range, inverted or
  non-finite range, range/unit on a non-numeric type). `ParameterSchema.Validate(IParameterSet)` is the
  common value check every operation composes with its own preconditions: unknown names, missing
  required (no-default) values, wrong CLR types, out-of-range numbers → typed `ValidationResult`
  failures. `OperationProgress` rejects a non-finite or out-of-`[0,1]` fraction at construction.
- **`OperationResult.Quality` (`QualityMetrics?`) is deferred.** No MVP operation emits it yet; it is
  added with the first operation that measures fit residual/SNR.
- **`OutputKind.InPlaceView` is not defined.** "In place" is a visualization concern, not a domain
  output; the two domain outputs are `DerivedDataset` and `Artifact`. Added only if a real operation
  needs a third kind.
- **`OperationInput.Region` (ROI) is omitted.** `RegionOfInterest` is **D02** (not MVP); MVP operations
  work on the whole dataset. `OperationInput` gains `Region` when D02 lands.
- **Enum inputs are validated.** `OperationDescriptor` rejects an undefined `OutputKind` **and** any
  undefined `DataKind` in `AcceptedInputs`; `IOperationRegistry.ApplicableTo` rejects an undefined
  `DataKind` query (a nonsense enum is a programming error, not "no matches").
- **Execution environment is injected, not self-captured.** `ProvenanceStep` requires an
  `ExecutionEnvironment` (doc 16). An operation owns no clock/host lookup, so the contract adds
  `IExecutionEnvironmentProvider` (with a `SystemExecutionEnvironmentProvider` default); the
  composition root supplies the real app version, tests supply a fixed environment for reproducibility.
- **Registration surface (ADR-005).** `AddAnalysisOperation<TOp>()` registers one operation;
  `AddOperationRegistry()` builds the `IOperationRegistry` over whatever modules registered;
  `AddExecutionEnvironment()` supplies the default provider. Each module exposes its own
  `AddXxxAnalysis()` (reference module: `AddReferenceAnalysis()`); the composition root calls them
  explicitly, then `AddOperationRegistry()` once. Duplicate operation ids are rejected at registry
  construction; an unregistered id is simply not found — there is no central switch/enum/reflection.
- **Reference operation** `reference.identity` (accepts `ScanImage`, no parameters, `Output = Artifact`)
  exercises the full path: validate (common schema check + its own precondition) → run headless
  (progress + cancellation) → emit a `ProvenanceStep` **into the artifact's `ProvenanceRecord`** →
  return that `AnalysisArtifact` derived from the input. It performs no real analysis; it proves the
  contract, the explicit-DI wiring, and the provenance flow.
