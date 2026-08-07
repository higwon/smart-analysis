# TASK-FF01 — TIFF (PSIA) reader → domain

- **Task ID:** FF01
- **Category:** FileFormat
- **Priority / MVP:** P0 / yes
- **Status:** tracked in [migration backlog](../31-migration-backlog.md) (not authoritative here)
- **Prep decisions:** [ADR-015](../../ai-context/adr/ADR-015-tiff-reader-library-and-boundary.md)
  (library = TiffLibrary/MIT; reader boundary = Application port + Infrastructure adapter; fixture
  strategy) — resolves the "Unverified / open" items below before implementation.

## Purpose
Read Park Systems PSIA-TIFF scan files into the new immutable domain model. TIFF is the primary
instrument format and the MVP input.

## User-facing behavior
User opens a `.tiff` scan file and it appears as a dataset in the workspace.

## Legacy reference (evidence)
- `Framework/File/FW.File.Image/Tiff/TiffReader.cs:19-58` — `ReadBaseScanDataOfTiff` /
  `ReadMetadataOnlyOfTiff`; switches on `Header.ImageType` to pick subtype (doc 01 §4.2).
- `Library/File/LIB.File.Tiff/*` — low-level TIFF read; `EScanImageType`, `EOpenFileType`.
- Header struct: `TiffHeaderModel` (~60 fields, `TiffHeaderModel.cs:11`).
- Parser is **WPF-free (grade B)**; the domain mapping is WPF-coupled and must be rewritten
  (doc 04). `TiffLibrary` (MIT) vs `BitMiracle.LibTiff` (BSD) — confirm which is actually used
  (doc 04 UNVERIFIED).
- legacy-analysis: doc 04, doc 01 §4.2.

## Inputs / Outputs
- Input: file path (later a stream).
- Output: an `AfmDataset` subtype (`ScanImageDataset` / `LineProfileDataset` /
  spectroscopy dataset) per `Header.ImageType` + `IsPiFM`, with `ScanBuffer`, `Axis` X/Y,
  `ChannelDescriptor`, `ScanMetadata`, `Provenance = ProvenanceRecord.Root`, and the file origin on
  `Source = DataSource("psia-tiff", path, contentHash)` (identity/source live on the dataset, **not**
  in provenance — ADR-013).

## Parameters
- `metadataOnly` (bool) — support the legacy deferred/metadata-only mode (`ETiffLoadMode`).

## Units
Axis steps/units and channel unit derived from header (scan size, gain, unit fields). Use the F01
unit system; do not duplicate gain/offset math.

## Preconditions
F01, F03, D01 exist.

## Dependencies
- Depends on: F01, F03, D01.
- Enables: A01 (flatten), W01/P01 (workspace), T01 (fixtures).
- Parallelizable with: F04, V01.

## Reuse / rewrite / drop
- **Reuse (extract):** the low-level TIFF byte reading + header parsing (WPF-free).
- **Rewrite:** the mapping to domain (no `BitmapImage` thumbnail in domain; thumbnail is a
  viz/persistence concern). Endianness explicit (legacy assumes host — doc 07 M3).
- **Drop:** any `FormatConvertedBitmap`/WPF imaging in the read path.

## Target placement (ADR-015 — supersedes the earlier "FileFormats → Domain")
There is **no** `SmartAnalysis.FileFormats` project (8-project structure, ADR-007). The reader is a
file-I/O adapter:
- **Port (Application):** `IScanFileReader` in `SmartAnalysis.Application` — references **Domain only**,
  no TIFF-library types.
- **Adapter (Infrastructure):** `PsiaTiffReader : IScanFileReader` under
  `SmartAnalysis.Infrastructure/FileFormats/Tiff/`, depending on **TiffLibrary (MIT)**; registered by
  explicit DI at the App composition root. TIFF-library types never cross into Application/Domain.

```csharp
// Application
public interface IScanFileReader
{
    bool CanRead(string path);
    Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken ct);
}
public sealed record ScanReadOptions(bool MetadataOnly = false);
// FileReadResult = success(AfmDataset) | failure(typed FileReadError:
//   Corrupt | Truncated | UnsupportedImageType | NotPsiaTiff | Io) — expected failures are values,
//   not thrown exceptions (doc 13).
```
On success the dataset is an **original/root** (ADR-013): `Dataset.Provenance = ProvenanceRecord.Root`
and the file origin goes on the **dataset's `Source`**, not in provenance —
`Dataset.Source = new DataSource("psia-tiff", originalFilePath: path, contentHash: hash)`
(`ProvenanceRecord` = `ParentId + Steps`; it must not duplicate identity/source). No UI reference (arch test).

## Errors & boundary conditions
- Corrupted/truncated file → typed error with context (legacy shows a message box — here return a
  failure the caller surfaces).
- Unknown `ImageType` → explicit unsupported error.
- NaN/Infinity pixels preserved; flagged in metadata if present.
- Embedded XML/text: **never `Encoding.Default`** (locale-dependent — doc 04/07 M2). Decode explicitly
  per BOM / XML declaration (or the confirmed PSIA rule) and **verify the real encoding with fixtures**
  before pinning one. Do not blindly force UTF-8 — legacy device files may be Windows-ANSI/locale, and a
  blind switch would corrupt Korean/special-character metadata.

## Performance
- Support metadata-only fast path (deferred open) as legacy does.
- Stream large files where feasible; single-copy into `ScanBuffer`.

## Done-when
- Reads a scan-image TIFF into `ScanImageDataset`; pixels, axes, units, and key metadata match
  legacy `TiffReader` output on the same file (fixture test).
- Line-profile and spectroscopy TIFF subtypes route correctly (parity with doc 01 §4.2 table).
- No WPF/commercial references (arch test).

## Legacy parity
- **Must match:** pixel values, axis origin/step/count, channel unit, and catalogued metadata
  fields.
- **Different:** domain types, no thumbnail in domain, explicit endianness/encoding.
- **Comparison:** fixture files + legacy-derived golden values (MV00/T01).

## Required test data (ADR-015)
Real PSIA-TIFF samples (legacy has samples under `NSISBuild/Sample`, `FW.UI.Common/Resource` —
doc 04). Strategy:
- **Commit a minimal curated corpus** under `tests/SmartAnalysis.Tests/Fixtures/Tiff/` — one 2D scan,
  one line-profile, one spectroscopy file, each as small as a real file allows — with a
  `README.md` recording provenance/instrument/expected shape. **A file may be committed only if all
  hold:** cleared for internal test/sample reuse, **no customer data**, no personal/confidential
  measurement. Prefer installer-distributed or purpose-made test files. If ownership/export status is
  unclear, use it **env-gated only, never committed**; real customer files are not committed even if small.
- **Env-gate a golden dir** (`SMARTANALYSIS_TIFF_GOLDEN_DIR`) for larger/sensitive files and
  legacy-derived golden values (from MV00/T01); tests **skip** (not fail) when absent.

## Docs to update on completion
doc 04 (confirm details), doc 12 (metadata mapping), INDEX status, T01 fixture list.

## Resolved (ADR-015)
- **TIFF library:** legacy PSIA **reader** uses **TiffLibrary (MIT)**; BitMiracle is legacy
  write/EZ-flatten only. New reader standardizes on **TiffLibrary**, Infrastructure-only behind
  `IScanFileReader`.
- **Reader boundary/API:** Application port + Infrastructure adapter (see Target placement).
- **Fixtures:** committed minimal corpus + env-gated golden dir (see Required test data).

## Implementation status (this PR)
Implemented per ADR-015 — Application port `IScanFileReader` + typed `FileReadResult`, Infrastructure
`PsiaTiffReader` on **TiffLibrary (MIT)**, explicit DI (`AddPsiaTiffReader`).
- **Shipped:** the **2D scan-image** path → `ScanImageDataset`. PSIA tags MagicNumber(0xC500,
  presence)/Header(0xC503)/Data(0xC502); header parsed with an explicit little-endian `BinaryReader`
  (no `unsafe`); pixels short/int/float → `physical = raw*DataGain + ZOffset` in the header `Unit`;
  axes raw[0..W]→real[0..XScanSize] **µm**; unit resolved via `IUnitRegistry` (dimensionless fallback);
  `Provenance = ProvenanceRecord.Root`; `Source = DataSource("psia-tiff", path, SHA-256 hash)`. Typed
  failures for missing-magic / short-header / truncated-payload / missing-file / non-2D image type.
- **Tests:** deterministic **synthetic PSIA-TIFF** (hand-written bytes) parsed by the real TiffLibrary
  → reader → domain, covering short/int/float, axes/units/values, provenance/source, and every typed
  failure. Plus **real files**: a committed tiny installer demo crop (`cheese-15x15.tiff`, 3.9 KB) as a
  CI read-regression guard, and an **env-gated** sweep (`SMARTANALYSIS_TIFF_SAMPLES_DIR`, default
  install path) — validated locally against the SmartAnalysis 2.0 samples: **all 27 `Samples/Image`
  2D scans read**; Profile/Spectroscopy/PiFM-spectra correctly route to typed `UnsupportedImageType`,
  while PiFM 2D maps (Z Height, single-wavenumber) read as images. Legacy **numeric** parity (golden)
  stays deferred to MV00/T01.
- **Deferred (follow-up):** line-profile & spectroscopy mapping (currently typed `UnsupportedImageType`);
  metadata-only fast path (option plumbed, not yet optimized); `DateTime` tag parse (`AcquiredAt`
  placeholder); comparing the exact magic **value**; physical values stored as `float` (legacy uses
  `double`) — revisit precision against golden; 1 MB chunked pixel streaming for very large images.

## Still open (resolve during follow-up)
- Full semantics of the ~60 header fields (only structurally catalogued — doc 00 gaps); map the
  MVP-required subset (axes origin/step/count, channel unit, image type) first, catalogue the rest.
- **Magic check:** `0xC500` is the PSIA **`MagicNumber` private tag ID**, not the magic value. Legacy
  only checks the **tag is present**. FF01 must confirm the tag's **expected value** from legacy
  constants/spec/fixtures and compare it (doc 07 M3), not just presence.
