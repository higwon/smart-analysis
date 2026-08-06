# GitHub Delivery Workflow (the delivery contract)

How implementation actually happens on GitHub. This is **not advisory** — it is the working
contract every AI implementation session and the reviewing user follow. It sits between the
migration plan (docs) and the code.

> **The one rule that governs everything below:**
>
> ```
> After opening the pull request, stop.
> Do not start or implement the next task until the user reviews and merges the current pull request.
> ```

## 1. The delivery loop

```
Migration Backlog (plan)
→ GitHub Epic (parent issue)
→ Task Issue (one TASK-*)
→ Task Branch (isolated)
→ AI implements ONLY that task + tests + doc updates
→ Commit / Push
→ Draft Pull Request  ──►  AI STOPS
→ User review
→ (changes requested → fixed on the SAME branch/PR → AI stops again)
→ User merges
→ Post-merge doc/status updates
→ User approves the next task → new session
```

Nothing in this loop lets the AI advance itself. The user gates every step from "Draft PR" to
"next task".

## 2. Source-of-Truth map

Each artifact owns one thing. Do not duplicate ownership (e.g. do not paste a whole Task Spec into
an Issue).

| Artifact | Is the source of truth for | Is NOT |
|---|---|---|
| **Migration Backlog** (`docs/migration/31-migration-backlog.md`) | the full task list, dependencies, priority, MVP flag, and **task status** | per-task technical detail |
| **Task Spec** (`docs/migration/specs/TASK-*.md`) | a task's technical **scope, acceptance criteria, validation contract** | status; live execution |
| **GitHub Epic** (parent issue) | a group of tasks' **goal & aggregate progress** | technical scope |
| **GitHub Task Issue** | **live execution**: work start, discussion, blockers, run state | the spec's content (it *links* the spec) |
| **Git Branch** | the **isolated workspace** for one task's implementation | anything shared across tasks |
| **Pull Request** | the **implementation result, test results, and user review** of one task | the plan |
| **ADR** (`docs/ai-context/adr/`) | **architecture/technical decisions** | task tracking |

**Do not copy a Task Spec's body into its Issue.** The Issue **links** the spec + design docs and
focuses on execution/state. If the spec and an Issue disagree, the **spec wins on scope**, the
**backlog wins on status**; fix the mismatch before implementing.

## 3. Core operating principles (mandatory)

- One `TASK-*` ↔ one GitHub Issue (as a rule).
- One Branch implements exactly one Task.
- One Pull Request completes exactly one Task Issue (as a rule).
- PRs are created as **Draft**.
- **AI never merges.** Final merge authority and decision are the user's.
- **Do not implement the next Task until the current PR is merged.**
- Review fixes happen on the **same** Branch/PR.
- Work that materially exceeds the current scope becomes a **new Issue candidate**, not scope creep.
- If a Task's scope changes, update the **Spec and Issue before** implementing.
- OPEN decisions are **not** finalized by the implementing AI — write an ADR and wait for review.
- Candidate dependencies (doc 20) are **not** installed before their deciding ADR.
- The legacy SmartAnalysis repo is **read-only reference**.
- **After opening the PR, stop and wait for user review.**

## 4. Epics (GitHub parent issues)

An Epic is a GitHub **parent issue** (no external Epic tool required). First Epic:

```
EPIC-MVP01 — Image Analysis Vertical Slice
```

**Goal:** open TIFF → domain dataset → workspace → 2D display → run Flatten → compare to legacy
golden → provenance → save & reopen workspace → restore lineage → Before/After UI.

Epic body must include: Epic ID · Goal · User Value · Included Scope · Excluded Scope · Related
Roadmap docs · MVP Checkpoints · Included Tasks · Task Dependencies · Current Progress · Completion
Criteria · Key OPEN Decisions · Current Blockers. (Template: `.github/ISSUE_TEMPLATE/epic.yml`.)

**Candidate tasks for EPIC-MVP01** (confirm IDs/scope against the current backlog, doc 31 — the
backlog is authoritative):
`TASK-F00, F01, F02, F03, D01, F04, F05, MV00, T01, FF01, W01, V00, V01, V02, A01, A02, T02, P01,
UX01, UIX01, UIX02, UIX03, U01, U02`.

The full product beyond the Image MVP is organized as **vertical-slice Epics** (Image / Profile /
Spectroscopy / PiFM / AI) in `docs/migration/35-product-epics-roadmap.md`:
`EPIC-MVP01, EPIC-UIX01, EPIC-IMAGE02, EPIC-PROFILE01, EPIC-SPEC01, EPIC-SPEC02, EPIC-PIFM01,
EPIC-PIFM02, EPIC-AI01`. The 4 MVP checkpoints (doc 32) map to Milestones M1–M4 under EPIC-MVP01.
Note: **U01/U02 are gated by the UIX02 visual-design user approval** (ADR-008) — an Issue for U01
stays `status:blocked` until that approval.

**Do not create all Issues at once.** Issue-creation policy:
- The full plan lives in the **Migration Backlog**.
- Create a GitHub Issue only for a task that is **now startable or imminent**.
- Later Issues whose predecessors haven't merged: create **when needed**.
- Design-uncertain tasks: **hold** Issue creation until the design settles.
- The Epic shows the **full roadmap link + the currently-created Issues**.

## 5. Task Issues

One `TASK-*` → one Issue. **Title:** `[TASK-<ID>] <imperative summary>`, e.g.
`[TASK-F00] Bootstrap repository and solution`, `[TASK-A01] Implement flatten operation`.

Issue body (template `.github/ISSUE_TEMPLATE/task.yml`) — **links**, not copies:
Task ID · Parent Epic · Purpose · **Source Task Spec (path/link)** · Required Reading · Scope ·
Out of Scope · Dependencies · Blocked By · Acceptance Criteria · Validation · Legacy Parity ·
Required Documentation Updates · ADR Requirement · Open Decisions · Reviewer Focus.

Rules:
- **No Spec → no implementation.** An Issue without a Source Task Spec is not startable.
- Predecessor Issue not merged → the Issue is `status:blocked`.
- On creation, verify the Task ID **matches the backlog**.
- Always record the **Source Spec path** and link the needed legacy-evidence docs.
- Acceptance Criteria must **not** contradict the Task Spec.
- Do not allow implementation larger than the task scope.
- Newly-discovered work is recorded as a **separate Issue candidate**, not piled into this Issue.

## 6. Branch naming

One branch per Issue. Format: `<type>/task-<id>-<slug>`.

```
chore/task-f00-solution-bootstrap
feat/task-f01-units-axes-buffers
feat/task-f03-domain-dataset
feat/task-ff01-tiff-reader
feat/task-a01-flatten-operation
docs/task-ux01-information-architecture
test/task-mv00-legacy-baseline
spike/task-v00-rendering
```

Prefixes: `feat/ fix/ chore/ docs/ test/ refactor/ spike/`. Never implement multiple tasks on one
branch. The branch must be traceable to its Issue + Task ID.

## 7. Commits

**Conventional Commits (English).** Examples:
```
chore: bootstrap smart analysis solution
feat: add physical unit foundation
feat(domain): add physical axis model
test(flatten): add legacy parity fixtures
docs(adr): decide buffer ownership strategy
```
Split by logical change; avoid trivially-small commits. **Never commit** generated/temp files or
build output (keep `.gitignore` current).

## 8. Pull Requests

One PR completes one Task Issue. **Title** mirrors the task, e.g.
`feat: implement TASK-A01 flatten operation`.

PR body (template `.github/pull_request_template.md`) starts with `Closes #<issue>` and includes:
Task · Parent Epic · Summary · Implemented Scope · Out of Scope · Tests · Numeric Parity ·
Architecture Validation · Documentation Updated · ADRs · Deviations from Legacy · Open Items ·
Reviewer Focus · Completion Checklist. It **links** the AI Completion Report (doc 41 §5) rather than
duplicating it.

- PRs are created **Draft**.
- Move Draft → **Ready for Review** only when: acceptance criteria met · tests run · numeric parity
  verified (where applicable) · architecture test passed · docs updated · needed ADR written · no
  out-of-scope code · Completion Report written.
- AI may **report** readiness but **never merges**.

## 9. User review & change handling

User picks one of: **Approve and merge · Request changes · Close without merge · Split scope ·
Create follow-up issue**.

Change handling:
- Small fixes and anything needed to meet the current acceptance criteria → **same Branch/PR**.
- Requests materially beyond the current task scope → **separate Issue**.
- If scope must be split → update the existing Issue **and Spec**.
- Design-decision changes → check whether an **ADR** is required.
- After applying review: re-run tests, re-update docs, then **request review again and stop.**

## 10. After merge (user-gated)

Only **after the user merges** do these updates happen (in the merge PR or a tiny follow-up):
- Close the Task Issue.
- Set the backlog Task status to **`done`**.
- Reflect the implementation in the Task Spec + affected docs.
- Update `INDEX.md` "Current status".
- Update the relevant Target Design doc(s).
- Update ADR statuses.
- Update Epic progress.
- Identify newly-startable tasks.

**Never mark a task `done` before its PR is merged.** Even if the next task is dependency-unblocked,
the AI does **not** auto-start it — the user designates/approves the next task in a new session.

## 11. Labels (minimal set — document now, create later)

Keep it small. Names + meaning:

**Area:** `area:foundation area:domain area:file-format area:analysis area:workspace
area:persistence area:visualization area:ui area:ux area:ai area:testing area:documentation`

**Status:** `status:planned status:ready status:in-progress status:blocked status:review`
(map to the status flow in doc 41; `done` = Issue closed after merge).

**Priority:** `priority:p0 priority:p1 priority:p2 priority:p3`

**Special:** `mvp needs-adr needs-legacy-access numeric-parity design-only spike`

Actual label creation is **not** done in this phase.

## 12. GitHub Project (optional — document, don't automate now)

If used, recommended fields: `Status · Task ID · Priority · Area · MVP · Parent Epic · Depends On ·
Spec · Issue · Pull Request`. Recommended statuses: `Planned · Ready · In Progress · In Review ·
Blocked · Done`.

Do not duplicate the backlog's role:
- **Migration Backlog** = long-term plan + technical dependencies (source of truth).
- **GitHub Project** = visual progress of the Issues/PRs currently in flight.

No Project automation is built in this phase.

## 13. Ready-to-use session prompts

### 13.1 Create the first Epic
```
Read docs/ai-context/42-github-delivery-workflow.md and docs/migration/31-migration-backlog.md.
Create ONE GitHub parent issue "EPIC-MVP01 — Image Analysis Vertical Slice" using the Epic
template. Fill it from the backlog + docs/migration/32-dependency-roadmap.md (goal, user value,
included/excluded scope, related roadmap, MVP checkpoints, the EPIC-MVP01 candidate task list,
task dependencies, completion criteria, key OPEN decisions). Do NOT create the individual Task
Issues yet, do NOT create branches, and do NOT write any product code. Report the Epic issue number.
```

### 13.2 Create the TASK-F00 Issue
```
Read docs/ai-context/42-github-delivery-workflow.md. Create ONE GitHub issue
"[TASK-F00] Bootstrap repository and solution" using the Task template, under EPIC-MVP01.
Link the source spec docs/migration/specs/TASK-F00-repository-solution-bootstrap.md and required
reading; fill scope/out-of-scope/acceptance/validation/doc-updates by LINKING the spec (do not copy
it). Verify the Task ID matches the backlog. Do NOT create a branch or any code yet. Report the
issue number.
```

### 13.3 Implement TASK-F00 and open a Draft PR
```
Read docs/ai-context/40-ai-working-agreement.md first.

Confirm the EPIC-MVP01 and the [TASK-F00] issue. Create the branch
chore/task-f00-solution-bootstrap and implement ONLY TASK-F00 per
docs/migration/specs/TASK-F00-repository-solution-bootstrap.md.

Do NOT start TASK-F01 or any other task.
Do NOT add commercial libraries.
Do NOT implement domain types, analysis algorithms, file parsers, or UI features.
Do NOT install any Candidate dependency.
Do NOT finalize any OPEN decision on your own.

When done: run the build/tests, update the required docs, set the backlog status to `review`,
commit, push, and open a DRAFT pull request that Closes the TASK-F00 issue, with the PR template
filled and the Completion Report linked.

After opening the pull request, stop.
Do not start, create a branch for, or implement the next task until the user reviews and merges
this pull request.
```

## 14. Where this connects
- Procedure for a session: doc 40 (AI Working Agreement) §§0, 15.
- Status flow + post-merge sync: doc 41 (Documentation Maintenance) §§2, 6.
- Per-task GitHub linkage fields: doc 33 (Work Spec Template).
- Templates: `.github/ISSUE_TEMPLATE/epic.yml`, `.github/ISSUE_TEMPLATE/task.yml`,
  `.github/pull_request_template.md`.
