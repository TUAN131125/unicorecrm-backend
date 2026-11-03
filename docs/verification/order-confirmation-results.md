# Order confirmation verification

Local functional acceptance: PASS on 2026-11-01 for the supported ordinary-acceptance/no-credit-blocker path. This is implementation and connected-test evidence, not a production deployment or full-repository release certification.

## Delivered behavior

The admitted endpoint `POST /workflows/order-confirmation/{orderId}/confirm-with-payment-plan` now confirms the version-matched DRAFT Order and activates its explicitly identified DRAFT Payment Plan in one shared local SQL transaction. Orders, Quotes and Payments retain their own persistence ownership. Receipt, confirmation actor/time/version, operational review, Order outbox, schedule changes and Plan event commit or roll back together. Retries return the immutable REPLAYED result, including actual persisted event/receipt IDs.

Order Detail displays the Agreement, actual Plan/state/version and operational review requirement. Its confirmation dialog requires explicit review/notes and accepts required physical-goods recipient/address data. Pending commands are locked and successful commands refresh authoritative Order and Plan data. The Order List confirmation action opens the same detail review instead of sending an incomplete request. Connected mode does not fall back to browser success or localStorage.

O1 snapshots and acceptance remain authoritative. Both a fresh STAFF_RECORDED acceptance and a fresh CUSTOMER_LINK consent were exercised during O2; neither becomes VERIFIED. Missing/stale source binding, Workspace authority, field/record authority and higher-signer policy fail closed. Existing acknowledged O1 drafts can receive a missing DRAFT Plan through bounded idempotent recovery; preparation does not itself confirm them.

See [business, transaction and configuration policy](../development/order-confirmation-policy.md).

## Acceptance gates

| Gate | Verdict |
|---|---|
| ORDER_CONFIRM_COMMAND | PASS |
| ORDER_PAYMENT_PLAN_ACTIVATION | PASS |
| OPERATIONAL_APPROVAL_POLICY | PASS |
| ORDER_TRANSACTION_CONSISTENCY | PASS |
| ORDER_CONFIRM_IDEMPOTENCY | PASS |
| ORDER_CONFIRM_CONCURRENCY | PASS |
| ORDER_SQL_PERSISTENCE | PASS |
| ORDER_FE_BE_CONNECTED | PASS |
| ORDER_AUTHORIZATION | PASS |
| ORDER_O2_FUNCTIONAL_ACCEPTANCE | PASS for the supported scope stated above |

## Actual evidence

Final isolated database: `UnicoreCRM_OrderConfirmationTest_Ready` on local SQL Server `DESKTOP-ICH225R`. The real ApiHost, real HTTP, actual issued Quote PDFs, SQL and Chromium were used. No O1 result was substituted for O2 execution.

52/52 connected assertions passed, script process exit 0. The evidence directory is `task-runtime/order-confirmation-o2/UnicoreCRM_OrderConfirmationTest_Ready/`:

- `assertions.json`, `http.json`, `context.json`: exact HTTP/SQL checks and source IDs.
- `browser.json`: PASS, including an upstream COMMITTED response deliberately aborted before delivery to Chromium; the real HTTP client retried the same intent and received the original REPLAYED command ID.
- `browser-restart.json`: PASS after actual host restart, fresh browser login, cold reads, refresh and logout/login.
- `chromium-draft.png`, `chromium-confirmed.png`, `chromium-restart.png`: inspected browser evidence.

The matrix covers anonymous 401, insufficient capability 403, foreign Workspace denial, stale Order and Plan versions, missing Plan, commercial and Plan currency mismatches, missing staff operational approval, credit-required denial, missing physical fulfillment, valid physical fulfillment, ordinary customer-link policy, higher-signer denial, non-DRAFT/double confirmation, exact replay, changed idempotent payload and eight concurrent requests.

A real SQL trigger fails Payments activation after Orders has staged confirmation. Order, Plan, schedule versions/amounts, receipt and outbox all roll back. The same key then commits successfully after fault removal. A missing-plan fixture blocks confirmation while owner preparation is faulted; removing that fault lets recovery create one DRAFT Plan and the real confirmation operation succeeds. No successful confirmation, approval or RBAC grant was written with SQL.

`task-runtime/order-confirmation-o2/sql-invariants.json` independently records:

| SQL invariant | Actual |
|---|---:|
| CONFIRMED Orders | 6 |
| ACTIVE Plans | 6 |
| Confirmation receipts | 6 |
| CONFIRMED without ACTIVE Plan | 0 |
| ACTIVE Plan without CONFIRMED Order | 0 |
| Duplicate current Plans | 0 |
| Missing confirmation actor/time | 0 |
| Order confirmation outbox events | 6 |
| Plan activation events | 6 |
| Payment records | 0 |
| Satisfied schedule amount | 0 |

Development was not migrated or seeded. The seven original Quote/document/B6/Order table counts and checksums still match the saved O1 baseline; see `development-db-after.json`. Test identities/roles were declared before business commands through the existing isolated fixture bootstrap. A separate reader identity remains unprivileged and its confirmation is denied. Roles were not elevated in response to failing tests.

## Contracts, migrations and checks

The existing operation path and `orders.confirm` capability are retained. The request supplement adds explicit Plan identity/version, session-bound operational review and optional fulfillment. Server-owned actor, buyer, prices and payment evidence are absent from the strict request. Generated clients and request projection were updated; operation count remains 365.

The existing project-extension mechanism records only this owner-authorized supplement and its narrow participant dependencies. Original pins and runtime protections were not disabled. No separate governance subsystem was introduced.

Actual additive migrations, applied only to isolated test databases:

- Orders: `20261101025934_OrderOperationalConfirmation`, `20261101031438_OrderConfirmationConcurrency`.
- Payments: `20261101025938_OrderPaymentPlanActivation`, `20261101031438_CurrentOrderPaymentPlan`.
- Workflows: `20261101025942_OrderConfirmationReceipt`.

Backend build: PASS, zero warnings/errors. Focused Quote/Order/Payment tests: 346/346 PASS, including seven new domain/security tests. API verification, Frontend typecheck/lint/build and bundle budget passed. Focused gates for presentation, mutation authority, API management, effective record access, commercial concurrency, sales/order contracts and guidance passed. Logs are in `task-runtime/order-confirmation-o2/`.

Full Backend execution: 481 passed, 4 failed. Remaining failures concern existing owner/contract mapping, unassigned Communications/AiExecution contexts, existing layer violations and Communications/AI endpoint admission. The final run contains no O2 confirmation/participant contract violation. These unrelated controls were not relaxed.

Full Frontend verification was executed. It passed 42 gates before finding an outdated conflict-test request body after the contract supplement. That fixture and the other sales/order fixture were updated to include the required Plan binding; both focused gates now pass. The 23-control mutation-negative matrix passed and restored every temporary source mutation byte-for-byte; no recovery journal remains. The independent compatibility gate still fails at 60 candidates versus budget 53. A complete 328-gate PASS is not claimed. Existing unrelated whitespace findings in untouched list/statistics files were retained.

## Remaining limits

Authoritative approved-credit consumption is not implemented by the current credit owner. Required credit and postpaid exposure therefore remain blocked; an Order's approval summary or client reference cannot substitute for that authority. Supported confirmation creates no payment receipt or satisfied amount. Payment-account instruction authority is also unavailable, so that optional path is blocked and the supported result truthfully reports `paymentInstructionCreated: false`.

Business-calendar scheduling requires a real calendar authority; supported calendar-day rules use Workspace local dates. Live Gmail and production B6 positive verification were NOT_RUN and remain outside O2. Automatic Deal WON, collection, invoice issuance, shipping execution and returns/refunds were not added.

No commit, push, reset, merge, rebase or graduation ZIP overwrite was performed. All test hosts were stopped; the final isolated database is retained for inspection.
