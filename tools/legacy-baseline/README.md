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

The legacy `.cs` are **compiled by path** (see `LegacyCalcDir` in the csproj) — legacy source is
**never copied** into this repo, and the legacy repo is only ever read.

## Regenerate
Requires the legacy repo present (read-only). From the repo root:

```bash
dotnet run --project tools/legacy-baseline -- tools/legacy-baseline/golden
```

Override the legacy source location if needed:

```bash
dotnet run --project tools/legacy-baseline -- tools/legacy-baseline/golden \
  --property:LegacyCalcDir="<...>/Framework/Analysis/FW.Analysis.Calculate"
```

(or set the `LEGACY_CALC_DIR` / `LEGACY_REPO_DIR` env vars). The committed golden JSON is validated in
CI by `LegacyBaselineGoldenTests` **without** the legacy engine.

## Deferred
Full **Whole/Line/Surface flatten orchestration** golden — the legacy orchestrator
(`FlattenScopeExecutor` / `*FlattenProcess`) lives in a WPF/Dialogs-coupled project and is not driven
here. A01 rebuilds that orchestration headlessly on top of these 1D/2D polynomial-fit goldens;
end-to-end legacy orchestration parity is captured later if a clean harness path is found.
