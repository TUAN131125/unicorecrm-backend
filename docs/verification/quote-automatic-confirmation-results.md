# Quote Detail and automatic confirmed Order — verification

Implementation and verification date: 2026-11-02. These results describe local isolated execution, not a production rollout.

## Result

Configured ordinary Quotes can issue real B5 PDFs without a persisted internal approval assessment. Valid staff-recorded acceptance and explicit customer-link consent automatically produce an Order **CONFIRMED** and Payment Plan **ACTIVE**, without a second human confirmation or a committed intermediate Order DRAFT.

Quote Detail now prioritizes commercial summary/products, uses a 70/30 desktop layout and one-column mobile layout, and presents one primary header action. Versions/archive and higher-assurance tools are secondary. Order Detail reads the actual confirmation origin, timestamp, accepted commercial snapshot and active Plan. It does not infer collection or Deal WON.

Rollout remains separate: apply the two additive migrations and configure the intended Workspace policy. No migration, fixture, business-data update or auto-confirm policy was applied to `UnicoreCRM_Development`.

## Root cause and business change

B5 previously required a saved B1 assessment/fingerprint/policy binding even for an assessment that would conclude approval was unnecessary. The ordinary policy now permits direct issuance without writing fake `APPROVED` or `NOT_REQUIRED`. Explicit `AlwaysRequireApproval` still takes precedence; other special approval deployments retain B1 by leaving the ordinary bypass disabled.

The new acceptance intent freezes `AutoConfirmRequested=true`. Existing rows default to false and keep their historical conversion route. New blocked intents never fall back to a successful durable DRAFT result.

O1's authoritative accepted-snapshot Order factory is enlisted in O2's existing shared SERIALIZABLE transaction. Shared O2 rules, Orders confirmation, Payments activation, evidence/events and workflow receipt commit together. Acceptance is already durable in its Quotes transaction. A lost acknowledgement replays the immutable receipt and original Order.

`ConfirmedBy=system:quote-policy`, `ConfirmationOrigin=WORKSPACE_POLICY` and the current policy version distinguish automatic confirmation from the original acceptance actor. The domain refuses an automatic policy origin attributed to a human actor. No human review record is invented.

See `backend/docs/development/quote-automatic-confirmation-policy.md` for settings, boundaries and preserved limitations.

## Source and contracts

The reviewed input was the dirty local working tree. Entry snapshot/hashes are under `task-runtime/quote-detail-auto-confirm/entry/` and `entry-hashes.json`; existing changes were retained. The task-specific changed-file manifest is `task-runtime/quote-detail-auto-confirm/changed-files.json`: 86 files, including 41 Frontend files (generated API artifacts included), 43 Backend/source/test/documentation files and two authority-extension manifests.

Backend changes cover:

- Quotes: ordinary policy, B5/delivery eligibility, acceptance intent/read model/mapping, queue status/retry, current-source/PDF/buyer validation and GetQuote policy projection.
- Orders: the existing factory's enlisted participant, system confirmation origin, domain guard, projection and nullable origin fields.
- Payments: the existing Plan factory can enlist in the shared transaction; activation uses the same participant and rules as manual O2.
- Workflows: shared `ConfirmationRules`, internal `AutomaticConfirmation`, recovery dispatch and registration. The original manual O2 endpoint remains intact.
- CRM: narrow owner-local Contact/Organization eligibility readers; no foreign persistence access or human capability grant.
- Verification: domain origin regression test and real HTTP/SQL/Chromium scripts.

Frontend changes cover Quote Detail, primary action/progress/document/acceptance/lifecycle/status components, shared optional commercial header typography, Quote action/read models, Order origin/acceptance presentation, bilingual guidance, API generation and the requested presentation regression assertions.

OpenAPI adds `ordinarySales`, `autoConfirmRequested`, `BLOCKED`, `confirmationOrigin` and `confirmationPolicyVersion`. Generated files were produced by `npm run api:generate`; API contract checks pass. No public conversion endpoint was added. Original admission pins and compatibility thresholds were retained; the explicit owner supplement and three narrow dependency edges were recorded through the existing extension mechanism.

Additive migrations:

| Owner | Migration | Effect |
|---|---|---|
| Quotes | `20261102014225_QuoteAutomaticConfirmationIntent` | Adds non-null intent flag, default false for existing rows |
| Orders | `20261102014228_OrderPolicyConfirmationOrigin` | Adds nullable origin and policy-version columns |

## HTTP, SQL and recovery evidence

Full connected matrix: **96 checks, 0 failures**.

Evidence: `task-runtime/quote-detail-auto-confirm/UnicoreCRM_OrderConfirmationTest_e78f4fdba1b1473f9040d6d20fdf1641/`.

SQL Server: `DESKTOP-ICH225R`. Database: the isolated allowlisted name above. Target, owner-schema migration histories, Orders, Plans, acceptances, HTTP statuses and host logs are captured there.

Observed HTTP behavior includes real B5 issuance 201; successful Quote/Order reads and acceptance/consent 200; anonymous 401; missing capability/FLS/cross-Workspace 403; invalid source-document, expired Quote and special-approval issuance 409; stale version 412; client actor/price override 422; expired invitation 410. Public preview does not consent.

Coverage includes:

- Staff and customer-link positive paths with authoritative amount 247500 VND, source Quote version and commercial fingerprint retained.
- Same-key replay and eight concurrent same/different-key acceptance requests producing one Order/Plan.
- A real SQL trigger throwing during Plan activation: no Order, orphan Plan or confirmation receipt commits; acceptance stays durable. Removing the fault and restarting the worker/API resumes the original intent.
- A real Quote acknowledgement fault after the shared commit: exactly one confirmed Order exists; restart acknowledges that same receipt rather than recreating it.
- Explicit special approval policy, higher signer, credit-required terms, disallowed product type, missing physical fulfillment and required payment instructions fail closed.
- FLS, stale version, foreign Quote document, expired invitation/Quote, actor spoofing and client price override negatives.
- Restart persistence and immutable ordinary assurance (`ORDINARY_SALES_UNVERIFIED`), without promoting it to B6 VERIFIED.

The full dataset contains eight confirmed Orders and eight active current Plans. Three genuinely blocked acceptances are classified separately, not counted as successful conversions.

Independent read-only SQL verification reports **12 invariants with zero violations**: duplicate Orders, duplicate current Plans, mismatched CONFIRMED/ACTIVE pairs, source fingerprint mismatch, automatic human attribution, missing/orphan receipts, orphan creation evidence/Plan events, invented satisfied amounts and durable auto-path drafts. Reproduce with `backend/scripts/verify-quote-automatic-confirmation-sql.ps1` against the allowlisted test database.

## Chromium and screenshots

The latest presentation-specific connected rerun also passes: **37 HTTP/SQL checks, zero failures**, real headless Chromium, no page errors. It waits for the ACTIVE Plan in Order Detail and asserts that no manual confirmation button is shown. It uses:

`task-runtime/quote-detail-auto-confirm/UnicoreCRM_OrderConfirmationTest_260d09cf41a94916bc9fcfb37c345fcf/`.

Its `browser/` folder contains fresh screenshots of DRAFT, issued PDF, SENT, accepted processing, confirmed Order link, staff acceptance modal, public preview/consent, Order Detail and relogin, plus desktop/tablet/mobile Quote views. The browser also asserts a 24px/600 title, exactly one primary header button, the actual ACCEPTED badge, no page horizontal overflow and no direct Order-create/manual-confirm HTTP request.

Visual review found and fixed a real stale status label: the legacy translation bridge remembered the SENT text node after React updated it to ACCEPTED. The already-localized status label now uses the bridge's existing skip boundary at its source. Evidence strings containing public invitation identifiers remain in audit, not ordinary customer-response presentation. Accepted/terminal/read-only Quotes no longer show an unavailable issuance action or its redundant readiness warning; actual read/download failures retain their error feedback.

**Before screenshots are unavailable.** The input source snapshot is retained. Automatic approval review rejected the attempted isolated baseline presentation replay with `blocked by policy` and no additional reason. No old screenshot is represented as a new result. Consequently the complete requested before/after visual evidence gate is not marked PASS.

## Quality checks

| Check | Result |
|---|---|
| Backend build | PASS, zero warnings/errors |
| Focused Quote/Order/Payment tests | PASS, 347/347 |
| Full Backend tests | 493 passed, 4 existing architecture/metadata failures |
| Frontend strict typecheck/type budget | PASS |
| Frontend project lint | PASS (`npm run lint`, repository strict compilation/syntax pipeline; standalone ESLint is not configured or claimed) |
| Production build/bundle budget | PASS |
| API contract/generated consistency | PASS |
| Quote/Order API boundaries and integrity | PASS |
| Quote approval/delivery/presentation contracts | PASS |
| Guidance, presentation responsibility, mutation/retention boundaries | PASS |
| Repository inventory | Regenerated through official generator and checked |
| Full Frontend verification | Stops at existing gate 12, `60 !== 59`; full 328-gate PASS is not claimed |

The four Backend failures remain endpoint admission, cross-owner inventory, persistence owner metadata and existing layer violations. The final full log contains none of the new automatic-confirmation contract-edge violations. Logs and TRX files are under `task-runtime/quote-detail-auto-confirm/`.

## Acceptance gates

PASS below means the configured isolated path and recorded evidence, not an enabled Development/production deployment.

| Gate | Result |
|---|---|
| ORDINARY_QUOTE_NO_APPROVAL | PASS |
| QUOTE_PDF_DIRECT_ISSUANCE | PASS |
| QUOTE_DETAIL_V3_UI | PASS, after-state evidence |
| QUOTE_TYPOGRAPHY | PASS |
| QUOTE_ACTION_HIERARCHY | PASS |
| CUSTOMER_LINK_ACCEPTANCE | PASS, explicit public consent; live email delivery untested |
| STAFF_RECORDED_ACCEPTANCE | PASS |
| AUTO_CONFIRMED_ORDER | PASS |
| PAYMENT_PLAN_ACTIVE | PASS |
| NO_DRAFT_MANUAL_STEP | PASS |
| ORDER_PRICING_INTEGRITY | PASS |
| ORDER_IDEMPOTENCY | PASS |
| ORDER_TRANSACTION_CONSISTENCY | PASS |
| QUOTE_ORDER_FE_BE_E2E | PASS |
| WORKSPACE_AUTHORIZATION | PASS, tested negative matrix |
| HIGH_ASSURANCE_GUARDS | PASS, ordinary acceptance remains unverified |
| CHROMIUM_VISUAL_ACCEPTANCE | FAIL for complete before/after requirement; connected after-state tests PASS |

## Remaining limits and rollout

- Live Gmail delivery was not run and is never reported as sent without provider success. Tests use controlled invitation links and synthetic addresses.
- Credit-consuming approval and payment instruction authority are still absent from existing owner contracts. Required cases remain blocked. Physical delivery and frozen operational review requirements also need genuine authority/facts; no successful substitute is created.
- Development/production rollout requires the additive migrations and an explicit Workspace policy choice, with its real limits/product types and assurance settings. Stop/drain legacy O1 workers and deploy all updated workers before enabling new intents; mixed legacy/new recovery binaries are not a supported rollout. None was silently enabled by these tests.
- Before screenshot replay was rejected as described above. Existing repository-wide failures remain separate from this task's focused and connected results. Diff checking also reports an existing extra blank line at the end of `extendedScreens.ts`; its suffix is byte-identical to the entry snapshot and was preserved.
- No commit/push/reset/merge/rebase, business-data reseed, historical bulk conversion or graduation ZIP overwrite was performed. Task-owned hosts stop in the verification script's `finally` block.
