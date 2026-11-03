# Manual Quote Acceptance & Deal Won MVP — acceptance

2026-10-29. Verdict **BLOCKED_AUTHORITY_GAP**. The requested employee-attested manual business route is a new product direction, but neither its precise policy nor the distinct owner commitment boundary has been ratified. There is no new production mutation, schema, frontend or authority change in this task. This is not a functional pass. Evidence: `task-runtime/manual-quote-won-20261029/`; decision proposal: [decision packet](../../../task-runtime/manual-quote-won-20261029/decision-packet.md).

## A. Current source truth

Local Backend `master` / `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend `feat/deal-compact-kanban-card` / `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; dirty state recorded before task. `CORE_AUTHORITY.md` ranks baseline and adopted OpenAPI, approved decisions, handoff/owner policies and architecture above current implementation or UI. Task risk is HIGH: durable cross-owner commercial commitments, security, concurrency and API changes. `markDealWonCommand` lookup yields ready operation, `deals.close`, workspace/record scope, required idempotency and If-Match, single-Deal transaction; readiness does not itself admit a manual verifier.

## B. Existing Quote and Deal contracts

B1 independently assesses current approval and fingerprint; B2 records `USER_CONFIRMED_SENT`, a human commercial-content assertion, not verified PDF-byte delivery. B3 adopted Option A captures `USER_REPORTED_ACCEPTANCE` as UNVERIFIED and leaves Quote SENT; it binds Buyer, revision, fingerprint and server actor/time with audit/outbox. B4 governs revision/expiry/archive; B5 issued document binds revision/fingerprint and PDF bytes; B6 operational verification remains blocked. Historical B3 explicitly blocks positive acceptance and Deal Won; new request is a proposed new branch, not evidence B6 has been satisfied.

`QuotesEndpoints` routes customer-decisions to the Quote owner and accept-and-close-deal to `QuoteAcceptanceGuard`: the latter checks workspace/permissions/FLS/version and returns 409, either verifier unavailable or atomic contract unavailable. No admitted source was found that writes positive ACCEPTED in the current owner route. `MarkDealWon.Handler` checks `deals.close`, workspace, record/FLS, version and evidence shape, then returns `DEAL_WIN_EVIDENCE_INVALID`: neither source owner has an admitted commitment verifier. `Deal.ChangeStage` only accepts category Open, blocking generic WON mutation. A public acceptance verifier plus race-safe owner contract must be explicitly admitted; a client source ID or previously displayed status does not suffice.

## C. Business decision and governance status

The user asked for a distinct manual business acceptance, while explicitly requiring separate approval where semantic details are missing. Applicable decisions are documented as **PROPOSED_PENDING_APPROVAL** in the packet; no source changed to imply approval. Owners must select precise capability, eligibility/evidence, meaning of ACCEPTED, Quote-to-Deal verification/concurrency and post-acceptance correction/revocation. Control process requires a project extension/adopted owner/API decision, updated OpenAPI/admission and generated client; the engineer cannot self-ratify. One especially material choice is whether the existing ACCEPTED status may trigger Order conversion and other consumers, or whether a separate business state is needed.

## D. Manual acceptance policy

Recommendation for review: dedicated manual acceptance capability with trusted actor/Workspace and FLS; SENT, current revision/fingerprint, valid unarchived/unsuperseded and B1 approval; current B5-issued document binding if chosen; channel, reported time, respondent, note and employee attestation, optional reference; append-only owner receipt and distinct `EMPLOYEE_ATTESTED` assurance. The quoted policy is a proposal, including B5 and capability choices, not current behavior. No customer signature or identity verification claim and no fabricated high-value threshold.

## E. Backend implementation

No manual acceptance code was implemented or enabled. Existing B3 response command, Quote guard, audit/outbox and B6 verification logic were retained. Existing source includes lifecycle enum ACCEPTED but that fact alone does not authorize a transition or settle its downstream meaning.

## F. DealWon evidence contract

Recommendation: Quote owner issues and verifies a current manual commitment bound to Workspace, Quote, Buyer, SourceDealId, revision, fingerprint and receipt; Deal owner requires `deals.close`, record/FLS/version/idempotency and checks owner proof before transition. A read then later Deal commit may race revocation: owner and architect must specify an admitted lease/lock or equivalent consistency boundary. No automatic Deal WON and no Order, Payment or Invoice. This proposal must remain distinct from operational verified acceptance and Gate C.

## G. Frontend UI/UX

No new manual action or success presentation was introduced. Connected Quote adapter sends B3 record commands to generated API; Deal adapter has a typed mark-won API, while connected Deal detail explicitly refuses a browser-derived Won proof. Deal Quote workspace exposes local acceptance action only outside connected mode. Pipeline direct-Won action currently presents a warning. Workflow `acceptQuoteAndCloseDealCommand` supplies a demo callback, but normal connected composition binds `RoutedHttpMutationAuthority`, whose execute method routes through HTTP and does not invoke that callback; normal demo composition binds `LocalMutationAuthority`. Explicit injected authority overrides are outside this normal-path finding. The generated production registry routes the workflow to the guarded backend endpoint. A future approved UI must consume Backend availability, show employee attestation clearly, preserve history and display typed errors without optimistic Won.

## H. HTTP/SQL acceptance

Fresh task-owned ApiHost plus local SQL used the earlier isolated synthetic Workspace/Deal/Quote fixture for denied commands and reads. Actual actor/Workspace, request paths, correlation IDs and responses are in `http-commercial-guards.json`; test used Quote `quote_a2cb3b12ea0e42709aa3ba622ba1e722`, revision 1, version 6, SENT and Deal `deal_7bab07c70e6e4a3a856c53c693d23510`, version 0, OPEN. POST `/workflows/quote-acceptance/{id}/accept-and-close-deal` → 409 `QUOTE_ACCEPTANCE_BLOCKED`; POST `/deals/{id}/mark-won` with claimed QUOTE_ACCEPTED → 422 `DEAL_WIN_EVIDENCE_INVALID`. Re-read confirmed state/version unchanged (`commercial-guards.json`). These are negative controls, not manual-acceptance or DealWon success. Fresh positive acceptance, concurrency and restart persistence cannot be run before an approved implementation exists.

## I. Connected Chromium journey

No new Connected Chromium end-to-end run in this task. The prior connected CRM completion evidence includes real Lead/Contact/Customer/Deal/Quote, approval, B5 PDF, SENT and UNVERIFIED readback; it does not include manual ACCEPTED or WON. The requested positive browser journey remains NOT_RUN / BLOCKED_AUTHORITY_GAP; no screenshot or functional pass is claimed.

## J. Negative security tests

Fresh real HTTP regression: nine checks PASS, including actor capability denial for approval/issuance, Quote field security, protected PDF and cross-Workspace denial, and Deal hidden-field search inference (`connected-security-results.json`, `http-security-regression.json`). The two guard requests in H independently confirm claimed Quote evidence cannot make WON and acceptance remains blocked. Broader requested manual-specific cases (forged attestation, stale version on new command, invalidated evidence and new capability) are NOT_RUN because no such command exists. Existing unit tests additionally exercise malformed claims, authorization, workspace and Deal field/version checks, but cannot substitute for the future live matrix.

## K. Concurrency and idempotency

Existing B3 owner has Quote command idempotency and version checks; no manual-acceptance concurrency guarantee has been tested because the proposed command does not exist. No assertion of one-winner success or safe cross-owner invalidation race can be made. Existing owner tests ran in the full suite; after adoption, test same/different key/body, stale version, competing decisions and restart against SQL.

## L. Regression B1–B6

Full backend test ran; existing B1–B6 coverage is included, while four architecture tests remain failed. The fresh live fixture read confirmed SENT, independent approval, recorded UNVERIFIED response and one delivery; protected document, access and seller checks passed. No independent operational source or VERIFIED assertion was created. Historical B5 document-byte and browser evidence is separate; this task did not repeat full B1–B6 live positive matrix.

## M. SQL preservation

Fresh verified local instance `DESKTOP-ICH225R`, `UnicoreCRM_Development`: entry 117,202 rows / 160 PK tables, migration identities and row hashes in `sql-before.json`. Final 117,275 rows: **zero original rows changed or missing; zero migration changes; 73 additions** (5 Deal access/audit, 5 IAM idempotency, 10 IAM audit, 30 authorization decisions, 9 record access decisions, 5 IAM security events, 4 Quote read audits and 5 IAM sessions). All additions are reported individually by table in `sql-preservation-final.json`; no business object was created/changed by the guard calls. Previous SQL_MUTATION_PARTIALLY_ATTRIBUTED verdict is unchanged. No reset, direct permission DML, historical mutation or migration.

## N. Quality before/after

Fresh `dotnet test UnicoreCRM.slnx --no-restore`: 474 passed / 4 failed / 478, the same architecture-failure baseline. Frontend 12 focused Quote/Deal, lifecycle, contract and strict gates PASS (`frontend-focused.json`); global `npx tsc --noEmit` PASS; production build PASS, bundle max 482.50 KiB / 500 KiB. Fresh canonical `npm run verify`: 48 PASS including lint, gate 49 `quality.repository-cleanup` FAIL on 14 exact-content duplicates, 279 gates NOT_RUN (`frontend-verify.log`). No quality budgets, rules, allowlists or historical test baselines were changed.

## O. Final verdict and blockers

Manual Quote Acceptance: BLOCKED_AUTHORITY_GAP. Manual Deal Won: BLOCKED_AUTHORITY_GAP. Requested end-to-end functional pass: NOT ACHIEVED; full quality is separate and not green. Owner policy, wire admission, downstream ACCEPTED semantics, Quote-owner evidence and revocation-vs-Won consistency are unresolved. B6 Operational VERIFIED remains blocked and is not inferred from manual reports. `NOT_RUN_POLICY_BLOCKED` remains the historical real after-flush fault-injection limitation, not a rollback pass.

## P. Git status and next priorities

Unicode-safe source hash and NUL-delimited status snapshots recorded at entry; final delta in `source-delta.json`. Existing dirty changes preserved; no commit, push, merge or reset. (1) Project/Quote/Deal/Security/API owners review and ratify the decision packet, including existing ACCEPTED consumers and revocation race. (2) Implement only adopted narrow Quote, Deal owner and connected UI contracts with migrations/API generation if admitted. (3) Execute real positive/negative HTTP/SQL and Chromium journey, restart and full regression after implementation.
