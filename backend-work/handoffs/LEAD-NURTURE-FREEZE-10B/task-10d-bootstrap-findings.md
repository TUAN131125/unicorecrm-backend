# Task 10D — repository governance bootstrap findings

Status: **BLOCKED — IMMUTABLE_SUBJECT_REBIND_REQUIRED**. No file outside `backend/backend-work/` was
created or modified. Lead subject `ff5047f997854935ce7fe2271dcc893ca6cce987` /
`f81f8b9e7684cbc51954a2456a2bbbebb1f78170` re-verified unchanged.

## 1. The blocking proof

`bootstrap_repository.py` is the canonical bootstrap. It creates the `backend-work/*` runtime
directories and copies 13 manifests into **`<repo>/architecture/`**, which is the adopted-governance
extensibility point `_gate_lib.adopted_gate_registry` and `validate_task.adopted_manifest` read.
There is no second extensibility point: both resolve that path literally.

Three control-plane facts collide:

1. `_gate_lib.adopted_gate_registry(repo_root)` reads `repo_root/architecture/gate-registry.json`,
   so the file must exist **in HEAD's tree** for the gates to be redefinable.
2. `_subject.working_tree_clean` whitelists only `backend-work/`, so `architecture/` cannot stay
   uncommitted.
3. `protected_write_patterns()` contains `architecture/**`, and `validate_diff_scope` raises
   `GLOBAL_PROTECTED_PATH` for any file matching it inside `baseCommit...HEAD`.
   `hardening-policy.json machineManagedRuntimePaths` does not exempt it.

Therefore `architecture/` must be present in HEAD **and** absent from `d3b1c117...HEAD`. The only
commit satisfying both is one at or before `d3b1c117` — the published pre-Lead base. Putting it
there rewrites `d3b1c117` and rebases `ff5047f`, producing a new functional Lead SHA.

**Verified empirically, not reasoned.** A throwaway clone of the backend at `ff5047f` was
bootstrapped with `bootstrap_repository.py --apply`, the 13 manifests were committed on top, and the
Lead packet was validated against it:

```
validate_diff_scope … --repo-root <clone>   exit 4
  GLOBAL_PROTECTED_PATH :: architecture/architecture-rules.json
  GLOBAL_PROTECTED_PATH :: architecture/canonical-owners.json
  GLOBAL_PROTECTED_PATH :: architecture/context-budget.json
  … (13 manifests)
```

The clone was deleted afterwards. The same collision applies to `tests/UnicoreCRM.*Tests` for a
different reason: those paths are not hard-denied, but adding them still requires a commit, and any
commit moves `HEAD` off `ff5047f`, breaking `subject.commitSha` for every recorded evidence row.

**Generalised:** the control system binds the review subject to `HEAD` and requires adopted
governance to pre-date the implementation it gates. Bootstrapping a repository *after* the
implementation commit is not expressible. Preserving `ff5047f` and bootstrapping are mutually
exclusive.

## 2. Factual architecture model (read-only inventory)

The brief's list of six modules is incomplete. `src/` contains **13 projects**:

| Project | References |
|---|---|
| UnicoreCRM.BuildingBlocks | — |
| UnicoreCRM.Platform | BuildingBlocks |
| UnicoreCRM.PlatformOperations | BuildingBlocks |
| UnicoreCRM.Fulfillment | BuildingBlocks |
| UnicoreCRM.Sales | BuildingBlocks, Platform |
| UnicoreCRM.Operations | BuildingBlocks, Platform |
| UnicoreCRM.Billing | BuildingBlocks, Platform |
| UnicoreCRM.CommercialEvidence | BuildingBlocks, Platform |
| UnicoreCRM.Crm | BuildingBlocks, Platform, **Sales** |
| UnicoreCRM.Workflows | BuildingBlocks, Platform, **Crm**, **Operations** |
| UnicoreCRM.Integrations | BuildingBlocks, Platform, PlatformOperations, **Crm** |
| UnicoreCRM.AI | BuildingBlocks, Platform, **Crm**, **Operations** |
| UnicoreCRM.ApiHost | all business modules |

**An owner is not an assembly.** `UnicoreCRM.Crm` hosts the `leads`, `contacts`, `customers`,
`deals` and `organizations` owners; `UnicoreCRM.Operations` hosts `tasks` and `support`;
`UnicoreCRM.Sales` hosts `products`, `quotes` and `orders`.

19 owner DbContexts follow one consistent convention:

```
UnicoreCRM.<Assembly>.<Owner>.Infrastructure.Persistence.<Owner>DbContext
```

e.g. `UnicoreCRM.Crm.Leads.Infrastructure.Persistence.LeadsDbContext`,
`UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.TasksDbContext`,
`UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.WorkflowsDbContext`.
Layers are folders inside each owner: `Domain`, `Application`, `Contracts`, `Infrastructure`.

**Consequence for the architecture gate:** a project-reference gate cannot express the required
boundaries. `Crm → Sales` already grants all of Crm sight of all of Sales at assembly level, and
`leads → contacts` is intra-assembly. Every ARCH-004/005/006/007 rule must be asserted at
**namespace** granularity (`Types.InAssembly(crm).That().ResideInNamespace("UnicoreCRM.Crm.Leads")
.ShouldNot().HaveDependencyOn("UnicoreCRM.Sales.Products.Infrastructure")`), which the shipped
starter tests do not do — they call `Assembly.Load("UnicoreCRM.Domain")`.

## 3. What is stale versus what is correct

| Artifact | Verdict |
|---|---|
| `architecture-rules.json` ARCH-001…012 | **Correct and layout-independent.** Keep verbatim. |
| `module-map.json` `physicalTopology`, `namespacePattern`, per-module namespaces | **Stale.** Declares `UnicoreCRM.Domain/.Application/.Infrastructure/.Api` and `UnicoreCRM.<Layer>.<Area>.<Owner>`; reality is `UnicoreCRM.<Assembly>.<Owner>.<Layer>`. Does not currently block (`status: ACTIVE_MAPPING` satisfies `validate_task`) but would make any namespace-driven gate wrong. |
| `gate-registry.json` architecture/contract/integration argv | **Stale for this repo.** Names `tests/UnicoreCRM.*Tests`, never created. |
| `wire_architecture_tests.py` + `architecture-tests/*.cs` | **Stale for this repo.** Generated csproj references four non-existent projects. |
| `hardening-policy.json machineManagedRuntimePaths` | **Incomplete.** Omits `backend-work/handoffs/**` and `backend-work/diagnostics/**` although `transition_task.py` requires `handoffs/`. |
| `dependency-allowlist.json`, `persistence-ownership.json` | **Empty/unassigned by design**, awaiting Governor/Architect. |
| `validate_pack.py` | **PASS** — the control package itself is intact. |

## 4. Required bootstrap content (specification, not installed)

For whoever performs the bootstrap after the ordering decision:

- **Persistence ownership**, one row per durable owner, e.g.
  `leads` → `LeadsDbContext`, schema `leads`,
  migrationRoot `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations`.
  Repeat for all 19 contexts; do not add a Lead-only row.
- **Cross-owner edges**, minimal and namespace-exact:

  | consumer | provider | allowed target | authority |
  |---|---|---|---|
  | `UnicoreCRM.Workflows.Atomic.Application` | leads | `UnicoreCRM.Crm.Leads.Contracts` | lead-contact-qualification-authority §10.2 |
  | `UnicoreCRM.Workflows.Atomic.Application` | contacts | `UnicoreCRM.Crm.Contacts.Contracts` | §10.2 |
  | `UnicoreCRM.Workflows.Atomic.Application` | tasks | `UnicoreCRM.Operations.Tasks.Contracts` | §10.2 |
  | `UnicoreCRM.Crm.Leads.Application` | products | `UnicoreCRM.Sales.Products.Contracts` | products-lead-snapshot-authority |

  Each with `persistenceCrossingAllowed: false`, `dbContextCrossingAllowed: false`,
  `publicHttpRequired: false`. Do not admit `Workflows → Crm` or `Crm → Sales` at assembly level.
- **Architecture gate**: a real NetArchTest project asserting ARCH-004/005/006/007 at namespace
  granularity for all 19 owners, driven by the registry rather than hardcoded per module.
- **Contract gate**: `npm run api:check` against the pinned live contract
  `d98462853a5c529ce1695978d35541a8bc000dc25b2781a62fd8bf5e91cd6a57`. Note `run_gate.py` executes
  argv with `cwd = repo_root` and offers no working-directory field, so either the argv uses
  `npm --prefix ../frontend/unicorecrm-web run api:check` or the control plane gains a `cwd` field.
- **integration / negative / two-workspace**: the repository's real mechanism is the maintained
  `scripts/verify-*.ps1` harnesses, which are per-suite and database-parameterised. They need a
  stable, category-separated command surface before they can be gates; `run_and_record_evidence.py`
  is disabled precisely to stop ad-hoc commands becoming evidence.
- **`machineManagedRuntimePaths`** += `backend-work/handoffs/**`, `backend-work/diagnostics/**`, so
  governance-generated files are legal without widening product write scope. This supersedes the
  `backend-work/**` entry Task 10C added to the packet's `allowedWritePaths` as a local workaround.

## 5. The decision required

Bootstrapping requires a commit; the control system binds the subject to `HEAD`; therefore one of:

- **(A)** Authorise rebasing `ff5047f` onto a governance-bootstrap commit. The Lead *code* is
  unchanged but the Lead *SHA* changes, all evidence is re-recorded against the new subject, and
  Task 10B's subject binding is superseded (not erased).
- **(B)** Freeze Lead under the shipped gate registry, which requires creating
  `tests/UnicoreCRM.ArchitectureTests`, `…ContractTests`, `…IntegrationTests` as real projects —
  still a commit, so still a rebind, and it accepts the layered-layout assumptions.
- **(C)** Amend the control plane so adopted governance may post-date the implementation subject —
  e.g. exempt `architecture/**` from `validate_diff_scope` when it is unchanged relative to the
  bootstrap, or bind the subject to a named ref rather than `HEAD`.

(A) is the smallest change to the repository; (C) is the smallest change to the control plane.
Both are control-plane owner decisions. Task 10D stops here rather than choosing.
