# 34 · Legacy-vs-new parity report (TASK-MV01)

**What this is.** A per-operation statement of how the new product's numbers relate to the legacy engine's:
verified equal, intentionally different (with the reason), or not comparable. It is the honest answer to
*"does the rewrite compute what the old software computed?"*

**How a claim is backed.** Every 🟢 row is enforced by a **parity test** in CI that feeds the *frozen legacy
golden*'s recorded inputs through the new code and compares outputs within the golden's tolerance. The golden
(`tools/legacy-baseline/golden/`) is produced offline by the MV00 harness driving the **real legacy primitives**
from `FW.Analysis.Calculate`, from a **clean** legacy tree whose commit + per-file SHA-256 are recorded in
`manifest.json`. CI never needs the legacy engine.

Legacy baseline: `develop @ 1451945a` · sources `SummaryStatisticsCalculator`, `PolynomialLeastSquaresRegression`,
`MultiplePolynomialRegression`, `BaselineCorrction`.

## Legend

| Status | Meaning |
|---|---|
| 🟢 **Parity verified** | A CI parity test asserts equality with the legacy golden within tolerance. |
| 🟡 **Intentional difference** | Behaviour deliberately differs; the reason is stated (ADR-backed where it is a policy decision). |
| ⚪ **No legacy counterpart** | Clean-room capability the legacy engine did not have, or whose legacy implementation is UI-coupled and cannot be driven headlessly. |

## Numeric core

| Area | New code | Legacy source | Status | Evidence / note |
|---|---|---|---|---|
| Summary statistics | `SummaryStatistics` | `SummaryStatisticsCalculator` | 🟢 | `SummaryStatisticsParityTests` — min/max/peak-to-peak/mid/mean/MAD/RMS/skewness/kurtosis over 5 recorded cases. |
| Statistics of an empty input | `SummaryStatistics` | `SummaryStatisticsCalculator` | 🟡 | Legacy returns sentinels; we return **NaN** so "no data" cannot be mistaken for a value — **ADR-016**. |
| 1D polynomial fit | `Polynomials.Fit1D` / `Infer1D` | `PolynomialLeastSquaresRegression` | 🟢 | `PolynomialParityTests` — orders 0–2 incl. a noisy line. The flatten (A01) and profile-flatten (A24) math core. |
| 2D polynomial fit | `Polynomials` (2D) | `MultiplePolynomialRegression` | 🟢 | `PolynomialParityTests` — plane (order 1) + curved surface (order 2). The surface-flatten math core. |
| ALS baseline | `AlsBaseline` | `BaselineCorrection` | 🟢 | `AlsBaselineParityTests` — sloping background with two peaks, at λ=1e5/1e7, p=0.01/0.5, 1 and 10 iterations. |
| ALS on a too-short profile | `AlsBaseline` | `BaselineCorrection` | 🟡 | Legacy returns the input unchanged; the clean-room **primitive rejects** it so a caller cannot receive a meaningless "baseline". The **operation** (A29 `profile.baseline`) matches legacy behaviour — it leaves the profile unchanged and warns. Asserted in the same parity test. |

## Operations whose legacy counterpart is UI-coupled

The legacy orchestration for these lives in WPF/dialog code (doc 03), so it cannot be driven headlessly to produce a
golden. Their **numeric cores are parity-verified above**; the operations themselves are covered by behavioural tests
(defining properties, ISO definitions, and hand-computed cases) rather than legacy comparison.

| Operation | New code | Core covered by |
|---|---|---|
| A01 Flatten (line/whole/surface) | `image.flatten` | 1D + 2D polynomial fit parity |
| A02 Image statistics | `image.statistics` | Summary statistics parity |
| A24 Profile flatten | `profile.flatten` | 1D polynomial fit parity |
| A29 Profile baseline (ALS) | `profile.baseline` | ALS baseline parity |
| A31 / A35 Range & region statistics | `curve.range-statistics`, `image.roi-statistics` | Summary statistics parity (the same golden core) |

## Clean-room capabilities (no legacy comparison)

Verified against their **defining property or standard**, not against legacy: A03/A03b areal roughness (ISO 25178),
A38/A38b profile roughness, A18 profile filter and A03b areal filter (**ISO 16610-21/61** — 50 % transmission at λc is
the lock), A25 Savitzky–Golay smoothing (a polynomial of degree ≤ order passes through unchanged), A15 peak detection
with width/SNR/filtering, A08 power spectrum, A06 grain detection, A07/A19 crop, A26 deglitch, A27 Fourier filter,
A30 spatial filter, A33 pixel math, A36/A37 profile extraction, A09 geometry.

## Coverage today

**4 of 4** legacy numeric primitives that can be driven headlessly are parity-verified (statistics, 1D fit, 2D fit,
ALS baseline), with **2 documented divergences**, both of which make an undefined result explicit rather than silent.

## Extending this report

1. Add the legacy calculator to `tools/legacy-baseline/LegacyBaseline.csproj` (compiled **by path**, never copied) and
   emit its cases in `Program.cs`.
2. Regenerate against a **clean** legacy tree: `LEGACY_CALC_DIR=<legacy>/Framework/Analysis/FW.Analysis.Calculate
   dotnet run --project tools/legacy-baseline -- tools/legacy-baseline/golden`.
3. Add a parity test under `tests/SmartAnalysis.Tests/Parity/` and a row here.

**Known next candidates.** `RoughnessCalculator` (→ A03: Sa/Sq/Sp/Sv/Sz/Ssk/Sku, and it also carries Sdq/Sdr/Sk/Spk/Svk
we have not built) and `LinePowerSpectrumCalculator` (→ A08). Both are free of WPF/DevExpress but depend on
`FW.Data.Quantity`, so the harness would need that project referenced — a deliberate step, not taken yet.
