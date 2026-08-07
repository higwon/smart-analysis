# ADR-014 — Operation contract implementation shape (environment provider; MVP deferrals)

- **Status:** accepted (ratified on the TASK-F04 PR)
- **Date:** 2026-08-07
- **Deciders:** project owner (via PR review)
- **Related:** ADR-003 (operation contract + registry), ADR-005 (explicit per-module DI registration),
  ADR-013 (provenance record shape), doc 13, doc 16

## Context
F04 implements the doc-13 operation contract + registry in `SmartAnalysis.Analysis` (Domain +
`Microsoft.Extensions.DependencyInjection.Abstractions` only). Turning the illustrative sketch into
code surfaced decisions the sketch left open: an operation's `RunAsync` must emit a `ProvenanceStep`,
and a `ProvenanceStep` **requires** an `ExecutionEnvironment` (ADR-013) — but an operation should own
no clock or host lookup, and it cannot know the app version. Several sketch fields also have no MVP
consumer yet and would be speculative to model now.

## Decision
1. **Execution environment is injected, not self-captured.** The contract adds
   `IExecutionEnvironmentProvider { ExecutionEnvironment Capture(); }`. Operations take it via
   constructor and call it when building their `ProvenanceStep`. A default
   `SystemExecutionEnvironmentProvider` snapshots OS/machine/UTC-now with an app version supplied by
   the composition root; tests inject a fixed environment (`ExecutionEnvironment.Unknown`) so runs stay
   reproducible. The timestamp is the only non-deterministic part and belongs to the environment, not
   the numeric result (`Descriptor.IsDeterministic` concerns output).
2. **Registration surface (ADR-005).** `AddAnalysisOperation<TOp>()` registers one operation as a
   singleton and exposes it as `IAnalysisOperation`; `AddOperationRegistry()` builds the
   `IOperationRegistry` over whatever the modules registered; `AddExecutionEnvironment()` supplies the
   default provider (`TryAdd`, so the composition root may override). Each module exposes its own
   `AddXxxAnalysis()` (reference module: `AddReferenceAnalysis()`). The composition root calls the
   module `Add*`s explicitly, then `AddOperationRegistry()` once (order-independent — the registry
   resolves operations lazily). Duplicate operation ids are rejected at registry construction; an
   unregistered id is simply not found. No reflection scan, no attributes, no central switch/enum.
3. **Guard parity across the assembly boundary.** Domain's `DomainGuard` is `internal`, so Analysis
   adds an internal `AnalysisGuard` (Text / NotNull / NonNegative / DefinedEnum) rather than widening
   Domain's API. Same invariants, enforced at construction.
4. **MVP deferrals (add when a real consumer exists), documented in doc 13:**
   - `OperationResult.Quality` (`QualityMetrics?`) — no MVP op emits fit residual/SNR yet.
   - `OutputKind.InPlaceView` — "in place" is a visualization concern; domain outputs are
     `DerivedDataset` and `Artifact` only.
   - `OperationInput.Region` (`RegionOfInterest`) — ROI is **D02** (not MVP); MVP ops use the whole
     dataset.
5. **Reference operation.** `reference.identity` (accepts `ScanImage`, no params, `Output = Artifact`)
   exercises validate → run (progress + cancellation) → emit `ProvenanceStep` → return an
   `AnalysisArtifact` derived from the input. It does no real analysis; it proves the contract, the
   explicit-DI wiring, and the provenance flow, and is the template for A## operations.

## Consequences
- Positive: operations stay headless and clock-free yet still record a reproducible environment; the
  "no central switch" goal holds with a purely explicit mechanism; A## operations have a working
  template. Contract references only Domain + DI abstractions (Architecture Guard stays green).
- Negative: every operation takes an environment provider dependency (intended — provenance is
  mandatory, ADR-004); the DI surface has three registration verbs to learn (documented in doc 13).
- Follow-up: real `IExecutionEnvironmentProvider` (with the built app version) is wired at the App
  composition root in **F02/U01**; `Region`/`Quality` land with **D02** and the first quality-emitting
  op; A01+ reuse this contract.

## Compliance
Tests (`OperationContractTests`): explicit-DI registration + discovery (`All`/`TryGet`/`ApplicableTo`);
headless run emits a `ProvenanceStep` and a derived artifact with the expected scalar + lineage;
progress reported start→finish; cancellation honored; duplicate-id and null-op rejected; unregistered
id not found; typed `Validate` failure for a non-`ScanImage` primary; descriptor well-formed. Architecture
Guard updated: `SmartAnalysis.Tests` now references `SmartAnalysis.Domain` + `SmartAnalysis.Analysis`;
Analysis references Domain only (no Infrastructure/UI/commercial).
