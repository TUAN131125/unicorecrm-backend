# Gate B6 — Verified Customer Acceptance: verification report

2026-10-26, Asia/Saigon. **PARTIAL — independent policy/binding/readiness/negative-assessment slice checked. Positive VERIFIED / Direct Sale ACCEPTED remain BLOCKED_CONTRACT. Linked acceptance/Won remain BLOCKED_GATE_C.** No full B6 or B6_VERIFIED_EVIDENCE_PASS claim.

## A. Git & Baseline

Backend master HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend feat/deal-compact-kanban-card HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`, unchanged. Both repos dirty at entry; fresh2845Backend/2695Frontend file hashes captured before code. Local source is authoritative implementation evidence, not GitHub.

SQL verified exact local Development:44Workspaces,251Quotes,193Deals,21issued PDFs,55customer decisions,0Accepted/Won/Orders/PaymentPlans. Original29table row hashes include B5 Documents and DocumentContents; no raw credentials/bytes copied for hashing. Final original rows changed/missing: **0**. The original CRM1,050/demo180Deals and later A/B1–B5 fixtures are covered by this superset. [sql-entry-review.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/sql-entry-review.json), [sql-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/sql-final-preservation.json), [source-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/source-final-preservation.json).

## B. Policy Decisions

Option B approved with user amendments; A portal/C provider compared in [policy decisions](D:/Project_All/UnicoreCRM/backend/docs/development/quote-verified-customer-acceptance-policy-decisions.md). Separate submit/read-proof/verify/accept permissions, verifier≠Recorder/Creator/ApprovalRequester, exact Workspace/record/source FLS. Initial VND limit≤100m uses authoritative GrandTotal; currencies require explicit policy, noFX. Config is versioned by effective values, missing/invalid exact-Workspace config fails closed.

**24h is a preacceptance authority freshness rule. It never automatically invalidates historical committed acceptance.** Actual revocation/invalidation of a past acceptance requires a separate lifecycle policy. No expiry worker, historical status mutation or restore workflow added.

## C. Identity Architecture

IAM active accounts/email verification/sessions and CRM Buyer affiliation facts are reusable but do not prove customer identity or legal representation. No approved concrete identity/authority source adapter is registered. No issuer is invented from config/payload, ContactId, matching email or a staff claim. `BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE` is unconditional in the current assessment. No new IAM, portal, customer membership, KYC or paid provider was created.

## D. Signer Authority

B6 documentary checklist/provenance/source requirements remain mandatory. Authority interval, scope, recorded revocation and24h preacceptance rules have focused tests; these tests use facts to exercise the rules and **are not Connected verification of a real signer**. Since actual authority evidence/owner is absent, no Recorder identity is inferred from a client field and no case can become VERIFIED.

Existing Quote creator and Approval Requester are checked for negative VERIFY-intent assessment; creator/requester gets403 SELF_VERIFICATION_FORBIDDEN even if explicitly granted verify capability. The Recorder-separation positive workflow cannot be exercised without a real stored case; it is not marked PASS.

## E. Issued Document Binding

Assessment requires explicit DocumentId; no automatic latest-PDF selection. Reuses Quote-owned B5 lookup, stored manifest/current CRM source authorization, actual bytes SHA/size, root/revision, commercial fingerprint, BuyerRef and current approval policy binding. Wrong revision/hash/fingerprint409; missing/fabricated/cross-Quote document404. A valid current delivery must match revision/content and remain only USER_CONFIRMED_SENT; it is not consent or proof of which PDF was delivered.

B5 source/result RV are separate from current Quote RV: actual fixture source2/result3/current5 after B2+B3. No equality assumption between them. B1 required-approval check includes creator evidence, ApprovedBy/ApprovedAt/requester and independence, matching B5; malformed legacy self-approved flags have a regression test.

## F. Evidence Storage

**BLOCKED_CONTRACT_ACCEPTANCE_PROOF_STORAGE / BLOCKED_CONTRACT_TRUSTED_SCANNER.** No approved scanner/inspection adapter or safe customer-proof store exists. Therefore no upload/proof download route, fabricated artifact/case ID, pending-proof row, B6 bytes table or evidence migration was added. Existing B5 tables are not repurposed for customer uploads.

Binary PDF-like input including OpenAction/JavaScript markers is refused422 at typed JSON ingress. This proves unsupported binary input cannot enter the slice; it **does not prove malware detection or a clean scanner verdict**. PDF validation/quarantine/scanning and immutable B6 SQL persistence remain blocked. Indefinite evidence retention is an approved technical limitation of a future proper store, not a completed legal policy or implemented B6 storage claim.

## G. Consent Workflow

No genuine customer-origin identity/representation source and verification mechanism is available. No staff button acts as customer consent. B3 reports remain UNVERIFIED. B5 download/approval/manual send do not promote trust. Current API always returns `status=BLOCKED_CONTRACT`, `verificationStatus=NOT_VERIFIED`, `lifecycleCommitted=false`. It never emits VERIFIED/ACCEPTED receipts.

## H. Backend Contracts

Implemented two **read-only assessment operations**, not verification/acceptance mutations:

| Method | Route | Behavior |
|---|---|---|
| GET | `/quotes/{quoteId}/customer-acceptance?documentId=...` | Authorized readiness/binding; no proof content or consent assertion |
| POST | `/quotes/{quoteId}/customer-acceptance/evaluate` | Intent-specific authorization, current If-Match, bounded reference JSON and request identity; diagnostic result only |

POST accepts typed intent/evidence/document/revision/hash references; unknown actor/workspace/verified/scanner/file fields rejected. The EvidenceId reference is **not resolved or endorsed as existing/verified** while the proof owner is unavailable. VERIFY intent needs quotes.verifyAcceptanceEvidence; ACCEPT intent needs quotes.acceptVerified + quotes.update. Both also require quotes.readAcceptanceEvidence + quotes.read, current record scope and all used/rendered/source FLS. Body bound8KiB including chunked input; private/no-store responses.

POST serializes with the same Quote aggregate SQL lock; only read audit commits. No Quote version, command mutation receipt, outbox or evidence write occurs. Repeated assessments reauthorize and inspect current facts; they are not mutation idempotency replay. Actual positive Direct Sale command remains unexposed, linked atomic workflow and MarkWon unchanged.

Files: [QuoteVerifiedAcceptanceAssessment.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteVerifiedAcceptanceAssessment.cs), [QuoteVerifiedAcceptancePolicy.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteVerifiedAcceptancePolicy.cs), [QuoteVerifiedAcceptanceEndpoints.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Contracts/QuoteVerifiedAcceptanceEndpoints.cs). Four capabilities explicitly assignable through AccessControl, **none added to Workspace Owner defaults**. Two read-only operations intentionally admitted; no blocked positive operation has a callable success path. Frozen canonical baseline/signatures were not repinned; legacy formal bounded-verifier coverage limitation remains, no trusted VERIFIED/FROZEN attestation fabricated.

## I. Frontend

Lazy-loaded `QuoteAcceptanceReadinessPanel` in existing Quote Detail. Typed application port/public read API/HTTP adapter, regenerated OpenAPI client. Panel distinguishes B3 report, B5 document binding, missing verification dependencies and Quote lifecycle; no upload/verify/accept action is offered. Exact document selection, safe permission/retry errors, abort/reset on account/Workspace/resource change. No generated DTOs in presentation/domain. No new heavy dependency.

API contract now337operations:311ready/26blocked; the two additions are assessments. Existing26blocked operations stay blocked; status of business acceptance was not promoted by client generation.

## J. API / SQL

Final suite **59 recorded requests/cases PASS**, plus **2 real cross-Quote document isolation requests PASS**. Authentication/setup requests are not all included in that count. [Main result](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/result.json), [HTTP cases with correlation/response evidence](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/cases.json), [Existing-document isolation](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/wrong-quote-document-cases.json).

Actual tests include exact binding, nonexistent source failure, stale412, self-verification403, forged fields422, wrong hashes/revision409, role denial403, anonymous401, foreign Workspace404 with sufficient grants, FLS403 via official role/membership contracts, repeat/concurrent assessments, effective expiry, archive, invalidated approval, linked guard, denied MarkWon and B5 protected download SHA. Invalid policy bool returns truthful blocked readiness rather than500.

Fixture SQL before/after assessments: source Quote stays **SENT/RV5**,1issued document and1UNVERIFIED decision. No extra trust/acceptance/history mutation. [assessment-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/assessment-sql.json), [readiness.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/readiness.json), [linked-deal-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/linked-deal-proof.json).

Fixtures:17new synthetic Development Workspaces including companion isolation and failed first test,14Quotes,6Contacts,2Deals;8new PDFs issued solely through B5 regression setup. **0new IAM Account/Credential rows**; approved existing synthetic accounts reused, new Workspace roles/membership only. No real email/customer interaction; no SQL permission edits/reset/reseed/cleanup. Inventory: [fixture-ledger.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/fixture-ledger.json). Original B5 PDF count21 remains preserved; current29PDFs all have matching SHA/size. No B6 verified proof exists.

## K. Connected Browser

Dedicated Playwright Chromium, no API mocks/personal browser/desktop control. **3/3PASS**: B3 UNVERIFIED plus exact B5 binding/PDF hash; refresh/logout/login; Backend stop/start; restricted actor without proof-read permission. Missing source/scanner/store stays explicit, Quote stays SENT, no positive action or file input.

[Browser run](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/connected-final.log), [Readiness/schema/download evidence](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/browser/review-proof.json), [restart-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/browser/restart-proof.json), [Screenshot](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/api-5047ed6f56dc48038cb75dd6a629a5c3/browser/readiness-blocked.png). Scenario2 verified consent is **BLOCKED_CONTRACT/NOT_RUN**, not counted as a browser PASS.

## L. Security

Trusted actor/Workspace and all capability/record/FLS checks run on each assessment; readonly request identity is no authorization bypass. Scoped foreign Workspace with sufficient capabilities404; hidden GrandTotal403 before binding disclosure. Raw proof-read permission is separate from quotes.read and mutations. Source B5 FLS/owner checks reused. No success branch exists even if arbitrary verification flags arrive.

Frontend security scan PASS. B6 artifact scan has no retained derived synthetic password/JWT candidate; traces/videos disabled and automatic Playwright error-context redaction retained. [frontend-security.log](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/frontend-security.log), [evidence-secret-scan.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/evidence-secret-scan.json). No sensitive proof or credential stored in source/log/report. B6 proof-file confidentiality/malware storage acceptance cannot be certified while no proof store exists.

## M. Concurrency

Two concurrent read assessments with same request identity both return NOT_VERIFIED/no lifecycle commit; source SQL unchanged. Existing Quote aggregate lock also serializes against edits. Strict stale If-Match412 and current capability/FLS deny repeat requests after permission changes.

Actual concurrent positive ACCEPT/REJECT, immutable proof deduplication and committed B6 replay remain **BLOCKED/NOT_RUN** because there is no positive mutation/proof owner. Existing Core/B1–B5 idempotency/concurrency regressions stay covered by the Backend suite. No invented B6 mutation receipt is used to claim replay PASS.

## N. Regression

Backend **283PASS/4baselineFAIL/287**, from276/4/280 at B6 entry;7new focused rules/security tests pass. All four failure names and every diagnostic line match baseline; no added/removed diagnostics. [baseline-comparison.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/baseline-comparison.json), [backend-final-tests.log](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/backend-final-tests.log).

Original29table preservation PASS, including all55B3 decisions and21PDF pairs. Global final0Accepted/0Won/0Orders/0PaymentPlans. Linked Deal full row hash identical across assessment, denied atomic acceptance and denied MarkWon; no Pipeline/Stage changes. Original business source edited only at narrow registration/capability/API boundaries; Quote/Deal business mutations and B1–B5 commands untouched.

## O. Performance

No verification latency, proof-storage throughput or production concurrency claim: those mechanisms are unavailable. Binding assessment reads at most one bounded B5 document, validates stored SHA/size and current source rights; GET without document reads no PDF bytes. POST holds Quote lock for assessment/read audit only. Read-audit growth needs operational monitoring.

Production Frontend build PASS: entry294.14KiB, largest497.35KiB <500KiB, no budget increase. New evidence readiness UI is a separate lazy chunk. Production sizing, scanner timing, B6 storage/transaction load, backup/restore/DR remain NOT_RUN; B5 backup gap not falsely closed.

## P. Quality

PASS: build0warnings/errors;7focused Backend tests; actual59+2API cases/SQL invariants;3Connected browser tests; strict-core/type-regions, API337-operation contract, HTTP foundation, API management boundaries, Quote API boundary, approval/delivery, Quotes paging/empty/error/retry/workspace race, capability manifest, presentation responsibility, mutation authority, frontend/backend separation, security scan, repository inventory/check, production build/budget. Diff checks have no whitespace errors (Git CRLF notices are informational).

Known baseline FAIL remains: Backend4architecture/owner/context/admission failures; global tsc3diagnostics (`deal-quote-transition-outcome` nullable model, `studio-first-visit`2optional strings); backend-readiness document-status coverage for preexisting Pipeline/Studio docs. No gate/budget was weakened. Full npm verify is not claimed green; focused checks and global diagnostic comparison reported separately. Independent reviewer found no material issue after B1 correction; this supports **the independent read-only slice only**.

### B6-01…30

| ID | Scenario | Result | Scope / limitation |
|---|---|---|---|
| B6-01 | Issued document binding | PASS | B5 identity/bytes/hash/revision match; source RV2/result3/current5 remain distinct. |
| B6-02 | Wrong DocumentId / existing document of another Quote | PASS | 404 for fabricated ID and both real cross-Quote document references. |
| B6-03 | Wrong revision | PASS | 409 QUOTE_ACCEPTANCE_BINDING_MISMATCH. |
| B6-04 | Wrong PDF hash / commercial fingerprint | PASS | 409; no verification or lifecycle writes. |
| B6-05 | Missing signer authority | PASS | Read assessment reports BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE; never VERIFIED. |
| B6-06 | Valid signer authority → VERIFIED | BLOCKED_CONTRACT | No approved concrete identity/authority source adapter; no positive verification endpoint. |
| B6-07 | Identity unverified | PASS | NOT_VERIFIED unconditional; no claim from ContactId/email/payload. |
| B6-08 | Manual employee claim | PASS | Actual B3 report remains UNVERIFIED/SENT alongside issued B5 document. |
| B6-09 | Missing evidence | PASS | Empty request422; unresolved evidence reference never treated as verified; proof store absent. |
| B6-10 | Forged verification flags | PASS | verifiedBy/verificationStatus/signerAuthorityVerified/scannerClean/pdfBytes extras422. |
| B6-11 | B6 evidence storage persistence | BLOCKED_CONTRACT | No safe proof store/scanner. B5 SQL storage is not reported as B6 proof storage. |
| B6-12 | Duplicate immutable evidence submission | NOT_RUN | No submission/proof identity or write contract exists. Repeated assessment is a different test. |
| B6-13 | Revoked real signer authority | BLOCKED_CONTRACT | Actual source unavailable; preacceptance revoked/expired/future/stale fact predicates covered by unit tests only. |
| B6-14 | Expired Quote | PASS | Real expired-date synthetic Quote assessment includes QUOTE_EXPIRED. |
| B6-15 | Archived Quote | PASS | Official archive command, then assessment includes QUOTE_STATE_INVALID. |
| B6-16 | Approval invalidated | PASS | Official commercial edit clears B1 binding; readiness reports APPROVAL_BINDING_STALE. |
| B6-17 | Wrong Workspace | PASS | With sufficient proof-read/intent grants in foreign fixture:404. |
| B6-18 | Restricted actor / anonymous | PASS | 403 and401 respectively; no proof access. |
| B6-19 | FLS | PASS | Official role/member access hides GrandTotal; readiness/assessment403, including repeated request identity. |
| B6-20 | Stale If-Match | PASS | 412 VERSION_CONFLICT. |
| B6-21 | Concurrent positive ACCEPT/REJECT decisions | BLOCKED_CONTRACT | Positive acceptance absent. Two concurrent read assessments both remain NOT_VERIFIED/no commit; not counted as positive decision-race PASS. |
| B6-22 | Committed mutation idempotency replay | NOT_RUN | No B6 mutation receipt exists. Repeated/concurrent assessment reauthorization and zero business writes separately verified. |
| B6-23 | Direct Sale verified acceptance | BLOCKED_CONTRACT | Separate typed acceptance mutation not exposed without trusted dependencies; legacy workflow409. |
| B6-24 | Linked verified evidence persistence | BLOCKED_CONTRACT | Linked B5 document binding/negative readiness PASS, but no B6 verified evidence is created. |
| B6-25 | Linked positive acceptance / Gate C | PASS | 409 ATOMIC_ACCEPTANCE_CONTRACT_UNAVAILABLE; MarkWon422 DEAL_WIN_EVIDENCE_INVALID; full Deal hash unchanged. |
| B6-26 | B6 verified evidence persists after login | BLOCKED_CONTRACT | No B6 evidence store; browser confirms B3 UNVERIFIED/B5 binding+PDF and blocked readiness across refresh/login. |
| B6-27 | B6 verified evidence persists after restart | BLOCKED_CONTRACT | No B6 proof persistence claimed; actual blocked readiness/B3/B5 survive host restart. |
| B6-28 | B3 historical records not promoted | PASS | All55 original decisions unchanged; new synthetic reports also UNVERIFIED. |
| B6-29 | B5 historical PDFs | PASS | All21 original document/content row hashes unchanged; all current29PDFs match stored SHA/size. |
| B6-30 | Original data preservation | PASS | 0changed/missing original rows in29tables;0Accepted/Won/Orders/PaymentPlans. |

Machine-readable matrix: [acceptance-matrix.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/acceptance-matrix.json). A passing rejection/readiness test never substitutes for a blocked positive verification, evidence persistence or acceptance case.

## Q. Gate C Readiness

**Not ready to provide a verified commercial commitment.** Still required: approved actual identity/authority issuer/provenance adapter; trusted PDF inspection/scanner; separately owned immutable B6 proof storage and case ledger; independent reviewer workflow; effective verification reader; separate Direct Sale acceptance command and positive tests. Deal-linked atomic acceptance/Won orchestration is GateC, not enabled here. No Order/Payment/Invoice/Shipping/provider-delivery/Restore path opened.

The readiness DTO is explicitly NOT_VERIFIED and cannot be used as a commitment receipt. No SourceId or B3 response can bypass MarkWon. No need to reapprove the settled framework policy; missing **actual trusted sources/contracts** must be supplied/implemented and checked before positive activation.

## R. Git Status

No commit/push/merge/reset/reseed. Heads unchanged; no existing file newly deleted. Source preservation/delta lists: [source-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/source-final-preservation.json). Backend adds4focused application/contract files,7test cases in one test file and real verification script; modifies only Quote registration/endpoints/capability constants and explicit AccessControl assignable capability list. Frontend adds application/public readiness read, lazy panel, connected spec/config and adapter/runtime/client updates; no route/module redesign. Root manifests changed only approved readiness admission/capability mapping. Historical fixtures and local changes remain.
