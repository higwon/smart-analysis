# Architecture Principles

The non-negotiable rules for new code. Every implementation session and ADR is bound by these.
They exist to make change scope **predictable** and the codebase **AI-workable**.

## 1. Layered architecture

The **initial structure is the consolidated set (ADR-007)** — fewer projects, split later. Do not
over-fragment early (no empty projects).

### Initial structure — created by F00 (ADR-007, 8 projects)

```
SmartAnalysis.Domain          // units, axes, buffers, datasets, channels, metadata, PROVENANCE, ROI — no UI/IO/viz
SmartAnalysis.Analysis        // operation contract + registry + operations (folders: Image/Spectroscopy/Profile/Pifm) — Domain only
SmartAnalysis.Infrastructure  // file formats + persistence + external (namespaces FileFormats/Persistence/External) — Domain only
SmartAnalysis.Visualization   // viz adapter interfaces + render-input models (no concrete chart lib) — Domain only
SmartAnalysis.Application     // workspace, active context, use-cases, orchestration — Domain/Analysis/Infrastructure/Visualization
SmartAnalysis.UI              // WPF views + view-models + first-party DesignSystem + concrete WPF viz impl (MVP) — Application/Visualization
SmartAnalysis.App             // exe: composition root wiring, startup
SmartAnalysis.Tests           // one test project: unit + architecture tests
```

**Provenance types live in `Domain`** (every dataset/artifact carries provenance) — so
`Persistence` depends on `Domain` only, **not** on Workflow (resolves OD-7).

### Deferred projects (NOT created at F00 — split when triggered, ADR-007)
`SmartAnalysis.Workflow` (workflow engine begins — AI01); `SmartAnalysis.AI`, `.ML` (AI/ML tasks);
`SmartAnalysis.Visualization.Wpf` (split concrete viz from UI when a chart lib is added — V03/V04);
`SmartAnalysis.Analysis.{Image,Spectroscopy,Profile,Pifm}` and `Infrastructure.{FileFormats,Persistence}`
(split when a folder grows or needs dependency isolation); extra test projects (per layer, on growth).
Namespaces mirror the future split so a split changes references, not code.

## 2. Dependency rules (allowed / forbidden)

Initial (consolidated) structure:
```
App → UI → Application → { Analysis, Infrastructure, Visualization(adapter) }
Analysis → Domain
Infrastructure → Domain          // FileFormats + Persistence + External live here; Persistence → Domain only
Visualization(adapter) → Domain(read-only render inputs)
UI → { Application, Visualization(adapter) }   // concrete WPF viz impl + DesignSystem live in UI (MVP)
Tests → (the projects under test)
```
When the deferred projects are split out, the direction extends but never reverses:
```
Workflow → { Analysis, Domain }        // provenance types are in Domain
AI → { Workflow(registry, schemas), Domain(read-only) }
Visualization.Wpf → Visualization(adapter)   // concrete lib lives ONLY here
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
