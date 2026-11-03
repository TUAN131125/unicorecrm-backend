# Gate B2 — Authoritative Quote Delivery & Evidence

Verified 24–25 October 2026, Development only.

**Verdict: Functional PASS for authoritative USER_CONFIRMED_SENT delivery.** PDF/document issuance and provider confirmation remain **BLOCKED_CONTRACT**, as approved. This is not a globally green quality gate or a FROZEN checkpoint: existing architecture/typecheck failures remain, and the live archived-fixture scenario is NOT_RUN (domain rejection is tested). No B3/Gate C work is enabled.

## A. Baseline

Backend: `master`, `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`. Frontend: `feat/deal-compact-kanban-card`, `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both workspaces were already dirty; their patches/status and SHA-256 inventories were captured before implementation in `task-runtime/quote-delivery/`.

SQL target verified as local `DESKTOP-ICH225R / UnicoreCRM_Development`. Entry contained **62 Quotes**. Final contains **141**: **79 fresh command-created B2 Quote fixtures**, retained across successful and failed harness attempts. None of the 62 entry Quotes changed. Original CRM demo remains **1,050 records / 180 Deals**; all entry CRM hash groups, Pipeline/PipelineStage rows, existing identities/credentials, Workspace/ACL rows and applied migrations are unchanged. The protection snapshot covers 26 tables, including pre-existing Quote command/outbox evidence.

Evidence: `preservation-final.json`, `fixture-manifest.json`, `protected-entry-row-hashes.json`, `protected-current-row-hashes.json`; SQL summaries under `task-runtime/quote-lifecycle/b2-entry/` and `b2-complete/`.

## B. Delivery policy

User-approved authority: `docs/development/quote-delivery-policy-decisions.md` and the explicit B2 approval reply.

- Require active trusted actor/Workspace, both `quotes.read` and `quotes.update`, record scope, commercial/approval/history read FLS and status/sentAt/history write FLS.
- First delivery accepts unarchived DRAFT/REVIEW; actual resend accepts SENT with a fresh DeliveryId. Terminal/archived states fail closed.
- Fresh server B1 assessment must match persisted commercial fingerprint and effective Workspace policy version. Require current NOT_REQUIRED or a valid independent APPROVED binding; never create an approval during sending.
- ValidUntil includes its entire Workspace-local calendar day. Backend clock and a valid authoritative Workspace timezone are required. Future reported times are rejected; request timestamps must match the existing UTC `Z` contract.
- EMAIL/GMAIL require valid recipientEmail matching the saved Quote email if present. Other channels require a destination under the approved nonblank/length policy. This does not verify ownership of external accounts.
- Only USER_CONFIRMED_SENT is accepted. Provider enum claims return 422 `QUOTE_DELIVERY_EVIDENCE_INVALID`.

## C. Backend implementation

Before B2, the admitted `/quotes/{quoteId}/send` route had no Backend handler; Connected action policy intentionally disabled delivery. The new `QuoteDeliveryCommands` reuses Quotes authorization, aggregate/command SQL application locks, B1 assessment, existing JSON persistence, immutable command records and outbox.

The strict send endpoint rejects unknown/duplicate fields, invalid headers, absent If-Match/idempotency and unsupported evidence. Domain mutation appends history, sets SENT on first delivery, preserves first SentAt, increments ResourceVersion and writes `QUOTE_DELIVERY_RECORDED` in the same Quotes transaction. No migration or foreign-owner table access was added.

Changed files: `Quotes/Domain/Quote.cs`, `Quotes/QuotesModule.cs`, `Quotes/Contracts/QuotesEndpoints.cs`, `QuoteContracts.cs`; added `QuoteDeliveryContracts.cs`, `Application/Common/QuoteDeliveryCommands.cs`, `QuoteDeliveryTests.cs` and `scripts/verify-quote-delivery.ps1`.

| Existing function | Contract | Backend after B2 | Frontend after B2 | Remaining gap |
|---|---|---|---|---|
| Draft/Product/pricing/list/B1 | Existing admitted contracts | Preserved | Preserved | Existing global quality failures |
| Manual delivery | recordQuoteSendEvidenceCommand | Authoritative transaction | Explicit confirmation | Human claim only |
| Delivery history | Optional read metadata admitted by user | Durable append-only history | Actor/channel/recipient/type/time/version | Member ID is displayed when no name directory is available |
| Gmail/share/download | Client helper | No provider participant | Never treated as committed send | Connected attachment issuance blocked |
| PDF/seller document | No suitable narrow seller read contract | No immutable document authority | Explicit unavailable state in Connected | BLOCKED_CONTRACT |
| Provider confirmation / acceptance / Won | Not admitted in B2 | Fail closed | No Connected B3 actions | Deferred |

## D. Content integrity

Delivery read metadata adds optional recordedAt, reportedSentAt, quoteRevision, resourceVersion, approvalContentFingerprint, approvalPolicyVersion and `documentAuthority=COMMERCIAL_CONTENT_ONLY`. The strict request was not broadened.

The record resourceVersion and immutable commercial snapshot refer to the **version before mutation**. Mutation response/outbox result version is the **version after mutation**. Snapshot, actor, Workspace, Quote/revision, approval fingerprint and effective policy version are retained in the immutable delivery outbox payload. SQL inspection verified all these bindings for **8 committed events** in the final acceptance run (`content-integrity-proof.json`, `immutable-delivery-snapshots.json`).

recordedAt is Backend time; reportedSentAt is explicitly an unverified human report. FileName is descriptive metadata. No attachment bytes, server-issued PDF, storage receipt, byte hash or provider delivery are claimed.

Document review found the Preview could fall back to `BrowserInvoiceConfigurationRepository` seller defaults even when broad Workspace configuration was readable. Connected Preview/PDF issuance is now blocked before consuming that fallback. Demo rendering remains available. No Studio capability or new seller boundary was granted.

## E. Frontend integration

Reused existing HTTP adapter/public command, confirmation modal, Detail actions and timeline. SENT appears only after Backend commitment. Errors distinguish approval/policy changes, expiry, invalid recipient/time and access denial. Unknown legacy delivery records are labeled unverified; manual records say “Người dùng xác nhận đã gửi”.

The modal preserves opening version/Workspace/target and stable per-payload DeliveryId across retries. Gmail opening no longer rewrites a Connected Draft and thereby invalidates approval. Unsaved commercial changes require saving and reassessment. Connected Gmail checks Backend-projected approval status rather than comparing the server fingerprint with the unrelated browser demo fingerprint.

PDF export/share/Gmail compose helpers do not invoke the send command. Connected PDF-dependent launch is BLOCKED_CONTRACT until seller/document authority is admitted; the negative browser journey verifies zero Quote writes. The application does not imply successful external sending.

Frontend source changes: Quote types, QuoteActionPolicy, QuoteDetailPage, QuoteDeliveryConfirmationModal, QuoteBuilderPreview, useQuoteBuilderController, new quoteDeliveryFeedback; optional OpenAPI read schema and deterministic generated artifacts. Added isolated delivery Playwright config/spec. Other generated clients change their shared specification fingerprint; no unrelated operation was redesigned or breaking baseline accepted.

## F. API and SQL

Final main run: **60 real API requests PASS**, plus **3 real foreign-record probes PASS**. Main evidence: `task-runtime/quote-delivery/api-9495c322be394f12b891322e31d0a474/` (`cases.json`, `http-evidence.json`, `sql-evidence.json`, `security-evidence.json`, `preservation.json`, `result.json`). Foreign probe: `task-runtime/quote-delivery/foreign-record-probe/`.

Requests include actual first delivery, independently approved delivery, resend, replay, bad key/identity payload, stale version, concurrent send, missing/invalidated approval, policy restart, expired Quote, bad channel/email/time, provider spoof, actor spoof, missing headers, access/FLS and Workspace denials. Correlation IDs are retained; success and rejection are checked against SQL/event counts, not only response JSON.

**91 recorded response schemas PASS** against current OpenAPI, including ProblemDetails. Instrumentation limitation: the final linked-browser observer omitted HTTP method, so 22 linked methods were inferred from the admitted route/read journey for offline schema checks. All main API/foreign probe command methods and manual-send methods were recorded explicitly. The observer has been corrected for future runs; this limitation is preserved in `response-schema-validation.json`.

## G. Security

A new dedicated Workspace `unicore-quote-delivery-security` (`ws_11f7ec273a2849a7b8d7e1aef5e53ed4`) was necessary to test actual SQL-backed FLS without changing B1 permissions. Official Development Workspace/AccessControl provisioning reused the existing creator/approver identities; IdentityAuth provisioning stayed disabled. No new identity, credential rewrite or email was created. CRM seed was disabled; the maintenance bootstrap entry point did not compose/reset demo data.

Its administrator has only the Quote/contact/context and AccessControl capabilities required to create the fixture. A role created by official `/access/roles`, then assigned through `/access/members/{membershipId}/access`, makes deliveryHistory READ_ONLY. Both a new send and a previously committed key replay return 403 after that policy change. SQL remains **one history record, one receipt, one event**. All B1/demo memberships and permissions are preserved. Future security harness runs use a fresh Workspace suffix to avoid changing this retained fixture.

Additional cross-Workspace proof uses an actor legitimately belonging to both Workspaces: current Workspace bootstrap succeeds, but foreign Quote GET/send return neutral 404 and zero Quote receipts/events. A nonmember Workspace also returns 403. No authorization bypass or broad Studio grant was introduced.

Frontend security scan PASS. The final task scan found one pre-existing configured credential copied into the initial dirty-entry patch. That artifact was redacted in place, with its pre-redaction SHA-256 recorded in `entry-snapshot-redaction.json`; the user's source was not changed. Rescanning 200 task text files found zero actual configured/derived credentials, JWTs or raw authorization values. No credential value was emitted to tool output.

## H. Concurrency and idempotency

- Same actor/key/payload/version returns REPLAYED without a second delivery or event.
- Changed payload/version under an existing key returns `IDEMPOTENCY_KEY_REUSED`.
- Same DeliveryId with changed payload returns conflict; a different actor cannot take over its receipt.
- Same DeliveryId/payload with a new key binds that key via an immutable REPLAYED command receipt. It does not change Quote/version/history/outbox. Later reuse of this alias key with another payload conflicts.
- A new DeliveryId is an actual resend: revalidate current rights/content/approval/policy and increment version.
- Two concurrent distinct sends at the same version return 200/412; SQL contains exactly one delivery/receipt/event for that race.

For the main low Quote, two real deliveries plus one new-key identity replay produce **three immutable command receipts and two delivery events**, not three sends. Authorization/scope/FLS is checked before replay, and replay returns a current authorized read projection with the historical commit receipt.

## I. Connected Chromium acceptance

**5/5 PASS**, dedicated Chromium, real Backend, no mocked API, no personal browser/computer control:

1. Server assessment, invalid-recipient error/retry, first manual commit, correct pre-mutation content version, refresh/logout/login, actual resend/history.
2. Deal Detail displays linked Quote SENT and retains it after refresh; old Deal/stage/Pipeline hashes remain unchanged.
3. Unassessed/restricted users have no delivery action.
4. Blocked document/PDF/Gmail launch and Workspace isolation do not mutate Quote.
5. A separate real API command advances the Quote while the modal is open; old opening version returns 412, keeps the modal open and creates no delivery.

Screenshots: final run `browser/manual-delivery-history.png`, `linked-deal-sent.png`, `document-boundary-blocked.png`. Initial locator failures and their fixtures/artifacts were retained. The final run passed all five journeys; no real email/provider delivery was performed.

## J. B1 regression and scenario matrix

**143/143 Quote-focused tests PASS**, including B1/Core/owner-contract regression and 24 added delivery cases. Final future-time assertion was also rerun after independent review to ensure it tests a valid future UTC-Z timestamp, not merely bad formatting.

| IDs | Evidence | Result |
|---|---|---|
| B2-01/02/03 | Real assessment, pending denial, independent approval/send | PASS |
| B2-04/23 | Real approved Quote edit invalidates binding; send rejected | PASS |
| B2-05 | Policy label/effective version change after host restart rejects old approval | PASS |
| B2-06 | Archived/terminal domain rejection, zero command/outbox intent | PASS unit; live archived fixture NOT_RUN |
| B2-07 | Real expired Quote denied; exact local midnight/inclusive date tested with fixed clock | PASS API + unit boundary |
| B2-08/09/10/11 | Recipient/channel/provider spoof/manual evidence | PASS API/SQL |
| B2-12/13/14/15/16 | Replay/key/DeliveryId/stale/concurrent send and SQL counts | PASS |
| B2-17/18/19 | Restricted actor, both Workspace isolation forms, actual FLS/replay denial | PASS API/SQL; browser restricted/isolation PASS |
| B2-20/21/22 | SENT readback, refresh/login, host restart and SQL history | PASS |
| B2-24 | New DeliveryId resend appends history; actor/version binding retained | PASS API/SQL/browser |
| B2-25/26 | Document launch cannot mutate status | PASS guard; actual Connected PDF/compose BLOCKED_CONTRACT |
| B2-27 | Linked Deal Quote SENT readback/refresh without Deal mutation | PASS API/SQL/browser |
| B2-28 | 26-table entry-row hash comparison, CRM/Pipeline/migrations | PASS |

The live archived fixture is not fabricated through SQL: no implemented owner archive command currently supplies it, and B2 does not implement the deferred archive lifecycle. Boundary/authorization behavior is verified in domain tests; this live scenario remains explicitly NOT_RUN.

## K. Quality

| Check | Result |
|---|---|
| Backend build | PASS, zero warnings/errors |
| Quote-focused regression | PASS 143/143 |
| Full Backend suite | 209/213 PASS; 4 baseline failures remain |
| Full diagnostic comparison | Same four failing tests and identical diagnostic sets; zero added/removed diagnostics |
| Frontend strict group | PASS 3/3 |
| Global tsc | FAIL: same 3 baseline diagnostics; no new diagnostic |
| Quote approval/Gmail/PDF, presentation responsibility, mutation authority, API management/Quote boundary | PASS |
| Generated API contract | PASS 329 operations; 303 ready/26 explicitly blocked |
| Production build/bundle budget | PASS, entry 290.98 KiB; largest 494.60 KiB under 500 KiB |
| Connected browser | PASS 5/5 |
| Response schemas | PASS 91; observer limitation disclosed above |
| Security/diff check | PASS |

Baseline Backend failures: owner mapping, layer/workflow boundaries, durable owner metadata, endpoint admission. Global tsc failures: nullable `model` in `tests/contract/deal-quote-transition-outcome.test.mts:42`, and two optional-string arguments in `tests/e2e/studio-first-visit.spec.ts:50–51`. No gate, allowlist, baseline or type-safety budget was weakened.

Independent read-only review found a request timestamp contract mismatch; it was fixed to UTC-Z only and regression-tested. Final review found no runtime defect in authorization/FLS/replay, alias receipts, approval/content/version binding or B3 exclusion. Review evidence is not a formal signed FROZEN admission.

## L. Remaining gaps / risks

- Narrow public seller/document metadata boundary is not admitted/implemented. Connected document issuance remains BLOCKED_CONTRACT for all roles; Demo PDF still works. No provider callbacks, verified delivery or PDF byte-level proof exist.
- Changed policy can block resend of immutable SENT Quotes; no automatic reapproval/lifecycle mutation bypass exists. Any future revision/reapproval path needs its own admitted lifecycle gate.
- Archived live-fixture test remains NOT_RUN; exact expiry midnight is deterministic unit evidence, not a manipulated Development server clock.
- Current global quality failures remain unresolved baseline work. No Critical/High new runtime issue was found; this does not constitute globally green/FROZEN release approval.
- History identifies the authenticated member by ID where no authorized display-name directory is available. A reported destination/time is a human claim, never proof of external delivery.

## M. Git and preservation

HEADs unchanged; no commit, merge, reset, CRM reseed, migration or fixture cleanup. All entry files/local changes are preserved, including pre-existing deleted files. `source-delta.json` lists the task delta relative to the captured dirty entry, rather than attributing the whole Git diff to B2. Original Quote/CRM/Pipeline/ACL hashes are unchanged; new fixtures and receipt/event counts are fully retained. All task-owned API/Vite hosts were stopped. B3, Gate C and Deal Won evidence remain blocked.
