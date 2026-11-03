# Connected CRM functional completion

Date: 2026-10-29, Asia/Saigon. Scope: existing admitted APIs only; frontend presentation/query reuse. No backend product source, API contract, governance, authentication bundle or SQL business data changed. No commit/push/merge/reset/rebase.

| Result | Verdict | Executed evidence |
|---|---|---|
| `CUSTOMER_360_WORK_HISTORY_PASS` | PASS for connected Work/History reads, navigation and retained Task-create entry | Real SQL IDs match HTTP 200 in two existing synthetic Workspaces; Chromium main tab/panel share one request per collection; History dialog, hidden panel, Task detail, refresh and logout/login pass |
| `TASK_DASHBOARD_CALENDAR_CONSISTENCY_PASS` | PASS within existing admitted date policy | Real connected Dashboard counts and matching persistent Task filters; existing assigned Task appears on its Workspace calendar date despite a different browser timezone; boundary/invalid-date/DST regressions pass |
| `DEAL_FOLLOWUP_UX_PASS` | PASS, executed after the first two slices passed | Real Deal-linked Task priority/deadline/title match SQL/HTTP; Chromium opens canonical Task Detail; no Deal/Quote mutation |

## What changed

- Customer's main Work tab now renders actual directly linked Tasks and History instead of an instruction placeholder. A shared controller/resource serves it and the side panel; changing modes or hiding the panel does not issue duplicate reads. Paging, retry, refresh, permission/FLS refusal and the existing Task-create/meeting entry points remain. Dialogs have one owner. Task navigation uses the current canonical Workspace path.
- Fixed the pre-existing Customer header disclosure found by the baseline contract: hidden `segment` was still rendered in header metadata. Hidden/masked segment and owner identity now respect field access; unavailable commercial metrics remain explicitly unavailable.
- Added shared Task date classification and a live clock. Existing backend semantics remain: overdue is `OPEN && DueAt < asOf`, including earlier today; due today is OPEN on the Workspace calendar date; completed is exactly COMPLETED; my open tasks are OPEN assigned to the current member. Cancelled work is excluded. Invalid due dates are not normalized into real calendar days. Calendar no longer falls back to every Task when member identity is unavailable.
- Dashboard overdue/today cards now open the corresponding existing saved view with `assignee=mine`; Task List preserves those filters in the URL. Calendar/List/Dashboard display Workspace time consistently.
- Deal follow-up now explicitly displays localized priority alongside existing status, assignee and due time. No complete/reschedule action was added to that widget.

## Operations and data authority

`getCustomer360`, `listTasks`, `getTask`, `listActivities`, `createTask`, `listDeals`, `listMyWorkspaces` and `getWorkspaceBootstrap` are `ADMITTED_IMPLEMENTED` in the current canonical operation registry and have corresponding existing endpoints/adapters. Existing authorization-context, record-access and member-directory APIs supply the current trusted scope. Endpoint presence alone was not used as admission evidence.

Customer Work/History uses `recordModuleKey=customers&recordId=<CustomerId>` through the existing Tasks adapter; no history is inferred from Tasks, Contact, Organization or Deal relationships. Deal follow-up uses the equivalent direct `deals` filter. Workspace timezone comes from existing connected Workspace bootstrap/runtime settings; no new configuration operation was introduced.

No implemented-but-blocked or missing operation was opened. Customer commercial/service/other unavailable projections remain unavailable; no inferred metrics, new operation or Customer360 contract change was introduced.

## HTTP, SQL and Chromium

Identified database: `DESKTOP-ICH225R / UnicoreCRM_Development`, resolved through existing configuration/UserSecrets helper without exposing credentials. Reused existing synthetic Customers, Tasks and Activities; no seed, migration, direct SQL DML, permission grant or fixture creation occurred. Task-owned ApiHost used existing `--demo-seed-host` support on 5098, disabling application background delivery. Task-owned Vite ran on 5399; headless Chromium used browser timezone `America/Los_Angeles`, while the tested Workspace used `Asia/Ho_Chi_Minh`.

Executed eight connected checks include SQL↔HTTP row IDs for two Customers, direct Task/Activity references, one shared collection request, one Activity dialog, actual Task Detail, refresh/logout/login, Calendar date `2026-11-15`, Dashboard filter/count consistency, authenticated read-only Task-create 403, unauthenticated Task 401, cross-Workspace Customer/Task denial and empty cross-Workspace Activity results. The final run recorded 122 HTTP 200, two 401, two 404 and four 403 responses across normal bootstrap/security checks.

All 32 per-Workspace Customer/Task/Activity/Deal count/hash groups match before and after. Backend-generated session/read-audit writes are normal API effects; existing CRM business rows remain unchanged. The authorized Task-create form was opened and verified, then closed without submission to preserve data. Fresh successful Task creation was **NOT_RUN** in this task; its existing mutation protocol regression passes. Real existing Tasks prove persisted read/navigation, not a newly committed write this run.

Evidence: `task-runtime/connected-crm-completion-20261029/` contains source preimages, `fixture.json`, `http-results.json`, `browser-results.json`, `sql-preservation.json`, three screenshots and before/after check logs. `run-connected-checks.ps1` plus `verify-connected-browser.cjs` reproduce the real connected checks without business mutations. In-process fixtures are labeled unit/protocol tests and are not used as backend acceptance evidence.

## Changed frontend files

- Customers: `presentation/detail/CustomerConnectedWorkspace.tsx`, `CustomerRelationshipPanel.tsx`, `CustomerConnectedRecordHeader.tsx`, new `CustomerWorkContent.tsx`; `presentation/hooks/useCustomerConnectedWorkspace.ts`, `useCustomerPanelData.ts`, new `useCustomerWorkController.ts`.
- Tasks: new `application/queries/taskDateClassification.ts`, new `presentation/hooks/useTaskClock.ts`, `presentation/pages/TaskListPage.tsx`, `public/index.ts`.
- CRM: `presentation/pages/WorkCalendarPage.tsx`, `presentation/dashboard/ConnectedDashboardView.tsx`, `read-models/dashboard/connectedDashboardQueries.ts`.
- Deals: `presentation/components/DealFollowUpTasks.tsx` only.
- Guidance: `src/guidance/content/crm/extendedScreens.ts` and `screens.ts`, with affected bilingual guidance/version updates.
- Tests: `tests/contract/customer-relationship-panel.test.mts`, `dashboard-connected.test.mts`, new `tests/fixtures/runtime/check-task-date-consistency.mts`, existing `tests/quality/integration/check-dashboard-runtime.mts` integration.
- Generated repository inventory only: `docs/quality/repository-inventory.json` and `.md`. Protected module/route/capability/workflow counts unchanged; no allowlist or threshold changed.

Paths in the first four bullets are relative to their owning module/workspace under `frontend/unicorecrm-web/src/`.

## Regression results and limits

| Check | Before | After |
|---|---|---|
| Global TypeScript | PASS | PASS |
| Lint (`quality.lint`) | PASS | PASS |
| Production build/bundle budget | PASS | PASS; largest chunk 484.21 KiB / 500 KiB limit |
| Route smoke | PASS 8/8 | PASS 8/8 |
| Customer relationship contract | FAIL: hidden segment exposed by existing header | PASS; disclosure fixed, canonical navigation and shared-query regressions added |
| Existing Customer Task-create protocol | Not separately rerun before changes | PASS; duplicate/unknown-outcome/retired authority behavior preserved |
| Dashboard/date contracts | Existing source inspected | PASS, including month/year/leap-day/DST, invalid dates, equality cutoff and assignee/status boundaries |
| Presentation, guidance, mutation authority, API check/management boundaries, backend separation/readiness | Existing baseline retained | PASS focused checks |
| Backend build | No backend changes | PASS, zero warnings/errors |
| Repository inventory/pipeline | Updated for added frontend files | PASS; 16 modules, 86 routes, 146 capabilities, 23 workflows retained |
| Focused real Chromium | NOT_RUN before edits; source placeholder/date inconsistency captured | PASS eight real connected checks, including security, SQL readback, refresh/relogin and browser/Workspace timezone difference |

Full `npm run verify`: **FAIL**, 48/328 gates passed; stopped at gate 49 `quality.repository-cleanup` because inventory contains 14 exact-content duplicate groups where that existing gate requires zero. The before-task inventory also contains 14 groups; their full group records are unchanged. No unrelated cleanup or threshold/allowlist weakening was attempted. Gates after that point were NOT_RUN by the full pipeline; relevant focused checks above ran separately. Functional verdicts do not imply full-quality, release or governance acceptance.

Still unavailable/not run: missing Customer projections/metrics; fresh Task mutation and reschedule/complete acceptance; changed-data restart scenarios. Existing refresh and relogin persistence were verified without changing business data. Authentication/invitation admission work remains outside this task; its approved bundle hash remains unchanged.
