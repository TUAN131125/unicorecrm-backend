# Connected Dashboard hydration verification

Result: **PASS for supported live reads; BLOCKED_CONTRACT for period revenue, period Lead counts
and the ranked Lead queue. Full Dashboard KPI completion is not claimed.**

Verified locally on 2026-10-23 (Asia/Ho_Chi_Minh), using the existing SQL Server
`DESKTOP-ICH225R / UnicoreCRM_Development`. No connection setting, Workspace configuration,
membership, permission or CRM record was changed. No seeder or migration was invoked.
Authentication sessions/read audits are normal effects of the acceptance reads.

Baseline Backend commit: `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`.
Baseline Frontend commit: `814cc4685b6d8f8ead2fc945f4607a2d0168320d`.
Both checkouts already contained unrelated uncommitted work. These changes remain uncommitted
and unmerged. Existing AI, Studio, currency and seed changes are outside this Dashboard change.

## Root cause and fix

`useDashboardReadModel` observed module repository snapshots without initiating authoritative
reads. Fresh login left these projections empty, which the Dashboard reduced to zero. A list
visit could populate a partial page, making the apparent total depend on navigation history.

Connected Dashboard now owns fresh read resources. Existing owner queries provide currency
buckets and scoped server counts; small server pages provide the visible queues. Workspace,
session, member and access revision identify the resource. Scope changes cancel and discard
old reads. Loading, forbidden, failed, unsupported and verified empty are distinct states.
The old snapshot model is explicitly limited to Demo Runtime.

There is no new endpoint, permission, OpenAPI declaration or cross-module SQL in product code.
No localStorage, demo arrays, partial collection cache or page sweep supplies a Connected KPI.
See the [source matrix and minimal owner contract proposals](connected-dashboard-design.md).

Each load issues at most 22 bounded reads: forecast, closing Deals, four Task queries,
15 Support queries and recent Activity. Individual collection windows have limits of 1, 5 or 6,
and no cursor is followed. Support needs multiple requests because its current filter accepts
one status/priority. Existing Deals handlers still materialize scoped Deals internally; this
change does not claim to optimize that pre-existing server implementation.

## Before / after evidence

Workspace: `unicore-demo-anh-duong`, `ws_f2332a12d56a421d8a722bd2f65c5efb`.
Control: `unicore-demo-anh-duong-control`, `ws_811689cfc4b446eab4f29dc109e2abf2`.
Use the existing demo account, select the demo Workspace, and open CRM > Dashboard directly.
The acceptance harness uses a separate Chromium context and its own Vite server on port 3039.

| Metric | Before fresh login | After fresh login | SQL / Backend evidence | Status |
|---|---|---|---|---|
| Open pipeline | 0; no forecast request | 124,445,000,000 VND / 165 open Deals | Existing forecast endpoint 200; SQL amount and count match | PASS |
| My overdue OPEN Tasks | 0 | 48 | Tasks totalCount 200 and scoped SQL count match | PASS |
| My Tasks due today | 0 | 6 | UI server-count difference matches SQL with Vietnam midnight boundaries | PASS |
| Open Support Cases | 0 | 15 | SQL matches sum of five scoped status counts, each HTTP 200 | PASS |
| High/critical open Support | 0 | 5 | UI server counts match SQL priority/status predicates | PASS |
| Period completed revenue | Unhydrated cache interpreted as 0 | Unavailable / dash | No admitted completion-period/currency sum | BLOCKED_CONTRACT |
| Period new Leads / score >=80 | Unhydrated cache interpreted as 0 | Unavailable / dash | No admitted creation-period aggregate | BLOCKED_CONTRACT |
| Ranked priority Lead queue | Empty cache | Unavailable message | No admitted global score ordering | BLOCKED_CONTRACT |

The 1,050 demo CRM rows remain intact. Counts and SHA-256 hashes across all nine CRM tables
and **all existing Workspace IDs** match before/after. This includes the original non-demo data.
Hash verification covers persisted rows, not just record counts.

Evidence:

- [Before screenshot](evidence/connected-dashboard/dashboard-before.png), [after screenshot](evidence/connected-dashboard/dashboard-after.png).
- [SQL expectations](evidence/connected-dashboard/expected.json), [actual API summaries](evidence/connected-dashboard/after-api-summary.json).
- [Before browser trace](evidence/connected-dashboard/before-browser.json), [after browser checks and sanitized HTTP metadata](evidence/connected-dashboard/after-browser.json).
- [Before data preservation](evidence/connected-dashboard/before-data-preservation.json), [after data preservation](evidence/connected-dashboard/after-data-preservation.json).
- [Quality results](evidence/connected-dashboard/quality-results.json), [full verify failure](evidence/connected-dashboard/verify-output.txt).

## Acceptance results

| Check | Result | Evidence / limit |
|---|---|---|
| Reproduce original fresh-login bug against actual SQL/API | PASS | Baseline browser shows zero and makes no forecast read |
| Fresh login directly to Dashboard | PASS | Three supported KPIs and both subcounts match SQL; no module visit first |
| Browser refresh | PASS | Authoritative results reload |
| Switch to control Workspace and return | PASS | Control pipeline/support zero; demo values return correctly |
| Visit Contacts, return to Dashboard | PASS | New Backend forecast read; no dependency on Contact projection |
| Delayed real Backend response during Workspace switch | PASS | Retired demo response cannot replace active control metrics |
| Forecast transport failure and retry | PASS | Error state, never fake zero; Support survives; refresh recovers |
| Bounded reads | PASS | No cursor sweep; collection limits <=6; no Orders/Quotes/Customers hydration |
| My work / closing Deals / recent Activity / Support sample | PASS | Fresh bounded owner pages visible in isolated browser; Support marked as a sample |
| SQL CRM data preservation | PASS | All Workspace row counts and hashes identical before/after |
| Denied-capability handling | PASS (unit) | Denied sections return FORBIDDEN with no data/query fallback |
| Real restricted-role / FLS mutation scenario | NOT_RUN | Existing membership/permissions were preserved; no synthetic authorization response |
| Period revenue, period Lead totals, priority ranking | BLOCKED_CONTRACT | Owner contract proposals documented; no fabricated metric |
| External delivery / AI provider calls | NOT_RUN | Not required for a read-only Dashboard fix; test host background delivery disabled |
| Independent code/evidence review | PASS (informal) | No substantive finding; not a formally attested REVIEW_PASS |

The final isolated browser run passed in 18.9 seconds (test body 16.8 seconds).
The baseline run and all after runs also passed their CRM data-preservation checks.

## Quality and remaining failures

PASS: production build and bundle budget; presentation responsibility; Dashboard runtime
(including the new deterministic count/timezone/permission tests); read models; metric catalog;
guidance; strict core; strict type regions; API contract; API management boundaries;
frontend/backend separation; regenerated repository inventory and its checker.

FAIL outside Dashboard scope:

- `npm run verify` stops at its first lint/TypeScript gate. Existing
  `tests/contract/deal-quote-transition-outcome.test.mts:42` has a possibly-null model;
  `tests/e2e/studio-first-visit.spec.ts:50,51` pass string-or-undefined values. There are three
  diagnostics. No diagnostic names a Dashboard file. Subsequent full-pipeline gates are NOT_RUN.
- `quality.backend-readiness`: two pre-existing `docs/design/studio-connections-api` Markdown
  documents lack status entries.
- `quality.quality-pipeline`: its existing test expects 347 gates near the start, but still
  expects indexed size 345 at line 335. This change adds a step to the existing Dashboard gate,
  not a new gate. The unrelated contradictory assertion was preserved.

These failures prevent claiming a clean repository-wide verification. They were not hidden or
fixed by relaxing tests. The repository inventory was regenerated as required; route, module,
capability, flag and workflow counts match the pre-change working tree.

## Files in this change

Frontend paths relative to `frontend/unicorecrm-web`:

- `src/modules/deals/public/deals.ts`: named public forecast read through Deals owner.
- `src/workspaces/crm/read-models/dashboard/connectedDashboardQueries.ts`: authoritative bounded reads, count guards and timezone bounds.
- `src/workspaces/crm/read-models/dashboard/useConnectedDashboardReadModel.ts`: scope-aware lifecycle and cancellation.
- `src/workspaces/crm/read-models/dashboard/useDashboardReadModel.ts`: name/comment restrict snapshots to Demo; pre-existing runtime-settings edits preserved.
- `src/workspaces/crm/presentation/pages/DashboardPage.tsx`: Connected/Demo composition.
- `src/workspaces/crm/presentation/dashboard/ConnectedDashboardView.tsx`: supported live metrics and explicit unavailable/error/forbidden states.
- `src/guidance/content/crm/screens.ts`: Dashboard bilingual help and version update.
- `tests/contract/dashboard-connected.test.mts`, `tests/e2e/dashboard-connected.spec.ts`, `playwright.dashboard-connected.config.ts`: deterministic and real browser verification.
- `scripts/quality/quality-pipeline.json`: deterministic regression step in the existing Dashboard gate.
- `docs/quality/repository-inventory.json`, `docs/quality/repository-inventory.md`: regenerated inventory, preserving the current working-tree structure.

Backend paths: `scripts/verify-connected-dashboard.ps1`, this results document,
`docs/verification/connected-dashboard-design.md`, sanitized evidence under
`docs/verification/evidence/connected-dashboard/`, and `.gitignore` for local harness artifacts.
No Backend production handler was changed for this task.

## Reproduce after-fix verification

Requirements: existing Development SQL identity/database, demo/control Workspaces, existing demo
credentials in ApiHost user secrets, installed .NET/Node dependencies and Playwright Chromium.
Do not run any seed command. The script fails closed if the exact previously approved local SQL
identity differs or if its own API port is occupied. It does not replace an existing API process.

From `D:\Project_All\UnicoreCRM\backend`:

```powershell
pwsh -NoProfile -File scripts/verify-connected-dashboard.ps1 -Phase after
```

The harness builds and starts the existing Development test-host mode on loopback 5098 with
background delivery disabled, authenticates the existing account, reads SQL/API, opens isolated
Chromium on 3039, validates UI, compares hashes, then stops its own host and restores temporary
process environment variables. No credential is written to evidence or source.

From `D:\Project_All\UnicoreCRM\frontend\unicorecrm-web`:

```powershell
npm run quality:gate -- --gate quality.dashboard-runtime
npm run build
npm run verify
```

`verify` is expected to expose the unrelated current-tree TypeScript failures listed above until
their owning work is corrected. The saved before evidence is a baseline observation; running the
`before` assertion against the fixed UI is intentionally not an after-fix acceptance command.
