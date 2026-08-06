# Legacy Analysis — Overview, Method & Headline Findings

Grounded static analysis of the existing **SmartAnalysis 2.0** software, performed to prepare
the `smart-analysis` rewrite. This document orients the other `legacy-analysis/*` files.

## Scope & method

- **Target:** `SmartAnalysis-Private` (Bitbucket `parksystems-corp/smartanalysis`, branch
  `develop`). 48 SDK-style projects, all `net8.0-windows`, ~169k C# LOC, 177 XAML files,
  installed as "SmartAnalysis 2.0" (NSIS installer).
- **Not analyzed:** the older `SMA.*` / `Mercy.System` predecessor solution (explicitly out of scope).
- **Approach:** read-only. Every non-trivial claim cites `Project/File.cs:line`. Unconfirmed
  items are labelled `UNVERIFIED`. The existing repo was not modified.

## Coverage

| Subsystem | Doc | Confidence |
|---|---|---|
| Solution structure, dependency graph, execution flow | 01 | High — verified across App/MainWindow/MainMenu |
| Domain & data model, unit system | 02 | High |
| Analysis / preprocessing / measurement algorithms (~75) | 03 | High — per-op cited |
| File formats & I/O (TIFF, PS-PPT, HDF5, SQLite, export) | 04 | High |
| UI / MVVM / DevExpress + SciChart | 05 | High |
| Persistence, history, provenance, reproducibility | 06 | High |

### Known gaps / not exhaustively covered
- **Runtime/reflection loading** of the two orphan projects (`FW.File.HDF5`,
  `SmartAnalysis.Dialog.ImageTool`) — flagged, `UNVERIFIED`.
- **Instrument-specific header field semantics** (the ~60-field TIFF/PSIA header) were
  catalogued structurally, not field-by-field validated against instrument firmware.
- **Native stitch engine internals** (`stitchdosa_api.dll` / `stitchdosa_engine.dll`) are
  closed-source; only the managed wrapper boundary was analyzed.
- **Exact numeric baselines** (golden values) are *not* captured here — establishing them is
  a task defined in [`../target-design/19-testing-and-validation.md`](../target-design/19-testing-and-validation.md).

## Headline findings (the ones that shape the rewrite)

1. **The numeric core is clean and reusable.** `Framework/Analysis/FW.Analysis.Calculate`
   contains **no DevExpress/SciChart** references at all; commercial-lib coupling lives only
   in ViewModels and one process class. ~25 algorithms are near-directly reusable (grade A),
   backed by open-source **MathNet.Numerics**. → doc 03.

2. **The domain model is WPF-bound and can't run headless.** Scan-data objects carry
   `BitmapImage` thumbnails and `INotifyPropertyChanged`; raw and processed data share one
   mutable type that is edited in place. → doc 02.

3. **There is no project/workspace file and no reproducibility.** Save = flatten to a single
   TIFF containing only final pixels + header. Processing history, parameters, and
   original→derived lineage are in-memory only and lost on reopen. File path is the de-facto
   identity. → doc 06.

4. **The shell is 100% DevExpress and charts are 100% SciChart.** ~179 files reference
   DevExpress, ~137 reference SciChart (~27% of source). But **2D image rendering is plain
   WPF `WriteableBitmap`** and the custom palette/MShape overlay survive a rewrite. → doc 05.

5. **Layering is inverted in places.** `LIB.File.SQLite` references *up* into
   `FW.Analysis.Calculate`/`FW.Common`; `FW.Data.Scan` (data) references
   `FW.Analysis.Calculate` (algorithms). No cycles, but no clean seam either. → doc 01.

6. **God ViewModels + no DI.** `MainWindowViewModel` (895 lines), `MainMenuCommandViewModel`
   (865), `ImageAnalysisViewModel` (876, holds 9 child View refs). Object graph is hand-wired;
   cross-VM comms via a global `Messenger.Default`. → doc 05.

7. **Data-type branching drives everything.** Four analysis views selected by
   `Header.ImageType` + `IsPiFM`: Image (with VectorScan sub-tab), Spectroscopy, PiFM, Profile.
   Fast PinPoint = the PS-PPT path. → doc 01 §4.

## How these findings map to the new design

| Finding | Design response |
|---|---|
| Clean numeric core | Extract algorithms behind an operation contract → doc 13, 03 |
| WPF-bound domain | New UI-free, immutable domain with owned buffers → doc 12 |
| No provenance/workspace | Real workspace file + reproducible provenance record → doc 16 |
| DevExpress/SciChart lock-in | Viz behind adapter, OSS libraries → doc 15, 20 |
| Inverted layering / God VMs | Strict layer rules, no central switch growth → doc 11 |
| Data-type branching | Polymorphic dataset model + operation applicability → doc 12, 13 |
