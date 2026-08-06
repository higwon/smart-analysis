# smart-analysis

Next-generation AFM (Atomic Force Microscopy) analysis software — a ground-up
re-implementation of the existing **SmartAnalysis 2.0** desktop application.

> **This repository currently contains preparation artifacts only — documentation,
> analysis, and migration planning. No product code has been written yet.**
> The design and migration plan here are the contract that later, feature-by-feature
> AI implementation sessions will follow.

---

## Why a new software (not a port)

The existing SmartAnalysis 2.0 is a mature, numerically-validated WPF/.NET application,
but three forces make a clean rebuild the right move rather than a copy:

1. **Continuous AI-assisted development.** The codebase must be understandable by an AI
   working inside a limited context window — explicit contracts, predictable change
   scope, headless-testable analysis, and living documentation.
2. **Full UI/UX redesign.** The new product is *not* a 1:1 re-skin. Information
   architecture, navigation, workspace/dataset exploration, before/after comparison,
   and history visibility are redesigned around the real AFM analysis workflow.
3. **Structural cleanup + license independence.** Remove dead code, God ViewModels,
   domain↔UI coupling, and — critically — the **DevExpress** and **SciChart** commercial
   dependencies (~27% of existing source files touch them).

The existing repository is used **only** as an analysis reference and numeric-behavior
baseline. It is never refactored as part of this work.

## What is preserved vs. rebuilt

| Preserve (numeric behavior must match) | Rebuild (intentionally different) |
|---|---|
| Analysis algorithm results (flatten, roughness, FFT, modulus, spectrum matching, …) | Entire UI shell, navigation, dialogs |
| Physical-unit / quantity system semantics | Domain model (UI-free, immutable, owned buffers) |
| File-format parsing (TIFF/PS-PPT/HDF5) semantics | Persistence + provenance (real workspace + reproducible history) |
| Supported instrument data types & workflows | Visualization layer (behind an adapter; open-source libs) |

## Documentation

Start at **[docs/INDEX.md](docs/INDEX.md)** — it is the map, reading order, and current
status of the whole preparation effort.

Top-level structure:

- **`docs/legacy-analysis/`** — grounded, file:line-cited analysis of the *existing*
  software (the evidence base).
- **`docs/target-design/`** — architecture, domain model, analysis-operation contract,
  workflow + AI layer, visualization strategy, persistence/provenance, UI/UX, ML, testing,
  and library policy for the *new* software.
- **`docs/migration/`** — feature inventory, migration backlog (stable task IDs),
  dependency roadmap, and per-feature work specifications.
- **`docs/ai-context/`** — the common working agreement every AI implementation session
  must read first, documentation-maintenance rules, and Architecture Decision Records (ADRs).

## Status

Preparation phase. See [docs/INDEX.md](docs/INDEX.md) → "Current status" for what is done,
what is still open, and the recommended first implementation task.

## License / dependency policy (hard rules for new code)

- **No DevExpress. No SciChart. No commercial-licensed core libraries.** See
  [docs/target-design/20-library-policy.md](docs/target-design/20-library-policy.md).
- Domain and analysis layers must not reference any UI, WPF-presentation, or charting type.
