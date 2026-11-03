# B6.4 — Independent Documentary Review acceptance

2026-10-27. **DEVELOPMENT_SIMULATION_FUNCTIONAL_PASS. OPERATIONAL_VERIFIED_ACCEPTANCE = BLOCKED_CONTRACT.** This is an independently authorized documentary review workflow for synthetic Development facts. It creates no VERIFIED customer evidence, Quote ACCEPTED, Deal WON or commercial commitment receipt. B6.5/Gate C remain unopened.

Evidence directory: `task-runtime/quote-independent-review-b6-4/` in the repository parent. Policy: [quote-independent-review-b6-4-policy.md](quote-independent-review-b6-4-policy.md). Results below are local executable evidence, not a production release/FROZEN claim.

## A. Git/SQL Baseline

Fresh entry HEADs: Backend `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` (`master`); Frontend `814cc4685b6d8f8ead2fc945f4607a2d0168320d` (`feat/deal-compact-kanban-card`). Existing dirty files and missing files were recorded before edits. SQL was guarded to local `DESKTOP-ICH225R / UnicoreCRM_Development`. Entry snapshot contains **6,494 rows / 37 tables**, not an assumed copy of the earlier 6,045-row checkpoint. Entry tests: 381 PASS / 4 FAIL.

## B. Existing Review Architecture

Before: B6.3B could reach simulation READY but had no independent review decision. After: the existing `IdentityAuthorityVersions` ledger appends `REVIEW_RECORDED`; existing Case/shared version, Quotes command/audit/outbox, CRM affiliation leases, proof storage and effective-CLEAN services are reused. Migration `20261027032029_QuoteIndependentReviewLedger` only extends ledger constraints. No new storage table, registry, provider or workflow engine.

## C. D1–D5 Policy

Canonical definitions are preserved, with the attachment's illustrative groups mapped into them:

| Canonical criterion | Required coverage |
| --- | --- |
| D1 identity/authority | Exact B5 binding, identity/authority/source evidence, retained customer-origin explicit unconditional signed consent |
| D2 trust/independence | Current capability, record/FLS access and persisted actor separation |
| D3 storage/provenance | Same-Case server hashes, current CLEAN, retained provenance and acquisition claim |
| D4 commercial eligibility | Authoritative value/currency, validity/timezone, current approval and commercial delivery evidence |
| D5 lifecycle isolation | Review only; no customer verification, acceptance, Won or Order/Payment |

Every positive finding needs an outcome, rationale and artifact references. Consent additionally requires a separate admitted CUSTOMER_CONSENT artifact, acquisition provenance and an explicit reviewer finding. The scanner does not establish consent semantics; the independent human finding is audited and remains simulation assurance. Backend facts are rechecked even with five PASS findings.

## D. Reviewer Authorization

Reuse `quotes.verifyAcceptanceEvidence`, `quotes.read`, `quotes.readAcceptanceEvidence`, protected content access, and existing CRM record/FLS authority. No new capability or default grant. Reviewer differs from Quote creator/requester, Case recorder, proof uploaders, identity recorder, source creator/preparer and checker. An otherwise independent B1/source approver is permitted by the approved matrix. Owner/admin status does not override exclusions.

## E. Review Domain & History

Projection distinguishes REVIEW_PENDING, NEEDS_MORE_EVIDENCE, REVIEW_REJECTED, REVIEW_APPROVED_SIMULATION, REVIEW_BLOCKED and REVIEW_INVALIDATED. Decisions retain exact bindings, findings, reason, reviewer/time, policy and simulation provenance. SQL has **14 review rows / 14 command-audit receipts / 14 outbox records**, zero missing/mismatched links and zero trust violations (`sql-review-ledger.json`). Seven provisional synthetic decisions created earlier in this task lack the final binding precondition; they remain readable historical records and cannot be effective authority. Seven decisions carry the final binding fingerprint. No historical rewrite/backfill.

## F. Backend Commands

Three admitted operations under `/quotes/{quoteId}/acceptance-cases/{caseId}/independent-review`: GET readiness, GET bounded history, POST `/decisions`. Outcomes are REQUEST_MORE_EVIDENCE, REJECT_REVIEW and APPROVE_SIMULATION. Negative decisions need authorization/separation and a current binding, but not positive checklist completeness. All writes currently require the explicitly enabled Development review policy; operational negative writes are not activated in this checkpoint. Metadata/history reads remain separately authorized.

## G. Source/Proof/Document Binding

Readiness returns a server fingerprint covering Case/document/Quote state, identity/source/check versions, policy, eligibility and effective scan metadata. Submit echoes this precondition alongside strong If-Match; stale facts return REVIEW_FACTS_CHANGED. Quote/Case/Source locks and the CRM lease protect command evaluation. Actual issued PDF bytes are verified for decisions; metadata/history reads avoid PDF byte reads. Review history is paged at up to 50 items without per-history party lookups.

`sql-review-bindings.json` confirms zero document-binding failures for all 14 reviews and zero failures for 52 retained proof references. All four Quotes with review decisions remain SENT.

## H. Development Simulation Isolation

Trusted Development environment plus exact Workspace Backend configuration is mandatory. Every simulation decision records `assuranceLevel=DEVELOPMENT_SIMULATION`, `actualSourceVerification=false` and environment/policy provenance. Production/Staging denial is covered by policy tests; no production service was contacted. Client-supplied actor, trust, environment or commercial-commitment flags cannot enable a path.

## I. Frontend

A lazy-loaded “Đánh giá độc lập bằng chứng” panel extends the existing Case UI with canonical findings, consent provenance, blockers, history and effective status. It awaits Backend success and refreshes both review and shared identity state. Requests abort/reset on scope changes. An independent review found stale human findings could be rebound after a same-Case update; this was fixed with the server precondition and input invalidation. Ordinary identical refresh preserves input; changed binding clears attestations. Final targeted review found no material remaining issue. Guidance is bilingual and versioned; no acceptance/Won action was added.

## J. API/SQL Acceptance

- `api-0c63e4b10f4142d1bc43458c819daba6/`: official isolated provisioning and real Core/B1/B2/B5/B6.1–B6.3B fixture chain PASS.
- `advanced-22a4b9e53efb45638aaa022461828101/`: final fingerprint-aware positive/negative review, wrong/missing findings/consent/document/revision/SHA, forged fields, stale version, replay, policy change, concurrency, rollback and revocation PASS.
- `security-4653fb4f78144ea68af88348ea5df728/`: final authority amount/currency/expiry, source version, current-CLEAN refusal, approval policy, persisted EXPIRED and archive guards PASS. Expiration uses the existing explicitly scoped Development lifecycle clock and official owner command; it is a labeled time simulation, not a claim that real wall time advanced. End-of-day and 24-hour boundaries are domain-policy coverage; no live 24-hour aging wait was claimed.
- `review-history-immutability.json`: six decisions observed repeatedly, zero historical snapshot changes.

`acceptance-matrix.json` maps RV-01–RV-45 to evidence and test classification, including the domain-only freshness/environment checks and the explicit Development time simulation.

Earlier failed harness runs remain retained. Singleton role arrays/date conversion were fixed in the harness, not by weakening Backend validation.

## K. Connected Browser

Final `read-b2a71a8b41fa49b0b058ebf82219549e/`: **4/4 PASS**, dedicated Chromium, actual API 5098/Vite 3044, no mocked API. Covers restricted Creator/Checker, independent review, B5/proof downloads with SHA comparison, identical-refresh input preservation, refresh/logout/login and Backend restart persistence, then official source revocation → REVIEW_INVALIDATED with immutable history and cleared attestation inputs. Approved/invalidated screenshots were visually inspected. An earlier four-scenario run also passed before final precondition hardening.

## L. Security

Actual API covers missing verify capability, self/Recorder/Checker exclusions, Owner with explicit verify still denied, hidden commercial FLS, OWN scope (masked 404) and cross-Workspace denial. Eight distinct forbidden actor relationships are individually covered by domain tests; overlapping Creator/Requester/Recorder roles in fixtures are not misrepresented as eight independent API actors. Security scan PASS; secret/token evidence scan is recorded separately. No default permissions or Studio bypass was introduced. Fixture setup uses official Workspace/AccessControl provisioning, with CRM/demo and identity reseeding disabled; existing accounts/credentials are reused, no real email.

## M. Concurrency & Recovery

Conflicting decisions produce exactly 200+412; same-key replay has no duplicate effect. Replay reauthorizes and projects current effectiveness rather than returning stale approval. A real post-flush fault rolls back ledger/receipt/outbox, and the same request succeeds after recovery. During supplemental verification, real AV signatures naturally became stale: the system refused CLEAN and exhausted the existing epoch retry budget. Official freshclam/sigtool validation updated definitions to 28166; the owned daemon was restarted and the real runtime repinned. Scanner configuration hash/limits and retry caps were unchanged; no counter reset or verdict override. Final cases were scanned against the new real runtime. Old scan/review histories remain retained; changed scan bindings require re-review.

## N. Data Preservation

`preservation-result.json`: **6,494/6,494 original rows across 37 tables unchanged; zero missing/changed**, including original CRM/demo, Pipeline, Quote, B5 bytes, B6 storage/scan/source ledgers and captured IAM/AccessControl rows.

| Aggregate | Entry → final |
| --- | --- |
| Quotes / Deals | 282→289 / 195→195 |
| Issued documents / contents | 45→52 / 45→52 |
| Proofs / protected contents | 51→78 / 51→78 |
| Cases / Workspaces | 16→23 / 77→79 |
| B3 customer decisions | 65→65, all UNVERIFIED |
| ACCEPTED / WON / Orders / PaymentPlans | 0→0 for each |

New rows are synthetic fixtures in `unicore-independent-review-a69d1cf5` and its official nonmember companion. Failed/quarantined fixtures were retained. All 52 issued PDFs match SQL bytes/size/SHA, total 2,627,554 bytes. No original fixture was deleted, reset or reseeded.

## O. Regression/Quality

Final ApiHost build: 0 warnings/errors. Backend **400 PASS / 4 FAIL / 404**; all 19 new tests pass. The four failure names/messages exactly match the fresh entry run (`backend-baseline-comparison.json`). Global TypeScript has the same three baseline diagnostics, no new diagnostic. `npm verify` was attempted and stops at that lint/typecheck failure; the 328-gate pipeline is not claimed green.

Focused typecheck 3/3, API/generated client, management boundaries, presentation, mutation authority, guidance, frontend/backend separation, Quote approval/delivery/presentation and repository inventory checks PASS. Security scan PASS. Build/bundle PASS: entry 297.15 KiB; largest 499.30 KiB under the unchanged 500 KiB limit. Contracts are now 360 operations / 334 ready / 26 still blocked; only three review operations were added.

The separately run backend-readiness gate FAILS on three existing unregistered Markdown documents. `baseline-document-status-evidence.json` proves those documents and `docs/document-status.json` were already present with identical entry hashes; this unrelated inventory debt was preserved. Regression claims cover the executed fixture chain, tests and preservation, not every historical browser suite. No Dynamic Pipeline registry rewrite.

## P. Remaining Operational Blockers

No actual approved identity/authority issuer or independent operational source exists. Operational verified acceptance therefore remains BLOCKED_CONTRACT. This task adds no cryptographic signature, live external revocation, legal retention or production deployment assurance. Existing Backend/TypeScript/document-inventory debt remains. Reviewer decisions are human documentary findings; CLEAN is technical safety only.

## Q. B6.5 Readiness

The independent review software/ledger/preconditions are available for Development preparation. Simulation receipts cannot authorize operational VERIFIED/ACCEPTED/WON. B6.5 requires its own admission and real approved-source prerequisites; it was not opened.

## R. Final Verdict

**B6.4 DEVELOPMENT_SIMULATION_FUNCTIONAL_PASS; OPERATIONAL_VERIFIED_ACCEPTANCE BLOCKED_CONTRACT.** Negative, positive simulation, persistence, authorization, current-effectiveness, rollback and preservation were exercised with real local infrastructure. This is not a full-suite or operational source-verification PASS.

## S. Git Status

Entry branches/HEADs and prior local files are preserved; no new deletion, commit, push, merge or reset. `final-source-manifest.json` records this task's delta relative to entry, plus final source hashes and statuses. Main changes: Quotes review contracts/rules/service; additive existing-ledger constraint migration; policy/metadata read support; three OpenAPI/generated operations; lazy panel/hook/adapter and focused actual QA harnesses. No new dependency, capability catalog, Proof store, provider, Order/Payment workflow or budget increase.
