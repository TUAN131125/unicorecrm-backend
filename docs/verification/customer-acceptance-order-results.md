# Customer Quote acceptance → automatic Order draft

Verified locally on 2026-11-01 against `UnicoreCRM_AcceptanceTest_20261101_Final3`, SQL Server `DESKTOP-ICH225R`, real ApiHost HTTP and real Chromium. This is local connected evidence, not a production deployment or a live Gmail delivery certification.

## Outcome and authority

Previously missing pieces were an ordinary customer acceptance policy, usable customer invitation/consent and staff attestation commands, durable acceptance-to-Order recovery, Order snapshot writes, and connected read UI. These are now implemented. Both allowed acceptance sources automatically create a persisted DRAFT Order and expose it in Quote Detail and Order List/Detail.

The owner-requested extension admits five new operations: `recordQuoteSalesAcceptance`, `createQuoteAcceptanceInvitation`, `listQuoteAcceptanceInvitations`, `previewCustomerQuoteAcceptance`, `consentCustomerQuoteAcceptance`. They are implemented. The original `convertAcceptedQuoteToOrderDraft` remains `ADMITTED_NOT_IMPLEMENTED`; no manual `/order-drafts/from-accepted-quote` endpoint is claimed. Automatic creation uses narrow internal owner contracts and durable recovery instead of that public operation.

B6 is not a prerequisite for a Workspace that explicitly allows ordinary sales acceptance within its configured currency/value limits. Ordinary `ACCEPTED` retains `ORDINARY_SALES_UNVERIFIED`, never VERIFIED. With `RequireVerifiedSignerAuthority: true`, the ordinary path is denied. Production VERIFIED acceptance still needs its actual B6 identity/signatory/source authority; simulation is not substituted. No automatic Order CONFIRMED or Deal WON transition was added.

See [policy and deployment configuration](../development/customer-acceptance-order-policy.md). Configuration is opt-in; the isolated test policy does not enable production Workspaces. No default role grants were added. The test fixture explicitly granted its synthetic document issuer permission.

Governance changes are limited to the explicit ordinary-sales extension, its five operation records, and the two narrow cross-owner queue/writer contracts and dependency edges. Original pins and B6 production security were not rewritten to claim verified acceptance. Extension verification is labelled local HTTP/SQL/Chromium pass with live Gmail NOT_RUN.

Backend additions include the token/attestation policy and commands, invitation history, Gmail delivery adapter, Quotes persistence, Orders factory/transaction/audit/outbox, and hosted recovery. Frontend additions include the public consent page, Quote acceptance/delivery panel, connected adapter, pending recovery status and Order acceptance/payment summary. The generated contract has 365 operations. Actual migrations are `20261030150242_QuoteSalesAcceptance`, `20261030150245_AcceptedQuoteOrderDraft`, and `20261101015124_AcceptedQuoteBuyerBinding`; all were applied by the isolated verification harness.

## Connected result

51/51 HTTP/SQL/Chromium assertions passed, process exit 0. Zero HTTP 5xx responses in the final recorded run.

| Check | Result |
|---|---|
| Staff attestation and explicit customer link consent | PASS |
| Preview/opening a link leaves Quote SENT | PASS |
| Accurate authoritative totals and payment snapshot | PASS: 247,500 VND |
| Idempotent retry, changed payload rejection | PASS |
| Eight concurrent requests, two recovery workers, one acceptance/Order | PASS |
| Stale version, Workspace isolation, missing capability and hidden commercial field | PASS |
| Client price/actor overrides | PASS: rejected |
| Explicit consent, invalid/expired token, archived/expired Quote | PASS |
| Higher signer assurance requirement | PASS: `VERIFIED_SIGNER_AUTHORITY_REQUIRED` |
| Order SQL failure | PASS: Order, creation evidence and outbox roll back; acceptance remains PENDING |
| Restart after SQL failure | PASS: pending conversion completes |
| Lost Quote acknowledgment after Order commit | PASS: recovery reuses one committed Order |
| Browser Order List/Detail, refresh, logout/login | PASS |
| Actual host restart, fresh login and Chromium reads | PASS |
| Missing Gmail configuration | PASS: FAILED / GMAIL_NOT_CONFIGURED, no SENT fabrication |
| External Gmail delivery | NOT_RUN: no test Workspace credentials/authorized recipient configured |

The immutable acceptance and source fingerprint bind the server commercial snapshot. Orders preserves buyer, source Quote/Deal references, line/totals/adjustments, payment agreement, acceptance evidence, server version and operational approval requirement. The connected fixture used direct-sale Quotes; a linked Deal positive scenario was not separately exercised in this run.

Evidence under `task-runtime/customer-acceptance-order-20261030/`:

- `connected/summary.json`, `connected/assertions.json`, `connected/http.json`, `connected/context.json`
- `connected/browser/result.json`, `connected/browser/restart-result.json` and screenshots
- `development-db-before.json`, `development-db-after.json`: all seven original business-table counts/checksums unchanged
- `backend-final-build.log`: build PASS, zero warnings/errors
- `backend-domain-tests.log`: Quote/Order tests 339/339 PASS
- `backend-focused-tests.log`: 342 PASS, one global endpoint admission failure listing existing Communications/AI endpoints outside this extension
- `api-check.log`: 365 operations, API contract PASS
- `frontend-final-build.log`: production build and bundle budget PASS
- `frontend-guidance.log`, `quality.guidance-runtime.log`, `quality.presentation-responsibility.log`, `quality.mutation-command-authority.log`, `quality.architecture.log`: PASS

Full-repository verification is not represented as a clean release gate. The final Frontend architecture run passed 54 gates and stopped at `quality.compatibility-retirement`: 60 compatibility candidates exceed the existing budget (53). This same baseline issue was present before this implementation; its threshold was not relaxed. Remaining architecture gates and the full 328-gate verify run are not claimed as PASS. Backend global endpoint admission discrepancies remain for their respective Communications/AI owners. API management boundaries and repository inventory both passed independently. The Frontend negative-control matrix proved 23 controls and restored each temporarily mutated source file byte-for-byte; its recovery journal is absent.

The final Gmail adapter also treats successful SMTP acceptance as success even if subsequent connection disposal fails, and maps TLS handshake failure to a bounded error. Live provider success remains unverified.

## Acceptance verdicts

| Gate | Verdict |
|---|---|
| CUSTOMER_EMAIL_ACCEPTANCE | BLOCKED for a real email round trip; controlled customer link flow PASS |
| STAFF_RECORDED_ACCEPTANCE | PASS |
| ACCEPTANCE_AUDIT | PASS: separate actual operation and session/bearer actor |
| AUTOMATIC_ORDER_CREATION | PASS |
| ORDER_PRICING_INTEGRITY | PASS |
| ORDER_IDEMPOTENCY | PASS |
| ORDER_SQL_PERSISTENCE | PASS |
| ORDER_WORKSPACE_ISOLATION | PASS |
| QUOTE_ORDER_FE_BE_E2E | PASS |
| GMAIL_PROVIDER_REAL_TEST | NOT_RUN |
| RUNTIME_SECURITY_REGRESSION | PASS for the executed local negative matrix and Quote/Order regressions |
| GOVERNANCE_SIMPLIFICATION | COMPLETED for this explicit extension; no security-control removal |

Actual B6 production higher-assurance verification remains BLOCKED by its existing operational-source authority; its successful production path was NOT_RUN. Ordinary sales and simulated B6 results are not evidence for that verdict.

## Remaining work

`confirmOrderWithPaymentPlan` is already ADMITTED, currently NOT_IMPLEMENTED, at `/workflows/order-confirmation/{orderId}/confirm-with-payment-plan`. Implement and verify its operational actor authority, concurrency and idempotency, required recipient/delivery details for physical goods, payment/fulfillment readiness, ordinary-acceptance review when policy requires it, and atomic audit/outbox. Deal WON must use its own business authority. Neither transition is inferred from acceptance or draft creation.

No commit, push, reset, merge or graduation ZIP overwrite was performed. Test hosts were stopped. The final isolated database is retained for inspection; Development business data was not migrated or seeded.
