# ADR-002 — Layered architecture with enforced dependency direction

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 11, doc 07 (H2/H3), doc 01 §2

## Context
Legacy layering is inverted/entangled: `LIB.File.SQLite → FW.Analysis.Calculate`,
`FW.Data.Scan → FW.Analysis.Calculate`, UI-pages ↔ process-dialogs mutually referenced, no DI,
God ViewModels holding Views (doc 01 §2, doc 07 H2/H3). Change scope is unpredictable — hostile
to AI-in-limited-context work.

## Decision
Adopt the layered structure and **allowed/forbidden dependency rules** in doc 11:
`App → UI → Application → {Workflow, Analysis, Persistence, FileFormats, Visualization(adapter), AI}`,
`Analysis → Domain`, `FileFormats → Domain`, viz concrete lib only in the impl project.
Domain/Analysis reference no UI/viz/commercial types. Use DI; ViewModels never hold Views.
Start with the "initial structure" (fewer projects); split to the "expanded structure" only when
a seam is proven.

## Consequences
- Positive: predictable change scope; headless testable; AI-workable.
- Negative: more discipline; some indirection (adapters, DI wiring).
- Follow-up: F02 sets up the skeleton + a dependency-direction test.

## Compliance
A build-time architecture test (e.g. NetArchTest) encodes the allowed/forbidden edges and fails
the build on violation (doc 19). This ADR's rules are in doc 40 §12 "must-not-change-alone".
