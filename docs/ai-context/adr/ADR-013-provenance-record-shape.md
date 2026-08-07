# ADR-013 — Provenance record shape v1 (lineage on the dataset; serialization in Infrastructure)

- **Status:** accepted (ratified on the TASK-F05 PR)
- **Date:** 2026-08-07
- **Deciders:** project owner (via PR review)
- **Related:** ADR-004 (mandatory provenance), ADR-007 (provenance in Domain), ADR-010 (Ports &
  Adapters), ADR-012 (dataset entities), doc 16; finalizes the doc-16 draft "Provenance record"

## Context
ADR-004 made provenance mandatory and doc 16 drafted a `Provenance` record. F05 implements it. Two
shape questions needed deciding: (a) how provenance attaches to a dataset without duplicating the
dataset's own `Id`/`Source`, and (b) where JSON serialization lives (Domain must stay attribute-free
per ADR-010). A C# naming clash also surfaced: a type named `Provenance` in namespace
`SmartAnalysis.Domain.Provenance` collides with the namespace (CS0118).

## Decision
1. **Shape (Domain, `SmartAnalysis.Domain.Provenance`):**
   - **`ProvenanceRecord`** = `{ DatasetId? ParentId, IReadOnlyList<ProvenanceStep> Steps }`. Lineage
     is `ParentId` + `Steps`; it does **not** duplicate the owning dataset's `Id`/`Source` (those live
     on the dataset — ADR-012). `ProvenanceRecord.Root` = original/imported (no parent, no steps);
     `DerivedFrom(parent, steps)` + immutable `Append(step)`.
   - **State rule (both-or-neither):** exactly two valid shapes — **Root** (`ParentId == null` and no
     steps) and **Derived** (non-empty `ParentId` **and** ≥1 step). "No parent + steps" and
     "parent + no steps" are **rejected** at construction; `Append` is invalid on `Root` (a step needs
     a parent — use `DerivedFrom`).
   - **Ordering & identity validation** (provenance is reproducibility data — validated hard): `Steps`
     are **contiguously ordered from 0** (step *i* has `Order == i`); step ids are **non-null, unique**
     within a record; `Append` requires the new step's `Order == Steps.Count`. Empty ids rejected:
     `ParentId`/`InputDatasetId` non-empty, `ParentResultId` null-or-non-empty; step parameter keys
     non-empty; warning/error lists contain no null elements.
   - **`ProvenanceStep`** captures the full doc-16 field set: input dataset id + version, operation id
     + version, **parameters with units** (`IReadOnlyDictionary<string, PhysicalValue>`), execution
     order, `ExecutionEnvironment`, typed `OperationWarning`/`OperationError` lists, `ParentResultId`,
     `UserEdit?`, `AiInvolvement?` (AI-proposed vs user-approved), `MlModelRef?` (model + version).
   - Supporting value types: `ExecutionEnvironment`, `OperationWarning`, `OperationError`, `UserEdit`,
     `AiInvolvement`, `MlModelRef`. Collections are defensively copied + read-only; warnings/errors are
     typed values, never swallowed exceptions or free-text (fixes doc 07 M5 / doc 06).
2. **Type name.** The aggregate is `ProvenanceRecord` (not `Provenance`) to avoid the
   namespace/type clash; dataset **members remain named `Provenance`** (`dataset.Provenance` returns a
   `ProvenanceRecord`).
3. **Mandatory (ADR-004).** `AfmDataset` and `AnalysisArtifact` **require** a `ProvenanceRecord`
   (a result without provenance is unrepresentable). Originals pass `ProvenanceRecord.Root`.
4. **Serialization is Infrastructure (ADR-010).** The Domain provenance types carry **no serializer
   attributes**. The JSON schema v1 + serialize/deserialize round-trip are a **Persistence (P01)**
   concern; doc 16 documents the target schema.

## Consequences
- Positive: reproducibility + lineage + AI/ML traceability captured; no `Id`/`Source` duplication;
  Domain stays serializer-free; F04 operations emit `ProvenanceStep`s into a `ProvenanceRecord`.
- Negative: dataset constructors grow another required argument (intended — provenance is mandatory).
- Follow-up: P01 implements JSON schema v1 + round-trip in Infrastructure; F04 populates steps
  (env via `ExecutionEnvironment` capture); W01 uses `ParentId` for the lineage view.

## Compliance
Tests: `Root`/`DerivedFrom`/`Append` semantics; step captures params-with-units (defensively copied,
read-only); typed warnings/errors preserved; AI/ML annotations; blank-id/negative-version rejection;
datasets/artifact require provenance. Architecture Guard keeps Domain UI/commercial-free. Serializer
attributes are absent from Domain (verified by review; P01 owns serialization).
