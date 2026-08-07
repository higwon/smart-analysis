# ADR-015 — PSIA-TIFF reader: library selection, file-reader boundary, and fixture strategy

- **Status:** proposed (ratify on the TASK-FF01-PREP PR; FF01 implements against it)
- **Date:** 2026-08-07
- **Deciders:** project owner (via PR review)
- **Related:** ADR-001 (no commercial libs), ADR-006 (dependency classification), ADR-007 (8-project
  structure), ADR-009/010 (dependency inversion / Ports & Adapters), ADR-013 (provenance),
  doc 04 (legacy file formats), doc 20 (library policy), FF01 spec, doc 11 (architecture)

## Context
FF01 reads Park Systems **PSIA-TIFF** scan files into the immutable domain (`AfmDataset`). It is the
MVP's primary input and completes MVP Checkpoint 1 (Headless Import). The FF01 spec left three items
open that must be decided **before** implementation so FF01 lands as one clean, reviewable PR:

1. **Which TIFF library** — doc 20 lists TiffLibrary as Approved "pending BitMiracle-vs-TiffLibrary
   confirm in FF01"; doc 04 marks the BitMiracle usage UNVERIFIED.
2. **Where the reader lives and its contract** — the FF01 spec says "Target placement:
   `SmartAnalysis.FileFormats` → Domain", but **no such project exists** (the solution is the 8
   consolidated projects of ADR-007; there is no FileFormats project).
3. **Test fixtures** — no PSIA-TIFF fixtures exist in this repo yet.

### Evidence gathered (legacy, read-only)
- PSIA **read** path `Library/File/LIB.File.Tiff/TiffFile.cs` uses **TiffLibrary 0.6.65 (MIT)**:
  `TiffFileReader.Open` / `CreateFieldReader` / `ReadImageFileDirectory`, reading PSIA **private tags
  `0xC500–0xC509`** (MagicNumber, Version, Data, Header, Comments, LineProfileHeader,
  SpectroscopyHeader, SpectroscopyData, ExtendedHeader). WPF-free, reuse grade **B+** (doc 04 §2).
- **BitMiracle.LibTiff.NET 2.4 (BSD)** is referenced by `FW.File.Image`, but grepping the source shows
  it is used only on the **write / tag-extender** path (`FW.File.Image/Tiff/TiffEzFlattenProcess.cs`)
  and one UI view-model — **not** on the PSIA read path. So it is not "unused", but it is out of FF01
  (reader) scope; the writer is FF02 (grade D, rewritten headless later).
- The PSIA header is a fixed C struct (`PsiaHeaderStruct`, `[StructLayout(Sequential, Pack=1)]`) read
  byte-for-byte, **little-endian assumed with no swap**; the extended header is XML read with
  `Encoding.Default`; the magic value is checked only for tag **presence**, not value (doc 04 §2 —
  known defects to fix, doc 07 M2/M3).
- `SmartAnalysis.Application` and `SmartAnalysis.Infrastructure` are currently **empty** — this ADR
  sets the first port/adapter convention.

## Decision

### 1. Library — standardize new reader code on **TiffLibrary (MIT)**
- **TiffLibrary 0.6.65, MIT**, promoted **Approved → confirmed** for the FF01 reader. Rationale:
  (a) it is the **proven** legacy PSIA read path (lowest porting risk, grade B+); (b) MIT is maximally
  permissive and aligns with the commercially-unencumbered goal; (c) it reads arbitrary **private
  tags**, which PSIA requires; (d) one TIFF library, not two.
- **BitMiracle.LibTiff.NET** is **not adopted for new reader code.** It stays a legacy-only reference
  (write/EZ-flatten); if a future writer (FF02) needs a tag-extender API, that is decided then, in its
  own ADR. New code must not take a BitMiracle dependency for reading.
- **Caveat + mitigation:** TiffLibrary is lightly maintained. Because the library is **isolated in
  Infrastructure behind an Application port** (decision 2), it is swappable without touching
  Domain/Application/Analysis if it ever proves limiting. Recorded as a watch item, not a blocker.

### 2. Boundary — Application **port**, Infrastructure **adapter** (no FileFormats project)
- **No new project.** The reader is a file-I/O adapter and belongs in **`SmartAnalysis.Infrastructure`**
  (ADR-007/009/010). The FF01 spec's "FileFormats → Domain" placement is **superseded** by this ADR.
- **Port (Application):** `SmartAnalysis.Application` defines `IScanFileReader` (name provisional). It
  references **only Domain types** — no TIFF-library types ever cross this boundary.
- **Adapter (Infrastructure):** `PsiaTiffReader : IScanFileReader` under
  `SmartAnalysis.Infrastructure/FileFormats/Tiff/`, depending on TiffLibrary. Registered by explicit
  DI at the App composition root (the same explicit-registration philosophy as ADR-005).
- **Contract shape (expected failures are values, not exceptions — consistent with doc 13):**
  ```csharp
  // Application
  public interface IScanFileReader
  {
      bool CanRead(string path);                              // extension + (later) magic sniff
      Task<FileReadResult> ReadAsync(string path, ScanReadOptions options, CancellationToken ct);
  }
  public sealed record ScanReadOptions(bool MetadataOnly = false);
  // FileReadResult = success(AfmDataset) | failure(typed FileReadError: Corrupt | Truncated |
  //                  UnsupportedImageType | NotPsiaTiff | Io), never a thrown exception for these.
  ```
  Input is a **path** now, a **stream** later (spec). The result carries a **root `ProvenanceRecord`**
  with source = file id + **content hash** (ADR-013). The returned `AfmDataset` subtype
  (`ScanImageDataset` / `LineProfileDataset` / spectroscopy) is selected from `Header.ImageType`.
- **Correctness rules FF01 must honor (fixing legacy defects):** endianness **explicit** (validate,
  don't assume host); **UTF-8** for the extended-header XML (not `Encoding.Default`); **verify the
  magic value** `0xC500`, not just tag presence; NaN/Infinity pixels preserved and flagged in metadata.

### 3. Fixtures — small committed corpus + env-gated golden dir
- **Commit a minimal curated corpus** of real PSIA-TIFF samples to this (private) repo under
  `tests/SmartAnalysis.Tests/Fixtures/Tiff/`: one 2D scan, one line-profile, one spectroscopy file,
  each as small as a real instrument file allows. Sourced from the legacy sample sets
  (`NSISBuild/Sample`, `FW.UI.Common/Resource`). **Ownership:** Park Systems internal sample data in a
  private repo — acceptable to commit; if any file is size- or confidentiality-sensitive, it goes to
  the env-gated dir instead, never committed.
- **Env-gated golden dir** for larger/sensitive files and legacy-derived golden values:
  `SMARTANALYSIS_TIFF_GOLDEN_DIR`; tests **skip** (not fail) when it is absent — mirroring the legacy
  HDF5 golden pattern (doc 04 §3). Legacy-derived golden values come from **MV00/T01**.
- **Binary hygiene:** fixtures are committed as-is (no LFS for the MVP's tiny corpus); a
  `Fixtures/Tiff/README.md` records each file's provenance, instrument, and expected shape.

## Consequences
- Positive: FF01 becomes implementation-ready with no license ambiguity; the reader is swappable
  (isolated behind a port); the first Application-port / Infrastructure-adapter convention is set for
  all later readers (FF03 PS-PPT, FF04 HDF5) to follow; tests are reproducible in CI (committed corpus)
  without leaking large/sensitive data (env-gate).
- Negative: adds TiffLibrary as an Infrastructure dependency (intended, Approved); a lightly-maintained
  library is on the read path (mitigated by port isolation); committing binary fixtures grows the repo
  slightly.
- Follow-up: **FF01** implements `IScanFileReader` + `PsiaTiffReader` + fixture tests; **doc 20** table
  updated here; **FF02** (writer) revisits BitMiracle-vs-TiffLibrary for the tag-extender write path in
  its own ADR; content-based format detection is **FF05**.

## Compliance
This is a decision/spec task — **no product code**. Verification: doc 20 shows TiffLibrary Approved
(confirmed) and BitMiracle scoped to legacy write; the FF01 spec's target placement + open "which
library" item are resolved; ADR indexed (INDEX + doc 41). FF01's own PR will carry the arch-test proof
that Domain/Application stay free of TIFF-library types and that only Infrastructure references TiffLibrary.
