# Testing & Numeric-Validation Strategy

No tests are written this phase. This defines *how* new implementations will be verified against
the legacy software, and how numeric parity is proven. Each work spec (doc 33) links its own
validation criteria and required baseline data.

## Principle: distinguish "must match" from "intentionally different"

| Must match legacy (within tolerance) | Intentionally different |
|---|---|
| Analysis operation numeric outputs | UI, navigation, dialogs |
| Unit conversions | Persistence format, provenance capture |
| File-parse results (pixels, axes, metadata values) | Identity model (id/hash vs path) |
| Roughness/FFT/modulus/matching numbers | Workflow, AI, ML additions |

Every operation's spec states which of its behaviors are parity-locked and which are new.

## Test types

1. **Parser fixture tests** — real instrument files (TIFF/PS-PPT/HDF5) → assert pixels, axes,
   units, metadata. Note: legacy commits **no binary fixtures** (doc 04); establishing a fixture
   corpus (with an env-gated golden dir like the legacy HDF5 approach) is a foundation task.
2. **Golden-dataset numeric parity** — run the same input through the **legacy engine** and the
   new engine; compare outputs within tolerance. Because the legacy numeric core is UI-free
   (`FW.Analysis.Calculate`, doc 03), it can be driven directly to emit baselines.
3. **Property/edge tests** — NaN/Infinity, empty data, reversed axes, out-of-range interpolation
   (legacy silently returns 0 — doc 07 M5), corrupted files, unit mismatch.
4. **Determinism/regression** — deterministic ops must be byte/tolerance-stable across runs and
   flagged by `IsDeterministic` (doc 13); algorithm-version bumps trigger a baseline review.
5. **Performance/stability** — large scans: memory ceiling, no leaks (legacy H6 copies), parallel
   correctness, cancellation honored, progress reported.
6. **Persistence round-trip** — save→reopen a workspace restores datasets **and lineage**
   (the legacy failure, doc 06); schema-migration tests across versions.
7. **Workflow serialization** — a workflow round-trips and re-executes to the same result.
8. **AI structured-output validation** — proposed workflow JSON validates against schema +
   registry; test rejection of unknown operation ids and out-of-range/units-mismatched params;
   test that an AI "result" without a real run is impossible by construction (doc 14).
9. **ML regression** — model version change runs a regression suite before adoption (doc 18).
10. **Architecture tests** — dependency-direction rules (doc 11) enforced by a build-time check
    (e.g. NetArchTest): Domain/Analysis reference no UI/viz/commercial types.

## Tolerances (define per operation, seed here)
- Default floating-point comparison: relative tolerance (e.g. 1e-6) with an absolute floor for
  near-zero; **each operation may tighten/loosen** and must state its tolerance in its spec.
- Unit conversions: exact within representable precision.
- Non-deterministic (ML) ops: metric-based acceptance, not exact match.

## Establishing baselines FIRST (baseline before new analysis code)

The golden baseline must exist **before** a new operation is implemented, so parity is verifiable
immediately (feedback §6). Order:

```
Legacy fixture selection → Legacy result generation/extraction → Freeze golden data
→ new Parser/Domain/Operation → new operation → immediate parity test → UI
```

Responsibilities are split into distinct tasks (doc 31):

| Task | Responsibility |
|---|---|
| **MV00** Legacy baseline extraction | Drive the legacy engine (UI-free `FW.Analysis.Calculate`) on fixtures; dump golden JSON (values+units); record legacy commit/branch, params, input hash, tolerance; normal + edge cases. **Decoupled from the new domain** → runs early, parallel with F00–F05. (Replaces the old F06.) |
| **T01** Fixture + golden corpus | Curate/commit fixtures + freeze the golden data (or env-gate a golden dir, mirroring legacy HDF5 tests, doc 04). |
| **T02** Per-operation parity test | Assert new operation output == golden within the op's tolerance; fail on excess. |
| **MV01** Comparison report | Per-op report of matches vs intentional differences (ADR-backed). |

Because MV00 drives the *legacy* engine (which has its own units), it does **not** depend on the new
F01 unit model — the new parity test (T02) maps the recorded unit strings via the new unit system.

## What links where
- Work spec (doc 33) → "Comparison method" + "Required test data" + "Must-match vs
  intentionally-different" fields point at the relevant baseline and tolerances.
- The tech-debt defects (doc 07 M5) become explicit test cases (they must *not* be reproduced).
