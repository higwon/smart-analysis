# Architecture Principles

The non-negotiable rules for new code. Every implementation session and ADR is bound by these.
They exist to make change scope **predictable** and the codebase **AI-workable**.

## 1. Layered architecture

Two views are given: a **realistic initial structure** (fewer projects) and an **expanded
structure** to split into later. Do not over-fragment early.

### Initial structure (start here)

```
SmartAnalysis.Domain          // AFM model, units, buffers — no UI, no IO, no viz
SmartAnalysis.Analysis        // operations + numeric algorithms — depends on Domain only
SmartAnalysis.FileFormats     // parsers/writers (TIFF, PS-PPT, HDF5) — Domain only
SmartAnalysis.Persistence     // workspace file, provenance, spectrum library (SQLite)
SmartAnalysis.Workflow        // operation registry, workflow engine, provenance recording
SmartAnalysis.Visualization   // adapter interfaces + render-data models (no concrete chart lib)
SmartAnalysis.Visualization.<Impl>  // concrete viz backend (e.g. ScottPlot/Skia) — isolated
SmartAnalysis.AI              // NL → structured workflow proposal; validates against registry
SmartAnalysis.Application     // app services, use-cases, DI composition root (no WPF types)
SmartAnalysis.UI              // WPF views + view-models — depends on Application + Viz adapter
SmartAnalysis.App             // exe: composition root wiring, startup
```

### Expanded structure (split when a layer grows)

`Analysis` → `Analysis.Abstractions` + `Analysis.Image` + `Analysis.Spectroscopy` + `Analysis.Pifm`;
`FileFormats` → one project per format; `Visualization.<Impl>` → 2D / 3D / curve backends;
`UI` → one module per analysis domain. Split only when a project exceeds comprehension or a
seam is proven necessary — never speculatively.

## 2. Dependency rules (allowed / forbidden)

```
App → UI → Application → { Workflow, Analysis, Persistence, FileFormats, Visualization(adapter), AI }
Workflow → { Analysis, Domain }
Analysis → Domain
FileFormats → Domain
Persistence → { Domain, Workflow(provenance types) }
AI → { Workflow(registry, schemas), Domain(read-only view) }
Visualization(adapter) → Domain(read-only render inputs)
Visualization.<Impl> → Visualization(adapter)   // concrete lib lives ONLY here
```

**Forbidden — enforced by review and, where possible, by analyzers/tests:**
- ❌ `Domain` or `Analysis` referencing **any** UI, WPF-presentation, charting, or DevExpress/
  SciChart type (`BitmapSource`, `DependencyObject`, `SciChart*`, `DevExpress*`, `Dialog`, ViewModel).
- ❌ Any layer below `UI` referencing a concrete visualization library.
- ❌ Library/IO layers referencing "up" into Analysis (the legacy H2 inversion must not recur).
- ❌ `Domain` referencing `Analysis` (the legacy `FW.Data.Scan → FW.Analysis.Calculate` inversion).
- ❌ ViewModels holding View references (legacy H3).

A dependency-direction test (e.g. NetArchTest / a custom check) should fail the build on
violation — see doc 19.

## 3. Core rules

1. **Analysis runs headless.** Every operation is executable and unit-testable with no UI,
   no WPF, no charts. (Kills legacy C2.)
2. **Domain is UI-free and (externally) immutable.** No `INotifyPropertyChanged`, no bitmaps,
   no observable collections in Domain. Results are new objects, never in-place edits. (C2.)
3. **No commercial libraries.** See doc 20. Concrete viz lib is isolated behind an adapter so
   it can be swapped without touching Domain/Analysis. (C1, M4.)
4. **No central switch growth.** New operations register themselves; adding one must not edit a
   shared enum+switch. (H4.)
5. **Explicit contracts over implicit rules.** Operations declare input types, parameter schema,
   preconditions, outputs, failure modes, determinism, and version (doc 13).
6. **Provenance is mandatory.** No result exists without a provenance record (doc 16).
7. **Buffer ownership is explicit.** Large arrays have a defined owner and lifetime; copy only at
   boundaries; prefer pooled/`Memory<T>` buffers (doc 12). (H6.)
8. **Dependency injection, no global mutable state.** A composition root wires services; no
   `Messenger.Default`, no static managers, no ambient singletons. (H5.)
9. **Async + cancellation + progress are first-class** for any operation that can be slow;
   cancellation must be honored, progress reported through the operation contract (doc 13).
10. **Errors and warnings are typed values**, not swallowed exceptions or free-text comments (doc 13).
11. **Identity is stable and content-based**, never a file path (doc 12, 16). (H1.)
12. **Framework independence at the seams.** UI framework (WPF) and viz library are replaceable;
    Domain/Analysis/Workflow never depend on either. (Keeps future Avalonia/cross-platform open.)

## 4. What this buys us (the "AI-workability" test)

For any feature, an AI session should be able to:
- find it from the feature inventory + backlog (doc 30, 31),
- read one spec + a bounded set of design docs + a bounded set of source files,
- implement it without reading the whole repo,
- test it headlessly against a numeric baseline,
- and know exactly which docs to update on completion (doc 41).

If a proposed design breaks that test, it violates these principles.

## 5. Open architecture decisions (validate per-task, record as ADRs)

- MVVM toolkit choice (CommunityToolkit.Mvvm assumed; ADR to confirm).
- Buffer abstraction: raw `T[]` + owner vs. a `ScanBuffer<T>` wrapper over `Memory<T>` (doc 12 OPEN).
- Whether `Workflow` and `Analysis.Abstractions` merge initially.
- Concrete visualization backend (doc 15 — pending a rendering spike).
