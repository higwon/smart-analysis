# TASK-F04 — Analysis Operation Contract + Registry

- **Task ID:** F04
- **Category:** Foundation
- **Priority / MVP:** P0 / yes
- **Status:** not-started

## Purpose
Define the single contract all analysis operations implement, and the registry that discovers
them — so UI, workflow, and AI call operations uniformly and adding one never edits a central
switch (fixes legacy H4: enum+switch dispatch, doc 03/07).

## User-facing behavior
Internal — but it directly shapes how every operation later appears in menus (registry
`ApplicableTo`) and to the AI (searchable descriptors).

## Legacy reference (evidence)
- Dispatch anti-pattern: `EImageProcessType`/`ESpectroscopyProcessType`/`EProfileProcessType`/
  `EPifmProcessType` ordinal enums == tab index; `ImageProcessViewModel.CreateProcessWindow`
  switch (`SmartAnalysis.Dialog.ImageProcess/ViewModel/ImageProcessViewModel.cs:137`).
- Clean numeric core to plug in: `Framework/Analysis/FW.Analysis.Calculate/*` (no UI/commercial
  types — doc 03 key finding).
- Design: [`../../target-design/13-analysis-operation-contract.md`](../../target-design/13-analysis-operation-contract.md).

## Inputs / Outputs
- Outputs: `IAnalysisOperation`, `OperationDescriptor`, `ParameterSchema`, `IParameterSet`,
  `OperationInput`, `OperationResult`, `ValidationResult`, `OperationWarning/Error`,
  `OperationProgress`, `IOperationRegistry`, DI registration.

## Parameters
n/a (framework). `ParameterSchema` must express: name, type, default, min/max range, unit,
help text.

## Preconditions
F03 (domain datasets), F05 (provenance types) exist.

## Dependencies
- Depends on: F03, F05, F02 (DI).
- Enables: **all `A##` operations** (they become independent, parallel tasks).
- Parallelizable with: FF01, V01.

## Reuse / rewrite / drop
- **New** framework (no legacy contract exists).
- **Reuse:** none directly, but it is designed to *host* the grade-A/B/C numeric code.

## Target placement
`SmartAnalysis.Analysis` (contract + registry) referencing `SmartAnalysis.Domain` only.
Consider `Analysis.Abstractions` split later (doc 11).

## Errors & boundary conditions
- `Validate` returns typed failures (wrong input kind, param out of range, missing secondary
  input) — never throws for expected invalidity.
- `RunAsync` honors `CancellationToken`; reports `IProgress<OperationProgress>`.
- Every successful run emits a `ProvenanceStep`; a run without provenance is a bug (assert).

## Performance
- Contract must not force buffer copies; operations take `ReadOnlyMemory`/`ScanBuffer` views.
- Async by default; long ops cancellable.

## Done-when
- Interfaces compile in `Analysis` referencing only `Domain`.
- A trivial reference operation (e.g. `NoOp`/`Invert`) implements the contract, registers, is
  discovered by `IOperationRegistry.ApplicableTo`, runs headless, emits provenance, and is
  unit-tested.
- Registry self-registration works via DI/assembly scan; adding an op requires **no switch edit**.
- Arch test: no UI/viz/commercial references.

## Legacy parity
- **Must match:** n/a (framework).
- **Different:** replaces enum+switch with registry (intentional).
- **Comparison:** n/a; validated by A01 (Flatten) parity once built on this.

## Required test data
None (framework); the reference op uses synthetic buffers.

## Docs to update on completion
doc 13 (lock the contract shape), INDEX status, ADR if the contract shape deviates from doc 13.

## Unverified / open
- Whether `Workflow` step wiring needs any addition to `OperationResult` (coordinate with AI01).
- `IParameterSet` representation: dictionary vs strongly-typed per-op record (recommend typed
  record + schema-derived validation).
