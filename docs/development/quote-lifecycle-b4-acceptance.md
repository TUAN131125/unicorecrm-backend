# Gate B4 — Authoritative Quote Revision, Expiration & Archive

Verification date: 2026-10-25. Scope: user-approved package A. Evidence root: `D:/Project_All/UnicoreCRM/task-runtime/quote-lifecycle-b4/`.

**Revision: Functional PASS. Expiration: Functional PASS. Archive: Functional PASS for implemented paths, with the live existing-obligation negative NOT_RUN.** This is not acceptance of the entire Quote Lifecycle or Commercial Release. Global repository quality is not green; baseline failures are listed below.

## A. Git & baseline

Backend `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both were already dirty. Entry status, file hashes, missing files, HEADs and SQL hashes were captured before implementation. No raw patch or credential snapshot was taken. The pre-existing deletion of `src/components/access/ReadOnlyAccessBanner.tsx` remains intact.

Entry SQL: 154 Quotes, 154 roots, max business revision 1, no archived Quotes, no Orders/PaymentPlans/QUOTE_ACCEPTED Deal evidence. Protected entry rows span 27 tables. `protected-preservation-result.json` compares every entry key/hash, including original CRM/demo, Dynamic Pipeline and A/B1/B2/B3 evidence; additions are reported separately. Original rows are not replaced by count-only checks. Final SQL: 218 Quotes, zero duplicate root ordinals or duplicate working roots; zero Orders/PaymentPlans/accepted Deal evidence. Test additions, including retained failed harness attempts: 64 Quotes, 38 customer decision records, 224 command/outbox records each, 6 synthetic Workspaces, 4 memberships, 9 roles, 1 contact, 1 Product, 2 Deals, 1 default Pipeline and its 7 stages. These additions do not alter any entry row.

## B. Approved policy

See `quote-lifecycle-b4-policy-decisions.md` for the user's package A decision. Revision sources: unarchived SENT/REJECTED/EXPIRED; one working DRAFT/REVIEW per root; new ID/number; increasing root ordinal; historical price capture retained; no inherited approval/delivery/customer evidence. Linked Deal must remain OPEN/unarchived in the same Workspace.

Expiration: explicit SENT→EXPIRED only after the inclusive ValidUntil local date, using Backend time and authoritative Workspace timezone. GET never persists lifecycle changes. Overdue commercial actions fail even while persisted status is SENT.

Archive: DRAFT/REVIEW/REJECTED/EXPIRED only, required reason, durable actor/time/version/audit/outbox; immutable commercial history. All three downstream owners must return clear proof. Missing/unavailable proof fails closed. No restore, hard deletion, silent stage/pipeline migration or Deal Lost/Won transition.

Backend configuration is explicit per Workspace: `QuoteLifecyclePolicies:Workspaces:<workspaceId>:{Version,AllowRevision,AllowExpiration,AllowArchive}`. All flags and a version are required; semantic policy content is hashed into the effective version. There is no global allow fallback or Studio redesign. Verification runners configure only their isolated Development process; a deployed host must be configured for each intended Workspace before B4 actions are available.

## C. Architecture and changes

Quotes owns `QuoteRevisionCommands`, `QuoteExpirationCommands`, `QuoteArchiveCommands`, domain transitions, snapshot reconstruction, policy reader, validity projection, HTTP validation and SQL transaction. Root and aggregate application locks serialize mutations; a unique `(WorkspaceId,RootQuoteId,QuoteRevision)` index prevents duplicate lineage ordinals. Case variants of ASCII entity IDs share the same SQL application lock.

New narrow `IQuoteRetentionVerifier`/lease contracts return Clear/Blocked/Unavailable. Deals, Orders and Payments implement verification in their own persistence boundaries, repeat authorization and hold SERIALIZABLE read/range leases through Quote commit. Quotes never reads their tables. Twelve exact contract dependency edges are admitted; default deny remains. Billing references the Sales public contract assembly, not its persistence.

Three additive migrations were applied to verified local Development: `20261025051129_QuoteRevisionLineage`, `20261025053037_QuoteRetentionReference` (Payments computed source projection/index), `20261025053042_QuoteRetentionReference` (Deals evidence index). Existing migrations were not edited. These schema changes do not create Order/Payment workflows.

Five operation admissions/shards and the project extension describe the approved policy. Shard-integrity hashes are synchronized only for those five changed shards; canonical OpenAPI baseline/checksums remain unchanged. Their provenance checks return CURRENT_VERIFIED. Frontend OpenAPI remains 330 operations, 304 ready / 26 blocked, with additive read filters/hints/validity.

## D. Revision evidence

`api-32c88969d33c4371878a3633c9ff3edd` (21 requests) proves new identity/number/root/parent, DRAFT/RV1, source full-row immutability, replay, changed payload rejection, stale If-Match, one working revision and two concurrent requests yielding 201+409. Product catalog price changes from 250000 to 500000 while the revision retains the captured 250000 and authoritative Product snapshot. Draft editing increments resource version, not business revision.

`api-778d604b3d054e9f878f48a078aef2a4` proves root revision 3 after archiving working revision 2, revision creator evidence distinct from inherited owner, new B1 assessment and independent approval, and unchanged linked Deal row. `api-50f3623062994f83b4ef224672269f74` additionally proves REJECTED→new DRAFT, retaining the rejected source's two customer records and one delivery. EXPIRED source revision is proved by the expiration runner.

Shipping snapshots normalize the persisted read type SHIPPING into pricing input SHIPPING_FEE; financial equality is checked before creation. Payment previews are rebound to the new Quote with fresh identities, without accepted evidence. Copied nonnull sender fields require target write FLS.

## E. Expiration evidence

`api-6c9911dcbae2462b8b4674615a069823` (16 requests) uses isolated quote clock instants immediately before and at Ho Chi Minh midnight. Before midnight the Quote is valid; after midnight GET reports OVERDUE while SQL remains SENT. Send is blocked, explicit expire commits EXPIRED, and history is preserved. Replay produces one event; stale requests return 412; invalid states fail. Restart/login readback remains EXPIRED.

The optional verification clock is Development-only, scoped to one exact configured Workspace and never replaces global TimeProvider/JWT/SQL/OS clocks. Invalid/missing timezone fail-closed is covered by focused tests. A malformed-timezone Connected fixture is NOT_RUN because the Workspace owner rejects invalid timezone configuration; no foreign-table corruption was used to simulate it.

## F. Archive evidence

`api-50f3623062994f83b4ef224672269f74` (31 requests) covers all four eligible states, reason validation, permission denial, replay/key reuse, stale versions, SENT denial, archived revision denial, active/archived/all-root list readback, retained approval/delivery/customer history, exact archive event counts and host restart.

Before the Payments projection migration, `api-534409f448f74045bff01ea1d9fd0db0` proves actual unavailable owner proof returns 409 QUOTE_ARCHIVE_BLOCKED / BLOCKED_CONTRACT, with no archive mutation, receipt or event. This historical negative must not be rerun expecting a missing schema after migration.

Live present downstream-obligation rejection is **NOT_RUN**: entry has no Orders, PaymentPlans or QUOTE_ACCEPTED Deal evidence, and the task forbids creating their workflows or inserting fake SQL evidence. Focused tests cover Blocked from each owner, missing/duplicate/unavailable participants, leases held through commit and reverse disposal. Clear proof from all three real owners is exercised by successful API archives. ACCEPTED archive denial is tested in domain tests; a verified ACCEPTED API fixture remains unavailable by design.

## G. Frontend

List adds active/archived/all search, pagination and Workspace scoping. Detail shows authoritative validity separately from persisted EXPIRED, immutable revision chain (including archived), archive metadata and contract/permission-gated actions. Direct editing of archived records is blocked. Archived shared actions become read/export only. Old source detail is refreshed after revision because its root-level action availability changes even when its resource version does not.

Lifecycle forms capture the opening resource version/Workspace, use stable attempt identities, prevent pending duplicate submission and register unsaved work. A revision navigates to the committed new Draft. VI/EN List and Detail guidance/version are updated. No client status mutation or implicit GET expiry is added.

## H. API / SQL evidence

Each runner records actual method/path/status/body/correlation IDs and independent SQL row/event readback. `response-schema-validation.json` validates successful and rejected responses against the full OpenAPI with references resolved; no mocked API. The latest report includes exact validated counts. Fixtures and failed harness attempts are retained and distinguishable by result.json; failed directories are not counted as successful acceptance.

SQL preservation is byte-hash based per original primary key. New synthetic Workspace/role/Quote/contact/Product/Deal rows are explicitly counted separately. Fixture provisioning uses official Development Workspace/AccessControl and owner commands; SQL use is read-only except the three approved EF migrations. The B4 short-role environment override was corrected to clear longer inherited arrays; only the new B4 approver assignment was reconciled through official AccessControl. Existing demo memberships/permissions are unchanged.

## I. Connected browser

Dedicated Chromium runs real frontend→API→SQL. Four journeys: revision→Builder edit/save→old source history/disabled repeat action→refresh/logout/login; REJECTED archive→retained evidence→archived search→direct edit blocked; restricted actor/foreign Workspace; overdue SENT→explicit EXPIRED→refresh/logout/login→archive EXPIRED. Desktop final evidence: `api-a53762c821984902a15e0cd951a7b6ce` (4/4 PASS). Final 390×844 archive/detail/search evidence is `api-d239abb2b8874f99b205f4a9f1efa30b` (4/4 PASS), with the same SQL assertions. `browser-normal/archived-search.png` was visually inspected and shows the retained result in the mobile card layout. The early `archived-mobile.png` catches the closing overlay animation and is excluded from visual QA evidence.

An earlier final run exposed stale source action cache. Resetting the source resource interrupted modal navigation; retaining the detail while refreshing fixed both navigation and availability. Both failed runs are retained. The corrected four-journey run passes. No personal browser profile was used.

## J. Security

`api-778d604b3d054e9f878f48a078aef2a4` (26 requests): all three mutations in another configured Workspace return 404; FLS-restricted revise replay/expire/archive return 403; independent B1 approval succeeds while creator/requester self-approval remains forbidden. No Studio privilege is granted to satisfy retention. Unknown request fields, fake expiration time and missing reason are rejected. Missing policy fails closed. Read access to valid historical records remains available.

## K. Concurrency and idempotency

Revision 201+409 root-working race, stable replay/new-identity guarantees and stale 412 are verified through real API. `api-72d021717937464fa90c83e330454c30` proves mixed-case simultaneous Archive paths yield one 200 and one 412, SQL RV2, one canonical QuoteId receipt and one archive event. Owner leases protect absence proof against concurrent downstream insert paths. SQL index plus root lock enforces ordinal uniqueness. No claim of unmeasured distributed load or performance benchmark.

## L. Regression

202 focused Quote tests pass, covering Core/B1/B2/B3 and B4. Full Backend: 268 pass / 4 fail / 272 total versus entry 231 pass / 4 fail / 235 total. The four failing test diagnostic sets are identical (`final-baseline-failure-comparison.json`); no new diagnostic is hidden. Actual B4 fixtures run existing create/edit, B1 assessment/independent approve/self-approval refusal, B2 send, B3 UNVERIFIED/reject. MarkWon/verified acceptance remain fail-closed and unchanged. Original Dynamic Pipeline rows and original Deal rows are hash-preserved; no Registry rewrite.

## M. Quality and acceptance matrix

Build: PASS, zero warnings/errors. Frontend strict group: PASS 3/3. API contract/generation, management boundaries, frontend separation, Quote API boundary, approval/delivery contracts, presentation responsibility, guidance, mutation authority, retention and secret scan: PASS. Regenerated repository inventory: PASS. Production build/budget: PASS, entry 291.49 KiB; largest guidance chunk 496.93 KiB versus B3 495.43 KiB (increase 1.50 KiB), limit unchanged 500 KiB.

`npm run verify`: FAIL at lint due to the same three pre-existing TypeScript diagnostics: nullable model in deal-quote-transition-outcome.test.mts:42 and undefined strings in studio-first-visit.spec.ts:50/51. It stops before other groups, which are not claimed as executed by this command. Focused groups are run independently.

Two additional existing governance failures are reported separately: quality-pipeline expects 345 while unchanged manifest has 348 gates; backend-readiness document-status list omits two existing Studio documents. Both scripts and their input manifests/documents hash-identically match entry. No threshold or test was weakened to hide them. Repository inventory was intentionally regenerated for the new B4 files and passes.

| Case | Verdict | Evidence |
|---|---|---|
| B4-01 SENT revision | PASS | Revision API/browser |
| B4-02 REJECTED revision | PASS | Final archive API |
| B4-03 EXPIRED revision | PASS | Expiration API |
| B4-04 Archived revision blocked | PASS | Archive API |
| B4-05 Number/ordinal unique | PASS | API/SQL concurrency |
| B4-06 Root preserved | PASS | Revision API/SQL |
| B4-07 Parent preserved | PASS | Revision API/SQL |
| B4-08 Old document immutable | PASS | Full-row hashes/browser SQL |
| B4-09 No approval inheritance | PASS | Independent revision B1 API |
| B4-10 No SENT/delivery inheritance | PASS | API/browser |
| B4-11 No customer evidence inheritance | PASS | API/browser |
| B4-12 Historical Product price | PASS | Real Product change + revision |
| B4-13 Concurrent revision | PASS | 201+409, one SQL revision |
| B4-14 Idempotent revision | PASS | Same target/one event |
| B4-15 Stale version | PASS | API 412 |
| B4-16 Inclusive local date | PASS | Isolated-clock API/unit |
| B4-17 After-midnight expiry | PASS | Isolated-clock API/SQL |
| B4-18 Invalid timezone | PASS unit; NOT_RUN Connected invalid config | Workspace authority rejects malformed fixture |
| B4-19 Persist expiration | PASS | API/SQL/browser |
| B4-20 Duplicate expiration | PASS | Replay/event count |
| B4-21 Invalid expiry state | PASS | API/unit |
| B4-22 Archive four eligible states | PASS | API/SQL |
| B4-23 Invalid reason | PASS | 422/disabled confirmation |
| B4-24 Archive permission | PASS | 403/no mutation |
| B4-25 Historical readback | PASS | API/browser/restart |
| B4-26 Linked archive | PASS | Full Deal row unchanged |
| B4-27 Workspace isolation | PASS | Configured foreign Workspace 404 |
| B4-28 FLS | PASS | Restricted API + sender unit |
| B4-29 Persistence | PASS | Refresh/logout/login/host restart |
| B4-30 Original data | PASS | 27-table entry-key hashes |

## N. Remaining backlog / limitations

Verified Customer Acceptance, Deal Won evidence integration, narrow Document Issuance and trusted provider delivery retain BLOCKED_CONTRACT. No Restore, Gate C, Order or Payment workflow is opened. Existing-obligation Connected fixture and malformed-timezone Connected fixture remain unexecuted for the reasons above. No new synthetic evidence is treated as a real commercial commitment. Policy enablement/configuration must be deployed explicitly per Workspace. No all-green repository/release claim is made.

## O. Final Git status and file inventory

Entry/final status and full exact B4-relative file deltas are in `backend-final-delta.json` and `frontend-final-delta.json`, including untracked additions. Main groups are Quotes commands/domain/read/HTTP/persistence, three owner verifiers and additive migrations, focused tests/verification scripts, frontend OpenAPI/generated artifacts, Quote List/Detail/Builder/panel/query/service/action guards, guidance and generated repository inventory. Root authority changes are limited to package A extension/operation admissions, exact dependency edges and corresponding shard integrity.

No original file is deleted by B4. Prior local changes remain; task edits extend relevant files. Both HEADs remain at entry commits. No commit, merge, reset, reseed, demo refresh, hard delete or credential publication occurred. Test-only processes are stopped in finally blocks; process environment overrides are restored. See final-verification.json for the last measured outcomes and preservation counts.
