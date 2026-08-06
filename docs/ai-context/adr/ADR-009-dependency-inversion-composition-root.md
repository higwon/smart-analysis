# ADR-009 — Dependency inversion: App is the composition root (Application ⊄ Infrastructure)

- **Status:** accepted — **amends ADR-007**; **clarified by
  [ADR-010](ADR-010-infrastructure-references-application-ports.md)** (adds `Infrastructure → Application`)
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** ADR-007 (initial structure — amended here), ADR-010 (completes the table), doc 11, doc 16, F00 spec

> ⚠ **Clarified by ADR-010:** the table below shows `Infrastructure → Domain`; this is **completed**
> to `Infrastructure → Domain, Application` — an Infrastructure adapter that implements an
> **Application-owned Port** must reference Application. `Application → Infrastructure` stays
> forbidden; the reference is one-way, so there is no cycle.

## Context
ADR-007 kept the 8-project consolidated structure but listed `Application → Infrastructure` as a
reference. That couples the use-case layer to concrete IO/persistence and inverts the intended
dependency: swapping an Infrastructure implementation could force Application changes, and UI could
reach persistence/parsers transitively. The **decisions of ADR-007 stand** (8 projects,
provenance-in-Domain, F00 = architecture gate, split triggers). Only the **reference direction is
corrected** here, following Ports & Adapters / dependency inversion.

## Decision

### Corrected project reference direction
```
Analysis        → Domain
Infrastructure  → Domain, Application       (Application added — implements Application Ports; ADR-010)
Visualization   → Domain

Application     → Domain, Analysis, Visualization          (NOT Infrastructure)
UI              → Application, Visualization                (NOT Infrastructure)
App             → UI, Application, Infrastructure           (composition root)
Tests           → the projects under test
```

Forbidden references (in addition to the ADR-002/doc 11 rules):
```
Application     → Infrastructure        ❌
UI              → Infrastructure        ❌
Analysis        → Infrastructure        ❌
Visualization   → UI                    ❌
Infrastructure  → UI                    ❌
Domain          → any other product project   ❌
```

### App is the composition root
- **Application** defines **Use Cases** and the **Ports** they need (interfaces).
- **Infrastructure** provides the **implementations (adapters)** of those Ports.
- **App** is the only project that references Infrastructure; it wires implementations to Ports in
  its DI composition root.
- **UI** uses **Application Use Cases only** — never a file system, SQLite, TIFF parser, or other
  Infrastructure implementation directly.
- Replacing an Infrastructure implementation requires **no Application change**.

Example:
```csharp
// Port — in Application (or Domain if it is a pure domain contract, see below)
public interface IWorkspaceRepository {
    Task SaveAsync(Workspace workspace, CancellationToken ct);
}
// Adapter — in Infrastructure
public sealed class WorkspaceRepository : IWorkspaceRepository { /* EF Core/SQLite/files here */ }
// Wiring — in App (composition root)
services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>();
```

### Interface (Port) placement rules
- **Domain** — abstractions that are part of the domain meaning itself: pure contracts tied to the
  analysis/domain model or dataset identity, independent of any external technology.
- **Application** — Ports the Use Cases require: repositories, file open, workspace save, external
  service calls, user settings, current execution context, orchestration ports.
- **Infrastructure** — implementation-internal contracts not exposed outward (technical adapters
  between Infrastructure pieces).

No implementation detail may leak into a Domain/Application interface: **no EF Core, SQLite, WPF,
TIFF-library, JSON-serializer, or external-SDK types; no concrete file-path policy.** Where a
technical shape is needed (JSON model, DB entity, workspace schema, migration), put a **DTO/mapping
in Infrastructure**, not on the Domain/Application type.

### Provenance domain model vs. persistence (keeps ADR-007's Domain placement)
- **Domain** holds the meaning: `DatasetIdentity`, `Provenance`, `ProvenanceStep`, `Lineage`,
  `OperationIdentity`, input/output relationship — with **no** EF/SQLite/JSON/WPF/file-format
  attributes or types.
- **Infrastructure** holds the storage shape: JSON serialization model, DB entity, workspace
  schema, file-persistence implementation, schema migration — mapping to/from the Domain types.

### F00 minimal architecture guard (scope)
F00 verifies the corrected reference graph with a **minimal** check — **not** the full type/namespace
Architecture Test matrix (that stays in F02). Acceptable minimal approaches (pick one, no new
Candidate package unless approved):
- rely on the project references themselves not creating a forbidden edge (primary), **and/or**
- a simple reference-graph verification test / MSBuild check / small architecture guard.
Do **not** install NetArchTest or other Candidate packages in F00; the full matrix is F02.

### Tests project split conditions (initially one `SmartAnalysis.Tests`)
Split into `SmartAnalysis.Domain.Tests`, `.Analysis.Tests`, `.Infrastructure.Tests`,
`.Architecture.Tests` when: test count grows large; platform targets diverge; Infrastructure
integration tests get slow; architecture tests need a separate run policy; or an AI can no longer
easily read/modify one layer's tests in isolation. No test projects are created in this phase.

## Consequences
- Positive: true dependency inversion; Infrastructure is swappable without touching Application;
  UI cannot bypass Use Cases; provenance meaning stays clean of storage tech.
- Negative: App must wire adapters explicitly (intended — that is the composition root's job).
- Follow-up: doc 11, doc 16, F00 spec, backlog F02, doc 40/41 updated; F00 acceptance asserts the
  forbidden edges are absent.

## Compliance
Project references encode the allowed direction; F00's minimal guard confirms no forbidden edge;
F02's full matrix enforces it thereafter. This (with ADR-007's retained decisions) is
"must-not-change-alone" (doc 40 §12).
