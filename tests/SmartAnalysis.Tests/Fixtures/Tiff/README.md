# PSIA-TIFF test fixtures

Committed per ADR-015 (small, purpose-appropriate real samples for reproducible CI). Only files
cleared for internal test reuse — no customer data, no personal/confidential measurements.

| File | Source | Shape | Notes |
|---|---|---|---|
| `cheese-15x15.tiff` | SmartAnalysis 2.0 installer demo sample (`Samples/Image/Cheese(1)(1).tiff`, 3.9 KB) | 2D scan image, 15×15, Topography (µm) | Tiny standard demo crop. Used by `PsiaTiffFixtureTests` as a real-file read regression guard. |

Larger/real-file **legacy numeric parity** (golden values) is not committed here — it is env-gated
via `SMARTANALYSIS_TIFF_SAMPLES_DIR` (see `PsiaTiffRealSampleTests`) and finalized under MV00/T01.
