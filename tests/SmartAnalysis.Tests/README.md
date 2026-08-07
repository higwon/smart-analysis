# SmartAnalysis.Tests

The initial single test project.

- **Target:** `net8.0`.
- **References:** only the **xUnit** test framework (Approved, doc 20). No product `ProjectReference`
  is needed: the guard validates the reference graph by **reading the `.csproj` files**.

## TASK-F00 content
`ArchitectureGuardTests` — the **minimal project-reference Architecture Guard**. It reads every
`.csproj` under `src/` and `tests/` and asserts (ADR-007/009/010):

- exactly the **8** expected projects exist;
- each project references **exactly** its allowed set;
- no **forbidden** edge exists (e.g. `Application → Infrastructure`, `UI → Infrastructure`);
- **only `App`** references `Infrastructure`;
- `Domain` references no other product project;
- the reference graph is **acyclic**.

> The full type/namespace Architecture-Test matrix (e.g. NetArchTest) is **TASK-F02**, not F00.
> No product-feature tests are implemented here.
