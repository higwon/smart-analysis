# ADR-016 — Summary statistics diverge from legacy on empty input (NaN, not sentinels)

- **Status:** accepted (ratify on the TASK-A02 PR)
- **Date:** 2026-08-10
- **Deciders:** project owner (via PR review)
- **Related:** doc 07 M5 (legacy silent-bad-values), doc 19 (parity), MV00 golden, TASK-A02, ADR-014

## Context
A02 reproduces the legacy `SummaryStatisticsCalculator` so results match the frozen MV00 golden within
tolerance. The legacy code, on **empty** input, runs its normal path and produces **sentinel** values:
`Min = double.MaxValue`, `Max = double.MinValue`, `PeakToPeak = -∞`, `Mid = 0`, and NaN for the rest —
a silent-bad-value bug (doc 07 M5). Copying that would propagate meaningless numbers (a huge "min", a
negative "peak-to-peak") into artifacts, provenance, and any UI.

The MV00 golden **records** the legacy empty-input values (it is the reference), and doc 19/MV00 say
that where legacy is known-buggy the new code may intentionally diverge with an ADR rather than copy
the bug.

## Decision
`SummaryStatistics.Compute` returns **all-NaN** (`SummaryStatisticsResult.Empty`, `Count = 0`) for empty
input. Every **non-empty** case reproduces the legacy formulas exactly (population RMS/Sq, mean-abs-dev/Sa,
Pearson skewness/kurtosis, bounded-point-average-roughness), matching the golden within `1e-9`.

The parity test asserts golden equality for all non-empty cases and asserts the **documented divergence**
(all-NaN) for the empty case — it does not assert the legacy sentinels.

## Consequences
- Positive: no meaningless sentinel numbers escape into artifacts/provenance/UI; "no data → not a number"
  is honest and safe for downstream math and display. All meaningful (non-empty) behavior stays byte/tolerance-faithful to legacy.
- Negative: one place where new output deliberately differs from legacy — captured here and in the golden
  so it is a decision, not drift.
- Follow-up: the image statistics operation additionally emits a typed `OperationWarning` and no histogram
  for a degenerate (empty/constant/non-finite) range, rather than a bogus distribution.

## Compliance
`SummaryStatisticsParityTests` (CI, no legacy engine) feeds the golden inputs through the new code and
compares to the golden outputs (NaN/Infinity-aware), treating the empty case as the documented divergence.
`SummaryStatisticsTests` covers the empty→NaN contract directly.
