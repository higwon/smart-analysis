# ADR-003 — Standard operation contract + registry (no central switch)

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** doc 13, doc 03, doc 07 (H4); **ADR-005 refines the registration mechanism**
  (explicit per-module DI)

## Context
Legacy operations are dispatched by ordinal enums (== tab index) and central `switch` statements
(`EImageProcessType` + `ImageProcessViewModel.CreateProcessWindow:137`, etc.). Every new operation
edits shared enums/switches and God-VMs — merge conflicts and unbounded reading for any change
(doc 07 H4). But the numeric core (`FW.Analysis.Calculate`) is clean and reusable (doc 03).

## Decision
All analysis/preprocessing/measurement functions implement a single `IAnalysisOperation` contract
with a self-describing `OperationDescriptor` (id, version, accepted inputs, parameter schema with
units/ranges, output kind, determinism, AI-readable summary/tags). Operations **self-register** in
an `IOperationRegistry`; UI menus and AI discovery come from `ApplicableTo`/`All`. Adding an
operation must not edit any shared enum/switch. Every run emits a `ProvenanceStep`.

## Consequences
- Positive: operations become independent, parallelizable tasks (A03–A16); uniform calling from
  UI/workflow/AI; provenance guaranteed; AI can discover ops without executing arbitrary code.
- Negative: slightly more ceremony per operation than a switch case.
- Follow-up: F04 implements the contract + registry; A01 (Flatten) is the reference implementation.

## Compliance
Registry is populated by **explicit per-module DI registration** (ADR-005) — not assembly scan; a
test asserts no operation requires a central switch edit and that every operation emits provenance.
Contract shape is "must-not-change-alone" (doc 40 §12).
