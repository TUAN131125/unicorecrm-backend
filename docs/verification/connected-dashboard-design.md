# Connected Dashboard hydration: diagnosis and contract gaps

Scope: read-only use of the existing `unicore-demo-anh-duong` and control workspaces. No seed,
Workspace configuration, permission, CRM mutation, migration, connection change or production
endpoint is admitted by this change. Existing uncommitted AI/configuration work is preserved.

## Diagnosis

DERIVED_FACT from DashboardPage, useDashboardReadModel, ConnectedCollectionProjection and
useRepositorySnapshot: the Dashboard subscribes to module projection repositories without
initiating reads. Those repositories start empty and are only populated by unrelated list/detail
screens. Therefore a fresh Dashboard mistakes absence of hydration for a verified empty dataset.
Visiting a paginated list does not fix correctness: its partial window is not an aggregate.
The Page also computes business dates using browser local time and labels mixed currencies as
the Workspace base currency. Quotes and Customers are unnecessary dependencies for the visible
Dashboard calculations. Reports are a separate read model and are not changed by this fix.

## Authoritative source matrix

| Surface | Owner / admitted operation | Decision |
|---|---|---|
| Open pipeline amount/count | Deals GET `/deals/forecast-summary`, `getDealForecastSummary` | Use response currency buckets; never sum browser Deal pages or mix currencies |
| My overdue Tasks | Tasks GET `/tasks`, OPEN + authenticated member + overdueAt, pageInfo.totalCount | Bounded page and server count; scope/field security applied by owner before SQL count |
| Tasks due today | Tasks same count query at Workspace midnight and next midnight | Difference of two authoritative counts; no Task scan; timezone-aware boundaries |
| Open Support Cases | Support GET `/support/cases`, one query per nonterminal status | Sum owner totalCount for new/in_progress/waiting_customer/waiting_internal/reopened |
| Urgent Support | Same, each open status and high/critical | Sum scoped totalCount; bounded representative queue, explicitly a sample |
| New Leads in selected period / score >=80 | Leads GET `/leads` has workState/owner/search, but no createdFrom/createdTo/score aggregate | BLOCKED_CONTRACT; never use NEW work state as date-created count or partial-page filter |
| Completed revenue in selected period | Orders GET `/orders` has state but no completion-date/currency sum | BLOCKED_CONTRACT; Order state, payment and revenue are not interchangeable |
| Priority Lead queue | Leads list has no global score ordering | BLOCKED_CONTRACT; latest updated records are not the highest-priority Leads |
| My work | Tasks list OPEN/member, dueAt ascending, limit 5 | Fresh bounded server page; display as a window |
| Closing Deals | Deals list OPEN, expectedCloseDate ascending, limit 5 | Fresh bounded server page; keep per-record currency |
| Recent Activity | Tasks GET `/activities`, descending, limit 6 | Owner applies referenced CRM permissions before count/page |

Source authority: existing public operations and their current owner handlers, persisted fields,
scope/FLS checks and OpenAPI. `getDealForecastSummary` is PRODUCTION_CONTRACT_READY in the control
system. It already authorizes `deals.read`, applies Workspace/owner scope and records read audit.
Tasks and Support list handlers execute SQL COUNT on the authorized query before pagination.
Existing Deal forecast/list handlers still materialize scoped Deals internally: this fix avoids
transferring them to the browser, but does not claim to optimize that pre-existing backend path.

## Implementation decision

IMPLEMENTATION_CHOICE: Connected Dashboard owns fresh, abortable read resources keyed by
Workspace, session/account/member and access revision. It consumes existing public module query
boundaries. It neither projects partial windows into module repositories nor uses them as totals.
Unmount/scope change cancels work; a new scope has no old result to display. Failed/forbidden,
loading, unavailable and verified zero are distinct states. Demo rendering stays on its explicit
runtime path. Error sections do not silently zero unrelated KPI sections.

Each load has bounded response size and no cursor sweep. Support requires a fixed set of small
queries because the existing endpoint accepts only one status/priority at a time. Data remains
filtered by the existing owner permissions. This adds no HTTP route, capability or OpenAPI claim.

## Minimal proposals, not implemented contracts

PROPOSAL — Leads-owned period summary: createdFrom inclusive / createdTo exclusive UTC bounds,
`createdCount`, `scoreAtLeast80Count`, and bounded top-priority records. Reuse canonical Lead
authorization, member/unassigned predicates and field security before COUNT/ranking. Query the
owner schema with COUNT/SUM, not full Lead profiles. Dates originate from the Workspace timezone.

PROPOSAL — Orders-owned completed-period summary: completedAt bounds, count and decimal-string
amounts grouped by currency, restricted to non-archived completed Orders. Reuse Orders read scope,
amount/completion field permissions and read audit. Do not infer completion from UpdatedAt or
create exchange-rate/revenue-recognition semantics. Restricted scopes that Orders cannot resolve
must remain denied/unavailable. Empty authorized result may be zero; permission denial may not.

The exact operation IDs/HTTP schemas, field-security representation and temporal semantics need
admission in the canonical contract before implementation. Per CORE_AUTHORITY, `PROPOSAL` is
not automatically authority to add a new endpoint. No cross-owner SQL dashboard endpoint is added.

Future optimization proposal: Support-owned conditional counts and Deals-owned SQL summary using
the same authorized predicates. This is not necessary to fix client hydration and is not presented
as a shipped performance improvement.
