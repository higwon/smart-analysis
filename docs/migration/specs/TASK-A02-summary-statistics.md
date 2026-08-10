# TASK-A02 — Summary statistics + histogram operation

- **Task ID:** A02
- **Category:** Analysis
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)

## Purpose
The second real operation on the F04 contract: whole-image **summary statistics** + histogram as an
`IAnalysisOperation` producing an `AnalysisArtifact`, verified for legacy parity against the MV00 golden.
It is the measurement counterpart to A01 (transform) and the template for A03 (roughness), A08 (PSD), etc.

## Legacy reference (evidence)
- Numeric core (reuse): `FW.Analysis.Calculate/SummaryStatisticsCalculator` (grade A) — `double[]` in,
  scalar stats out. Reproduced (not copied) in `SmartAnalysis.Analysis`.
- Parity target frozen by **MV00** (`tools/legacy-baseline/golden/summary-statistics.json`).

## Inputs / Outputs
- Input: `OperationInput { Primary: ScanImageDataset }`.
- Output: `OperationResult { Artifact }` — scalars (with the channel/Z unit) + a `Histogram`.

## Parameters
| name | type | default | range | unit | notes |
|---|---|---|---|---|---|
| binCount | int | 256 | ≥1 | — | histogram bin count |

## Statistics (legacy-parity formulas)
`min`, `max`, `peakToPeak` (= max−min), `mid`, `mean` (Z unit); `meanAbsoluteDeviation` (Sa),
`rms` (Sq = population RMS about the mean), `boundedPointAverageRoughness` (Z unit); `skewness`,
`kurtosis` (dimensionless Pearson moments); `count`. Histogram = uniform bins over the finite value range.

## Target placement
`SmartAnalysis.Analysis` (operation + pure `SummaryStatistics`), referencing Domain only. No WPF/commercial.

## Errors & boundary conditions
- Empty input → all-NaN result (**intentional divergence from legacy sentinels — ADR-016**).
- Degenerate range (empty/constant/all-non-finite) → **no histogram** + a typed `OperationWarning`.
- Non-finite (NaN/Inf) pixels → stats propagate non-finite + a typed warning (never a silent 0).

## Done-when
- `image.statistics` registered + discoverable via `ApplicableTo(ScanImage)` (explicit DI, no switch).
- Artifact carries the stat scalars (with units) + histogram + a `ProvenanceStep`.
- **Golden parity** (`SummaryStatisticsParityTests`) passes within `1e-9` for all non-empty cases; empty
  divergence asserted. No WPF/commercial refs (arch test).

## Implementation status (this PR)
Implemented: pure `SummaryStatistics.Compute(ReadOnlySpan<double>)` + `BuildHistogram`, reproducing the
legacy formulas (parity verified vs MV00 golden — incl. Sa/Sq, Pearson skew/kurtosis, BPAR, and NaN/Inf
edges); `StatisticsOperation : IAnalysisOperation` (`image.statistics`) over the image's physical Z; the
non-finite warning is detected from the **input pixels** (so `±Infinity` is caught, not just `NaN`);
Domain `Histogram` value object with **structural equality** (`IEquatable`/`==`/`GetHashCode` over
unit + range + ordered counts) + optional slot on `AnalysisArtifact`; `AddImageAnalysis()` DI module.
Tests: pure numeric + histogram (+ Histogram invariants/equality), golden parity, operation
run/registration/provenance, and non-finite warnings for NaN/±Infinity (none when all finite).

## Still open (follow-up)
- Histogram has no legacy golden (binning is standard); add one if a legacy histogram baseline is needed.
- Statistics over line-profile/spectrum datasets (extend `AcceptedInputs`) when those slices arrive.
