# Accepted Quote → Order Core: local review

Review date: 2026-10-30, Asia/Saigon. Subject: actual dirty Backend and Frontend working trees in `D:/Project_All/UnicoreCRM`, including local uncommitted implementations. This report does not promote canonical pins or claim production acceptance.

## Outcome

O1 production conversion remains **BLOCKED_AUTHORITY / NOT_IMPLEMENTED**. The operation itself is admitted. Two separate dependencies remain: actual VERIFIED customer acceptance, and an admitted Quotes → Orders commercial snapshot/acceptance participant contract with the required dependency edge. The independently reproducible Order read defects were fixed and tested against an isolated real SQL database, real ApiHost and Chromium.

No conversion endpoint, acceptance bypass, Direct Order form, confirmation implementation, Deal WON transition or business schema was added. No commit, push, reset or graduation ZIP write was performed.

## 1. What is missing

| Area | Actual local implementation | Missing for O1 |
|---|---|---|
| Quote B1 | SQL draft/commercial data and PaymentAgreementJson | Accepted snapshot handoff to Orders |
| B2 internal approval | Version/content/policy binding and independent approval checks | Internal approval does not establish customer acceptance |
| B3 customer decision | Append-only employee-reported acceptance stays UNVERIFIED; rejection changes lifecycle | Trusted customer consent and verification |
| B4 lifecycle | Revision, expiration and archive rules | Conversion must consume those owner predicates atomically |
| B5 documents | Issued document binding, immutable bytes and commercial hash | Verified acceptance must bind to the exact document/revision |
| B6 | Proof storage/scanning, identity/source documentary workflow and independent review/readiness infrastructure | Readiness still returns BLOCKED_CONTRACT / NOT_VERIFIED / false; identity authority source blocker remains; manual simulation has ActualSourceVerification=false |
| Order domain/persistence | Owner-local SQL read state, buyer, lines/totals, sourceQuoteId/sourceDealId, version and read audit | Creation factory/handler, server number/ID assignment, immutable source version/fingerprint/evidence, command receipts, unique conversion key, transactional audit/outbox |
| Commercial snapshot | Quote has payment agreement storage | Order entity/SQL/read DTO currently omit Payment Agreement snapshot and source fingerprint/version. OpenAPI OrderReadModel also omits these fields; do not silently change pins |
| Source uniqueness | Nonunique WorkspaceId + SourceQuoteId index | Durable uniqueness plus locking and replay semantics |
| Order reads | Two authenticated GET routes and dedicated HTTP adapter | Fixed projection and cold-refresh defects described below |

Authoritative policy `DEC-P06-ACCEPTED-QUOTE-ORDER-CONVERSION` specifies **one durable Order per Quote**, rather than allowing a new Order whenever the Quote resource version or idempotency key changes. The request body is strict `{ quoteId }`; If-Match and Idempotency-Key carry concurrency/replay context. Buyer, prices, totals and identity must not be client overrides.

## 2. Can acceptance create an Order now?

No. `QuoteVerifiedAcceptanceAssessment` unconditionally returns NOT_VERIFIED and no positive acceptance mutation. `QuoteAcceptanceGuard` fails closed; a legacy ACCEPTED flag or employee report is not a verified commitment.

Canonical transaction contract explicitly separates acceptance and conversion: `acceptQuoteAndCloseDeal` **does not create an Order**. No automatic conversion event integration is admitted by that separation. Quote Detail already has a conditional connected conversion action, with deterministic intent/idempotency metadata and the authoritative Quote version; it was retained without inventing ACCEPTED state.

## 3. Operation admission versus implementation

All six rows below are `backendAuthorityStatus=ADMITTED`, `finalStatus=ADMITTED_NOT_IMPLEMENTED` in the canonical operation registry. Registry implementation status is stale for the actual two read routes; this review does not rewrite it.

| Operation | Actual runtime |
|---|---|
| listOrders | Implemented, GET /orders |
| getOrder | Implemented, GET /orders/{orderId} |
| convertAcceptedQuoteToOrderDraft | No endpoint/handler, POST probe returns 404 |
| acceptQuoteAndCloseDeal | Negative guard only; no successful production acceptance |
| confirmOrderWithPaymentPlan | No endpoint, canonical POST probe returns 404 |
| createOrderDraftCommand | Not implemented; direct creation was outside priority |

Sources: `design-authority/canonical-design/authority/operation-registry.json`; `.unicore-ai/manifests/operations/by-id/convertAcceptedQuoteToOrderDraft.json`; actual OrdersEndpoints/OrdersModule and Quotes acceptance code.

Admission of a wire operation does not authorize arbitrary dependencies. `cross-owner-contract-map.json` prohibits foreign persistence access and contains no accepted-commercial-snapshot participant for Orders. `dependency-allowlist.json` defaults to DENY and admits Orders → Quotes retention contracts only. Reading QuotesDbContext or inventing a cross-owner contract would violate the current boundary. The [unicore-backend builder skill](D:/Project_All/UnicoreCRM/.unicore-ai/skills/02-vertical-slice-builder/SKILL.md) explicitly requires: “AUTHORITY_GAP or ARCHITECTURE_BLOCK whenever implementation would require inventing business semantics, ownership or a new cross-boundary contract.” This is the reason creation implementation remains blocked despite operation admission.

## 4. B6 dependency

Production acceptance needs B6 completed with actual approved identity, signer authority and consent provenance; documentary simulation cannot supply these. Linked Quotes additionally have `LINKED_ACCEPTANCE_BLOCKED_GATE_C`, so B6 alone does not complete linked atomic acceptance/Deal closure. The verified commitment participant and the Gate C transaction must also exist.

Order owner-local code can be developed separately with explicitly synthetic fixtures after its snapshot/evidence handoff contract is admitted. Such fixtures cannot establish a production Quote→Order success claim.

## 5. Changes completed independently

- Fixed Order list server-page cache projection to enter the existing backend projection scope after an authoritative response. The connected repository guard remains enforced.
- Fixed collection/detail resources to consume CustomerOrder already mapped by OrderHttpApiAdapter/OrderModuleDataAuthorityBridge. Removed a second DTO mapping and the demo save path, which could require mutation capability or reprice a read.
- Fixed Order Detail hooks below its loading return. Cold refresh previously raised “Rendered more hooks than during the previous render.” Hooks now run in stable order.
- Updated the existing source contract check for the actual mapped resource boundary. Real browser regression exercises the behavioral failure.
- Repaired Order read verification for explicit --migrate / --seed-demo commands, current owner capability provisioning and Workspace-bound access fixtures. Retention contracts remain allowed; commercial lookups remain prohibited.
- Added optional real Chromium read verification, ApiHost restart/relogin snapshot comparison, absent conversion/confirmation probes and isolated evidence output. Existing databases are refused instead of recreated.
- Regenerated Frontend repository inventory for the source metrics; module/route/capability/workflow inventory was not changed by this task.

## 6. Verification

Evidence directory: `D:/Project_All/UnicoreCRM/task-runtime/order-core-o1-20261030`.

| Check | Result / scope |
|---|---|
| Backend build | Succeeded, 0 warnings, 0 errors |
| HTTP + SQL + Chromium harness | 291 assertions passed, 0 failed |
| SQL | Isolated UnicoreCRM_O1_20261030_Review_09 on local DESKTOP-ICH225R; 4 explicit Order fixtures, 0 ACCEPTED Quotes, 94 Order read audits |
| Order reads | Values/snapshots/lineage, pagination/search/filter/sort, FLS, record scope, workspace isolation, denied capabilities and corrupt persisted data exercised |
| Persistence | Real host restart + authenticated relogin; complete Order JSON unchanged; SQL row count unchanged |
| Chromium | Two independent login sessions; List, Detail and cold Detail refresh; six real Order GET responses returned 200; screenshots saved |
| Targeted .NET tests | 342 passed, 1 failed / 343. Existing endpoint metadata test reports unrelated local endpoints absent from adopted authority; no pins changed to hide it |
| Conversion success/retry/concurrency/stale version | BLOCKED / NOT_RUN: no conversion implementation or admitted source participant |
| Unverified Quote conversion denial | NOT_RUN as a semantic conversion test. Route absence is 404, not proof of a QUOTE_ORDER_CONVERSION_BLOCKED guard |
| Conversion transaction fault/rollback | BLOCKED / NOT_RUN: no transaction exists to inject a conversion failure into |
| Frontend production build | Succeeded; bundle budget passed |
| Frontend focused checks | Typecheck 3/3, strict regions/core, API contract, commercial authoritative queries, presentation responsibility, guidance, pipeline ownership and repository inventory passed |
| Frontend full verification | FAILED after 58 passed gates, at quality.compatibility-retirement: 60 candidates exceed the unchanged limit of 53. None of the files changed here is a compatibility candidate. Remaining full-pipeline gates were not executed. See frontend-verify.log |

Reproduction from workspace root after building Backend:

```powershell
& backend/scripts/verify-orders-read-core.ps1 `
  -DatabaseName UnicoreCRM_O1_NewUniqueDatabase `
  -SqlServer DESKTOP-ICH225R -Port 5349 -Browser `
  -EvidenceDirectory task-runtime/order-core-o1-replay
```

Fixture Orders are SQL-seeded test data, including an existing CONFIRMED read fixture. Their rendering and restart persistence do not prove accepted-Quote conversion or operational confirmation.

Only the final Review_09 isolated database was retained for inspection; the eight failed disposable setup databases were removed. The verification host and Vite processes were stopped. Full-pipeline failure thresholds and endpoint-admission pins were left unchanged. Local diff checks for the changed files passed; unrelated existing whitespace findings in other working-tree files were not edited.

## 7. Next steps for Order Confirmation

1. Admit the Quotes-owned verified commitment/commercial snapshot lease and its exact Orders consumer edge, authorization, workspace/FLS rules, version/expiry/revocation checks and transaction lifetime. Resolve Payment Agreement persistence/read exposure without an implicit pin change.
2. Complete actual B6 production verification and the necessary Gate C acceptance boundary; keep internal approval and employee reports separate.
3. Implement Orders-owned conversion under the admitted operation: strict request, immutable copied snapshots/evidence, server identity/number/version, workspace+Quote uniqueness, serialized source lease, idempotency receipt, audit/outbox and one atomic creation transaction. Initial state DRAFT; no implicit CONFIRMED or Deal WON.
4. Execute all positive/negative conversion HTTP/SQL/Chromium tests, concurrent intents/retries, stale versions, injected rollback and restart persistence against disposable SQL.
5. Implement admitted confirmOrderWithPaymentPlan via explicit Orders/Payments/Deals participants: version-matched DRAFT Payment Plan, backend credit-policy evaluation and approved-evidence consumption where required, payment instruction, agreed Deal closure semantics and atomic audit/outbox. Do not use UI readiness percentages as confirmation authority.

Status is local tested read remediation with production conversion blocked; no FROZEN or independent-review acceptance is claimed.
