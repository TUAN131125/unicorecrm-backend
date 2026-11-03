# Quote Core — verification record

Subject: local Checkpoint A, 2026-10-24. No commit, push or merge. This is local implementation evidence, not a production release or formal frozen acceptance.

## A. Baseline & Git State

Backend checkout is `D:/Project_All/UnicoreCRM/backend`, branch `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`. Frontend is `D:/Project_All/UnicoreCRM/frontend/unicorecrm-web`, branch `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`.

Both checkouts were dirty on entry (136 backend / 288 frontend porcelain entries, including collapsed untracked directories). Their exact tracked diffs and porcelain statuses are retained under `task-runtime/quote-core/{backend,frontend}-entry.*`. Source was inspected locally; no older GitHub checkout, reset, stash, cleanup or checkout replacement was used. `source-delta.json` distinguishes task changes from preserved unrelated dirty changes. Dynamic Pipeline source, registry, migrations and original data were preserved; the Deals module receives only Quote relationship DI registration and MarkWon protection.

## B. Architecture Review

Reused Quote read contracts, `quotes.Quotes`, read audit, workspace resolution, record evaluator, field projection, SQL/EF conventions and existing Quote UI/HTTP adapter. Quote Domain now owns Draft creation/content replacement. Server pricing uses `decimal` and the user-approved precision table: VND/JPY 0, USD/EUR 2, KWD 3, HALF_UP.

Added separate Products-owned commercial snapshot contract; the frozen six-field Lead Product reader is unchanged. CRM verifies buyers through Contact/Organization owner contracts. Its Deal source lease holds the existing Deal aggregate lock in a read-only owner transaction until Quote commit. Quotes never queries CRM/Product foreign tables or DbContexts. No reverse Sales→Crm project reference or distributed transaction was added. Workspace exposes a narrow Quote enablement/currency policy reader.

The user's revised decision is recorded in `docs/development/quote-core-contract-proposal.md`. New Product-linked prices/taxes must match owner authority; Quote write permission does not grant Product catalog-edit or price-override permission. Unchanged captured pricing survives subsequent catalog changes during Draft editing. Snapshot provenance and workspace policy version are retained in immutable outbox evidence.

## C. Backend Commands

| Operation | Route | Result |
|---|---|---|
| createQuoteCommand | POST /quotes | Implemented; Direct Sale creation, 201 |
| createQuoteForDeal | POST /deals/{dealId}/quotes | Implemented; OPEN/unarchived owner-verified source, Deal If-Match, 201 |
| updateQuoteDraftCommand | POST /quotes/{quoteId}/update-draft | Implemented; DRAFT-only, Quote If-Match, 200 |

All three require trusted Workspace/actor, current capability/FLS, Idempotency-Key and server validation. Mutations return authoritative identity, number, totals, ResourceVersion, command/audit/event IDs and UTC time. Resource actions `quote.create` and `quote.update` are declared through AccessControl. Retry returns stored commitment evidence under current record access and read projection. Reusing a key with different content conflicts.

QuoteRevision starts at 1 and does not change during Draft replacement. ResourceVersion starts at 1 and increments on accepted replacement. Quote creation changes no Deal stage or activity. Approval-bound Drafts, archived Quotes and non-DRAFT lifecycle states cannot be edited by this core.

## D. Data Model & Migration

Additive migration `20261023200111_QuoteDraftCore`: nullable Product snapshot JSON, immutable Quote command/audit/idempotency records, immutable outbox, SQL number sequence and unique Workspace/QuoteNumber index. ResourceVersion is an EF concurrency token. Number allocation uses SQL sequence, never record count or frontend time; rollback gaps are allowed. Legacy numeric Q- numbers are considered when initializing the sequence.

The verified target remained `DESKTOP-ICH225R / UnicoreCRM_Development`. No new database, connection-string change, MDF/LDF, CRM seed, fixture deletion or old migration edit. The initial Quote table was empty; migration retained count 0. CRM count/hash preservation is recorded before migration and after API/browser runs. The demo Workspace retains 1,050 records including 180 Deals, with unchanged SHA-256 hashes for all nine business tables. Quote persistence ownership is explicitly assigned in `.unicore-ai/manifests/persistence-ownership.json`.

Initial migration execution exposed a SQL statement construction syntax error; that unapplied migration transaction rolled back. The new migration was corrected before its successful application. The initial empty-set hash comparison also required normalizing SQL DBNull; no business data loss occurred. EF reports no pending model changes.

## E. Pricing Evidence

`acceptance/api-result.json` records actual request/response bodies and SQL readback. Verified server examples in VND:

| Case | Authoritative result |
|---|---|
| 2 × 100000, 10% line discount, exclusive 10% tax | subtotal 200000; discount 20000; tax 18000; total 198000 |
| 110 inclusive 10% | tax 10; total 110 |
| 2 × 100, no tax | total 200 |
| 1.5, VND HALF_UP | total 2 |
| Zero price | total 0 |
| 2 × 1000000000000000000 | total 2000000000000000000 |
| Mixed lines, line/document discounts, fee and document tax | subtotal 360; discount 54; tax 59; total 360 |

Negative, exponent, excess decimal scale, currency mismatch, excessive discount and overflow are rejected. Focused tests also cover USD/EUR/KWD precision. Payment previews are calculated from the backend total; caller previews are not accepted as input. Existing payment IDs survive unchanged term replacement. Untouched existing agreement command content retains exact decimal strings, FIXED amounts and advanced due rules. Terms that cannot round-trip faithfully through the current controls are read-only with a bilingual notice; unrelated Draft edits preserve them. This restriction applies to connected authoring, retaining existing demo behavior. Frontend retains exact decimal inputs for mutations, treats numerical calculations as preview, and displays a reconciliation notice when the server result differs.

## F. Security & Authorization

Canonical `quotes.create` / `quotes.update`; no new capability name. Workspace membership and actor are server resolved. Owner must be an active member; legacy ownerless rows remain inaccessible to OWN scope. OWN lists apply the owner predicate in SQL. TEAM/CUSTOM remain denied where AccessControl lacks facts.

Current record scope and FLS are checked before replay; optional read fields are projected under current policy. Required hidden fields fail closed. Unauthorized replay, restricted user, foreign Workspace, unknown buyer/Deal, catalog price substitution and stale Quote version are refused. Real existing Deal, Contact Buyer and Product IDs from a different Workspace were also submitted under the control Workspace and rejected (404/422/422). Two competing updates at version 2 produce exactly one 200 and one 412; concurrent same-key creation produces one COMMITTED and one REPLAYED response for the same Quote. Separate concurrent creates receive distinct numbers.

## G. Deal Integration

Deal-based create derives and fixes BuyerRef/SourceDealId/source references from the owner lease; request cannot supply buyer/source-path identity. The Deal must be OPEN/unarchived at creation and match If-Match. The owner lock remains held through Quote SQL commit. Linked GET /quotes?sourceDealId=... returns the new document; every original Deal hash stays unchanged.

Frontend reuses the existing builder, picker, list/detail, authoring lifecycle and Deal quote area. Deal creation uses the dedicated API and the opening Deal version; Draft edits use the editor's opening Quote version. Success adopts the returned Quote; no browser Quote number/version/status is promoted to authority.

## H. MarkWon Security

Both QUOTE_ACCEPTED and ORDER_CONFIRMED are fail-closed because no authoritative commitment verifier is implemented. Handler checks trusted access, record scope, write FLS and expected version, then rejects the unverified evidence. It never invokes mutation execution or historical unverified replay. No client-supplied ID or timestamp can close Won through this endpoint.

`connected-guard/` contains actual requests/responses and before/after business hashes, plus committed Deal audit/idempotency/outbox counts. Owner-state verification and successful acceptance/confirmation remain BLOCKED_CONTRACT; acceptance/order lifecycle was not implemented. Descriptive unit-test IDs are not claimed as persisted SENT/ACCEPTED fixtures.

## I. Connected E2E

Browser acceptance uses isolated Chromium, private frontend 127.0.0.1:3044 and ApiHost 127.0.0.1:5098. No API mocks or personal browser/Windows control. Actual create/update response schemas are validated. Evidence lives under `task-runtime/quote-core/acceptance/browser/`.

Connected browser flow PASS (1 test): actual login → Deal → Create Quote → owner Product → save → detail → Deal linked quote → logout/login → edit Draft → refresh. SQL confirms DRAFT, ResourceVersion 2, total 300000 VND and correct source Deal. Final browser identity and SQL readback are recorded in `browser-result.json` and `sql-result.json`. Earlier iterations found a duplicate test selector, missing resource-action admission, UTC serialization mismatch and an undeclared action blocker; these were corrected without weakening contract/security checks. The Quote save now waits for its own clean form/unsaved-work state before navigating, preserving the global navigation guard. Cold-start waits are bounded explicitly. Browser console includes initial anonymous 401 and the existing CrmAiFloatingUtility/DealFollowUpTasks render warning; no fatal page error occurred. Failed attempts remain synthetic fixtures; no cleanup deletion is performed.

| Acceptance | Case | Evidence/result |
|---|---|---|
| QT-01 | Direct Sale create | PASS API/SQL |
| QT-02 | Deal create | PASS API/browser/SQL |
| QT-03 | Product authoritative snapshot | PASS API/browser/SQL; override rejected; catalog history retained |
| QT-04 | Manual line | PASS API/SQL |
| QT-05 | Multiple lines | PASS API/SQL |
| QT-06 | Exclusive tax | PASS API/SQL |
| QT-07 | Inclusive tax | PASS API/SQL |
| QT-08 | Discounts/adjustments | PASS API/SQL |
| QT-09 | Currency | PASS API + precision unit tests |
| QT-10 | Edit Draft | PASS API/browser/SQL; version 1→2, revision remains 1 |
| QT-11 | Refresh | PASS browser/API |
| QT-12 | Logout/login | PASS browser/API |
| QT-13 | Backend restart | PASS API/SQL |
| QT-14 | Retry create | PASS sequential + concurrent same-key |
| QT-15 | Stale version | PASS 412 |
| QT-16 | Concurrent updates | PASS one 200 + one 412 |
| QT-17 | Restricted user | PASS API + FLS/OWN unit tests |
| QT-18 | Cross Workspace | PASS API |
| QT-19 | Foreign Deal | PASS actual foreign ID + unknown ID API |
| QT-20 | Invalid Buyer | PASS foreign/unknown Buyer API |
| QT-21 | Immutable Quote edit | PASS focused unit guard; connected SENT/ACCEPTED/archive fixtures BLOCKED |
| QT-22 | Deal linked Quote | PASS browser/API/SQL |
| QT-23 | Original demo preservation | PASS unchanged 9 table hashes / 1050 records / 180 Deals |
| QT-24 | Invalid MarkWon evidence | PASS connected rejection + 14 guard tests; positive owner commitment verification BLOCKED_CONTRACT |

Checkpoint A core connected create/read/edit functionality is verified. Overall Functional PASS / formal frozen acceptance is not asserted while the QT-21 connected fixture gap and quality/contract gaps remain.

## J. Quality

See exact commands/logs/TRX under `task-runtime/quote-core/`. Build, focused tests, migration model consistency and actual API/SQL assertions have separate evidence. Full backend architecture suite is not green: baseline owner/layer/endpoint metadata failures remain. Quote persistence metadata was corrected; new owner namespace diagnostics under the stale global module map are disclosed separately. Targeted Quote boundary tests enforce no foreign persistence and no reverse CRM dependency.

| Check | Final outcome |
|---|---|
| Backend focused Quote / Product / CRM verifier / MarkWon tests | PASS 87/87 |
| Backend complete architecture suite | FAIL: 139 passed / 4 failed / 143 total |
| Backend build / EF model consistency | PASS / no pending model changes |
| Connected API/SQL including real foreign records | PASS |
| Successful API create/update response schemas | PASS 17/17 |
| Connected Chromium flow + SQL readback | PASS 1/1 |
| Connected MarkWon guard / unchanged Deal evidence counts | PASS; successful owner-verified Won unavailable |
| Frontend strict typecheck group | PASS 3/3 |
| Quote tests, guidance, presentation responsibility, mutation authority, API, security scan, repository inventory | PASS individually |
| Frontend production build / bundle budget | PASS; entry 290.98 KiB, largest chunk 492.96 KiB < 500 KiB |
| Frontend full verification / global tsc | FAIL: same 3 pre-existing test diagnostics |
| Frontend quality-pipeline / backend-readiness | FAIL: baseline 348 vs 345 gate-count assertion / unrelated unregistered Studio connection docs |

Complete backend failure comparison: layer/workflow 138 unchanged messages; endpoint admission 55 unchanged; durable owner 16→15 (Quotes mapping repaired); owner-map 549→553, with four additional namespace diagnostics for the newly added Product/Deal verifier namespaces. The global registry still describes an obsolete layer-first topology. Those four new diagnostics remain unresolved metadata gaps; they are not presented as an exact-baseline PASS. See `backend-final-failure-comparison.json`.

Global frontend tsc diagnostics: `tests/contract/deal-quote-transition-outcome.test.mts:42` nullable model; `tests/e2e/studio-first-visit.spec.ts:50,51` optional string. A transient concurrent-build tsc artifact lookup failure was rerun serially; the final log contains only these three baseline diagnostics.

Frontend app typecheck, focused Quote/presentation/API/guidance/mutation checks and production build are reported individually. Full verification remains blocked by the three pre-existing test TypeScript diagnostics and other recorded baseline quality debt. No assertions or budgets were weakened to claim success. Formal trusted-gate/frozen release acceptance is not claimed.

## K. Remaining Contract Gaps

- Quote Approval, delivery, Acceptance, conversion, Order and Payment execution remain unavailable.
- MarkWon cannot succeed until Quote/Order owners provide commitment verification; immutable evidence must never be replaced by client assertions.
- The global AG-PRODUCT-SNAPSHOT registry still covers Deal line items and direct Orders. The user decision and implemented owner contract resolve the Quote Draft leg locally; registry-wide reconciliation is outside this checkpoint.
- Product price override has no admitted policy; it is refused. Manual lines remain supported under Quote write/FLS validation.
- Direct-sale SourceLead verification is unavailable; unsupported references fail validation.
- Input description schema permits 4000 characters while Quote read snapshot permits 2000. Core refuses descriptions exceeding the faithful read limit instead of truncating or returning an invalid response.
- Only the five agreed currency precisions are supported and currencies must also be enabled by Workspace configuration; no automatic conversion.
- Connected immutable SENT/ACCEPTED/expired source fixture probes that lack real owner-issued fixtures are BLOCKED, with unit guard coverage reported separately.

## L. Git Status

No commit/push/merge. Existing dirty changes remain in place. `source-delta.json`, final porcelain statuses and source hashes enumerate the exact task delta and retained baseline changes. 16 tracked backend files and 19 tracked frontend files differ from entry; 56 backend and 183 frontend dirty tracked diffs remain byte-identical to entry. New owner contracts, pricing/command code, additive migration, tests, verification scripts and this report remain reviewable locally. `source-hashes.json` covers current changed tracked/untracked files; untracked inventory includes pre-existing files and is not labeled entirely task-created.

Reproduction:

```powershell
cd D:\Project_All\UnicoreCRM\backend
dotnet build src/UnicoreCRM.ApiHost --no-restore
dotnet test tests/UnicoreCRM.ArchitectureTests --no-restore --filter 'FullyQualifiedName~Quote|FullyQualifiedName~MarkDealWonSecurityTests'
pwsh -NoProfile -File scripts/verify-quote-core.ps1 -Mode Api
pwsh -NoProfile -File scripts/verify-quote-core.ps1 -Mode Browser
pwsh -NoProfile -File scripts/verify-quote-core-guard.ps1
```
