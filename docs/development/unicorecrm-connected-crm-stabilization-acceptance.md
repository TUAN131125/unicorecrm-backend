# UniCoreCRM — Local source reconciliation and Connected CRM stabilization

Date: 27/10/2026, Asia/Saigon. Acceptance uses local working trees, real Development SQL Server, real ApiHost, generated HTTP adapters and Codex-owned headless Playwright Chromium. Evidence directory: `../../../review-artifacts/connected-crm-stabilization-20261027/` from this repository root's parent workspace.

## J. Verdict and evidence limits

**Functional verdict: `PARTIAL_CONNECTED_FUNCTIONAL_PASS`. Full Quality Gate: FAIL.**

A single linked synthetic chain exists and has been read through HTTP, SQL and browser: Lead → canonical Contact → Organization affiliation → Customer relationship profile → Customer Task/Activity → Deal/default Pipeline → Quote → approval assessment NOT_REQUIRED → manual delivery SENT → employee-reported customer response UNVERIFIED. Customer 360 now displays the related Deal through its owner query.

The required Main Journey is incomplete at authoritative PDF issuance. Its existing synthetic actor receives HTTP 403 for `quotes.issueDocument`; its Workspace also lacks a ready seller configuration. A separate high-value synthetic Quote cannot enter independent approval: HTTP 409 `QUOTE_APPROVAL_REQUIRED`, detail `NO_ELIGIBLE_APPROVER`. No permission relaxation, SQL permission writes, fabricated PDF, or independent-approver substitute was used. Manual delivery is a separate admitted operation and succeeded without claiming PDF issuance. Browser evidence covers login, persisted linked record screens, Customer-related Deal, refresh and logout/login; initial record creation used official HTTP commands, not browser form submission.

Operational verified acceptance remains `BLOCKED_CONTRACT`. B6.5 and Gate C remain closed. No test result below establishes full production acceptance or a frozen authority baseline.

## A. Source truth

| Repository | Local branch / HEAD | Remote verified with read-only `git ls-remote` | Divergence |
|---|---|---|---|
| Backend | `master`, `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` | `master` = `7b734f98a7173af2e24e094392826a20b6e11359` | One local commit ahead: `13cd39e` Connected CRM P0 checkpoint, plus dirty implementation |
| Frontend | `feat/deal-compact-kanban-card`, `814cc4685b6d8f8ead2fc945f4607a2d0168320d` | feature = same HEAD; `main` = `99db1773c03ce4a4b820db7e793da6eca872f862` | Feature matches its upstream; local `main` has checkpoint `7d41aba` beyond remote main; extensive dirty implementation |

Before-edit artifacts: `backend-status-before.txt`, `frontend-status-before.txt`, `*-before.patch`, `*-hashes-before.txt`. Backend entry state: 85 tracked modifications, 1,145 untracked files. Frontend entry state: 219 tracked modifications, one tracked deletion, 439 untracked files. No staged changes were present. These existing changes belong to earlier work and are not attributed to this task. Important source hashes, including local Quotes endpoints, MarkDealWon, runtime composition, adapters and generated clients, are included in the per-repository hash manifests.

Local Quotes endpoints include B1–B6.4 commands, unlike the old GitHub list/get-only snapshot. Local MarkDealWon is already fail-closed without an admitted owner verifier. Those historical review targets were not treated as proven local defects.

### Current Source Truth Matrix

`IMPLEMENTED_VERIFIED` below is scoped to the concrete positive connected flow and persistence evidence, not a claim that every operation or security combination in the module was executed.

| Module | Frontend / HTTP boundary | Backend / SQL owner | Connected evidence this run | Status |
|---|---|---|---|---|
| Authentication | Connected login/session/refresh/logout | IdentityAuth / iam | API login/session; browser logout/login; real refresh via public Connected identity runtime | IMPLEMENTED_VERIFIED |
| Workspace | Connected context/bootstrap | Workspace + AccessControl | Real membership/bootstrap and foreign Workspace read/write denial; A→B→A with two member Workspaces not executed | PARTIAL |
| Lead | Lead runtime/official lifecycle commands | Crm.Leads / leads | WEB source, NEW→CONTACTING→VERIFYING→NURTURE, conversion, browser detail, restart reads | IMPLEMENTED_VERIFIED |
| Contact | Connected Contact adapter | Crm.Contacts / contacts | Canonical Contact created by nurture, affiliation and preserved owner, browser detail | IMPLEMENTED_VERIFIED |
| Organization | Organization adapter | Crm.Organizations / organizations | Official create, Contact primary affiliation, UI and SQL join | IMPLEMENTED_VERIFIED |
| Customer | Customer adapter and 360 | Crm.Customers + conversion workflow / customers + workflow | Existing Contact conversion, Customer 360 and canonical related Deal | IMPLEMENTED_VERIFIED |
| Task/Activity | Tasks adapter and relationship panel | Operations.Tasks / tasks | Customer Task/manual NOTE, timeline and restart reads, SYSTEM write refusal, replay | IMPLEMENTED_VERIFIED |
| Deal | Dedicated Deal adapter/generated client | Crm.Deals / deals | Canonical BuyerRef/source Lead/default Pipeline, UI and SQL | IMPLEMENTED_VERIFIED |
| Dynamic Pipeline | Studio registry and Deal pipeline runtime | Deals owns persisted Pipeline/Stages | Existing default applied; persistence verified. Historical configuration change and full Kanban transitions not rerun | IMPLEMENTED_NOT_VERIFIED |
| Quote Core | QuoteHttpApiAdapter/generated client | Sales.Quotes / quotes | Deal source/version lease, decimal totals, version, UI and SQL | IMPLEMENTED_VERIFIED |
| Quote B1–B5 | Dedicated Quote commands/document panel | Quotes and seller Workspace configuration | NOT_REQUIRED assessment and manual SENT verified; high-value independent approval and PDF blocked | PARTIAL |
| B6.1–B6.4 | Proof/Identity/Review adapters and panels | Quotes proof/scan/identity/source/review ledgers | Source review, original row preservation, 14 retained simulation reviews checked in SQL; new full B6 browser mutation suite not rerun | IMPLEMENTED_NOT_VERIFIED |
| Operational acceptance / Gate C | Availability refuses unsupported writes | Quotes acceptance guard / Deals MarkWon guard | Direct HTTP refusal; no ACCEPTED/WON from this fixture | BLOCKED_CONTRACT |

## B. Architecture and contracts

The actual execution path is frontend composition → module application port → dedicated HTTP adapter → generated client → ApiHost named endpoint → owning application handler → owner persistence/SQL. Public Customer sales queries use `readDealPage` exported from Deals, with canonical `customer.relationshipRef` as buyerType/buyerId and a bounded page of 20. They do not copy Deal commercial state into Customers or write browser repositories.

Official chain used:

1. POST `/leads`; POST `/leads/{id}/advance-work-state` with current If-Match.
2. POST `/workflows/lead-qualification/{id}/nurture`; the owner workflow creates Contact and a nurture Task.
3. POST `/organizations`; POST `/contacts/{id}/organization-relationships` with Contact version.
4. POST `/workflows/lead-customer-conversion/{id}` using EXISTING Contact; no SQL conversion and no duplicate identity.
5. POST `/tasks` and `/activities` with Customer `recordRef`, and a consistent canonical Contact relationship for Activity.
6. POST `/deals`; authoritative default Pipeline applies.
7. POST `/deals/{id}/quotes` with Deal version. Buyer/source fields are derived by the server and forbidden in this command body.
8. Quote-owned request-approval, send, customer-decisions and protected document issuance operations.

ApiHost module composition and module registration were inspected, including Quotes services/policies/scan worker, Deals pipeline validation, Workspace context, AccessControl projection enforcement and CRM conversion participants. SQL migrations were inventoried read-only in `sql-business-evidence.json`; no migration was applied.

## C. Quality baseline and fixes

### Backend

Baseline reproduced **400 passed / 4 failed / 0 skipped**. Exact test names, messages, stacks and source locations are in `backend-baseline-failures.json`, baseline TRX and `backend-baseline-diagnosis.md`.

| Failing test | Root cause | Result |
|---|---|---|
| `EndpointMetadataTests.Actual_ApiHost_endpoints_obey_route_operation_and_authorization_admission` | 55 actual endpoints missing from effective admitted-operation inventory, including Communications/AI/outbound operations | Still FAIL; endpoint existence is not authority to admit a business operation |
| `ArchitectureTests.Owner_mapping_is_complete_and_cross_owner_edges_are_explicit` | 549 findings: legacy layer-first namespace mappings and unmapped/unapproved edges against module-first implementation | Still FAIL; no allowlist generated from observed dependencies |
| `ArchitectureTests.Durable_contexts_and_migrations_have_explicit_owner_metadata` | 15 unassigned implemented contexts / incomplete frozen owner metadata | Still FAIL; no fabricated assignment decision |
| `ArchitectureTests.Layer_and_workflow_dependencies_obey_the_canonical_boundaries` | Actual Application→Infrastructure dependencies across multiple owners | One complete local EF authorizer boundary fixed; 138→136 findings; remaining guard stays FAIL |

`ServiceAccessAuthorizer` is an EF grant lookup/audit adapter implementing an existing contract. It was moved into `AccessControl/Infrastructure/Persistence/EfServiceAccessAuthorizer.cs` and DI was updated. Predicate, workspace binding, audit, clock and SaveChanges behavior were preserved. This is not a broad workflow relocation or architecture redesign.

Security fixes in Deals list:

- Apply the same `DealFieldSecurity.Project` used by GetDeal before serializing list records; previously optional HIDDEN fields were returned.
- Exclude withheld notes/contactId/sourceLeadId from search matching; otherwise matching counts reveal hidden values even after the response projection is fixed.
- Real-handler regression tests verify withheld wire properties, readable fields, required-field refusal before persistence, unchanged stored aggregate, hidden-search counts and visible-name search.
- Response-leak red proof: 1 FAIL / 3 PASS before the first fix. Search-inference red proof: 3 FAIL / 5 PASS before the second fix.
- Final full suite: **408 passed / 4 baseline failures / 0 skipped / 412 total**, including eight new FLS tests. Evidence: `backend-final.log`, `final-search-fls*.trx`, `deal-search-red.log` and `deal-fls-before.log`.

No tests were disabled, no assertion was removed to admit a forbidden edge, and no architecture/admission budget was increased. Full Quality remains FAIL.

### TypeScript

The reported three diagnostics were reproduced at the same source locations:

- `tests/contract/deal-quote-transition-outcome.test.mts:42`: controller may return null. The test probe now reads `model?.toast?.type`.
- `tests/e2e/studio-first-visit.spec.ts:50,51`: a callback did not retain narrowing of an optional environment variable. An explicitly typed local string is captured after the existing required-input check.

Global compiler/lint and focused strict checks passed after the fixes. No `any`, suppression, tsconfig exclusion or strictness reduction was added.

### Frontend quality work

- Registered three missing documents as SUPPORTING metadata: Workspace pipeline selection documents current verified implementation (CURRENT); the two explicitly PROPOSED Studio Connections design documents remain TRACKER. Existing BLOCKED product/contract states were preserved.
- Corrected the architecture AST browser-global detector to ignore type/property declarations named `document`; direct runtime `document`, localStorage, shorthand and typeof negative controls remain denied.
- Moved shared type definitions out of component/consumer cycles while retaining compatible type re-exports. Readiness reports zero dependency cycles.
- Aligned current generated inventory figures and the exact admitted HTTP-adapter inventory; registered seven existing compatibility candidates honestly as open review debt. The retirement ceiling of 53 was retained, so the observed 60 still fails its guard.
- Registered the Customer-related Deals regression in the existing Customers gate, and made the pipeline's self-check compare the gate index with the canonical manifest rather than a stale literal count.
- Added per-product accessible remove labels in the existing interested-product picker; its contract now checks the actual extracted control owner.
- Classified `CONNECTED_PIPELINE_API_REQUIRED` as an internal refusal so shared error presentation maps it to safe unavailable copy.
- Corrected Quote source fallback copy: a Deal-derived Quote with no denormalized customer/deal label is not described as a direct Quote or rendered as an empty Deal link.
- Reconciled exact source guards with current owner contracts: Quote FLS uses `lineItems` rather than the separate Order `items` key; the Deal collection is Pipeline-scoped; the extracted Quote list hook uses the Quote runtime and shared cursor pager; the Customer detail route uses an authority-keyed, non-retaining Connected resource. Assertions for scope, owner query, projection eviction and safe audit labels remain explicit.

Canonical gate-by-gate execution is recorded in `frontend-all-results.json` and `frontend-all-gates/`. Each gate still runs through `scripts/quality/run-quality-pipeline.mjs`; the external evidence harness continues after failure to collect independent results and does not turn a failure into a pass. Final counts and remaining gates are recorded in the machine-readable evidence and the final verification appendix below.

## D. Connected CRM chain

Synthetic marker: `712d140c4ca849739339863ed255cbb0`. Workspace: `ws_772b487b87e84456b5020631fbaedd27`, key `ki-m-th-pipeline-provisioning-c019e52d`. Existing official synthetic identity: `pipeline.provisioning@example.com`. Credentials remained process-only.

| Record | Identity / proof |
|---|---|
| Lead | `lead_67820c48adcb4f05bd5f54fdbb7f57a6`; source WEB; conversion preserved canonical Contact |
| Contact | `contact_d239ffed62fd4f4aaa41c77b3643e4b9` |
| Organization | `organization_1c80d99698cd4a8a9e38c617b489fa73`; primary buyer affiliation |
| Customer | `customer_b9c9e0eb0a5a4e1292410ef4f3581538`; CONTACT profile points to the same Contact |
| Customer Task | `task_abc4b42a0fc842348f3f3e48de4eba0a`; direct Customer target; separate nurture Task also exists |
| Manual Activity | `activity_6f2619544ea14350b1e7016046c47129`; NOTE, consistent Customer/Contact references |
| Deal | `deal_04d6299a1c9d49a28ca3f9e446190364`; canonical Contact BuyerRef/source Lead |
| Pipeline | `dpl_cf1e1c9a8b99ba20a929b649c6106cfa`; server-selected default, DISCOVERY / OPEN |
| Quote | `quote_cf249e0ac32f43789f9ee496c8782e39`, Q-0000000290, revision 1; sourcePath DEAL and SourceDealId exact |

Backend monetary evidence: quantity `2` × unit price `12345.67`, discount `5%`, tax NONE, VND precision. Stored subtotal `24691`, discount `1235`, grand total `23456`. These are backend decimal/rounding results, not client-written totals. The Quote retained SENT after employee-reported acceptance; the decision ledger records `USER_REPORTED_ACCEPTANCE` / `UNVERIFIED`.

The SQL relationship join in `sql-business-evidence.json` ties these identities together, including OrganizationRelationships, Customer.RelationshipId, Quote.BuyerId and Quote.SourceDealId. Customer owner and Contact identity owner were not overwritten by conversion.

## E. Security and commercial guards

| Scenario | Actual evidence / status |
|---|---|
| Foreign Workspace read/write | Direct HTTP 403; PASS for tested nonmember Workspace |
| Invalid BuyerRef | Official command rejection; see final journey HTTP evidence |
| Forged Contact linkage | Direct Activity write HTTP 404; PASS |
| SYSTEM manual Activity | Direct HTTP 422; PASS |
| Exact idempotency replay | Same Activity aggregate returned; 201 replay; PASS |
| Same key / changed payload | HTTP 409 IDEMPOTENCY_KEY_REUSED; PASS |
| Stale Quote If-Match | HTTP 412 VERSION_CONFLICT; PASS |
| Concurrent Customer decisions | One commit and one stale version rejection; final evidence records exact statuses |
| PDF issuance permission | HTTP 403 and disabled/readiness messages on UI; fail-closed PASS, issuance journey BLOCKED |
| Approval / creator | High-value request has NO_ELIGIBLE_APPROVER; creator approve denied 403. This does not substitute for an actual independent approval execution |
| Schema-valid forged WON evidence | HTTP 422 DEAL_WIN_EVIDENCE_INVALID using QUOTE_ACCEPTED/sourceId/occurredAt; PASS |
| Linked atomic acceptance | HTTP 409 QUOTE_ACCEPTANCE_BLOCKED; no ACCEPTED/WON mutation; PASS |
| Restricted optional Deal FLS | Eight real-handler tests including wire and search inference; PASS. A restricted-field direct HTTP fixture was not created; no pre-existing Deal field policies were found, and no permission SQL writes were used |
| SQL injected failure rollback | NOT_RUN in this task; no claim based only on prior evidence |
| A→B→A member Workspace switch | NOT_RUN; current identity has no second admitted member Workspace fixture |
| B6 simulation isolation | Source guards plus retained SQL review rows; 14/14 DEVELOPMENT_SIMULATION and actualSourceVerification=false, zero linked ACCEPTED Quotes |

Source safety trace: Connected Deal Quote actions do not expose direct accept; public acceptance routes use mutation-command dispatch; Quotes acceptance guard blocks the missing verifier/atomic owner contract; MarkDealWon refuses both evidence source branches before mutation or replay. Caller source IDs, booleans and document possession do not establish owner authority. See `commercial-review.md` for exact file references and B6.5/Gate C gaps.

## F. Browser, SQL and persistence

Real headless Chromium was launched from the repository Playwright installation. No user desktop or personal browser was controlled. No Backend API was mocked. The local acceptance ApiHost uses its existing `--demo-seed-host` mode, which runs normal HTTP authorization/persistence while disabling unrelated background email/webhook/AI dispatch. The switch name does not invoke data seeding. All bootstrap flags and automatic migrations were explicitly disabled.

Evidence includes browser screenshots for Lead, Contact, Organization, Customer, Deal, Quote, Customer-related Deal, Customer History and relogin. `browser-http.json` records method/path/status/correlation metadata and page errors. Official command responses and negative HTTP checks are in the journey artifacts; SQL command/audit/outbox data corroborate persisted transitions. Early harness validation errors are harness-input corrections, not presented as production bugs.

The owned ApiHost was stopped and restarted between journey executions. Subsequent API/browser reads retained the same identities, relationships, Task, Activity, Quote and decision ledger. No existing host was reused or terminated. Browser form-write acceptance for every step, a fresh PDF issue/download/restart chain and full B6 mutation browser acceptance remain unverified here.

## G. Performance and bundle

No speculative query rewrite or systemwide load benchmark was introduced. API request durations for the connected fixture are retained in `journey-result.json`; browser HTTP evidence is separate. Deals list persistence currently reads the authorized Workspace set before in-memory filtering/paging; source review identifies a scaling risk, not a measured new regression. No N+1 improvement claim is made.

Before the new guidance, largest chunk was 499.30 KiB. Customer guidance initially pushed it to 500.27 KiB and correctly failed the unchanged 500 KiB ceiling. Splitting the existing pure-data field-guidance content into a separate Vite chunk restored PASS: entry 297.26 KiB, largest 480.76 KiB at that checkpoint. The final canonical build gate provides the post-final-edit figures. No dependency or budget increase was added.

Final production build: **PASS**, entry **297.26 KiB**, largest chunk **481.10 KiB / 500.00 KiB**. Evidence: `frontend-all-gates/quality.build.log` and its canonical JSON report. This includes the final related-Deal panel and Quote source/guidance changes.

## H. Data preservation

The SQL server and database were checked against local `DESKTOP-ICH225R/UnicoreCRM_Development`, and machine identity was verified before mutation. Initial snapshot: **101,951 original rows across 160 primary-key tables**. It records per-row primary-key identity and SHA-256 over the entire SQL JSON row, including stored binary columns. Snapshot artifacts contain hashes/identities, not raw credentials or PDF/proof bytes.

Final comparison after all acceptance mutations: **zero missing original rows, zero changed original hashes**. Final total: **103,971 rows**, comprising the 101,951 original rows plus **2,020 new rows**. New synthetic business records, session/security records, read audits and authorization decisions are enumerated separately in `sql-preservation.json`. No new Order, Payment or Invoice rows were created. Existing IAM, roles, Pipeline configuration, demo records, PDFs, proof ciphertext, scanner ledger and identity/source/review versions are covered by the same original-row comparison.

Independent content check: **52/52 stored PDF documents** match their stored SHA-256 and file-size metadata. Preserved ledgers include 78 proof contents, 105 scan results, 69 source versions and 89 identity/review versions at inspection time. No proof was promoted to operational verification. No original immutable row was edited.

## I. Remaining issues with evidence

| Priority / owner | Business impact | Root cause / affected files | Narrow next fix / acceptance |
|---|---|---|---|
| P1 / Workspace seller + AccessControl owners | Main chain cannot issue authoritative PDF | Existing synthetic Workspace seller incomplete and actor lacks quotes.issueDocument; HTTP 403, document panel blockers | Provision a dedicated official end-to-end fixture with approved seller and explicit document permission through owner commands; issue/download/restart same linked Quote without changing user data |
| P1 / Quote approval + IAM owner | Required independent approval cannot be completed | High-value Quote has no eligible independent approver in fixture | Official independent membership/capability fixture, then request→independent approve→commercial binding invalidation and concurrency evidence |
| P1 / Architecture Governor + owner teams | Full backend quality cannot pass | Four exact baseline failures listed in C | Reconcile approved operation admission, namespace/contract owner maps and persistence assignments; remove actual foreign infrastructure dependencies per owner; all four guards pass without allowlist inflation |
| P2 / Frontend quality owners | Full frontend quality remains red beyond the three compiler diagnostics | Exact failures and logs in frontend-all-results.json and final appendix; source-based consumer inventories, compatibility retirement debt, copy/bilingual and other current guard findings | Review each against current approved execution paths; fix real product defects and reconcile exact metadata while retaining negative controls/budgets |
| P2 / Deals persistence owner | Possible scaling pressure in larger Workspaces | ListDeals Handler materializes authorized records before page/count; measured large-workspace regression not established | Measure representative CRM list/Kanban queries and narrow SQL query pagination if justified, preserving scope/FLS/count behavior |
| Contract blocker / Quotes + Deals owners | Operational VERIFIED→ACCEPTED→WON is intentionally unavailable | No admitted operational verifier receipt and atomic commitment orchestration | B6.5 and Gate C require separate approved tasks; do not implement within stabilization |

## K. Next roadmap (at most three tasks)

1. Provision one owner-approved full CRM acceptance fixture with seller, independent approver, document issuer and restricted actor; finish the same linked PDF/delivery/history and Workspace-isolation journeys.
2. Reconcile the backend architecture/admission/persistence authority baseline and frontend execution-aware quality metadata, fixing the remaining actual boundary/copy defects; retain current budgets and negative controls.
3. Close the operational verification and atomic commercial commitment contracts as a separately approved B6.5/Gate C task. No acceptance/Won write should open before those decisions and evidence exist.

## L. Git and mutation statement

No reset, checkout-to-remote, reseed, database reset, commit, push, merge, rebase or PR creation was performed. All existing local work was preserved. Mutations used official commands against synthetic identifiers; SQL was read-only. Only the processes started for this task were stopped. Final task-specific source deltas, hashes and gate results are supplied in the evidence directory; baseline dirty files are distinguished from this task's edits.

## Final verification appendix

| Check | Final result | Evidence |
|---|---|---|
| Backend build/full tests | 408 PASS / 4 confirmed baseline FAIL / 412 total; no skipped tests | `backend-final.log`, final-search-fls TRX |
| Frontend complete non-acceptance gate collection | **286 PASS / 42 FAIL / 328 total**, after actual focused reruns replace earlier results for six repaired gates | `frontend-final-results.json`, canonical per-gate JSON/logs |
| Final `npm run verify` | **FAIL**, 31 gates pass before WF-21 exact caller inventory fails at gate 32; later gates are not claimed to have run in this fail-fast invocation | `frontend-verify-final.json`, `frontend-verify-final.log` |
| Final lint/global TypeScript + strict checks | PASS 4/4 | `frontend-types-final-report.json`, `frontend-types-final.log` |
| API/generated client consistency | PASS, 360 operations / 334 ready / 26 explicitly blocked in pinned frontend contract; this is not proof that every actual Backend operation is admitted | `frontend-api-final.log`, `frontend-all-gates/quality.api-contract.*` |
| Document/backend readiness and dependency cycles | PASS; missing documentation registered, zero cycles | `frontend-all-gates/quality.backend-readiness.*` |
| Final repository inventory | PASS after regeneration | `quality.repository-inventory-final.json` |
| Guidance / presentation / pipeline ownership | PASS | matching `quality.*-final.json` reports |
| Production build/bundle | PASS, largest 481.10 KiB / 500 KiB | `frontend-all-gates/quality.build.*` |
| Real Connected fixture | 40 successful recorded checks in the final persistence/browser replay, two blocked steps: PDF issuance and independent approval; the final concurrency proof is a separate exact HTTP/SQL artifact | `journey-result.json`, `browser-http.json`, `concurrency-evidence.json`, screenshots |
| SQL preservation | 101,951/101,951 originals present and hash-identical across 160 tables | `sql-before.json`, `sql-after.json`, `sql-preservation.json` |

All 42 remaining frontend failures, their canonical owner group, observed error and exact gate logs are enumerated in `frontend-remaining-failures.md`. They include unresolved consumer/coordinator inventories, independent negative-control prerequisites, compatibility retention debt, bilingual/copy/form contracts, source/runtime fixture expectations, CSS loading in Node route smoke, and eight Backend contract-hardening failures. The latter expose stale operation counts/provider fixtures and missing metadata/error catalog entries for newer operations. No arbitrary operation-count or compatibility ceiling was raised. These current failures are not all labelled pre-existing: only the original three compiler diagnostics and four Backend failures have explicit entry execution evidence.

Failure distribution: architecture 6, contract 16, integration 9, route-smoke 3, backend-contract-hardening 8. The five critical browser-fixture gates passed within their own test scope; they are not substituted for the separate real Connected Main Journey.

The final source delta relative to the captured dirty entry tree is **6 Backend paths** (including the moved authorizer's old/new paths, FLS handler/test and this report) and **41 Frontend paths**. Exact paths and before/after SHA-256 values are in `source-delta.json`; generated repository inventory/compatibility metadata also captures earlier local structural drift and must not be attributed entirely to this task. `*-status-final.txt` preserves the final Git state.

Final browser evidence includes public Connected refreshSession, six linked record screens, Customer-related Deal after refresh, manual Activity History, logout/login and correct Deal-derived Quote source copy. The concurrent HTTP customer-response check returned **200 and 412**; `concurrency-evidence.json` retains both complete responses, their shared expected version, request correlation IDs and the post-race Quote read. A schema-valid forged `QUOTE_ACCEPTED` win request returned **422 DEAL_WIN_EVIDENCE_INVALID**, and the linked acceptance workflow returned **409 QUOTE_ACCEPTANCE_BLOCKED**. The final fixture remains SENT/UNVERIFIED (resource version 6 after the final race) and its Deal remains OPEN. The task-owned ApiHost and Vite processes were stopped after verification.
