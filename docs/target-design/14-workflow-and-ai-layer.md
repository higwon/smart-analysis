# Workflow Engine & AI Layer

The AI never calls analysis functions ad-hoc. It proposes a **Workflow** — a validated,
serializable graph of registered operations — which the user reviews and the engine executes
through the same contract (doc 13) as manual use. This keeps AI output auditable and prevents
the AI from bypassing the validated numeric engine.

## Workflow model

```csharp
public sealed record Workflow(
    string Id, int Version, string Name,
    IReadOnlyList<WorkflowStep> Steps,           // ordered / DAG
    WorkflowInputBinding Input,
    ProvenanceOrigin Origin);                    // Manual | AiProposed | Template

public sealed record WorkflowStep(
    string StepId,
    string OperationId,                          // must exist in IOperationRegistry
    IParameterSet Parameters,                    // validated against the op's schema
    IReadOnlyList<StepInputRef> Inputs,          // wire outputs → inputs (typed)
    StepCondition? Condition,                    // optional branch
    ApprovalState Approval);                     // AiProposed vs UserApproved (per step)
```

Engine supports (requirements from the brief §18): input data binding, ordered/conditional
steps, typed step-to-step wiring, type validation before run, failure handling, warnings,
cancellation, progress, intermediate results, result caching (by op id+version+params+input
hash), re-run, serialization, versioning, user-edit history, and a clear
**AI-proposed vs user-approved** flag on every step.

### Execution
- Validate the whole workflow against the registry (op exists, params in schema, types wire) —
  **before** running anything.
- Run steps honoring cancellation/progress; each step emits a `ProvenanceStep` (doc 16).
- Cache intermediate results by content key for cheap re-run/compare.
- A step failure surfaces as a typed error; the engine stops or branches per policy, never
  silently continues.

## AI layer responsibilities

**Allowed:**
- Interpret a natural-language analysis request.
- **Search** the operation registry (`Summary`/`Tags`, doc 13) for suitable operations.
- Draft a `Workflow` (`Origin = AiProposed`, every step `Approval = AiProposed`).
- Propose parameter candidates (within each op's declared ranges/units).
- Explain preconditions/risks **before** execution.
- Compare multiple run results; summarize; draft a report.
- Give user-understandable rationale.

**Forbidden (hard guardrails):**
- ❌ Produce numeric results without running the real engine on real data.
- ❌ Compute anything bypassing a registered, validated operation.
- ❌ Invent channels, units, or material identifications.
- ❌ Claim an analysis ran when it did not; hide warnings/errors; omit provenance.
- ❌ Execute unregistered code, or mutate original data without explicit user approval.

## The validation boundary (how guardrails are enforced)

```
NL request
  → AI proposes structured Workflow JSON  (LLM output)
  → Schema validation (JSON schema of Workflow + per-op ParameterSchema)
  → Registry validation (every OperationId exists; params in range/units; inputs type-check)
  → User review & approval (per step; AiProposed → UserApproved)
  → Workflow engine executes via IAnalysisOperation
  → Provenance records Origin=AiProposed + who approved
```

AI output is **data (a proposal), not commands**. It is only ever executed after schema +
registry validation **and** explicit user approval. An AI proposal that references a
non-existent operation or an out-of-range/units-mismatched parameter is rejected at validation,
not "best-efforted".

## Provenance of AI involvement

Every step records: `Origin` (Manual/AiProposed/Template), `Approval` (who approved, when),
and — if ML was used — the model id + version (doc 16 record). This satisfies the brief's
requirement to distinguish AI-suggested vs user-approved and to trace ML model/version.

## Relationship to ML (doc 18)

ML models (e.g. artifact detection) are exposed to the workflow **as operations** implementing
the same contract, with `IsDeterministic=false` and a recorded model version. The AI *assistant*
(LLM) orchestrates workflows; ML *models* are just non-deterministic operations. They are
different concerns and must not be conflated.

## OPEN decisions
- LLM provider/SDK and hosting (local vs cloud) — record as ADR; keep behind an `IAssistant`
  interface so it is swappable and testable with a fake.
- Workflow storage location: inside the workspace file vs a separate library (doc 16).
- Whether workflows are pure DAGs or allow loops (start: linear + simple conditionals).
