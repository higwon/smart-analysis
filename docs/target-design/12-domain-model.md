# Core Domain Model (new software)

Proposed UI-free, immutable AFM domain. Derived from the legacy model analysis (doc 02) but
**not** a copy — it fixes C2 (WPF-bound, mutable), H1 (path identity), H6 (buffer copies).

> C# below is **illustrative sketch**, not code to create this phase. Names are provisional.

## Design decisions & rationale

| Question (doc 02) | Legacy | New decision | Why |
|---|---|---|---|
| Common base for Image/Curve/Spectrum? | single inheritance `BaseScanData` | **Yes**, thin `AfmDataset` base + composition | avoid deep inheritance; share identity/metadata/provenance |
| Raw vs Processed as types? | one shared, mutated in place | **same type, distinguished by `Provenance`** | processed is just a dataset with a non-empty history |
| Tree/ParentId in domain? | fused into UI tray node | **No** — lineage lives in Provenance; tree is a UI/workspace projection | separates domain from navigation (fixes fusion) |
| Immutable? | no | **Yes, externally immutable** | headless test + reproducibility |
| Who owns big arrays? | domain object, unclear lifetime, 3–5 copies | **explicit `ScanBuffer` owner**, copy only at boundaries | memory + ownership clarity (H6) |
| Buffer abstraction? | raw `Array` | **`ScanBuffer<T>` over `Memory<T>`** (implemented F01; owned array, pooling deferred — ADR-011) | slicing without copy, endianness-safe |
| Metadata strong vs dict? | ~60-field struct + loose dict | **strong core + typed extension bag** | discoverable + extensible |
| Unit conversion layer? | domain Quantity + duplicated formulas | **Domain unit system, single source** | remove duplication (doc 02 weakness) |
| Result = dataset or artifact? | new tray item | **Analysis produces a new `AfmDataset` (derived) or an `AnalysisArtifact`** (scalars/tables) | both needed |
| Identity? | file path | **content/id-based `DatasetId`** | H1 |

## Type overview

```csharp
// --- Identity & provenance ---
public readonly record struct DatasetId(Guid Value);          // stable, persisted
public sealed record DataSource(                              // where it came from
    string? OriginalFilePath, string FormatId, string? ContentHash);

// --- Units & axes (foundation; see TASK-F01) ---
public sealed record Dimension(string Name);                  // Length, Force, Current, ...
public sealed record Unit(string Symbol, Dimension Dimension, // affine to base unit
    double ScaleToBase, double OffsetToBase);
public readonly record struct PhysicalValue(double Value, Unit Unit);
public sealed record Axis(string Name, Unit Unit,             // physical axis descriptor
    double Origin, double Step, int Count, AxisDirection Direction);

// --- Buffers (explicit ownership) ---
public sealed class ScanBuffer<T> : IDisposable                // owns the memory
{
    public ReadOnlyMemory<T> Memory { get; }                   // read-only view for consumers
    public int Width { get; } public int Height { get; }       // for 2D; 1D uses Height=1
    // owner disposes; consumers never dispose; slicing returns views, not copies
}

// --- Channels & metadata ---
public sealed record ChannelDescriptor(                        // strongly typed, no string.Contains
    string Key, ChannelKind Kind, Unit Unit, string DisplayName);
public sealed record ScanMetadata(                             // strong core...
    string InstrumentModel, DateTimeOffset AcquiredAt, /* ~core fields */
    IReadOnlyDictionary<string, string> Extended);             // ...+ typed extension bag

// --- Datasets (polymorphic, immutable) ---
public abstract record AfmDataset(DatasetId Id, DataSource Source,
    ScanMetadata Metadata, Provenance Provenance);

public sealed record ScanImageDataset(/*...*/, Axis X, Axis Y,
    ChannelDescriptor Channel, ScanBuffer<float> Z) : AfmDataset;   // 2D map (+ vector-scan variant)
public sealed record LineProfileDataset(/*...*/, Axis X,
    ChannelDescriptor Channel, ScanBuffer<float> Y) : AfmDataset;
public sealed record ForceCurveDataset(/*...*/,               // spectroscopy point(s)
    IReadOnlyList<ForceCurve> Curves) : AfmDataset;
public sealed record SpectrumDataset(/*...*/, Axis X,          // PiFM spectrum
    ChannelDescriptor Channel, ScanBuffer<float> Intensity) : AfmDataset;
// PinPoint / Fast-PinPoint map onto ForceCurve/ScanImage datasets (see doc 01 §4).

// --- Analysis outputs ---
public sealed record AnalysisArtifact(DatasetId Id, DatasetId SourceId, // scalars/tables/masks
    string OperationId, IReadOnlyDictionary<string, PhysicalValue> Scalars,
    IReadOnlyList<Grain>? Grains, /* histograms, matches, ... */ Provenance Provenance);
```

`Provenance` is defined in doc 16; every dataset/artifact carries it, and lineage
(`ParentId`) lives **there**, not in a UI tree.

## Immutability & buffer ownership rules

- Datasets are `record`s with read-only members; an operation returns a **new** derived dataset.
- `ScanBuffer<T>` is the single owner of a numeric block. Consumers receive `ReadOnlyMemory<T>`.
- Copy only when crossing a boundary that requires it (file read → domain; domain → viz render
  input). No per-layer re-copy (kills the legacy 3–5× copy chain).
- Physical values are computed from raw via the **unit system only** — no duplicated
  gain/offset math scattered across services (fixes doc 02 weakness, legacy
  `ImageBaseScanData.cs:170` / `SpectroscopyDataService.cs:148`).

## Unit system (carry the legacy strength, fix the weakness)

The legacy `FW.Data.Quantity` is a genuine dimensioned system (~22 dimensions, affine
normalizer, convertibility checks) — **behaviorally reuse it** (grade B, doc 03), but:
- No global mutable `static` unit table — use an injected, immutable `UnitRegistry`.
- Axis raw↔real transform is a pure function of `(Axis, rawIndex)`; one definition.

## What maps from legacy

| Legacy | New |
|---|---|
| `BaseScanData`/`ImageBaseScanData`/… hierarchy | `AfmDataset` records (composition over deep inheritance) |
| `PhysicalZDataCollection`, raw `Data Array` | `ScanBuffer<float>` + `Axis`/`Unit` |
| `TiffHeaderModel` (~60 fields) | `ScanMetadata` core + `Extended` bag |
| `ProcessHistoryLog` (in-memory, free-text) | `Provenance` (serialized, structured — doc 16) |
| `BaseTrayItemModel` (`Id`/`ParentId` + View) | UI/workspace concern; domain keeps only `DatasetId` + provenance lineage |
| WPF `BitmapImage Thumbnail` | removed from domain; thumbnails are a viz/persistence concern |

## Implemented in D01
- **Channels**: `ChannelKind` (typed: Topography/Deflection/Amplitude/Phase/Current/Force/… + explicit
  `Unknown`) and `ChannelDescriptor` (key, kind, `Unit`, display name) — no `string.Contains` guessing.
- **Metadata**: `ScanMetadata` — an immutable **value object with structural (content-based) equality**
  (core fields + `Extended` key/value pairs, order-independent). Strong core (instrument model +
  acquired-at) + typed `Extended` string bag (defensively copied; **keys non-empty, values non-null**;
  keys compared Ordinal — per-instrument key normalization is a parser follow-up). `ScanMetadata.Unknown`
  is the explicit placeholder for derived/synthetic datasets. Full ~60-field legacy header mapping stays
  a documented follow-up (doc 00 gaps) via `Extended`.
- **Dataset upgrade**: `AfmDataset` carries `ScanMetadata Metadata`; the scan/profile/spectrum/
  force-curve datasets carry `ChannelDescriptor`(s) (value unit via `Channel.Unit`) instead of a bare
  `Unit`. **`metadata` is a required constructor argument** (callers pass `ScanMetadata.Unknown`
  explicitly for derived data, so a real importer can't silently omit it). Only `Provenance` (F05) remains.

## Implemented in F03
- **Datasets as Id-based entities (ADR-012)**: `DatasetId` (identity, never a path; non-empty
  enforced), `DataSource` (format id + optional path/hash, provenance-only), abstract `AfmDataset`
  base (`class`, **equality by `Id` only**, `IDisposable`), and concrete `ScanImageDataset`,
  `LineProfileDataset`, `SpectrumDataset`, `ForceCurveDataset`, plus `AnalysisArtifact` (Id-entity,
  no buffers) — composed from F01 types with buffer↔axes consistency validated at construction.
- **Buffer ownership (ADR-011/012)**: a dataset **owns** its `ScanBuffer`(s); `Dispose()` releases
  them. Constructor **transfers ownership on success**, leaves it with the caller **on failure**;
  the same buffer instance can't fill two roles (force-curve). Reload with the same `DatasetId`
  compares equal regardless of buffer instances (real H1 fix).
- **Deferred (incremental build):** ~~`ChannelDescriptor`/`ScanMetadata` → **D01**~~ (done);
  `Provenance` member on `AfmDataset`/`AnalysisArtifact` → **F05** (ADR-004); force-curve
  approach/retract segment model → **D03/EPIC-SPEC01**.

## Implemented in F01
- **Units**: `Dimension`, `Unit` (affine `ScaleToBase`/`OffsetToBase`), `PhysicalValue`
  (`TryConvertTo` → typed `UnitConversion`; cross-dimension = typed failure), immutable injected
  `IUnitRegistry` + `StandardUnits` (no static singletons).
- **Axes**: `Axis` (name, unit, origin, step, count, direction) with a single `RawToReal` transform;
  reversed axes explicit; out-of-range index throws.
- **Buffers**: `ScanBuffer<T>` — owned array over `Memory<T>`, copy-free slicing, `IDisposable`
  (ADR-011, resolves OD-1).

## OPEN decisions (record as ADRs when resolved)

- ~~`ScanBuffer<T>` backing~~ — **decided (ADR-011)**: owned array over `Memory<T>`; revisit pooling
  (ArrayPool) or mmap only on evidence, behind the same API.
- Whether `ForceCurve` approach/retract split is stored or recomputed on demand.
- Extension metadata: `Dictionary<string,string>` vs. a typed variant union.
- Whether vector-scan is a distinct dataset type or a `ScanImageDataset` variant (legacy: sub-mode).
