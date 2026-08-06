# UI/UX Principles

The new UI is **not** a 1:1 re-skin of the DevExpress/SciChart shell. It is redesigned around
the real AFM analysis workflow. This doc sets the principles; per-feature UX decisions live in
the feature inventory (doc 30) under keep/improve/merge/drop.

## Legacy UX problems to fix (from doc 05)

- **No single "current dataset" source** — the active item is read three different ways
  (`TrayVM.OpenedTiffItem`, via the View, and `DockLayoutManager.ActiveDockItem`). → one
  explicit active-dataset concept.
- **Deep dialog forest** — each operation is a modal dialog with tab-index-coupled types;
  repetitive multi-step work is slow. → inline, non-modal parameter panels where possible.
- **Fragile document identity** by string caption. → stable dataset ids.
- **Processing history invisible/unpersisted.** → history as a first-class, always-visible panel.
- **UX constrained by controls** — ribbon/backstage/docking dictated by DevExpress. → IA driven
  by workflow, not by a control suite.

## Redesign principles

1. **One workspace, one active context.** A single, explicit "current dataset / current
   comparison" model that every panel binds to. No ambiguous active-item resolution.
2. **Datasets and lineage are visible and navigable.** A workspace explorer shows originals and
   derived results as a lineage graph (from provenance, doc 16) — not a throwaway tree.
3. **Operations are discoverable, not buried in modal tabs.** Applicable operations come from the
   registry (`ApplicableTo`, doc 13); parameters entered in a contextual panel with live preview
   and **before/after** built in.
4. **Before/After and multi-result comparison are first-class**, not something the user
   reconstructs manually (legacy overlay/compare is view-only, doc 05).
5. **History & reproducibility surfaced.** Every result shows its provenance step; a workflow can
   be saved, re-run, and applied to another dataset (repeat-work efficiency).
6. **Progress, cancel, and typed errors are always visible** for long operations (doc 13) — no
   silent modal "Processing…" with no cancel.
7. **Novice vs expert.** Sensible defaults + guided flow for routine work; full manual parameter
   control and advanced operations for experts (never remove expert control — see doc 10 personas).
8. **AI assistant integrated, not bolted on.** NL request → *reviewable* proposed workflow →
   approve → execute (doc 14). Manual and AI paths converge on the same operations.
9. **Fewer screen/dialog transitions.** Merge redundant dialogs; prefer contextual panels over
   window hops.
10. **Theme-aware, framework-swappable UI.** WPF first; no control-suite lock-in so the shell can
    evolve (doc 11 framework independence).

## Keep / Improve / Merge / Remove (lens applied per feature in doc 30)

- **Keep (must-have user capability):** open instrument files; 2D/3D/curve/spectrum viewing;
  the validated operations (flatten, roughness, filter, FFT, modulus, matching, PSD, grain…);
  palette/colormap control; export.
- **Improve (redesign flow/UX):** the process-dialog forest → contextual panels; active-dataset
  model; history visibility; before/after & comparison.
- **Merge (unify):** the two 3D stacks (doc 07 M4); overlapping preprocessing dialogs across
  Image/Profile/Spectroscopy that already delegate to the same numeric core (doc 03).
- **Remove (dead/low-value):** ImageTool (empty), Tip Estimation & Reference-Subtraction stubs,
  Watershed grain stub (doc 03/07).
- **AI-simplify:** repetitive multi-step corrections; parameter suggestion; report drafting.
- **Preserve manual control:** all operation parameters remain fully user-editable; region/cursor
  selection stays precise for experts.

## Traced flows to redesign (baselines in doc 05)
- **Image Flatten** (legacy: ribbon → modal `ImageProcessView` → SciChart preview → done) →
  contextual flatten panel with inline before/after on the 2D image + histogram.
- **Spectrum compare** (legacy: `DXTabItem` overlap VM → SciChart series + DevExpress cursor grid)
  → comparison workspace with OSS curve view, cursor/annotation, and a results table.

## Note on this phase
No UI is built now. These principles feed the architecture, feature inventory, and roadmap so the
UI can be implemented per-feature later without re-litigating IA each time.
