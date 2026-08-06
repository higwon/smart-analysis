<!--
One PR completes one Task Issue. Create as DRAFT. The AI never merges — the user does.
After opening the PR, STOP: do not start the next task until the user reviews and merges this PR.
See docs/ai-context/42-github-delivery-workflow.md §8.
-->

Closes #<!-- task issue number -->

## Task
<!-- TASK-<ID> — <title> -->

## Parent Epic
<!-- EPIC-<ID> (#<issue>) -->

## Summary
<!-- What this PR delivers, in a few sentences. -->

## Implemented Scope
<!-- What was implemented (must match the Task Spec scope). -->

## Out of Scope
<!-- What was deliberately not done here (link follow-up Issue candidates). -->

## Tests
<!-- Tests added/run and their results. -->

## Numeric Parity
<!-- vs legacy golden (MV00/T01): what was compared, tolerance, pass/fail + numbers. N/A if none. -->

## Architecture Validation
<!-- Dependency-direction / arch test result (Domain/Analysis reference no UI/viz/commercial types). -->

## Documentation Updated
<!-- Which docs were updated (doc 41 mapping): backlog status → review, INDEX, design docs, specs. -->

## ADRs
<!-- ADRs added/changed, or "None". OPEN/Candidate decisions must be resolved via ADR + user review. -->

## Deviations from Legacy
<!-- Intentional numeric/behavior differences + the ADR that records them, or "None". -->

## Open Items
<!-- Anything left open / unverified for the reviewer. -->

## Reviewer Focus
<!-- Where the user should concentrate review. -->

## Completion Report
<!-- Link the AI Completion Report (doc 41 §5) — do not duplicate it here. -->

## Completion Checklist
- [ ] Only this task's scope implemented (no scope creep)
- [ ] Acceptance criteria met (per the Task Spec)
- [ ] Tests run; numeric parity verified where applicable
- [ ] Architecture test passed
- [ ] Required docs updated; backlog status set to `review`
- [ ] Needed ADR(s) written; no OPEN/Candidate decision finalized ad-hoc
- [ ] No commercial libs; no Candidate dependency installed without an ADR
- [ ] Legacy repo untouched
- [ ] Created as **Draft**; **not** merged by AI
- [ ] Stopping here for user review — next task not started
