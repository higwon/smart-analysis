# PSIA-TIFF test fixtures

Committed per ADR-015 (small, purpose-appropriate real samples for reproducible CI). Only files
cleared for internal test reuse — no customer data, no personal/confidential measurements.

| File | Source | Shape | Notes |
|---|---|---|---|
| `cheese-15x15.tiff` | SmartAnalysis 2.0 installer demo sample (`Samples/Image/Cheese(1)(1).tiff`, 3.9 KB) | 2D scan image, 15×15, Topography (µm) | Tiny standard demo crop. Used by `PsiaTiffFixtureTests` as a real-file read regression guard. |
| `Spectroscopy.tiff` | In-house demo data made for an internal briefing (설명회); confirmed cleared for test reuse | Force volume, 8×8 grid, 64 points × 4096 samples, 1.75 × 1.75 µm | The **boustrophedon** acquisition UX12 was found on — 32 of its 64 points are not where their index would put them, so it is the only fixture here that can demonstrate the ordering defect. Required by `RealForceVolumeMapTests`, which pins reader → geometry → point ordering → volume image. 5.5 MB, committed to plain git deliberately (no LFS): it only earns its keep by being present on every run. |

A real force-volume map is committed above rather than env-gated, because a fixture that is only
sometimes there is a test that only sometimes runs. Larger/real-file **legacy numeric parity** (golden values) is not committed here — it is env-gated
via `SMARTANALYSIS_TIFF_SAMPLES_DIR` (see `PsiaTiffRealSampleTests`) and finalized under MV00/T01.
