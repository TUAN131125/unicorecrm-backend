# Connected Quote List and Workspace-owned Pipeline selection

Local verification, 2026-10-24. Task 1 functional result: **PASS**. Task 2 functional result: **PASS**. Quote Lifecycle has not started. This record does not declare a production release or a frozen/global-quality checkpoint.

Workspace root: `D:/Project_All/UnicoreCRM`. Evidence root: `task-runtime/quote-list-pipeline/`.

## Task 1 — Repair Connected Quote List

### Root cause and before/after

Before the change, connected Chromium reproduced both “Thao tác này chưa khả dụng” and “Không thể tải danh sách Báo giá”. The browser's actual `GET /quotes?limit=25&sortBy=updatedAt&sortDirection=desc` returned **200**, with a valid items array and `totalCount: 50`; this was not a valid empty Workspace and not a Backend availability failure.

`QuoteListPage` used `useServerPagedModuleCollection` and the generic module query adapter. That route did not use the Quote HTTP Adapter or its Money/read-model mapper. It then called `replaceQuotes` outside the synchronous Backend read-projection scope. `ConnectedCollectionProjection.replace` correctly refused the write with `CONNECTED_LOCAL_WRITE_FORBIDDEN`; the paging runtime normalized the exception and the safe error presenter displayed the unavailable message. The guard remains enforced.

The exact pre-fix browser request correlation `c60a7793-1e89-44d4-a03d-5a584289246d` joins `quotes.ReadAuditRecords` with operation `listQuotes`, outcome `READ`, and Workspace `ws_772b487b87e84456b5020631fbaedd27`. Pre-fix HTTP, DOM/screenshot, Backend SQL logs, SQL Quote rows and this audit join are retained in `before/`. The exception class and prohibited projection call were also reproduced in the regression test; no browser exception stack is claimed where only safe UI copy was captured.

The repaired list uses `useQuoteListQuery` → Quote API runtime → `QuoteHttpApiAdapter` → generated `CommercialApiClient.listQuotes` → existing `GET /quotes` → owner authorization/FLS → trusted Workspace/OWN SQL predicates. Successful mapped pages enter the projection inside `runBackendProjection("quotes", ...)`; Workspace eviction uses the existing scope-reset boundary. Neither the guard nor authorization was relaxed. Command/idempotency paths were not changed.

The list renders the paging runtime's scoped page rather than another screen's shared collection. It uses server cursors/counts, resets selection on query/page-size/scope changes, clears filters and row-action state on Workspace change, rejects late retired-scope responses, and exposes a Refresh action. Search covers the Backend's declared number/title fields. Connected status/linked-Deal filters execute on the server; other old page-only filters are not advertised as collection-wide filtering. The linked-Deal selector uses owner-backed Deal options plus an explicit ID input, so selection is not limited to Quotes on the current page. Demo filters remain available in demo mode.

A second defect was found during the real network-recovery test: the legacy translation bridge remembers original Text-node copy, so React reusing a state panel's button across empty/loading/error could restore “Tạo báo giá” or “Hủy” over “Thử lại”. Quote List now gives these distinct states distinct React keys. A test mounts the real translation bridge and verifies the error action label after an empty-to-error transition. The global translation bridge was not rewritten.

### File changes

- `frontend/unicorecrm-web/src/modules/quotes/presentation/hooks/useQuoteListQuery.ts` — typed owner query and scoped read projection.
- `frontend/unicorecrm-web/src/modules/quotes/presentation/pages/QuoteListPage.tsx` — page-owned results, server filtering, refresh, scope reset, distinct state identities.
- `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteFilterPopover.tsx` — faithful connected filters and explicit linked-Deal ID.
- `frontend/unicorecrm-web/tests/quality/unit/{check-quotes.mts,quoteListPagingChecks.ts}` — HTTP mapping, cursor navigation, empty/error/retry, projection guard, late Workspace response and translation-state regression.
- Shared guidance, connected acceptance config/spec and Backend verification script are listed below.

### API / SQL / browser acceptance

| Case | Result and evidence |
|---|---|
| First and second cursor pages, 25/50 page size, previous navigation | PASS actual HTTP and rendered rows |
| Checkpoint A Quote visibility | PASS exact number search finds `quote_d31da058b071471d9277fef9806d7722`; list → detail → back → refresh |
| Status and linked Deal filters | PASS server responses; every returned item matches both filters |
| Empty result | PASS 200 with zero items and an empty panel |
| Invalid limit/cursor | PASS 422, distinct from empty success |
| Non-member Workspace | PASS 403 |
| Real transport failure and retry | PASS API-only Chromium network disconnection, error panel, restore network, Retry success; no route interception or fabricated response |
| Logout/login and refresh | PASS real authenticated browser flow |
| Workspace switching | PASS existing `demo.anhduong@example.com` member switches original Demo → isolation Workspace → original Demo; correct `X-Workspace-Id`, SQL-consistent empty Quote lists, no prior Workspace Quote shown |
| Response schemas | PASS 17/17 captured Quote list/detail responses against generated OpenAPI validation |
| Persistence and audit | PASS HTTP correlations join actual SQL read audit; all 50 existing Quote rows unchanged |

The list SQL still filters by trusted Workspace and, for OWN, authoritative OwnerId; status/source/buyer/search predicates, deterministic sort and cursor validation remain in their existing owners. Required hidden read fields still fail closed and optional fields are projected by Backend FLS. This task did not change role/ACL data or run an exhaustive restricted-role/FLS policy matrix; frontend error propagation and the existing focused security tests are reported separately from that unexecuted matrix.

Task 1 remaining risks: collection-wide filters beyond the declared API filters are unavailable; Quote-list statistics remain based on the currently loaded page. No new lifecycle capability is exposed.

## Task 2 — Workspace-owned Pipeline selection

### Root cause, compatibility and before/after

The prior Deal controller treated a `pipeline` query parameter and page dropdown as selection authority, filtered the table and Kanban to that selection, and sent a user-selected PipelineId on creation. Merely changing the Workspace default could therefore leave existing assignments outside the page's chosen view. This was frontend behavior; inspection found no Backend registry defect requiring a rewrite.

The existing Backend create policy already resolves the current Workspace default when PipelineId is absent. Deal Detail already reads the authoritative PipelineId assigned to its Deal. Backend pipeline/stage foreign keys and rules retain historical definitions. These mechanisms are reused, not replaced.

The page now displays read-only Workspace-default context, removes the pipeline URL selection, and sends no PipelineId from connected create/duplicate. The create form shows a read-only default; edit retains the record's assigned PipelineId. Deal Detail continues using that assignment. Existing external API callers retain the currently admitted explicit PipelineId contract; this UI change does not silently remove wire compatibility.

The table reads the authorized collection across all pipeline assignments and shows each assignment's pipeline/stage labels. Default Kanban retains its existing per-stage paging. Other configured pipelines have separate read-only boards with their own IDs/stage definitions/cursors and local response storage; they cannot overwrite the shared default-board projection. Users open a historical card's Deal Detail to manage that Deal. Search/owner/access scope apply across these boards. The UI explicitly scopes stage/date/value controls to the default board so those controls do not reinterpret old stage codes or silently hide historical records.

No Deal stage, PipelineId, default configuration, registry implementation or data migration was changed. A future migration must be a separately admitted operation with an explicit mapping; switching a default is not a migration. An unavailable/unknown assignment remains represented by the unfiltered table rather than being recategorized into default stages.

SQL compatibility evidence shows the original Demo's 180 Deals retain `dpl_3bc83638864b54b220d4390da48dfc30`. In the Quote control Workspace the default is `dpl_cf1e1c9a8b99ba20a929b649c6106cfa`, while the existing Deal remains assigned to `pipeline_56d0aa7237c9465a811ae3f03707ef5b`. The isolation Workspace has existing assignments across seven pipelines and a separate current default. `after/{pipelines-sql,deal-assignments-sql,deals-sql,switch-api}.json` records the exact facts.

### File changes

- `src/modules/deals/presentation/hooks/useDealPipelineController.ts` — default authority, URL cleanup, all-assignment table, create/duplicate payloads and per-record labels.
- `src/modules/deals/presentation/hooks/useDealStageWindows.ts` — local-only historical responses with independent cursors.
- `src/modules/deals/presentation/components/DealFormModal.tsx` — read-only create default and assigned edit pipeline.
- `src/modules/deals/presentation/pages/DealPipelinePage.tsx` — read-only context, default board and historical board composition.
- `src/modules/deals/presentation/components/DealAssignedPipelineBoards.tsx` — historical read-only presentation and correctly scoped filter notice.
- `tests/e2e/deal-pipeline-connected.spec.ts` — maintenance of the prior mutating acceptance scenario: Studio-saved default, read-only form, omitted create PipelineId, historical boards and scoped visual matrix. TypeScript checked; this mutating suite was not executed in the preservation run.
- `tests/contract/deal-workspace-pipeline-selection.test.mts`, `tests/quality/integration/check-deal-pipeline-authority.mts`, `tests/quality/architecture/check-deal-stage-window-query.mts` — default/no-selector/all-assignment/independent-window regression checks, admitted through existing gates.
- `docs/architecture/workspace-pipeline-selection.md` — policy, compatibility and limits. These paths are relative to `frontend/unicorecrm-web/`.

### API / SQL / browser acceptance

| Case | Result |
|---|---|
| Page ignores `?pipeline=client-cannot-select-this` | PASS URL removed, displayed default matches Workspace owner registry |
| Pipeline selector removed from Deal page/form | PASS source/focused checks and connected page assertion |
| Historical assignment remains visible | PASS actual old-pipeline card → Deal Detail response retains original PipelineId → all-assignment table contains same Deal |
| Studio multi-pipeline administration retained | PASS existing configuration screen and enabled Add Pipeline control; no configuration write executed |
| Workspace switch reads new default | PASS registry Workspace IDs and displayed defaults checked across both existing Demo memberships and return switch |
| Two old pipelines with a shared stage code | PASS focused runtime: independent rows and load-more cursors, no shared repository writer |
| Existing default policy | PASS Backend focused pipeline tests and inspected current owner policy; no new Deal/configuration mutation was needed for this read acceptance |
| Registry/data preservation | PASS exact SHA-256 of `deals.Pipelines` and `deals.PipelineStages` before/after |

Task 2 remaining risks: historical board request fan-out grows with configured pipelines/stages; windows remain bounded and independently paged. The existing table loader has a visible 5,000-record safety cap. Large-workspace load acceptance is not claimed. The old mutating pipeline acceptance scenarios were adapted to Studio-owned defaults and retain their custom-stage/cross-pipeline/currency/isolation assertions, but were not run here and are not counted as a new browser PASS. Their default restoration occurs on the success path; a future deliberate mutating run must account for failure cleanup in its isolated test Workspace.

## Shared verification and preserved state

The connected browser run passed **1/1 in 19.6 seconds** (22.8 seconds runner total), recording **107 domain GET responses and zero domain writes**. No API response was mocked. Browser network disconnection targeted ApiHost only; the application frontend stayed connected. Evidence: `after/browser-http.json`, `browser-read-audits.json`, `browser-dom.txt`, screenshots, schema-validation JSON and `test-results/.last-run.json`.

`backend/scripts/verify-quote-list-pipeline.ps1` verifies the existing local SQL target, uses existing identities/memberships and process-only credentials, owns/stops its isolated ApiHost5098, launches private Chromium/Vite3044, and checks preserved data. No seed, reset, database creation, ACL change or fixture deletion. Configuration/credentials/tokens are not emitted to evidence.

| Verification | Outcome |
|---|---|
| Backend build | PASS, 0 warnings / 0 errors |
| Focused Backend Quote/MarkWon/Pipeline tests | PASS 93/93 |
| Connected API, SQL and Chromium addendum flow | PASS |
| Quote focused tests incl. projection/state regression | PASS |
| Pipeline authority and stage-window focused gates | PASS |
| Frontend strict typecheck group | PASS 3/3 |
| Guidance / presentation / mutation authority | PASS |
| API contract and API-management boundaries | PASS; 329 operations, 303 ready / 26 blocked |
| Frontend/backend separation | PASS |
| Repository inventory / security scan | PASS |
| Production build / bundle budget | PASS; entry 290.98 KiB, largest chunk 494.60 KiB < 500 KiB |
| Global frontend tsc / full verify | FAIL: same three pre-existing test diagnostics; no addendum source/test diagnostic |
| Quality-pipeline gate | FAIL: existing `348 !== 345` count assertion |
| Backend-readiness gate | FAIL: existing two unregistered Studio connection design documents |

Full verify stops at the lint/typecheck gate; the remaining 327 gates were not claimed as executed. Global TypeScript failures are `tests/contract/deal-quote-transition-outcome.test.mts:42` (nullable model) and `tests/e2e/studio-first-visit.spec.ts:50,51` (optional string). They are unchanged entry files. The previous full Backend architecture failures recorded under Quote Core were not repaired by this frontend-focused task or presented as green.

The regenerated inventory retains the entry structure: 16 modules, 86 route definitions, 70 loadable route modules, 137 capabilities, 13 flags and 23 workflows. The older AGENTS protected route/loadable counts differ from the existing dirty entry inventory; this task did not add routes, modules, capabilities or flags.

All captured existing Backend files remain unchanged, including Quote Core commands/pricing/idempotency and the Dynamic Pipeline registry. All nine CRM business-table count/hash rows match before/after, including the original **1,050 records / 180 Deals**. All **50 Quote rows** and both pipeline tables match exactly. Read-audit and authentication-session records are expected verification effects, not commercial mutations.

Task-entry patches/statuses/hashes and final source deltas are under `task-runtime/quote-list-pipeline/`. P1's five source snapshots were captured separately before its disjoint work and override those hashes for comparison. Existing dirty changes remain; Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` and Frontend HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d` are unchanged. No commit, push or merge.

Reproduce the non-mutating connected acceptance:

```powershell
cd D:\Project_All\UnicoreCRM\backend
pwsh -NoProfile -File scripts/verify-quote-list-pipeline.ps1 -Mode After
```

`-Mode Before` is historical fault-reproduction intent and is not expected to pass after the repair.
