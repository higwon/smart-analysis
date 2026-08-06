# ADR-010 — Infrastructure references Application to implement Application-owned Ports

- **Status:** accepted — **clarifies [ADR-009](ADR-009-dependency-inversion-composition-root.md)**
  (completes its reference table)
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** ADR-009, ADR-007, doc 11, doc 16, F00 spec

## Context
ADR-009 established the dependency inversion (Application ⊄ Infrastructure; App = composition root;
Infrastructure implements Application/Domain Ports) but its reference table listed
`Infrastructure → Domain` only. If a Port is **owned by Application** (e.g. `IWorkspaceRepository`),
the Infrastructure adapter that implements it **must reference Application**. Otherwise the Port
placement rule and the reference table contradict each other.

## Decision
Complete the reference direction:

```
Analysis        → Domain
Visualization   → Domain
Application      → Domain, Analysis, Visualization
Infrastructure   → Domain, Application          ← added (to implement Application-owned Ports)
UI              → Application, Visualization
App             → UI, Application, Infrastructure   (composition root)
Tests           → the projects under test
```

Core rule (unchanged from ADR-009, now consistent):
```
Application → Infrastructure   ❌ forbidden
Infrastructure → Application    ✅ allowed (only to implement Application-owned Ports)
```

No cycle: Application does **not** reference Infrastructure, so `Infrastructure → Application` is
one-way. This is textbook Ports & Adapters — Application knows only its Ports; Infrastructure
depends on those Port abstractions, not the other way around.

### Port location ⇒ Infrastructure references
- **Domain Port** (pure, technology-independent domain contract, e.g. dataset-identity contract):
  the adapter references **Domain** only.
- **Application Port** (use-case Port: `IWorkspaceRepository`, `IFileOpenService`,
  `IUserSettingsStore`, `IWorkspacePersistence`, external-service ports, use-case adapter contracts):
  the adapter references **Application and Domain**.

```csharp
// SmartAnalysis.Application
public interface IWorkspaceRepository { Task SaveAsync(Workspace w, CancellationToken ct); }
// SmartAnalysis.Infrastructure  (references Application + Domain)
public sealed class WorkspaceRepository : IWorkspaceRepository { /* EF Core/SQLite/files */ }
// SmartAnalysis.App  (composition root)
services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>();
```

### Boundaries that stay
- `Infrastructure` references `Application` **only for Port implementation** — it does **not** own
  Use Cases / orchestration; those stay in Application. Infrastructure depends on the **public Port
  contracts**, not on Application internals.
- Still forbidden: `Application → Infrastructure`, `UI → Infrastructure`, `Analysis → Infrastructure`,
  `Visualization → UI`, `Infrastructure → UI`, `Domain → any other product project`.
- `Infrastructure` references no UI/WPF types. Application Ports expose **no** EF Core/SQLite/WPF/
  TIFF-library/JSON-serializer/external-SDK types and no concrete file-path policy — technical
  shapes are Infrastructure DTOs.
- UI never news-up or calls an Infrastructure implementation directly; only **App** registers
  Infrastructure adapters in DI. **App remains the only composition root.**

## Consequences
- Positive: the inversion is now complete and internally consistent; adapters can implement
  Application Ports; Infrastructure is swappable without touching Application.
- Negative: none material (the reference is one-way; no cycle).
- Follow-up: doc 11, doc 16, F00 spec, ADR-007/009 notes, backlog F02 arch-test rules, Epic #1 and
  TASK-F00 issue #2 updated.

## Compliance
F00's minimal project-reference guard allows `Infrastructure → Application` and forbids
`Application → Infrastructure` / `UI → Infrastructure` / cycles. F02's full type/namespace matrix
asserts: Application types don't depend on Infrastructure; Infrastructure adapters depend only on
allowed Application Ports; UI doesn't depend on Infrastructure; Domain depends on no other product
assembly; only App is a composition root. This (with ADR-007/009) is "must-not-change-alone"
(doc 40 §12).
