# Work-Spec Template

Copy this for each migration task before implementation. It is the hand-off contract to an AI
(or human) implementation session. Fill every field; if unknown, write `UNVERIFIED` or `OPEN`,
never guess. Keep specs in [`specs/`](specs/) named `TASK-<ID>-<slug>.md`.

> An implementation session reads: this spec → the design docs it references → the cited
> `legacy-analysis/*` evidence → the cited legacy source files. It should not need the whole repo.

---

```markdown
# TASK-<ID> — <Feature name>

- **Task ID:** <stable id from backlog, doc 31>
- **Category:** Foundation | Domain | FileFormat | Analysis | Workspace | Persistence |
  Visualization | UI | AI | ML | Testing | MigrationValidation | Documentation
- **Priority / MVP:** P0..P3 / yes|no
- **Status:** tracked in the [migration backlog](31-migration-backlog.md) — the backlog is the
  single source of truth for status (doc 41 §2). Do **not** restate an authoritative status here;
  this spec is the source of truth for **scope/contract** only.

## Purpose
Why this exists (1–3 sentences).

## User-facing behavior
What the user can do when this is done (or "internal — no direct UI").

## Legacy reference (evidence, do not copy blindly)
- Projects / files / classes / methods: `Project/File.cs:line`
- Relevant legacy-analysis doc section(s): e.g. doc 03 §B #1
- Reuse grade of the source code: A|B|C|D|E and what that means here

## Inputs
Data + types (domain types, not UI).

## Outputs
Data + types (DerivedDataset | Artifact | provenance | render input | file | ...).

## Parameters
| name | type | default | range | unit | notes |

## Units
Which quantities/units are involved; conversion expectations.

## Preconditions
What must be true of input/state to run.

## Dependencies
- **Depends on (must exist first):** <task ids>
- **Enables (follow-on):** <task ids>
- **Related / could-merge-with:** <task ids>
- **Parallelizable with:** yes/no + which

## Reuse / rewrite / drop
- Reusable legacy logic (and how to extract it, e.g. drop WPF `Point[]`):
- Must-rewrite parts:
- Merge/remove:
- Forbidden dependencies to strip: DevExpress / SciChart / WPF types in signatures

## Target placement (new architecture)
Which project/layer this lands in (doc 11) + which contracts it implements (doc 13/14/15/16).

## UI/UX direction (if applicable)
Keep/improve/merge/remove decision (doc 17, 30) and the intended interaction.

## Errors & boundary conditions
NaN/Inf, empty, reversed axes, out-of-range, corrupted, unit mismatch — how each is handled
(typed failure/warning, never silent — doc 13, doc 07 M5).

## Performance considerations
Large-buffer handling, buffer ownership, async/cancellation/progress, parallelism.

## Done-when (acceptance)
Concrete, checkable conditions.

## Legacy parity
- **Must match legacy (within tolerance):** which outputs + tolerance
- **Intentionally different from legacy:** what and why
- **Comparison method:** how (frozen golden data from MV00/T01, round-trip, etc.)

## Required test data
Fixtures / golden datasets needed (doc 19).

## Docs to update on completion
Which docs (doc 41 mapping) + ADRs to add.

## Unverified / open questions
Anything the implementer must confirm.
```

## How to use
1. Pull the task row from [`31-migration-backlog.md`](31-migration-backlog.md).
2. Copy the block above into `specs/TASK-<ID>-<slug>.md`.
3. Fill from the cited `legacy-analysis/*` evidence and `target-design/*` decisions.
4. The implementation prompt = "Read `ai-context/40-ai-working-agreement.md`, then implement
   `specs/TASK-<ID>-…md`."
