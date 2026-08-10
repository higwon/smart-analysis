# legacy-baseline (TASK-MV00)

Freezes the **legacy numeric ground truth** the new MVP operations are validated against (parity —
T01/T02/A01/A02). This is a one-off generation harness — **not product code**: it lives outside
`src/`, is **not** in `SmartAnalysis.sln`, and never ships.

## What it does
Drives the **clean** legacy numeric primitives from `FW.Analysis.Calculate` (net8.0-windows; deps =
MathNet/log4net/FW.Data.* only — **no DevExpress/SciChart**) on deterministic **synthetic** inputs and
writes golden JSON + a manifest:

- `SummaryStatisticsCalculator` → `golden/summary-statistics.json` (enables **A02**).
- `PolynomialLeastSquaresRegression` (1D) → `golden/polynomial-fit-1d.json` (Line/Whole flatten core).
- `MultiplePolynomialRegression` (2D) → `golden/polynomial-fit-2d.json` (Surface flatten core).
- `golden/manifest.json` — the exact **legacy commit/branch**, MathNet version, notes.

The legacy `.cs` are **compiled by path** (`LegacyCalcDir`) — legacy source is **never copied** into
this repo, and the legacy repo is only ever read.

## Provenance guarantees (why the golden is trustworthy)
- **Single source of truth:** git commit/branch are derived from the **same** `LegacyCalcDir` the code
  was compiled from (via `git rev-parse --show-toplevel`) — the manifest can't record a different repo
  than the one compiled.
- **Clean tree only:** generation **refuses** (non-zero exit) if the compiled `.cs` have uncommitted
  changes, so the recorded commit always reproduces the golden. The manifest also records each source
  file's SHA-256.
- **No machine paths:** no absolute path is written to the golden; `LegacyCalcDir`/`LEGACY_CALC_DIR` is
  **required** (no personal default).

## Regenerate
Requires the legacy repo present (read-only) and a **clean** working tree for the three primitive files.
From the repo root, set `LEGACY_CALC_DIR` to the legacy `FW.Analysis.Calculate` folder:

```bash
LEGACY_CALC_DIR="<legacy>/Framework/Analysis/FW.Analysis.Calculate" \
  dotnet run --project tools/legacy-baseline -- tools/legacy-baseline/golden
```

The committed golden JSON is validated in CI by `LegacyBaselineGoldenTests` **without** the legacy
engine (structure, a hand-checked value, fit self-consistency, and **`InputSha256` recomputed from the
recorded inputs**).

## Deferred
Full **Whole/Line/Surface flatten orchestration** golden — the legacy orchestrator
(`FlattenScopeExecutor` / `*FlattenProcess`) lives in a WPF/Dialogs-coupled project and is not driven
here. A01 rebuilds that orchestration headlessly on top of these 1D/2D polynomial-fit goldens;
end-to-end legacy orchestration parity is captured later if a clean harness path is found.
