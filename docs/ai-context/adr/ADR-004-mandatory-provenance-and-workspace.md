# ADR-004 — Mandatory provenance + real workspace file

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 16, doc 06 (Critical C3), doc 02 (H1)

## Context
Legacy has **no workspace file** and **no reproducibility**: save = flatten to a single TIFF with
only final pixels + header; processing history/params are in-memory free-text; `ParentId` is a
throwaway Guid never serialized, so lineage is lost on reopen; file path is the de-facto identity
(doc 06, doc 02). This is incompatible with the product's reproducibility and AI-audit goals.

## Decision
1. Every dataset/artifact carries a structured, serializable **`Provenance`** record: source
   identity + hash, and an ordered list of `ProvenanceStep`s capturing operation id+version,
   parameters **with units**, order, environment, warnings/errors, parent-result id,
   user edits, AI-suggested-vs-approved, and ML model+version.
2. A **real workspace file** persists originals + derived datasets + provenance, and **restores
   original→derived lineage on reopen**.
3. **Identity** is a stable `DatasetId` (+ content hash), never a file path; moved sources relink
   by hash.
4. No result exists without provenance (enforced).

## Consequences
- Positive: reproducibility, auditability, AI-involvement traceability, robust identity.
- Negative: larger persistence surface; schema versioning + migration required.
- Follow-up: F05 (provenance types + JSON schema v1), W01 (workspace model), P01 (save/reopen +
  lineage). Legacy flatten-to-TIFF becomes an *export*, not the workspace format.

## Compliance
An assertion that operations emit provenance; a round-trip + reproducibility test (doc 19);
provenance schema version in code == doc 16. This is "must-not-change-alone" (doc 40 §12).
