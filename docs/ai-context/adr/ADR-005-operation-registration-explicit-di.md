# ADR-005 — Operation registration via explicit per-module DI

- **Status:** accepted
- **Date:** 2026-08-06
- **Deciders:** project owner
- **Related:** ADR-003 (operation contract + registry — this refines its *registration mechanism*),
  doc 13, F04 spec, doc 40 (working agreement)

## Context
ADR-003 established the operation contract + registry and the goal "no central enum/switch". But
"self-register" was under-specified — an implementer could interpret it as reflection assembly scan,
attribute scan, static-constructor side effects, a source generator, or a central manual list.
Removing the central switch must not introduce a *different* implicit/global mechanism (hidden
reflection, global static registration) that is equally hard for an AI-in-limited-context to reason
about.

## Options considered
1. **Reflection / attribute assembly scan** — automatic, but "magic": discovery is implicit,
   ordering/failure is opaque, and it hides what's registered. ✗
2. **Static-constructor registration** — global side effects, order-dependent, hard to test. ✗
3. **Central manual list** — re-creates a single choke point that grows with every op. ✗
4. **Explicit per-module DI registration** — each analysis module exposes an
   `AddXxxAnalysis(IServiceCollection)` that registers its operations; the composition root calls
   each module explicitly; the registry queries what was registered. ✓

## Decision
Use **explicit per-module DI registration**:

```csharp
public static class ImagingAnalysisModule {
    public static IServiceCollection AddImagingAnalysis(this IServiceCollection services) {
        services.AddAnalysisOperation<FlattenOperation>();
        services.AddAnalysisOperation<RoughnessOperation>();
        return services;
    }
}
```

Principles: **no central enum · no central switch · no operation-id branching · no magic reflection
auto-discovery · module-based explicit registration · duplicate operation-id validation at
registration · no execution of an unregistered operation.** Adding an operation edits only its
module's `Add*()`.

## Consequences
- Positive: registration is explicit and greppable; modular; testable; no hidden global state; an
  AI session sees exactly one module file to touch when adding an op.
- Negative: a new module must be wired into the composition root once (explicit, by design).
- Follow-up: F04 implements `AddAnalysisOperation<T>` + the registry; each analysis module (imaging,
  spectroscopy, pifm, profile) gets its own `Add*()`.

## Compliance
A test asserts: adding an operation requires no central switch/enum edit; duplicate ids are
rejected; executing an unregistered id fails. This mechanism is "must-not-change-alone" (doc 40 §12).
