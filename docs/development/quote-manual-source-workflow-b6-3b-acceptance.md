# B6.3B acceptance — 2026-10-27

**DEVELOPMENT_SIMULATION_FUNCTIONAL_PASS. Actual-source verification: BLOCKED_CONTRACT.** No operational issuer/channel has been approved. Human documentary cross-checks in this task are synthetic attestations, not cryptographic identity verification. B6.4/B6.5/Gate C remain unopened.

## 1. Source Admission

Before: registry could record DRAFT/REVOKED descriptors; admission remained blocked. After: existing registry supports independently approved, versioned simulation sources with exact Case/subject/issuer/authority/channel evidence. Edits return to DRAFT; revocation is terminal. Creator/latest evidence preparer cannot approve. No default grants. Source constraints migration `20261027015446_QuoteManualSourceAdmission` applied only to guarded local Development SQL; no new storage tables.

## 2. Manual Independent Source Check

Existing command now accepts structured observation, established channel, exact scope and separate effective-CLEAN confirmation proof. Backend computes bindings/provenance; actor and trust fields cannot be supplied. Checker differs from creator/preparer/approver, Quote creator/requester and subject recorder. Reusing proof ID or SHA cannot renew freshness. Freshness uses observedAt, at most 24 hours, honoring narrower Workspace policy.

## 3. Identity/Authority Readiness

The isolated Development Workspace can reach `SOURCE_CHECKED_MANUAL` and `READY_FOR_INDEPENDENT_REVIEW`, with `assuranceLevel=DEVELOPMENT_SIMULATION` and `actualSourceVerification=false`. Production/Staging cannot enable this path. Assessment rechecks current source/policy, CRM affiliation, scope/value/currency, Quote validity/timezone, approval, delivery and issued PDF bytes/hash. Revocation invalidates readiness; idempotency replay returns the current assessment. Root acceptance stays NOT_VERIFIED/BLOCKED, with no acceptance or Won authority.

## 4. API/SQL/Browser Positive & Negative Tests

Evidence root: `task-runtime/quote-identity-source-b6-3b/` (repository parent).

| Evidence | Result |
| --- | --- |
| `api-89369c76821641b0a029328bf366f89c/result.json` | Simulation admission/check/readiness, stale observation, self-approval/check denial, replay, restart PASS |
| `security-5cfb52f6d9ef46b79454aa2c75f39aca/result.json` | Approval/check concurrency each 200+412; revocation, fresh replay assessment, FLS/cross-Workspace, policy change and SQL rollback PASS |
| `advanced-2b94a0c01c954e9a9046448fbc5b1a0b/` | Scope mismatch, absent channel/proof, false trust fields, affiliation and real scanner INVALID_PDF/content denial PASS |
| `read-25db0b50a3564ce8bb4c8f7f96160a0e/` | Connected Playwright 2/2 PASS, actual API 5098/Vite 3044, no mocked API; approval → explicit version binding → manual check → READY → refresh/logout/login → revoke → AUTHORITY_INVALID |

Browser screenshots `browser/manual-source-ready.png` and `manual-source-revoked.png` record both states; revoked screenshot visually inspected. Earlier browser run also passed 2/2. Initial security harness exceeded the unchanged five-proof Case quota while preparing another browser proof; retained failed evidence, then corrected fixtures to use fresh Cases. No quota/security relaxation. Final ApiHost build: 0 warnings/errors. Final small replay guard additionally rechecks source approver/preparer separation before returning a receipt.

## 5. Security

Existing manage-source/record-check/assess and protected-content capabilities retained; no added default permission. Case/Quote record authorization, FLS, Workspace isolation and CRM owner lease remain mandatory. Exact SHA/source/subject/policy versions are server-bound. Real existing scanner and storage reused; scanner policy unchanged. No second PDF store, provider or acceptance override. Official provisioning/bootstrap creates isolated synthetic memberships; no SQL permission writes or real email. Credentials remain process-only; evidence scan recorded separately.

Final independent read-only review closed the stale-readiness UI finding and confirmed pre-replay separation checks; no material finding remained in that targeted review (`independent-review-final.json`).

## 6. SQL Preservation

`preservation-result.json`: all **6,045 original rows across 37 tables** retain their entry SHA-256; zero changed/missing. This includes original demo/checkpoint records, IAM credentials, memberships, Pipeline data, documents and proof bytes. Entry snapshots were not overwritten.

Before → after: Quotes 278→282; Cases 12→16; proofs/content 34→51; issued documents/content 41→45; Workspaces 75→77 (synthetic test Workspace plus nonmember companion). Deals remain 195. New rows are isolated synthetic fixtures; unsafe uploaded proof is retained/quarantined. All 45 issued PDFs match stored size/SHA (2,284,042 bytes total). Decisions remain 65 UNVERIFIED (51 acceptance reports/14 rejection reports). ACCEPTED Quotes/Won Deals/Orders/PaymentPlans remain **0/0/0/0**.

## 7. Regression

Final Backend suite: **381 PASS / 4 FAIL / 385**, versus entry 370 PASS / 4 FAIL / 374. All eleven new tests pass. The four failing names/messages exactly match entry (`baseline-failure-comparison.json`); they are existing architecture/admission failures, so the full suite is not green.

Frontend typecheck quality group 3/3 PASS; API contract and affected boundary/presentation/approval/delivery/guidance gates PASS; repository inventory PASS. Global tsc retains the same three entry diagnostics in `deal-quote-transition-outcome.test.mts:42` and `studio-first-visit.spec.ts:50,51`; no clean global-tsc claim. Bundle PASS: entry 296.73 KiB, largest 498.79 KiB under unchanged 500 KiB budget. OpenAPI still 357 operations, 331 ready/26 blocked; eleven existing contracts extended, no new route. B5/B6 storage/scanner and prior commercial guards are exercised in the real fixture chain; SQL preservation is stronger than aggregate counts alone. This is not a claim that every historical browser suite was rerun.

## 8. Remaining Blockers

Actual-source verification remains **BLOCKED_CONTRACT** until a real issuer, independently established channel, provenance/revocation method and allowed access scope are approved. A synthetic approved source cannot authorize operational verification. Existing unrelated Backend/tsc baseline failures remain. No legal retention conclusion is introduced.

## 9. Readiness for B6.4

B6.3B workflow is available for Development integration preparation. No VERIFIED/ACCEPTED/WON or commercial commitment receipt was introduced. Positive operational prerequisites are unresolved; B6.4/B6.5 require their own authorized checkpoint. No further source-system design is proposed here.

## 10. Git Status / File Changes

Both repositories retain entry HEAD/branch and prior local work. No reset/reseed, commit, push or merge. Task delta is captured relative to entry hashes in `final-source-manifest.json`, rather than attributing all pre-existing dirty files to this task.

Main changes: Quotes contracts, `QuoteManualSourceWorkflow`, identity evaluation/Case/source services and policy reader; EF confirmation reuse lookup and source constraints migration; existing OpenAPI/generated client/authority records; `QuoteManualSourceControls`, identity hook/panel/model/adapter; focused unit tests and actual Development API/browser harnesses. UI now refreshes assessment after source mutations and suppresses stale READY while refresh is pending or failed. No Dynamic Pipeline registry rewrite.
